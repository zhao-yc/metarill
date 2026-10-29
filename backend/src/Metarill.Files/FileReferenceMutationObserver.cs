using Metarill.Core.Contracts;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Files;

/// <summary>只同步 FileLibrary 字段；业务记录与引用校验、增删必须处于同一实际数据库事务。</summary>
public sealed class FileReferenceMutationObserver(SqlRowStore store, FileMetadataContext registry, IRuntimeAuthorization authorization, IIdGenerator ids, FileLibraryOptions? options = null) : IRecordMutationObserver
{
    private sealed record PhysicalDeletionProof(RuntimeActor Actor,string Project,IReadOnlyDictionary<string,object?> Record,IReadOnlyList<long> Parents);
    // 证明仅保存在当前观察器内部并以原事务弱引用隔离；不能由HTTP、上下文字段或另一个观察器伪造。
    private readonly ConditionalWeakTable<DbTransaction,ConcurrentDictionary<(string Page,string Table,long Id),PhysicalDeletionProof>> deletions=new();

    /// <summary>原Service完成范围授权后，物理删除前重新锁真实来源与父行，不信任调用者提交的字段快照。</summary>
    public async Task BeforePhysicalDeleteAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
    {
        if(!context.IsDelete||context.IsCreate||AuthorizationAction(context)!="delete")throw new InvalidOperationException("物理删除准备的操作状态无效。");
        var catalog=registry.Catalogs.SingleOrDefault(item=>item.ConfigPages.Any(page=>page.Id==context.PageId))??throw new UnauthorizedAccessException("来源页面未登记。");
        var page=catalog.ConfigPages.Single(page=>page.Id==context.PageId);var table=catalog.ConfigTables.Single(table=>table.Id==page.ConfigTableId);
        if(table.TableName!=context.TableName||page.IsDelete!=1)throw new UnauthorizedAccessException("来源页面未开放该表删除。");
        var columns=registry.PhysicalColumns(catalog,table);if(!columns.Any(column=>column.ControlKind.Equals("FileLibrary",StringComparison.OrdinalIgnoreCase)))return;
        if(!columns.Any(column=>column.ColumnName=="yz_project_id"))throw new NotSupportedException("物理文件删除要求真实项目归属列。");
        if(columns.Any(column=>column.ColumnName=="delete_no"))throw new InvalidOperationException("逻辑删除不能获取物理删除证明。");
        if(context.Transaction is null||context.Transaction.Connection is null||!ReferenceEquals(context.Transaction,store.Session.CurrentTransaction))throw new InvalidOperationException("物理删除准备要求原业务连接及事务。");
        var actor=authorization.RequireActor();if(actor.ProjectId!=context.ProjectId||catalog.YzProjectId!=context.ProjectId||context.RecordId<=0)throw new UnauthorizedAccessException("物理删除项目范围无效。");RequireMutationPermission(context,authorization);
        var scope=registry.PageScope(catalog,page);var record=await LockRecordAsync(context,columns,cancellationToken,scope);var parents=await LockParentsAsync(context,scope,record,cancellationToken);
        var proofs=deletions.GetValue(context.Transaction,_=>new());if(!proofs.TryAdd((context.PageId,context.TableName,context.RecordId),new(actor,context.ProjectId,new ReadOnlyDictionary<string,object?>(record),Array.AsReadOnly(parents))))throw new InvalidOperationException("同一记录物理删除已经准备，不能覆盖证明。");
    }

    /// <summary>字段省略保持引用不变，显式 null 清空，任何非法引用令整个业务事务回滚。</summary>
    public Task BeforeCommitAsync(RecordMutationContext context, CancellationToken cancellationToken = default)=>BeforeCommitCoreAsync(context,cancellationToken,false);

