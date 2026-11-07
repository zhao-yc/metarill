using Metarill.Core.Contracts;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

public sealed record IndexColumnOption(string ConfigColumnId, string ColumnName, string Label, bool SystemColumn);
public sealed record IndexWorkbenchColumn(string Id, string ConfigColumnId, string ColumnName, int SortNo, string SortDirection);
public sealed record IndexWorkbenchIndex(string Id, string IndexName, string IndexType, int IsEnabled, IReadOnlyList<IndexWorkbenchColumn> Columns);
public sealed record IndexWorkbenchSnapshot(string ConfigTableId, string ConfigTableName, string TableName, IReadOnlyList<IndexColumnOption> ColumnOptions, IReadOnlyList<IndexWorkbenchIndex> Indexes, string Revision);

/// <summary>沿用索引工作台完整列表协议；只原子维护托管元数据，绝不执行业务库DDL。</summary>
public sealed class IndexWorkbenchBusiness(SqlRowStore store, HttpRuntimeAuthorization authorization, IIdGenerator ids)
{
    private sealed record Scope(Dictionary<string, object?> Project, Dictionary<string, object?> Table, List<Dictionary<string, object?>> Columns,
        List<Dictionary<string, object?>> Indexes, List<Dictionary<string, object?>> IndexColumns, IReadOnlyList<Dictionary<string,object?>> IndexableColumns);

    /// <summary>读取也使用项目到表的固定聚合锁，版本和完整列表来自同一代记录。</summary>
    public Task<IndexWorkbenchSnapshot> SnapshotAsync(JsonElement request, CancellationToken cancellationToken = default)
    {
        MetadataWorkbenchRules.Object(request, "configTableId"); var tableId = MetadataWorkbenchRules.Id(request, "configTableId");
        RequireAdministrator();
        return store.Session.InTransactionAsync(async () => Snapshot(await ReadScopeAsync(tableId, cancellationToken)), cancellationToken);
    }

