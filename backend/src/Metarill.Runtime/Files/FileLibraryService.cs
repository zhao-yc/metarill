using System.Globalization;
using System.Security.Cryptography;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Files;

/// <summary>本地文件库只使用固定磁盘根与服务端随机键，公开与受保护读取共用真实状态检查。</summary>
public sealed class FileLibraryService(SqlRowStore store, MetadataRegistry registry, IIdGenerator ids, IRuntimeAuthorization authorization, FileLibraryOptions options)
{
    private const string Files = "yz_file_library";
    private const string Folders = "yz_file_library_folder";
    private const string References = "yz_file_ref";
    private string Root => Path.GetFullPath(options.RootDirectory);

    /// <summary>创建待上传记录，供应商、对象键和访问级别均由服务端决定。</summary>
    public Task<FileUploadPlan> PlanUploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default) => store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor();
        FileValidation.RequireName(request.OriginalName);
        var column = ResolveUploadColumn(request.ConfigColumnId, actor);
        var policy = FileValidation.Policy(column, options);
        if (request.SizeBytes < 0 || request.SizeBytes > policy.MaximumBytes) throw new InvalidOperationException("文件大小超过服务端或控件上限。");
        if (!policy.Extensions.Contains(Path.GetExtension(request.OriginalName))) throw new InvalidOperationException("文件扩展名不在允许范围。");
        if (request.FolderId is { } folder) RequireOwner(await RequiredAsync(Folders, folder, true, cancellationToken), actor);
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

    /// <summary>持有文件记录锁完成有界流写入和摘要核验，上传成功仍须显式完成后才可读取。</summary>
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

    /// <summary>受保护文件首期只向同项目所有者或管理员解析；提供页或记录标识不会自动提权。</summary>
    public async Task<FileDescriptor> ResolveAsync(long id, string? pageId = null, long? recordId = null, CancellationToken cancellationToken = default)
    {
        var actor = authorization.RequireActor(); var row = await RequiredAsync(Files, id, false, cancellationToken);
        RequireReadable(row, actor); return Describe(row);
    }

    /// <summary>返回可寻址本地流供 ASP.NET Core 启用 Range；绝不代理或重定向远端地址。</summary>
    public async Task<FileContentHandle> OpenContentAsync(long id, bool publicOnly = false, string? pageId = null, long? recordId = null, CancellationToken cancellationToken = default)
    {
        var row = await RequiredAsync(Files, id, false, cancellationToken);
        if (publicOnly)
        {
            if (SqlRowStore.Text(row, "status") != "ready" || SqlRowStore.Text(row, "access_mode") != "PUBLIC") throw new UnauthorizedAccessException("文件不存在或不可公开访问。");
        }
        else RequireReadable(row, authorization.RequireActor());
        var path = FileValidation.StoragePath(Root, SqlRowStore.Text(row, "storage_key"));
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != SqlRowStore.Id(row, "size")) { await stream.DisposeAsync(); throw new InvalidOperationException("文件对象大小异常。"); }
        return new FileContentHandle(stream, FileValidation.ContentType(SqlRowStore.Text(row, "name")), SqlRowStore.Text(row, "name"), stream.Length, SqlRowStore.Text(row, "sha256"));
    }

    /// <summary>删除前锁定文件并确认无有效引用；数据库先失效，磁盘清理失败明确返回。</summary>
    public async Task<FileDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var path = await store.Session.InTransactionAsync(async () =>
        {
            var actor = authorization.RequireActor(); var row = await RequiredAsync(Files, id, true, cancellationToken); RequireOwner(row, actor);
            if ((await store.ReadForUpdateAsync(References, new Dictionary<string, object?> { ["file_id"] = id }, cancellationToken)).Count > 0) throw new InvalidOperationException("文件仍被业务记录引用，不能删除。");
            await store.UpdateAsync(Files, id, new Dictionary<string, object?> { ["status"] = "deleted", ["delete_no"] = DateTime.UtcNow }, cancellationToken);
            return FileValidation.StoragePath(Root, SqlRowStore.Text(row, "storage_key"));
        }, cancellationToken);
        return new FileDeleteResult(id, TryDelete(path));
    }

    /// <summary>文件列表仅包含当前项目且归本人所有；管理员可查看当前项目。</summary>
    public async Task<IReadOnlyList<FileDescriptor>> ListAsync(long? folderId = null, CancellationToken cancellationToken = default)
    {
        var actor = authorization.RequireActor(); var filters = OwnedFilters(actor);
        filters["folder_id"] = folderId;
        return (await store.ReadAsync(Files, filters, cancellationToken)).OrderByDescending(x => SqlRowStore.Id(x)).Select(Describe).ToArray();
    }

    /// <summary>创建目录时锁定父目录，防止与父目录删除发生竞态。</summary>
    public Task<FileFolder> CreateFolderAsync(string name, long? parentId = null, CancellationToken cancellationToken = default) => store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor(); FileValidation.RequireName(name);
        if (name.Length > 100) throw new InvalidOperationException("目录名称最多100字符。");
        if (parentId is { } parent) RequireOwner(await RequiredAsync(Folders, parent, true, cancellationToken), actor);
        var id = ids.NextId();
        await store.InsertAsync(Folders, new Dictionary<string, object?> { ["id"] = id, ["name"] = name, ["parent_folder_id"] = parentId, ["owner_id"] = actor.AccountId, ["yz_project_id"] = Project(actor), ["insert_time"] = DateTime.UtcNow }, cancellationToken);
        return new FileFolder(id, name, parentId);
    }, cancellationToken);

    /// <summary>目录树按同项目所有权过滤，不把客户端指定目录作为权限。</summary>
    public async Task<IReadOnlyList<FileFolder>> ListFoldersAsync(long? parentId = null, CancellationToken cancellationToken = default)
    {
        var actor = authorization.RequireActor(); var filters = OwnedFilters(actor); filters["parent_folder_id"] = parentId;
        return (await store.ReadAsync(Folders, filters, cancellationToken)).Select(r => new FileFolder(SqlRowStore.Id(r), SqlRowStore.Text(r, "name"), NullableId(r, "parent_folder_id"))).ToArray();
    }

    /// <summary>首期只删除空目录，不提供隐式递归删除。</summary>
    public async Task DeleteFolderAsync(long id, CancellationToken cancellationToken = default) => await store.Session.InTransactionAsync(async () =>
    {
        var actor = authorization.RequireActor(); RequireOwner(await RequiredAsync(Folders, id, true, cancellationToken), actor);
        if ((await store.ReadForUpdateAsync(Folders, new Dictionary<string, object?> { ["parent_folder_id"] = id }, cancellationToken)).Count > 0 ||
            (await store.ReadForUpdateAsync(Files, new Dictionary<string, object?> { ["folder_id"] = id }, cancellationToken)).Count > 0)
            throw new InvalidOperationException("目录不为空，不能删除。");
        await store.UpdateAsync(Folders, id, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow }, cancellationToken);
        return true;
    }, cancellationToken);

    /// <summary>显示名可修改，扩展名保持原类型以避免把已上传内容重新解释成可执行类型。</summary>
    public Task RenameAsync(long id,string name,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        FileValidation.RequireName(name);
        var actor=authorization.RequireActor();var row=await RequiredAsync(Files,id,true,token);RequireOwner(row,actor);
        if(!string.Equals(Path.GetExtension(name),Path.GetExtension(SqlRowStore.Text(row,"name")),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("更名时不能改变文件扩展名。");
        await store.UpdateAsync(Files,id,new Dictionary<string,object?>{{"name",name},{"update_time",DateTime.UtcNow}},token);return true;
    },token);

    /// <summary>移动文件先锁定目标目录，和空目录删除共用同一行锁。</summary>
    public Task MoveAsync(long id,long? targetFolderId,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        var actor=authorization.RequireActor();
        if(targetFolderId is{} folder)RequireOwner(await RequiredAsync(Folders,folder,true,token),actor);
        RequireOwner(await RequiredAsync(Files,id,true,token),actor);
        await store.UpdateAsync(Files,id,new Dictionary<string,object?>{{"folder_id",targetFolderId},{"update_time",DateTime.UtcNow}},token);return true;
    },token);

    /// <summary>目录更名只修改显示字段，保留原有项目及所有者。</summary>
    public Task RenameFolderAsync(long id,string name,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        FileValidation.RequireName(name);if(name.Length>100)throw new ArgumentException("目录名称最多100字符。");
        RequireOwner(await RequiredAsync(Folders,id,true,token),authorization.RequireActor());
        await store.UpdateAsync(Folders,id,new Dictionary<string,object?>{{"name",name}},token);return true;
    },token);

    /// <summary>目录移动串行锁定项目，遍历父链拒绝环；不能跨所有者或跨项目移动。</summary>
    public Task MoveFolderAsync(long id,long? parentId,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        var actor=authorization.RequireActor();
        await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",Project(actor)}},token);
        RequireOwner(await RequiredAsync(Folders,id,true,token),actor);
        var seen=new HashSet<long>{id};var current=parentId;
        while(current is{} parent)
        {
            if(!seen.Add(parent))throw new InvalidOperationException("目录不能移动到自身或子目录。");
            var ancestor=await RequiredAsync(Folders,parent,true,token);RequireOwner(ancestor,actor);current=NullableId(ancestor,"parent_folder_id");
        }
        await store.UpdateAsync(Folders,id,new Dictionary<string,object?>{{"parent_folder_id",parentId}},token);return true;
    },token);

    /// <summary>上传控件必须来自当前项目可编辑页面，不能用其他项目列配置改变访问级别。</summary>
    private ColumnMetadata? ResolveUploadColumn(string? id, RuntimeActor actor)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var catalog in new[] { registry.Platform, registry.Demo }.Where(c => c.YzProjectId == actor.ProjectId))
        {
            var column = catalog.ConfigColumns.SingleOrDefault(c => c.Id == id && c.ControlKind.Equals("FileLibrary", StringComparison.OrdinalIgnoreCase));
            if (column is null) continue;
            foreach (var field in catalog.ConfigPageColumns.Where(f => f.ConfigColumnId == id && f.IsEdit == 1))
            {
                var page = catalog.ConfigPages.Single(p => p.Id == field.ConfigPageId);
                if (page.ConfigTableId != column.ConfigTableId) throw new UnauthorizedAccessException("文件字段与页面所属表不一致。");
                foreach (var operation in new[] { "create", "update" }.Where(op => op == "create" ? page.IsNew == 1 : page.IsSave == 1))
                {
                    try { authorization.RequirePagePermission(page.Id, operation); return column; }
                    catch (UnauthorizedAccessException) { }
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

    /// <summary>项目和所有者均来自服务端身份，管理员也必须在当前项目中操作。</summary>
    private static void RequireOwner(IReadOnlyDictionary<string, object?> row, RuntimeActor actor)
    {
        if (SqlRowStore.Text(row, "yz_project_id") != actor.ProjectId || !actor.IsAdministrator && SqlRowStore.Id(row, "owner_id") != actor.AccountId)
            throw new UnauthorizedAccessException("文件或目录不存在或不可访问。");
    }
    /// <summary>公开状态和授权读取规则统一处理，未完成对象不可解析。</summary>
    private static void RequireReadable(IReadOnlyDictionary<string, object?> row, RuntimeActor actor)
    {
        if (SqlRowStore.Text(row, "status") != "ready" || SqlRowStore.Text(row, "yz_project_id") != actor.ProjectId) throw new UnauthorizedAccessException("文件不存在或不可访问。");
        if (SqlRowStore.Text(row, "access_mode") != "PUBLIC") RequireOwner(row, actor);
    }
    /// <summary>返回固定本地路由，不把存储键、物理路径或客户端 URL 暴露给前端。</summary>
    private static FileDescriptor Describe(IReadOnlyDictionary<string, object?> row)
    {
        var id = SqlRowStore.Id(row); var ready = SqlRowStore.Text(row, "status") == "ready"; var isPublic = SqlRowStore.Text(row, "access_mode") == "PUBLIC";
        var url = ready ? $"/admin/sys/file-library/{(isPublic ? "public-content" : "protected-content")}/{id}" : null;
        return new(id, SqlRowStore.Text(row, "name"), SqlRowStore.Text(row, "content_type"), SqlRowStore.Id(row, "size"), SqlRowStore.Text(row, "access_mode"), SqlRowStore.Text(row, "status"), url, isPublic ? "REDIRECT_URL" : "LOCAL_STREAM", NullableId(row, "folder_id"), SqlRowStore.Text(row, "sha256"));
    }
    /// <summary>只创建可信根目录，拒绝根目录自身为符号链接。</summary>
    private void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
        if (File.GetAttributes(Root).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("文件根目录不能是符号链接。");
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
    /// <summary>基础设施列表统一施加项目和所有权条件。</summary>
    private static Dictionary<string, object?> OwnedFilters(RuntimeActor actor)
    {
        var result = new Dictionary<string, object?> { ["yz_project_id"] = Project(actor) };
        if (!actor.IsAdministrator) result["owner_id"] = actor.AccountId;
        return result;
    }
    /// <summary>读取可空关联键，不把空值转换成零标识。</summary>
    private static long? NullableId(IReadOnlyDictionary<string, object?> row, string field) => row.GetValueOrDefault(field) is null ? null : SqlRowStore.Id(row, field);
}

