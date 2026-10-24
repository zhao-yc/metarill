using System.Globalization;
using System.Security.Cryptography;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Files;

/// <summary>本地文件库只使用固定磁盘根与服务端随机键，公开与受保护读取共用真实状态检查。</summary>
public sealed partial class FileLibraryService(SqlRowStore store, FileMetadataContext registry, IIdGenerator ids, IRuntimeAuthorization authorization, FileLibraryOptions options)
{
    private const string Files = "yz_file_library";
    private const string Folders = "yz_file_library_folder";
    private const string References = "yz_file_ref";
    private string Root => Path.GetFullPath(options.RootDirectory);

    /// <summary>创建待上传记录，供应商、对象键和访问级别均由服务端决定。</summary>
    public Task<FileUploadPlan> PlanUploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default) => PlanUploadCoreAsync(request, false, cancellationToken);

    /// <summary>仅服务端旧上传兼容入口可指定公开导入，普通计划仍由元数据决定访问模式。</summary>
    private Task<FileUploadPlan> PlanUploadCoreAsync(FileUploadRequest request, bool legacyPublic, CancellationToken cancellationToken) => store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor();
        FileValidation.RequireName(request.OriginalName);
        var column = ResolveUploadColumn(request.ConfigColumnId, actor);
        var policy = FileValidation.Policy(column, options);
        if (legacyPublic) policy = policy with { AccessMode = "PUBLIC" };
        if (request.SizeBytes < 0 || request.SizeBytes > policy.MaximumBytes) throw new InvalidOperationException("文件大小超过服务端或控件上限。");
        if (!policy.Extensions.Contains(Path.GetExtension(request.OriginalName))) throw new InvalidOperationException("文件扩展名不在允许范围。");
        if (request.FolderId is { } folder) RequireProject(await RequiredAsync(Folders, folder, true, cancellationToken), actor);
        var id = ids.NextId();
        var now = DateTime.UtcNow;
        await store.InsertAsync(Files, new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = request.OriginalName, ["content_type"] = FileValidation.ContentType(request.OriginalName),
            ["size"] = request.SizeBytes, ["storage_key"] = Guid.NewGuid().ToString("N") + ".blob", ["access_mode"] = policy.AccessMode,
            ["status"] = "planned", ["owner_id"] = actor.AccountId, ["folder_id"] = request.FolderId, ["yz_project_id"] = Project(actor),
            ["sha256"] = FileValidation.Digest(request.Sha256), ["insert_time"] = now, ["insert_manager_id"] = actor.AccountId
        }, cancellationToken);
        return new FileUploadPlan(id, new LocalUploadPlan("local", "SERVER_MULTIPART", $"/admin/sys/file-library/local-upload/{id}",
            new Dictionary<string, string>(), new Dictionary<string, string>(), DateTimeOffset.UtcNow.AddMinutes(30)));
    }, cancellationToken);

    /// <summary>持有文件记录锁完成有界流写入和摘要核验，上传后为私有草稿，显式完成后才可绑定或公开。</summary>
    public async Task<FileDescriptor> UploadAsync(long id, Stream body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        string? temporary = null;
        try
        {
            return await store.Session.InTransactionAsync(async () =>
            {
                var actor = authorization.RequireActor();
                var row = await RequiredAsync(Files, id, true, cancellationToken); RequireOwner(row, actor);
                if (SqlRowStore.Text(row, "status") != "planned") throw new InvalidOperationException("文件状态不允许重新上传。");
                if (DateTime.UtcNow - Convert.ToDateTime(row["insert_time"], CultureInfo.InvariantCulture) > TimeSpan.FromMinutes(30)) throw new InvalidOperationException("上传计划已过期。");
                EnsureRoot();
                var destination = FileValidation.StoragePath(Root, SqlRowStore.Text(row, "storage_key"));
                temporary = destination + ".part." + Guid.NewGuid().ToString("N");
                var expected = SqlRowStore.Id(row, "size");
                await store.UpdateAsync(Files, id, new Dictionary<string, object?> { ["status"] = "uploading" }, cancellationToken);
                long actual = 0;
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[65536];
                    while (true)
                    {
                        var read = await body.ReadAsync(buffer, cancellationToken);
                        if (read == 0) break;
                        actual += read;
                        if (actual > expected || actual > options.MaxFileBytes) throw new InvalidOperationException("实际上传大小超过已确认计划。");
                        digest.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    await output.FlushAsync(cancellationToken);
                }
                if (actual != expected) throw new InvalidOperationException("实际上传大小与计划不一致。");
                var hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
                var requestedHash = SqlRowStore.Text(row, "sha256");
                if (requestedHash.Length > 0 && !string.Equals(requestedHash, hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("上传内容摘要与计划不一致。");
                // 数据库提交结果不明时保留随机对象供完成或重试核验，不能删除可能已提交的文件。
                File.Move(temporary, destination, true); temporary = null;
                await store.UpdateAsync(Files, id, new Dictionary<string, object?> { ["status"] = "uploaded", ["sha256"] = hash, ["update_time"] = DateTime.UtcNow }, cancellationToken);
                row["status"] = "uploaded"; row["sha256"] = hash;
                return Describe(row);
            }, cancellationToken);
        }
        finally { if (temporary is not null) TryDelete(temporary); }
    }

    /// <summary>再次核对真实对象大小及摘要后发布 ready；重复完成是安全幂等操作。</summary>
    public Task<FileDescriptor> CompleteAsync(long id, CancellationToken cancellationToken = default) => store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor();
        var row = await RequiredAsync(Files, id, true, cancellationToken); RequireOwner(row, actor);
        var status = SqlRowStore.Text(row, "status");
        if (status is not ("uploaded" or "ready")) throw new InvalidOperationException("文件尚未上传完成。");
        var path = FileValidation.StoragePath(Root, SqlRowStore.Text(row, "storage_key"));
        await using var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (content.Length != SqlRowStore.Id(row, "size")) throw new InvalidOperationException("已上传对象大小异常。");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(hash, SqlRowStore.Text(row, "sha256"), StringComparison.Ordinal)) throw new InvalidOperationException("已上传对象摘要异常。");
        await store.UpdateAsync(Files, id, new Dictionary<string, object?> { ["status"] = "ready", ["update_time"] = DateTime.UtcNow }, cancellationToken);
        row["status"] = "ready";
        return Describe(row);
    }, cancellationToken);

    /// <summary>受保护文件按草稿/就绪状态核对当前身份及真实引用；客户端标识不授予权限。</summary>
    public async Task<FileDescriptor> ResolveAsync(long id, string? pageId = null, long? recordId = null, CancellationToken cancellationToken = default)
    {
        var actor = authorization.RequireActor(); var row = await RequiredAsync(Files, id, false, cancellationToken);
        await RequireReadableAsync(row, actor, cancellationToken); return Describe(row, allowProtectedDraft: true);
    }

    /// <summary>返回可寻址本地流供 ASP.NET Core 启用 Range；绝不代理或重定向远端地址。</summary>
    public async Task<FileContentHandle> OpenContentAsync(long id, bool publicOnly = false, string? pageId = null, long? recordId = null, CancellationToken cancellationToken = default, string? expectedProjectId = null)
    {
        var row = await RequiredAsync(Files, id, false, cancellationToken);
        if (publicOnly)
        {
            if (SqlRowStore.Text(row, "status") != "ready" || SqlRowStore.Text(row, "access_mode") != "PUBLIC"
                || expectedProjectId is not null && SqlRowStore.Text(row, "yz_project_id") != expectedProjectId) throw new UnauthorizedAccessException("文件不存在或不可公开访问。");
        }
        else await RequireReadableAsync(row, authorization.RequireActor(), cancellationToken);
        var path = FileValidation.StoragePath(Root, SqlRowStore.Text(row, "storage_key"));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != SqlRowStore.Id(row, "size")) { await stream.DisposeAsync(); throw new InvalidOperationException("文件对象大小异常。"); }
        return new FileContentHandle(stream, string.IsNullOrWhiteSpace(SqlRowStore.Text(row,"content_type"))?"application/octet-stream":SqlRowStore.Text(row,"content_type"), SqlRowStore.Text(row, "name"), stream.Length, SqlRowStore.Text(row, "sha256"));
    }

    /// <summary>删除前锁定文件并确认无有效引用；数据库先失效，磁盘清理失败明确返回。</summary>
    public async Task<FileDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var path = await store.Session.InTransactionAsync(async () =>
        {
            var actor = authorization.RequireActor(); var row = await RequiredAsync(Files, id, true, cancellationToken); RequireDeleteOwner(row, actor);
            if ((await store.ReadForUpdateAsync(References, new Dictionary<string, object?> { ["file_id"] = id }, cancellationToken)).Count > 0) throw new InvalidOperationException("文件仍被业务记录引用，不能删除。");
            await store.UpdateAsync(Files, id, new Dictionary<string, object?> { ["status"] = "deleted", ["delete_no"] = DateTime.UtcNow }, cancellationToken);
            return FileValidation.StoragePath(Root, SqlRowStore.Text(row, "storage_key"));
        }, cancellationToken);
        return new FileDeleteResult(id, TryDelete(path));
    }

    /// <summary>Service兼容入口也按真实读取过滤；原HTTP入口另复核控件访问级别。</summary>
    public Task<IReadOnlyList<FileDescriptor>> ListAsync(long? folderId = null, CancellationToken cancellationToken = default)
        => ListVisibleCoreAsync(null,null,folderId,false,cancellationToken);

    /// <summary>创建目录时锁定父目录，防止与父目录删除发生竞态。</summary>
    public Task<FileFolder> CreateFolderAsync(string name, long? parentId = null, CancellationToken cancellationToken = default) => store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor(); name=DisplayName(name,100);
        await LockFolderScopeAsync(actor,cancellationToken);
        if (parentId is { } parent) RequireProject(await RequiredAsync(Folders, parent, true, cancellationToken), actor);
        await RequireSiblingNameAsync(actor,parentId,name,null,cancellationToken);
        var id = ids.NextId();
        await store.InsertAsync(Folders, new Dictionary<string, object?> { ["id"] = id, ["name"] = name, ["parent_folder_id"] = parentId, ["owner_id"] = actor.AccountId, ["yz_project_id"] = Project(actor), ["insert_time"] = DateTime.UtcNow }, cancellationToken);
        return new FileFolder(id, name, parentId);
    }, cancellationToken);

    /// <summary>缺省返回完整项目共享树；显式父ID仅作为Service调用的展示筛选，不授予权限。</summary>
    public async Task<IReadOnlyList<FileFolder>> ListFoldersAsync(long? parentId = null, CancellationToken cancellationToken = default)
    {
        var tree=await ListFolderTreeAsync(null,cancellationToken);
        return parentId is null?tree:tree.Where(folder=>folder.ParentFolderId==parentId).ToArray();
    }

    /// <summary>首期只删除空目录，不提供隐式递归删除。</summary>
    public async Task DeleteFolderAsync(long id, CancellationToken cancellationToken = default) => await store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor();await LockFolderScopeAsync(actor,cancellationToken);RequireProject(await RequiredAsync(Folders, id, true, cancellationToken), actor);
        if ((await store.ReadForUpdateAsync(Folders, new Dictionary<string, object?> { ["parent_folder_id"] = id,["yz_project_id"]=Project(actor) }, cancellationToken)).Count > 0 ||
            (await store.ReadForUpdateAsync(Files, new Dictionary<string, object?> { ["folder_id"] = id,["yz_project_id"]=Project(actor) }, cancellationToken)).Any(row=>SqlRowStore.Text(row,"status")!="deleted"))
            throw new InvalidOperationException("目录不为空，不能删除。");
        await store.UpdateAsync(Folders, id, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow }, cancellationToken);
        return true;
    }, cancellationToken);

    /// <summary>按原读取权修改展示名，存储键、已验证MIME、内容及历史引用均不改变。</summary>
    public Task RenameAsync(long id,string name,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        name=DisplayName(name,255);
        var actor=authorization.RequireActor();var row=await RequiredAsync(Files,id,true,token);await RequireReadableAsync(row,actor,token);
        await store.UpdateAsync(Files,id,new Dictionary<string,object?>{{"name",name},{"update_time",DateTime.UtcNow}},token);return true;
    },token);

    /// <summary>移动文件先锁定目标目录，和空目录删除共用同一行锁。</summary>
    public Task MoveAsync(long id,long? targetFolderId,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        var actor=authorization.RequireActor();
        if(targetFolderId is{} folder)RequireProject(await RequiredAsync(Folders,folder,true,token),actor);
        await RequireReadableAsync(await RequiredAsync(Files,id,true,token),actor,token);
        await store.UpdateAsync(Files,id,new Dictionary<string,object?>{{"folder_id",targetFolderId},{"update_time",DateTime.UtcNow}},token);return true;
    },token);

    /// <summary>目录更名只修改显示字段，保留原有项目及所有者。</summary>
    public Task RenameFolderAsync(long id,string name,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        name=DisplayName(name,100);var actor=authorization.RequireActor();await LockFolderScopeAsync(actor,token);
        var row=await RequiredAsync(Folders,id,true,token);RequireProject(row,actor);await RequireSiblingNameAsync(actor,NullableId(row,"parent_folder_id"),name,id,token);
        await store.UpdateAsync(Folders,id,new Dictionary<string,object?>{{"name",name}},token);return true;
    },token);

    /// <summary>项目共享目录移动按固定范围锁检查父链及同级重名；跨项目或循环始终拒绝。</summary>
    public Task MoveFolderAsync(long id,long? parentId,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        var actor=authorization.RequireActor();
        await LockFolderScopeAsync(actor,token);
        var source=await RequiredAsync(Folders,id,true,token);RequireProject(source,actor);
        if(parentId==NullableId(source,"parent_folder_id"))return true;
        var seen=new HashSet<long>{id};var current=parentId;
        while(current is{} parent)
        {
            if(!seen.Add(parent))throw new InvalidOperationException("目录不能移动到自身或子目录。");
            var ancestor=await RequiredAsync(Folders,parent,true,token);RequireProject(ancestor,actor);current=NullableId(ancestor,"parent_folder_id");
        }
        await RequireSiblingNameAsync(actor,parentId,SqlRowStore.Text(source,"name"),id,token);
        await store.UpdateAsync(Folders,id,new Dictionary<string,object?>{{"parent_folder_id",parentId}},token);return true;
    },token);

    /// <summary>按固定顺序锁定当前项目目录；仅依赖文件基础表，跨连接移动仍不能产生环。</summary>
    private async Task LockFolderScopeAsync(RuntimeActor actor, CancellationToken token)
    {
        var transaction=store.Session.CurrentTransaction ?? throw new InvalidOperationException("目录范围锁必须加入当前事务。");
        var provider=store.Session.Provider;
        await using var command=transaction.Connection!.CreateCommand(); command.Transaction=transaction;
        var parameter=command.CreateParameter(); parameter.ParameterName="folder_project"; parameter.Value=Project(actor); command.Parameters.Add(parameter);
        var query="SELECT "+provider.QuoteIdentifier("id")+" FROM "+provider.QuoteIdentifier(Folders)
            +" WHERE "+provider.QuoteIdentifier("yz_project_id")+"="+provider.ParameterPlaceholder(parameter.ParameterName)
            +" AND "+provider.QuoteIdentifier("delete_no")+" IS NULL ORDER BY "+provider.QuoteIdentifier("id");
        command.CommandText=provider.BuildSelectForUpdate(query);
        await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) { /* 读取完整结果，所有目录行锁持有至事务结束。 */ }
    }

    /// <summary>上传控件必须来自当前项目可编辑页面，不能用其他项目列配置改变访问级别。</summary>
    private ColumnMetadata? ResolveUploadColumn(string? id, RuntimeActor actor)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var catalog in registry.Catalogs.Where(c => c.YzProjectId == actor.ProjectId))
        {
            var column = catalog.ConfigColumns.SingleOrDefault(c => c.Id == id && c.ControlKind.Equals("FileLibrary", StringComparison.OrdinalIgnoreCase));
            if (column is null) continue;
            foreach (var field in catalog.ConfigPageColumns.Where(f => f.ConfigColumnId == id && f.IsEdit == 1))
            {
                var page = catalog.ConfigPages.Single(p => p.Id == field.ConfigPageId);
                if (page.ConfigTableId != column.ConfigTableId) throw new UnauthorizedAccessException("文件字段与页面所属表不一致。");
                foreach (var operation in new[] { "create", "update" }.Where(op => op == "create" ? page.IsNew == 1 : Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page)))
                {
                    try { authorization.RequirePagePermission(page.Id, operation); return column; }
                    catch (Exception denied) when (denied is UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            }
        }
        throw new UnauthorizedAccessException("没有该文件控件的编辑权限。");
    }

    /// <summary>白名单基础设施查询可显式加入当前事务锁。</summary>
    private async Task<Dictionary<string, object?>> RequiredAsync(string table, long id, bool locked, CancellationToken token)
    {
        if (id <= 0) throw new InvalidOperationException("文件或目录标识无效。");
        var filter = new Dictionary<string, object?> { ["id"] = id };
        var rows = locked ? await store.ReadForUpdateAsync(table, filter, token) : await store.ReadAsync(table, filter, token);
        return rows.SingleOrDefault() ?? throw new UnauthorizedAccessException("文件或目录不存在或不可访问。");
    }

    /// <summary>上传/完成只认实际上传者及当前项目，超级读取身份不能代替别人的上传所有权。</summary>
    private static void RequireOwner(IReadOnlyDictionary<string, object?> row, RuntimeActor actor)
    {
        if (SqlRowStore.Text(row, "yz_project_id") != actor.ProjectId || SqlRowStore.Id(row, "owner_id") != actor.AccountId)
            throw new UnauthorizedAccessException("文件或目录不存在或不可访问。");
    }
    /// <summary>已上传草稿仅上传者或当前超级身份可读；就绪保护文件按真实业务引用读取，所有权不作为永久授权。</summary>
    private async Task RequireReadableAsync(IReadOnlyDictionary<string, object?> row, RuntimeActor actor, CancellationToken token)
    {
        var status=SqlRowStore.Text(row,"status");var mode=SqlRowStore.Text(row,"access_mode");
        if(SqlRowStore.Text(row,"yz_project_id")!=actor.ProjectId||status is not ("uploaded" or "ready")||mode is not ("PUBLIC" or "PROTECTED"))throw new UnauthorizedAccessException("文件不存在或不可访问。");
        if(mode=="PUBLIC")
        {
            if(status=="ready")return;
            throw new UnauthorizedAccessException("公开文件尚未完成上传。");
        }
        if(actor.IsAdministrator||authorization.IsCurrentRealmSuper())return;
        if(status=="uploaded")
        {
            if(SqlRowStore.Id(row,"owner_id")==actor.AccountId)return;
            throw new UnauthorizedAccessException("没有该文件草稿的读取权限。");
        }
        if(await FileReferenceReadAuthorization.CanReadAsync(store,registry,authorization,SqlRowStore.Id(row),actor,token))return;
        throw new UnauthorizedAccessException("没有当前文件的业务记录读取权限。");
    }
    /// <summary>返回固定本地路由，不把存储键、物理路径或客户端 URL 暴露给前端。</summary>
    private FileDescriptor Describe(IReadOnlyDictionary<string, object?> row, bool allowProtectedDraft = false)
    {
        var id = SqlRowStore.Id(row); var ready = SqlRowStore.Text(row, "status") == "ready"; var isPublic = SqlRowStore.Text(row, "access_mode") == "PUBLIC";
        var url = ready || allowProtectedDraft && !isPublic && SqlRowStore.Text(row,"status")=="uploaded" ? $"/admin/sys/file-library/{(isPublic ? "public-content" : "protected-content")}/{id}" : null;
        if (ready && isPublic && options.PublicContentProjectId is { } project)
        {
            // 提示只标识宿主已发布的项目；公开读取仍按真实行重新检查归属与状态。
            if (!long.TryParse(project, NumberStyles.None, CultureInfo.InvariantCulture, out var projectId) || projectId <= 0
                || projectId.ToString(CultureInfo.InvariantCulture) != project) throw new InvalidOperationException("公开文件项目路由配置无效。");
            url += "?projectId=" + project;
        }
        return new(id, SqlRowStore.Text(row, "name"), SqlRowStore.Text(row, "content_type"), SqlRowStore.Id(row, "size"), SqlRowStore.Text(row, "access_mode"), SqlRowStore.Text(row, "status"), url, isPublic ? "REDIRECT_URL" : "LOCAL_STREAM", NullableId(row, "folder_id"), SqlRowStore.Text(row, "sha256"));
    }
    /// <summary>只创建可信根目录，拒绝根目录自身为符号链接。</summary>
    private void EnsureRoot()
    {
        FileValidation.RequireStorageRoot(Root);
        Directory.CreateDirectory(Root);
        FileValidation.RequireStorageRoot(Root);
    }
    /// <summary>物理删除失败不恢复已经失效的数据库记录，留待维护清理。</summary>
    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    /// <summary>数据库项目字段采用 bigint，HTTP 层仍使用字符串。</summary>
    private static long Project(RuntimeActor actor) => long.Parse(actor.ProjectId, CultureInfo.InvariantCulture);
    /// <summary>读取可空关联键，不把空值转换成零标识。</summary>
    private static long? NullableId(IReadOnlyDictionary<string, object?> row, string field) => row.GetValueOrDefault(field) is null ? null : SqlRowStore.Id(row, field);
}
