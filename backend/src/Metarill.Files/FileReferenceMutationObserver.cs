using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Files;

/// <summary>只同步 FileLibrary 字段；业务记录与引用校验、增删必须处于同一实际数据库事务。</summary>
public sealed class FileReferenceMutationObserver(SqlRowStore store, FileMetadataContext registry, IRuntimeAuthorization authorization, IIdGenerator ids, FileLibraryOptions? options = null) : IRecordMutationObserver
{
    /// <summary>字段省略保持引用不变，显式 null 清空，任何非法引用令整个业务事务回滚。</summary>
    public async Task BeforeCommitAsync(RecordMutationContext context, CancellationToken cancellationToken = default)
    {
        var catalog = registry.Catalogs.SingleOrDefault(c => c.ConfigPages.Any(p => p.Id == context.PageId))
            ?? throw new UnauthorizedAccessException("来源页面未登记。");
        var page = catalog.ConfigPages.Single(p => p.Id == context.PageId);
        var table = catalog.ConfigTables.Single(t => t.Id == page.ConfigTableId);
        if (table.TableName != context.TableName) throw new UnauthorizedAccessException("来源页面与业务表不一致。");
        var tableColumns = catalog.ConfigColumns.Where(c => c.ConfigTableId == table.Id && c.IsVirtualColumn == 0).ToArray();
        var fileColumns = tableColumns.Where(c => c.ControlKind.Equals("FileLibrary", StringComparison.OrdinalIgnoreCase)).ToArray();
        var affected = context.IsDelete ? fileColumns : fileColumns.Where(c => context.SubmittedFields.ContainsKey(c.ColumnName)).ToArray();
        if (affected.Length == 0) return;
        if (context.Transaction.Connection is null || !ReferenceEquals(context.Transaction, store.Session.CurrentTransaction))
            throw new InvalidOperationException("文件引用必须与业务记录使用同一连接及事务。");
        var actor = authorization.RequireActor();
        if (actor.ProjectId != context.ProjectId || catalog.YzProjectId != actor.ProjectId || context.RecordId <= 0)
            throw new UnauthorizedAccessException("文件引用项目范围无效。");
        authorization.RequirePagePermission(page.Id, context.IsDelete ? "delete" : context.IsCreate ? "create" : "update");
        RequireSupportedScope(page);
        if (!context.IsDelete)
            foreach (var column in affected)
                if (!catalog.ConfigPageColumns.Any(f => f.ConfigPageId == page.Id && f.ConfigColumnId == column.Id && f.IsEdit == 1))
                    throw new UnauthorizedAccessException("来源页面未开放该文件字段写入。");
        var record = await LockRecordAsync(context, tableColumns, cancellationToken);
        var requested = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
        foreach (var column in affected)
        {
            var values = context.IsDelete ? [] : FileValidation.ParseFileIds(context.SubmittedFields[column.ColumnName]);
            if (!context.IsDelete && !values.SequenceEqual(FileValidation.ParseFileIds(record.GetValueOrDefault(column.ColumnName))))
                throw new InvalidOperationException("文件引用与实际业务记录字段不一致。");
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
        var current = (await store.ReadForUpdateAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = context.RecordId, ["yz_project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture) }, cancellationToken))
            .Where(r => tablePageIds.Contains(SqlRowStore.Text(r, "page_id")) && requested.ContainsKey(SqlRowStore.Text(r, "field_name"))).ToArray();
        foreach (var column in affected)
        {
            var policy = FileValidation.Policy(column, options ?? new());
            foreach (var fileId in requested[column.ColumnName])
            {
                var row = files[fileId];
                var alreadyAttached = current.Any(r => SqlRowStore.Id(r, "file_id") == fileId && SqlRowStore.Text(r, "field_name") == column.ColumnName);
                if (!actor.IsAdministrator && SqlRowStore.Id(row, "owner_id") != actor.AccountId && !alreadyAttached)
                    throw new UnauthorizedAccessException("不能通过猜测标识引用其他人的文件。");
                if (SqlRowStore.Text(row, "access_mode") != policy.AccessMode || SqlRowStore.Id(row, "size") > policy.MaximumBytes || !policy.Extensions.Contains(Path.GetExtension(SqlRowStore.Text(row, "name"))))
                    throw new InvalidOperationException("文件不符合当前控件的访问级别、大小或扩展名规则。");
            }
        }
        foreach (var old in current)
            await store.UpdateAsync("yz_file_ref", SqlRowStore.Id(old), new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow }, cancellationToken);
        foreach (var (field, fileIds) in requested)
            foreach (var fileId in fileIds)
                await store.InsertAsync("yz_file_ref", new Dictionary<string, object?>
                {
                    ["id"] = ids.NextId(), ["file_id"] = fileId, ["page_id"] = long.Parse(page.Id, CultureInfo.InvariantCulture),
                    ["record_id"] = context.RecordId, ["field_name"] = field, ["yz_project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture),
                    ["insert_time"] = DateTime.UtcNow, ["insert_manager_id"] = actor.AccountId
                }, cancellationToken);
    }

    /// <summary>上下文未提供父记录范围证明；无法完整执行的页面范围明确拒绝。</summary>
    private static void RequireSupportedScope(PageMetadata page)
    {
        if (string.IsNullOrWhiteSpace(page.WhereSql)) return;
        foreach (var clause in Regex.Split(page.WhereSql.Trim(), @"\s+AND\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            if (!Regex.IsMatch(clause.Trim(), @"^`?yz_project_id`?\s*=\s*@current_yz_project_id$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new NotSupportedException("该页面包含尚未证明的记录范围，文件引用同步拒绝执行。");
    }

    /// <summary>实际记录读取使用上下文原事务，表和字段只允许发布元数据中的名称。</summary>
    private async Task<Dictionary<string, object?>> LockRecordAsync(RecordMutationContext context, IReadOnlyList<ColumnMetadata> columns, CancellationToken token)
    {
        var provider = store.Session.Provider;
        var names = columns.Select(c => c.ColumnName).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("id")) throw new InvalidOperationException("业务表没有声明主键。");
        await using var command = context.Transaction.Connection!.CreateCommand(); command.Transaction = context.Transaction;
        var query = "SELECT " + string.Join(",", names.Select(provider.QuoteIdentifier)) + " FROM " + provider.QuoteIdentifier(context.TableName) + " WHERE " + provider.QuoteIdentifier("id") + "=" + Parameter(command, "record_id", context.RecordId);
        if (names.Contains("yz_project_id")) query += " AND " + provider.QuoteIdentifier("yz_project_id") + "=" + Parameter(command, "project", long.Parse(context.ProjectId, CultureInfo.InvariantCulture));
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

    /// <summary>参数名称由实现固定，值通过数据库参数绑定。</summary>
    private string Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
        return store.Session.Provider.ParameterPlaceholder(name);
    }
}