    /// <summary>先完整校验所有索引及其字段再写入；任何一项失败均保留整个原聚合。</summary>
    public Task<IndexWorkbenchSnapshot> SaveAsync(JsonElement request, string? revision = null, CancellationToken cancellationToken = default)
    {
        var input = IndexWorkbenchRules.Parse(request, revision); var actor = RequireAdministrator();
        return store.Session.InTransactionAsync(async () =>
        {
            var scope = await ReadScopeAsync(input.TableId, cancellationToken);
            if (input.Revision != Revision(scope)) throw new MetadataRevisionConflictException();
            var existing = scope.Indexes.Where(IsManaged).ToDictionary(row => SqlRowStore.Id(row));
            var names = scope.Indexes.Where(row => !IsManaged(row)).Select(row => SqlRowStore.Text(row, "index_name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var columns = scope.IndexableColumns.ToDictionary(row => SqlRowStore.Id(row));
            var byName = columns.Values.ToDictionary(row => SqlRowStore.Text(row, "column_name"), StringComparer.OrdinalIgnoreCase);
            var childRows = scope.IndexColumns.ToDictionary(row => SqlRowStore.Id(row));
            var usedIndexes = new HashSet<long>(); var usedChildren = new HashSet<long>();
            var normalized = new List<(IndexWorkbenchRules.Index Index, List<(IndexWorkbenchRules.Column Input, Dictionary<string, object?> Physical)> Columns)>();
            foreach (var index in input.Indexes)
            {
                if (!names.Add(index.Name)) throw new InvalidOperationException("同一表内索引名不能重复（不区分大小写）。");
                if (index.Id is { } indexId && (!existing.ContainsKey(indexId) || !usedIndexes.Add(indexId))) throw new InvalidOperationException("索引ID重复或不属于当前表的托管索引。");
                var currentColumns = new List<(IndexWorkbenchRules.Column, Dictionary<string, object?>)>(); var physicalIds = new HashSet<long>();
                foreach (var column in index.Columns)
                {
                    if (column.Id is { } childId && (!usedChildren.Add(childId) || index.Id is null || !childRows.TryGetValue(childId, out var child) || SqlRowStore.Id(child, "config_index_id") != index.Id))
                        throw new InvalidOperationException("索引字段ID重复或不属于当前索引，不能跨索引移动。");
                    Dictionary<string, object?>? physical;
                    if (column.ColumnId is { } columnId)
                    {
                        if (!columns.TryGetValue(columnId, out physical)) throw new InvalidOperationException("索引只能引用当前表的合法物理字段。");
                        if (!SqlRowStore.Text(physical!, "column_name").Equals(column.Name, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("物理列名与配置字段ID不一致。");
                    }
                    else if (!byName.TryGetValue(column.Name, out physical)) throw new InvalidOperationException("系统列也必须在元数据中显式声明，不能凭名称构造物理列。");
                    if (!physicalIds.Add(SqlRowStore.Id(physical!))) throw new InvalidOperationException("同一索引不能重复引用字段。");
                    currentColumns.Add((column, physical!));
                }
                normalized.Add((index, currentColumns));
            }
            foreach (var (index, references) in normalized)
            {
                var id = index.Id ?? ids.NextId();
                var values = Audit(new() { ["index_name"] = index.Name, ["index_type"] = index.Type, ["is_unique"] = index.Type == "UNIQUE" ? 1 : 0,
                    ["is_enabled"] = index.Enabled, ["config_table_id"] = input.TableId, ["config_table_name"] = SqlRowStore.Text(scope.Table, "name"),
                    ["yz_project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture), ["yz_project_name"] = SqlRowStore.Text(scope.Project, "name") }, actor);
                if (index.Id is null) { values["id"] = id; Created(values, actor); await store.InsertAsync("config_index", values, cancellationToken); }
                else await store.UpdateAsync("config_index", id, values, cancellationToken);
                foreach (var (column, physical) in references)
                {
                    var childId = column.Id ?? ids.NextId();
                    var child = Audit(new() { ["config_index_id"] = id, ["config_index_index_name"] = index.Name, ["config_column_id"] = SqlRowStore.Id(physical),
                        ["config_column_name"] = SqlRowStore.Text(physical, "name"), ["column_name"] = SqlRowStore.Text(physical!, "column_name"), ["sort_no"] = column.SortNo, ["sort_direction"] = column.Direction,
                        ["yz_project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture), ["yz_project_name"] = SqlRowStore.Text(scope.Project, "name") }, actor);
                    if (column.Id is null) { child["id"] = childId; Created(child, actor); await store.InsertAsync("config_index_column", child, cancellationToken); }
                    else await store.UpdateAsync("config_index_column", childId, child, cancellationToken);
                }
            }
            // 完整替换只作用于托管集合，ux_*等平台基础设施索引不由此页面删除或改名。
            foreach (var row in scope.IndexColumns.Where(row => existing.ContainsKey(SqlRowStore.Id(row, "config_index_id")) && !usedChildren.Contains(SqlRowStore.Id(row))))
                await store.UpdateAsync("config_index_column", SqlRowStore.Id(row), Audit(new() { ["delete_no"] = BusinessCalendar.Now }, actor), cancellationToken);
            foreach (var row in existing.Values.Where(row => !usedIndexes.Contains(SqlRowStore.Id(row))))
                await store.UpdateAsync("config_index", SqlRowStore.Id(row), Audit(new() { ["delete_no"] = BusinessCalendar.Now }, actor), cancellationToken);
            return Snapshot(await ReadScopeAsync(input.TableId, cancellationToken));
        }, cancellationToken);
    }

    /// <summary>只接受真实认证上下文中的当前项目管理员；Agent不能借内部调用绕过能力白名单。</summary>
    private RuntimeActor RequireAdministrator()
    { var identity = authorization.RequireIdentity(); if (identity.GrantId is not null) throw new UnauthorizedAccessException("索引设计需要网页登录管理员身份。"); return authorization.RequireAdministrator(); }
    /// <summary>项目→表→原字段→索引→索引字段统一锁序；归属异常时不继续修补或静默跨项目。</summary>
    private async Task<Scope> ReadScopeAsync(long tableId, CancellationToken cancellationToken)
    {
        var projectId = long.Parse(RequireAdministrator().ProjectId, CultureInfo.InvariantCulture);
        var project = (await store.ReadForUpdateAsync("yz_project", new Dictionary<string, object?> { ["id"] = projectId }, cancellationToken)).SingleOrDefault() ?? throw new UnauthorizedAccessException("当前项目不存在。");
        var table = (await store.ReadForUpdateAsync("config_table", new Dictionary<string, object?> { ["id"] = tableId, ["yz_project_id"] = projectId }, cancellationToken)).SingleOrDefault() ?? throw new UnauthorizedAccessException("配置表不存在或不属于当前项目。");
        if (Flag(table, "is_view") || Flag(table, "is_function_table")) throw new InvalidOperationException("视图或功能表不能配置实体索引。");
        Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(SqlRowStore.Text(table, "table_name"));
        var columns = await store.ReadForUpdateAsync("config_column", new Dictionary<string, object?> { ["config_table_id"] = tableId }, cancellationToken);
        var indexes = await store.ReadForUpdateAsync("config_index", new Dictionary<string, object?> { ["config_table_id"] = tableId }, cancellationToken);
        var children = new List<Dictionary<string, object?>>();
        foreach (var index in indexes.OrderBy(row => SqlRowStore.Id(row))) children.AddRange(await store.ReadForUpdateAsync("config_index_column", new Dictionary<string, object?> { ["config_index_id"] = SqlRowStore.Id(index) }, cancellationToken));
        if (columns.Concat(indexes).Concat(children).Any(row => SqlRowStore.Id(row, "yz_project_id") != projectId)) throw new InvalidOperationException("索引聚合包含项目归属不一致的记录。");
        foreach (var column in columns) Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(SqlRowStore.Text(column, "column_name"));
        if (columns.GroupBy(row => SqlRowStore.Text(row, "column_name"), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) throw new InvalidOperationException("同表物理列名重复，请先修复元数据。");
        return new(project, table, columns, indexes, children, CompileIndexableColumns(table,columns));
    }
    /// <summary>候选只来自显式物理列；虚拟、联接和布局控件不属于可持久化本表字段。</summary>
    private static bool IsPhysical(Dictionary<string, object?> row) => !Flag(row, "is_virtual_column") && !Flag(row, "is_join_column") && SqlRowStore.Text(row, "control_kind") is not ("Title" or "Tab");
    /// <summary>候选与保存共用完整表的存储计划；同时保留原索引禁止联接/布局的稳定列限制。</summary>
    internal static IReadOnlyList<Dictionary<string,object?>> CompileIndexableColumns(Dictionary<string,object?> table,IReadOnlyList<Dictionary<string,object?>> rows)
    {
        var definition=MetadataRepository.DecodeRow<TableMetadata>(table);var columns=rows.Select(MetadataRepository.DecodeRow<ColumnMetadata>).ToArray();var storage=MetadataStoragePolicy.Compile(definition,columns);var physical=storage.PhysicalColumns.Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
        return rows.Where(row=>IsPhysical(row)&&physical.Contains(SqlRowStore.Text(row,"id"))).ToArray();
    }
    private static bool IsManaged(Dictionary<string, object?> row) => IndexWorkbenchRules.Managed(SqlRowStore.Text(row, "index_name"));
    private static bool Flag(IReadOnlyDictionary<string, object?> row, string field) => MetadataWorkbenchRules.StoredFlag(row.GetValueOrDefault(field)) != 0;
    /// <summary>响应投影保持原UI形状，系统列同样提供真实ID避免DDL引用悬空。</summary>
    private static IndexWorkbenchSnapshot Snapshot(Scope scope)
    {
        var options = scope.IndexableColumns.OrderBy(row => Convert.ToInt32(row.GetValueOrDefault("sort_no") ?? 0, CultureInfo.InvariantCulture)).ThenBy(row => SqlRowStore.Id(row))
            .Select(row => new IndexColumnOption(SqlRowStore.Text(row, "id"), SqlRowStore.Text(row, "column_name"), SqlRowStore.Text(row, "name") + "（" + SqlRowStore.Text(row, "column_name") + "）", IndexWorkbenchRules.SystemColumn(SqlRowStore.Text(row, "column_name")))).ToArray();
        var indexes = scope.Indexes.Where(IsManaged).OrderBy(row => SqlRowStore.Text(row, "index_name"), StringComparer.Ordinal).ThenBy(row => SqlRowStore.Id(row))
            .Select(row => new IndexWorkbenchIndex(SqlRowStore.Text(row, "id"), SqlRowStore.Text(row, "index_name"), SqlRowStore.Text(row, "index_type"), Convert.ToInt32(row["is_enabled"], CultureInfo.InvariantCulture),
                scope.IndexColumns.Where(child => SqlRowStore.Id(child, "config_index_id") == SqlRowStore.Id(row)).OrderBy(child => Convert.ToInt32(child["sort_no"], CultureInfo.InvariantCulture)).ThenBy(child => SqlRowStore.Id(child))
                .Select(child => new IndexWorkbenchColumn(SqlRowStore.Text(child, "id"), SqlRowStore.Text(child, "config_column_id"), SqlRowStore.Text(child, "column_name"), Convert.ToInt32(child["sort_no"], CultureInfo.InvariantCulture), SqlRowStore.Text(child, "sort_direction"))).ToArray())).ToArray();
        return new(SqlRowStore.Text(scope.Table, "id"), SqlRowStore.Text(scope.Table, "name"), SqlRowStore.Text(scope.Table, "table_name"), options, indexes, Revision(scope));
    }
    /// <summary>列定义与所有索引（含非托管）都影响版本，防止旧页面覆盖其他编辑器的更新。</summary>
    private static string Revision(Scope scope) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { scope.Project, scope.Table }.Concat(scope.Columns.OrderBy(row => SqlRowStore.Id(row))).Concat(scope.Indexes.OrderBy(row => SqlRowStore.Id(row))).Concat(scope.IndexColumns.OrderBy(row => SqlRowStore.Id(row)))
        .Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value)), JsonDefaults.Options))).ToLowerInvariant();
    /// <summary>审计值由服务器身份生成，不接受客户端提交账号、项目或时间。</summary>
    private static Dictionary<string, object?> Audit(Dictionary<string, object?> values, RuntimeActor actor)
    { values["update_time"] = BusinessCalendar.Now; values["update_manager_id"] = actor.AccountId; values["update_user_name"] = actor.AccountName; values["update_manager_real_name"] = actor.AccountName; return values; }
    /// <summary>新节点补齐服务器创建人和创建时间。</summary>
    private static void Created(Dictionary<string, object?> values, RuntimeActor actor)
    { values["insert_time"] = BusinessCalendar.Now; values["insert_manager_id"] = actor.AccountId; values["insert_user_name"] = actor.AccountName; values["insert_manager_real_name"] = actor.AccountName; }
}