    /// <summary>按钮文件引用由宿主封闭回调取得当前来源权限，内部标记不对HTTP开放，普通CRUD保持原路径。</summary>
    private async Task BeforeCommitCoreAsync(RecordMutationContext context,CancellationToken cancellationToken,bool currentFileScope)
    {
        var catalog = registry.Catalogs.SingleOrDefault(c => c.ConfigPages.Any(p => p.Id == context.PageId))
            ?? throw new UnauthorizedAccessException("来源页面未登记。");
        var page = catalog.ConfigPages.Single(p => p.Id == context.PageId);
        var table = catalog.ConfigTables.Single(t => t.Id == page.ConfigTableId);
        if (table.TableName != context.TableName) throw new UnauthorizedAccessException("来源页面与业务表不一致。");
        var tableColumns = registry.PhysicalColumns(catalog, table);
        var fileColumns = tableColumns.Where(c => c.ControlKind.Equals("FileLibrary", StringComparison.OrdinalIgnoreCase)).ToArray();
        var affected = context.IsDelete ? fileColumns : fileColumns.Where(c => context.SubmittedFields.ContainsKey(c.ColumnName)).ToArray();
        if (affected.Length == 0) return;
        if (context.Transaction is null || context.Transaction.Connection is null || !ReferenceEquals(context.Transaction, store.Session.CurrentTransaction))
            throw new InvalidOperationException("文件引用必须与业务记录使用同一连接及事务。");
        var actor = authorization.RequireActor();
        if (actor.ProjectId != context.ProjectId || catalog.YzProjectId != actor.ProjectId || context.RecordId <= 0)
            throw new UnauthorizedAccessException("文件引用项目范围无效。");
        RequireMutationPermission(context,authorization);
        if(context.BusinessButtonId is{} buttonId&&!currentFileScope)
        {
            await authorization.WithButtonFileCurrentAsync(context.PageId,buttonId,actor,context.Transaction,
                ()=>BeforeCommitCoreAsync(context,cancellationToken,true),cancellationToken);return;
        }
        // 标准Service已按写前范围授权；写后允许离页，仅复用完整计划及真实父锁，不以新值重做页面筛选。
        var scope = registry.PageScope(catalog,page);
        var proof = context.IsDelete&&!tableColumns.Any(column=>column.ColumnName=="delete_no")?await ConsumePhysicalDeletionAsync(context,actor,cancellationToken):null;
        var record = proof is null?await LockRecordAsync(context, tableColumns, cancellationToken):proof.Record.ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
        var parents=await LockParentsAsync(context, scope, record, cancellationToken);
        if(proof is not null&&!proof.Parents.SequenceEqual(parents))throw new UnauthorizedAccessException("物理删除后实际父范围已变化，文件解绑未提交。");
        var requested = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
        var hiddenEmpty = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in affected)
        {
            var values = context.IsDelete ? [] : FileValidation.ParseFileIds(context.SubmittedFields[column.ColumnName]);
            var actual = record.GetValueOrDefault(column.ColumnName);
            if (!context.IsDelete && !values.SequenceEqual(FileValidation.ParseFileIds(actual)))
                throw new InvalidOperationException("文件引用与实际业务记录字段不一致。");
            if (!context.IsDelete && !catalog.ConfigPageColumns.Any(f => f.ConfigPageId == page.Id && f.ConfigColumnId == column.Id && f.IsEdit == 1))
            {
                // 新增观察上下文包含全表值；隐藏的真实空字段只有在确认无引用后才能无效果跳过。
                if (!context.IsCreate || !EmptyValue(context.SubmittedFields[column.ColumnName]) || !EmptyValue(actual))
                    throw new UnauthorizedAccessException("来源页面未开放该文件字段写入。");
                hiddenEmpty.Add(column.ColumnName); requested[column.ColumnName] = values; continue;
            }
            var policy = FileValidation.Policy(column, options ?? new());
            if (values.Count > policy.MaxCount) throw new InvalidOperationException("文件数量超过控件上限。");
            requested[column.ColumnName] = values;
        }
        // 统一按文件ID排序加锁，与文件删除共用文件行锁，避免引用检查和删除交错。
        var files = new Dictionary<long, Dictionary<string, object?>>();
        foreach (var fileId in requested.Values.SelectMany(v => v).Distinct().OrderBy(id => id))
        {
            var row = (await store.ReadForUpdateAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = fileId }, cancellationToken)).SingleOrDefault();
            if (row is null || SqlRowStore.Text(row, "yz_project_id") != actor.ProjectId || SqlRowStore.Text(row, "status") != "ready")
                throw new UnauthorizedAccessException("引用文件不存在、未完成或属于其他项目。");
            files[fileId] = row;
        }
        var tablePageIds = catalog.ConfigPages.Where(p => p.ConfigTableId == table.Id).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var references = await store.ReadForUpdateAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = context.RecordId, ["yz_project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture) }, cancellationToken);
        if (references.Any(row => hiddenEmpty.Contains(SqlRowStore.Text(row, "field_name"))))
            throw new UnauthorizedAccessException("隐藏文件字段已有引用，不能作为新增空值跳过。");
        foreach (var field in hiddenEmpty) requested.Remove(field);
        affected = affected.Where(column => !hiddenEmpty.Contains(column.ColumnName)).ToArray();
        var current = references.Where(r => tablePageIds.Contains(SqlRowStore.Text(r, "page_id")) && requested.ContainsKey(SqlRowStore.Text(r, "field_name"))).ToArray();
        foreach (var column in affected)
        {
            var policy = FileValidation.Policy(column, options ?? new());
            foreach (var fileId in requested[column.ColumnName])
            {
                var row = files[fileId];
                // 原V8仅首次绑定允许就绪保护文件上传者免记录读取；解绑历史不能恢复首次绑定例外。
                if(!actor.IsAdministrator&&!authorization.IsCurrentRealmSuper()&&SqlRowStore.Text(row,"access_mode")!="PUBLIC")
                {
                    var firstOwned=SqlRowStore.Id(row,"owner_id")==actor.AccountId&&!await HasReferenceHistoryAsync(context,fileId,cancellationToken);
                    if(!firstOwned&&!await FileReferenceReadAuthorization.CanReadAsync(store,registry,authorization,fileId,actor,cancellationToken))
                        throw new UnauthorizedAccessException("没有引用文件的当前业务记录读取权限。");
                }
                if (SqlRowStore.Text(row, "access_mode") != policy.AccessMode || SqlRowStore.Id(row, "size") > policy.MaximumBytes || !policy.Extensions.Contains(Path.GetExtension(SqlRowStore.Text(row, "name"))))
                    throw new InvalidOperationException("文件不符合当前控件的访问级别、大小或扩展名规则。");
            }
        }
        foreach (var old in current)
            await store.UpdateAsync("yz_file_ref", SqlRowStore.Id(old), new Dictionary<string, object?> { ["delete_no"] = BusinessCalendar.Now }, cancellationToken);
        foreach (var (field, fileIds) in requested)
            foreach (var fileId in fileIds)
                await store.InsertAsync("yz_file_ref", new Dictionary<string, object?>
                {
                    ["id"] = ids.NextId(), ["file_id"] = fileId, ["page_id"] = long.Parse(page.Id, CultureInfo.InvariantCulture),
                    ["record_id"] = context.RecordId, ["field_name"] = field, ["yz_project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture),
                    ["insert_time"] = BusinessCalendar.Now, ["insert_manager_id"] = actor.AccountId
                }, cancellationToken);
    }

    /// <summary>文件行锁已持有，再以同事务当前读核对含墓碑引用历史；不使用RR快照或忽略已解绑记录。</summary>
    private async Task<bool> HasReferenceHistoryAsync(RecordMutationContext context,long fileId,CancellationToken token)
    {
        var provider=store.Session.Provider;await using var command=context.Transaction.Connection!.CreateCommand();command.Transaction=context.Transaction;
        command.CommandText=provider.BuildSelectForUpdate("SELECT "+provider.QuoteIdentifier("id")+" FROM "+provider.QuoteIdentifier("yz_file_ref")+" WHERE "+provider.QuoteIdentifier("file_id")+"="+Parameter(command,"history_file",fileId));
        return await command.ExecuteScalarAsync(token) is not null;
    }

    /// <summary>普通入口仍取原动作，业务更新只检查实际按钮；来源状态在任何授权调用前核验。</summary>
    internal static void RequireMutationPermission(RecordMutationContext context,IRuntimeAuthorization authorization)
    {
        var action=AuthorizationAction(context);
        if(context.BusinessButtonId is {} button)authorization.RequireButtonPermission(context.PageId,button);
        else authorization.RequirePagePermission(context.PageId,action);
    }
    /// <summary>保留导入/复制的原页动作；真实身份和页权仍逐次核验，矛盾状态及未知枚举不能转成其它动作。</summary>
    internal static string AuthorizationAction(RecordMutationContext context)
    {
        if(context.BusinessButtonId is not null&&(string.IsNullOrWhiteSpace(context.BusinessButtonId)||context.RecordId<=0||context.IsDelete||context.IsCreate||context.AuthorizationOperation is not (null or RecordMutationOperation.Update)))
            throw new UnauthorizedAccessException("业务按钮文件来源只允许更新实际既有记录。");
        if(context.IsDelete&&context.IsCreate)throw new UnauthorizedAccessException("文件变更状态矛盾。");
        return context.AuthorizationOperation switch
        {
            null => context.IsDelete ? "delete" : context.IsCreate ? "create" : "update",
            RecordMutationOperation.Import when !context.IsDelete => "import",
            RecordMutationOperation.Copy when context.IsCreate&&!context.IsDelete => "copy",
            RecordMutationOperation.Create when context.IsCreate&&!context.IsDelete => "create",
            RecordMutationOperation.Update when !context.IsCreate&&!context.IsDelete => "update",
            RecordMutationOperation.Delete when context.IsDelete => "delete",
            _ => throw new UnauthorizedAccessException("文件变更操作与实际记录状态不一致。")
        };
    }

    /// <summary>父键只取已锁源记录；父锁在文件/引用之前取得，当前读拒绝并发删除后的父行。</summary>
    private async Task<long[]> LockParentsAsync(RecordMutationContext context, FilePageScope scope, IReadOnlyDictionary<string, object?> record, CancellationToken token)
    {
        var parents = scope.Parents.Select(scope => (Scope: scope, Id: scope.Project is null ? FileReferenceScope.CanonicalId(record.GetValueOrDefault(scope.Source.ColumnName)) : FileReferenceScope.PositiveId(record.GetValueOrDefault(scope.Source.ColumnName)))).ToArray();
        var logical=await FileCrossParentScope.ValuesAsync(scope.Crosses,store,record,FileReferenceScope.PositiveId(context.ProjectId),true,token);
        var ids=parents.Select(parent=>parent.Id).Concat(scope.GlobalParents.Select(parent=>FileReferenceScope.CanonicalId(record.GetValueOrDefault(parent.ColumnName)))).Concat(logical).ToArray();
        // 同一页面的@parentId只有一个实际值，多条等值条件不得被解释为互不相干的父身份。
        if (ids.Distinct().Skip(1).Any()) throw new UnauthorizedAccessException("实际记录不满足同一父记录范围。");
        var provider = store.Session.Provider;
        foreach (var parent in parents.DistinctBy(parent => (parent.Scope.Parent.Id, parent.Id)).OrderBy(parent => parent.Scope.Parent.TableName, StringComparer.Ordinal).ThenBy(parent => parent.Id))
        {
            await using var command = context.Transaction.Connection!.CreateCommand(); command.Transaction = context.Transaction;
            var query = "SELECT " + provider.QuoteIdentifier("id") + " FROM " + provider.QuoteIdentifier(parent.Scope.Parent.TableName)
                + " WHERE " + provider.QuoteIdentifier("id") + "=" + Parameter(command, "scope_parent_id", parent.Id);
            if(parent.Scope.Project is{} owner)query+=" AND "+provider.QuoteIdentifier("yz_project_id")+"="+Parameter(command,"scope_project",FileReferenceScope.IdentityValue(owner,FileReferenceScope.PositiveId(context.ProjectId)));
            if (parent.Scope.HasSoftDelete) query += " AND " + provider.QuoteIdentifier("delete_no") + " IS NULL";
            command.CommandText = provider.BuildSelectForUpdate(query);
            if (await command.ExecuteScalarAsync(token) is null) throw new UnauthorizedAccessException("父记录不存在、已删除或不属于当前项目。");
        }
        return ids;
    }

    /// <summary>新增隐藏字段只允许真实null或空JSON数组，不能把损坏文本或非空值当成无效果。</summary>
    private static bool EmptyValue(object? value)
    {
        if (value is null) return true;
        if (value is JsonElement json) return json.ValueKind == JsonValueKind.Null || json.ValueKind == JsonValueKind.Array && json.GetArrayLength() == 0;
        if (value is string text)
        {
            try { using var document = JsonDocument.Parse(text); return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() == 0; }
            catch (JsonException) { return false; }
        }
        return value is System.Collections.ICollection collection && collection.Count == 0;
    }

    /// <summary>实际记录读取使用上下文原事务，表和字段只允许发布元数据中的名称。</summary>
    private async Task<Dictionary<string, object?>> LockRecordAsync(RecordMutationContext context, IReadOnlyList<ColumnMetadata> columns, CancellationToken token,FilePageScope? deletionScope=null)
    {
        var provider = store.Session.Provider;
        var names = columns.Select(c => c.ColumnName).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("id")) throw new InvalidOperationException("业务表没有声明主键。");
        await using var command = context.Transaction.Connection!.CreateCommand(); command.Transaction = context.Transaction;
        var query = "SELECT " + string.Join(",", names.Select(provider.QuoteIdentifier)) + " FROM " + provider.QuoteIdentifier(context.TableName) + " WHERE " + provider.QuoteIdentifier("id") + "=" + Parameter(command, "record_id", context.RecordId);
        if (names.Contains("yz_project_id")) query += " AND " + provider.QuoteIdentifier("yz_project_id") + "=" + Parameter(command, "project", FileReferenceScope.IdentityValue(columns.Single(column=>column.ColumnName=="yz_project_id"),FileReferenceScope.PositiveId(context.ProjectId)));
        if(deletionScope?.Condition is{} condition)query+=" AND "+condition.BindConstants(command,store.Session,"physical_delete");
        if (!context.IsDelete && names.Contains("delete_no")) query += " AND " + provider.QuoteIdentifier("delete_no") + " IS NULL";
        command.CommandText = provider.BuildSelectForUpdate(query);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new UnauthorizedAccessException("实际记录不存在或不在当前项目范围。");
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++) result[reader.GetName(i)] = await reader.IsDBNullAsync(i, token) ? null : reader.GetValue(i);
        if (context.IsDelete && names.Contains("delete_no") && result.GetValueOrDefault("delete_no") is null)
            throw new InvalidOperationException("文件清理要求业务记录已在同一事务中删除。");
        return result;
    }

    /// <summary>缺准备、换身份／事务或记录仍存在时拒绝；真实不存在后一次消费原锁定快照，随后引用仍由原事务写入。</summary>
    private async Task<PhysicalDeletionProof> ConsumePhysicalDeletionAsync(RecordMutationContext context,RuntimeActor actor,CancellationToken token)
    {
        var key=(context.PageId,context.TableName,context.RecordId);
        if(!deletions.TryGetValue(context.Transaction,out var proofs)||!proofs.TryGetValue(key,out var proof)||proof.Actor!=actor||proof.Project!=context.ProjectId)throw new UnauthorizedAccessException("物理删除缺少同一身份及事务的真实记录证明。");
        var provider=store.Session.Provider;await using var command=context.Transaction.Connection!.CreateCommand();command.Transaction=context.Transaction;
        // 只按真实主键检查任何项目的行都已不存在，不能用项目筛选掩盖被重建或移走的记录。
        command.CommandText=provider.BuildSelectForUpdate("SELECT "+provider.QuoteIdentifier("id")+" FROM "+provider.QuoteIdentifier(context.TableName)+" WHERE "+provider.QuoteIdentifier("id")+"="+Parameter(command,"deleted_id",context.RecordId));
        if(await command.ExecuteScalarAsync(token) is not null)throw new InvalidOperationException("业务记录尚未物理删除，不能清理文件引用。");
        if(!proofs.TryRemove(key,out _))throw new InvalidOperationException("物理删除证明已经消费。");return proof;
    }

    /// <summary>参数名称由实现固定，值通过数据库参数绑定。</summary>
    private string Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
        return store.Session.Provider.ParameterPlaceholder(name);
    }
}
