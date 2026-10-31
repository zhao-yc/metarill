using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

[assembly: InternalsVisibleTo("Metarill.Metadata.Tests")]
namespace Metarill.Runtime.Metadata;

/// <summary>仅布局参数前检可创建；尚未打开数据库事务，执行后错误不得借此声明安全重试。</summary>
public sealed class MetadataLayoutInputException:InvalidOperationException
{
    internal MetadataLayoutInputException(Exception error):base(error.Message,error){}
}

public sealed class MetadataRevisionConflictException() : InvalidOperationException("版本冲突：页面或字段配置已变化，请刷新后重试。");
public sealed record MetadataRowsSnapshot(IReadOnlyList<Dictionary<string, object?>> Rows, string Revision);
public sealed record DetailLayoutGroup(string Key, string Name);
public sealed record DetailLayoutDefinition(int Version, string Mode, IReadOnlyList<DetailLayoutGroup> Groups);
internal sealed record DetailLayoutSave(long PageId, string Revision, DetailLayoutDefinition Layout, IReadOnlyDictionary<long, string> Fields);
internal sealed record MetadataPatch(long Id, IReadOnlyDictionary<string, object?> Values);

/// <summary>协调平台元数据聚合写入；锁、版本校验与所有字段更新在同一数据库事务内完成。</summary>
public sealed partial class MetadataWorkbenchBusiness(SqlRowStore store, HttpRuntimeAuthorization authorization, MetadataRegistry registry, IIdGenerator ids, ConfigurationMutationObserver configurationMutations)
{
    private sealed record PageScope(Dictionary<string, object?> Project, Dictionary<string, object?> Page,
        Dictionary<string, object?> Table, List<Dictionary<string, object?>> Columns, List<Dictionary<string, object?>> Fields);
    private sealed record TableScope(Dictionary<string, object?> Project, Dictionary<string, object?> Table, List<Dictionary<string, object?>> Columns);

    /// <summary>取得页面布局完整快照，读取也采用相同聚合锁顺序，避免版本与字段跨代。</summary>
    public Task<object> DetailLayoutSnapshotAsync(JsonElement request, CancellationToken cancellationToken = default)
    {
        var pageId=ParseLayoutInput(()=>{MetadataWorkbenchRules.Object(request,"pageId");return MetadataWorkbenchRules.Id(request,"pageId");});
        return InMetadataTransactionAsync(async () => BuildLayoutSnapshot(await ReadPageScopeAsync(pageId, cancellationToken)), cancellationToken);
    }

    /// <summary>保存显式布局和完整字段归属，缺失字段、跨页字段或版本冲突均整体拒绝。</summary>
    public Task<object> SaveDetailLayoutAsync(JsonElement request, CancellationToken cancellationToken = default)
    {
        var input=ParseLayoutInput(()=>MetadataWorkbenchRules.LayoutSave(request));
        return InMetadataTransactionAsync(async () =>
        {
            var scope = await ReadPageScopeAsync(input.PageId, cancellationToken);
            RequireRevision(input.Revision, PageRevision(scope));
            var currentIds = scope.Fields.Select(row => SqlRowStore.Id(row)).ToHashSet();
            if (!currentIds.SetEquals(input.Fields.Keys)) throw new InvalidOperationException("布局字段必须完整且属于当前页面，请刷新后重试。");
            var actor = authorization.RequireAdministrator();
            foreach (var field in scope.Fields)
                if(await store.UpdateAsync("config_page_column",SqlRowStore.Id(field),Audit(new(){["detail_group_key"]=input.Fields[SqlRowStore.Id(field)]},actor),cancellationToken)!=1)
                    throw new InvalidOperationException("详情布局字段已变化，请刷新后重试。");
            if(await store.UpdateAsync("config_page",input.PageId,Audit(new(){["detail_layout"]=JsonSerializer.Serialize(input.Layout,JsonDefaults.Options)},actor),cancellationToken)!=1)
                throw new InvalidOperationException("详情布局页面已变化，请刷新后重试。");
            return BuildLayoutSnapshot(await ReadPageScopeAsync(input.PageId, cancellationToken));
        }, cancellationToken);
    }

    /// <summary>只包装纯输入解析，数据库操作和提交异常不能被重新分类为参数前检失败。</summary>
    private static T ParseLayoutInput<T>(Func<T> parse)
    {
        try{return parse();}
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException or OverflowException)
        {throw new MetadataLayoutInputException(error);}
    }

    /// <summary>沿用原字段工作台的显示字段与标志名，响应另带用于并发校验的版本。</summary>
    public Task<MetadataRowsSnapshot> PageFieldsAsync(string pageId, CancellationToken cancellationToken = default) => InMetadataTransactionAsync(async () =>
    {
        var scope = await ReadPageScopeAsync(MetadataWorkbenchRules.ParseId(pageId), cancellationToken);
        return new MetadataRowsSnapshot(FieldRows(scope), PageRevision(scope));
    }, cancellationToken);

    /// <summary>批量只写声明的标志与布局组；先验证所有原记录，再执行任何更新。</summary>
    public Task<MetadataRowsSnapshot> SavePageFieldsAsync(JsonElement request, string? revision, CancellationToken cancellationToken = default)
    {
        var patches = MetadataWorkbenchRules.PageFieldPatches(request);
        MetadataWorkbenchRules.Revision(revision);
        return InMetadataTransactionAsync(async () =>
        {
            await LockProjectAsync(cancellationToken);
            var first = await FindOwnedRowAsync("config_page_column", patches[0].Id, true, cancellationToken);
            var pageId = SqlRowStore.Id(first, "config_page_id");
            var scope = await ReadPageScopeAsync(pageId, cancellationToken);
            RequireRevision(revision, PageRevision(scope));
            var fields = scope.Fields.ToDictionary(row => SqlRowStore.Id(row));
            var groups = ReadLayout(scope.Page).Groups.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var patch in patches)
            {
                if (!fields.ContainsKey(patch.Id)) throw new InvalidOperationException("不能跨页面或项目批量修改字段。");
                if (patch.Values.TryGetValue("detail_group_key", out var key) && key is string text && text.Length > 0 && !groups.Contains(text)) throw new InvalidOperationException("字段引用了不存在的布局标签。");
            }
            var actor = authorization.RequireAdministrator();
            foreach (var patch in patches) await store.UpdateAsync("config_page_column", patch.Id, Audit(patch.Values.ToDictionary(pair => pair.Key, pair => pair.Value), actor), cancellationToken);
            var next = await ReadPageScopeAsync(pageId, cancellationToken);
            return new MetadataRowsSnapshot(FieldRows(next), PageRevision(next));
        }, cancellationToken);
    }

    /// <summary>列排序只读取当前项目表下的字段，不执行客户端传入的表名或 SQL。</summary>
    public Task<MetadataRowsSnapshot> ColumnOrderAsync(string tableId, CancellationToken cancellationToken = default) => InMetadataTransactionAsync(async () =>
    {
        var scope = await ReadTableScopeAsync(MetadataWorkbenchRules.ParseId(tableId), cancellationToken);
        return new MetadataRowsSnapshot(ColumnRows(scope), TableRevision(scope));
    }, cancellationToken);

    /// <summary>顺序批量写入使用同一表版本和数据库行锁，不接受系统字段或其他表的字段。</summary>
    public Task<MetadataRowsSnapshot> SaveColumnOrderAsync(JsonElement request, string? revision, CancellationToken cancellationToken = default)
    {
        var patches = MetadataWorkbenchRules.ColumnOrderPatches(request);
        MetadataWorkbenchRules.Revision(revision);
        return InMetadataTransactionAsync(async () =>
        {
            await LockProjectAsync(cancellationToken);
            var first = await FindOwnedRowAsync("config_column", patches[0].Id, true, cancellationToken);
            var tableId = SqlRowStore.Id(first, "config_table_id");
            var scope = await ReadTableScopeAsync(tableId, cancellationToken);
            RequireRevision(revision, TableRevision(scope));
            var ids = scope.Columns.Select(row => SqlRowStore.Id(row)).ToHashSet();
            if (patches.Any(patch => !ids.Contains(patch.Id))) throw new InvalidOperationException("不能跨表或项目批量修改字段顺序。");
            var actor = authorization.RequireAdministrator();
            foreach (var patch in patches)
            {
                await store.UpdateAsync("config_column", patch.Id, Audit(patch.Values.ToDictionary(pair => pair.Key, pair => pair.Value), actor), cancellationToken);
                await configurationMutations.SynchronizeColumnOrderDerivedAsync(patch.Id,MetadataWorkbenchRules.ParseId(actor.ProjectId),cancellationToken);
            }
            var next = await ReadTableScopeAsync(tableId, cancellationToken);
            return new MetadataRowsSnapshot(ColumnRows(next), TableRevision(next));
        }, cancellationToken);
    }

    /// <summary>在打开数据库事务前拒绝非管理员入口。
    /// </summary>
    private Task<T> InMetadataTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    { authorization.RequireAdministrator(); return store.Session.InTransactionAsync(operation, cancellationToken); }

    /// <summary>每条聚合先锁当前项目行，锁定身份来自服务端认证上下文。</summary>
    private async Task<Dictionary<string, object?>> LockProjectAsync(CancellationToken cancellationToken)
    {
        var actor = authorization.RequireAdministrator();
        var rows = await store.ReadForUpdateAsync("yz_project", new Dictionary<string, object?> { ["id"] = MetadataWorkbenchRules.ParseId(actor.ProjectId) }, cancellationToken);
        return rows.SingleOrDefault() ?? throw new UnauthorizedAccessException("当前项目不存在。");
    }
    /// <summary>按项目归属读取真实原记录，不能用客户端关联标识替换所有权。</summary>
    private async Task<Dictionary<string, object?>> FindOwnedRowAsync(string table, long id, bool locking, CancellationToken cancellationToken)
    {
        var actor = authorization.RequireAdministrator();
        var filters = new Dictionary<string, object?> { ["id"] = id, ["yz_project_id"] = MetadataWorkbenchRules.ParseId(actor.ProjectId) };
        var rows = locking ? await store.ReadForUpdateAsync(table, filters, cancellationToken) : await store.ReadAsync(table, filters, cancellationToken);
        return rows.SingleOrDefault() ?? throw new InvalidOperationException("记录不存在或不属于当前项目。");
    }
    /// <summary>页面、物理表、页面字段和原列全部核验归属，并锁住参与版本的原记录。</summary>
    private async Task<PageScope> ReadPageScopeAsync(long pageId, CancellationToken cancellationToken)
    {
        var project = await LockProjectAsync(cancellationToken);
        var page = await FindOwnedRowAsync("config_page", pageId, true, cancellationToken);
        var table = await FindOwnedRowAsync("config_table", SqlRowStore.Id(page, "config_table_id"), true, cancellationToken);
        var columns = await store.ReadForUpdateAsync("config_column", new Dictionary<string, object?> { ["config_table_id"] = SqlRowStore.Id(table) }, cancellationToken);
        var fields = await store.ReadForUpdateAsync("config_page_column", new Dictionary<string, object?> { ["config_page_id"] = pageId }, cancellationToken);
        var ownedColumns = columns.Where(row => SqlRowStore.Text(row, "yz_project_id") == authorization.RequireActor().ProjectId).Select(row => SqlRowStore.Id(row)).ToHashSet();
        if (fields.Any(row => SqlRowStore.Text(row, "yz_project_id") != authorization.RequireActor().ProjectId || !ownedColumns.Contains(SqlRowStore.Id(row, "config_column_id")))) throw new InvalidOperationException("页面字段的表或项目引用不一致。");
        return new(project, page, table, columns, fields);
    }
    /// <summary>锁定排序聚合并拒绝混入归属不一致的元数据。</summary>
    private async Task<TableScope> ReadTableScopeAsync(long tableId, CancellationToken cancellationToken)
    {
        var project = await LockProjectAsync(cancellationToken);
        var table = await FindOwnedRowAsync("config_table", tableId, true, cancellationToken);
        var columns = await store.ReadForUpdateAsync("config_column", new Dictionary<string, object?> { ["config_table_id"] = tableId }, cancellationToken);
        if (columns.Any(row => SqlRowStore.Text(row, "yz_project_id") != authorization.RequireActor().ProjectId)) throw new InvalidOperationException("表字段的项目引用不一致。");
        return new(project, table, columns);
    }
    /// <summary>审计信息始终来自服务端身份，不接受客户端覆盖。</summary>
    private static Dictionary<string, object?> Audit(Dictionary<string, object?> values, RuntimeActor actor)
    { values["update_time"] = BusinessCalendar.Now; values["update_manager_id"] = actor.AccountId; return values; }
    /// <summary>显式版本不一致时阻断本次写入。</summary>
    private static void RequireRevision(string? supplied, string actual)
    { MetadataWorkbenchRules.Revision(supplied); if (!string.Equals(supplied, actual, StringComparison.Ordinal)) throw new MetadataRevisionConflictException(); }
    /// <summary>页面版本覆盖布局、字段及其原列定义。</summary>
    private static string PageRevision(PageScope scope) => Hash(new[] { scope.Page, scope.Table }.Concat(scope.Columns.OrderBy(row => SqlRowStore.Id(row))).Concat(scope.Fields.OrderBy(row => SqlRowStore.Id(row))));
    /// <summary>表排序版本覆盖该表全部活动列。</summary>
    private static string TableRevision(TableScope scope) => Hash(new[] { scope.Table }.Concat(scope.Columns.OrderBy(row => SqlRowStore.Id(row))));
    /// <summary>哈希包含原始记录，键和记录顺序固定，不依赖进程内状态。</summary>
    private static string Hash(IEnumerable<Dictionary<string, object?>> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows.Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value)), JsonDefaults.Options)))).ToLowerInvariant();
    /// <summary>未配置布局沿用常显；已持久化的错误布局必须显式报错。</summary>
    private static DetailLayoutDefinition ReadLayout(Dictionary<string, object?> page)
    { var raw = SqlRowStore.Text(page, "detail_layout"); if (string.IsNullOrWhiteSpace(raw)) return new(1, "none", []); using var document = JsonDocument.Parse(raw); return MetadataWorkbenchRules.Layout(document.RootElement); }
    /// <summary>详情布局只返回设计元数据，不读取业务表数据。</summary>
    private static object BuildLayoutSnapshot(PageScope scope)
    {
        // 保留原快照的两个不同ID，避免前端或MCP把页面字段ID误当成表字段ID；整批一次建索引。
        var columns=scope.Fields.ToDictionary(field=>SqlRowStore.Text(field,"id"),field=>SqlRowStore.Text(field,"config_column_id"),StringComparer.Ordinal);
        return new
        {
            pageId=SqlRowStore.Text(scope.Page,"id"),pageName=SqlRowStore.Text(scope.Page,"name"),revision=PageRevision(scope),detailLayout=ReadLayout(scope.Page),
            fields=FieldRows(scope).Select(row=>new{pageColumnId=row["id"],columnId=columns[(string)row["id"]!],name=row["configColumnName"],columnName=row["configColumnColumnName"],sortNo=row["sortNo"],isInEdit=row["isInEdit"],detailGroupKey=row["detailGroupKey"]}).ToArray()
        };
    }
    /// <summary>只投影工作台展示所需属性，字段顺序跟随原列排序。</summary>
    private static IReadOnlyList<Dictionary<string, object?>> FieldRows(PageScope scope)
    {
        var columns = scope.Columns.ToDictionary(row => SqlRowStore.Id(row));
        return scope.Fields.OrderBy(row => Convert.ToInt32(columns[SqlRowStore.Id(row, "config_column_id")].GetValueOrDefault("sort_no") ?? 0, CultureInfo.InvariantCulture)).ThenBy(row => SqlRowStore.Id(row)).Select(row =>
        {
            var column = columns[SqlRowStore.Id(row, "config_column_id")];
            var result = new Dictionary<string, object?> { ["id"] = SqlRowStore.Text(row, "id"), ["configColumnName"] = SqlRowStore.Text(column, "name"), ["configColumnColumnName"] = SqlRowStore.Text(column, "column_name"), ["sortNo"] = column.GetValueOrDefault("sort_no") ?? 0, ["detailGroupKey"] = SqlRowStore.Text(row, "detail_group_key") };
            foreach (var (alias, physical) in MetadataWorkbenchRules.PageFlags) result[alias] = MetadataWorkbenchRules.StoredFlag(row.GetValueOrDefault(physical));
            return result;
        }).ToArray();
    }
    /// <summary>排序工作台不暴露连接或系统配置。</summary>
    private static IReadOnlyList<Dictionary<string, object?>> ColumnRows(TableScope scope) => scope.Columns.OrderBy(row => Convert.ToInt32(row.GetValueOrDefault("sort_no") ?? 0, CultureInfo.InvariantCulture)).ThenBy(row => SqlRowStore.Id(row)).Select(row => new Dictionary<string, object?> { ["id"] = SqlRowStore.Text(row, "id"), ["name"] = SqlRowStore.Text(row, "name"), ["columnName"] = SqlRowStore.Text(row, "column_name"), ["sortNo"] = row.GetValueOrDefault("sort_no") ?? 0 }).ToArray();
}

/// <summary>显式解析工作台白名单；null、缺失与不合法类型不作隐式默认或脚本求值。</summary>
internal static class MetadataWorkbenchRules
{
    internal static readonly IReadOnlyDictionary<string, string> PageFlags = new Dictionary<string, string>(StringComparer.Ordinal)
    { ["isInGrid"]="is_in_grid", ["isInEdit"]="is_in_edit", ["isMust"]="is_must", ["isReadOnly"]="is_read_only", ["isInFilter"]="is_in_filter", ["isImportant"]="is_important", ["isPrint"]="is_print" };
    /// <summary>对象只允许明确键名，重复键不能覆盖校验值。</summary>
    internal static void Object(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("请求必须是对象。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidOperationException("请求包含重复或不允许的字段：" + property.Name);
    }
    /// <summary>标识只接受正整数，禁止表达式或科学记数。</summary>
    internal static long ParseId(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : throw new InvalidOperationException("标识必须为有效正整数。");
    /// <summary>读取显式提交的字符串或数字标识。</summary>
    internal static long Id(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.String ? ParseId(value.ToString()) : throw new InvalidOperationException("缺少有效标识：" + field);
    /// <summary>缺失和null不能隐式变成空字符串。</summary>
    internal static string Text(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidOperationException("缺少字符串字段或显式null不被允许：" + field);
    /// <summary>版本必须来自服务端快照的确定性哈希。</summary>
    internal static void Revision(string? value) { if (value is null || value.Length!=64 || !Regex.IsMatch(value, "^[a-f0-9]{64}$")) throw new InvalidOperationException("缺少有效版本，请先读取快照。"); }
    /// <summary>统一数据库标志的数字、布尔和空值表示。</summary>
    internal static int StoredFlag(object? value) => value is null ? 0 : value is bool flag ? (flag ? 1 : 0) : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    /// <summary>只接受版本1布局、唯一标签标识与有效名称。</summary>
    internal static DetailLayoutDefinition Layout(JsonElement value)
    {
        var layout=DetailLayoutConfiguration.Parse(value);
        return new(layout.Version,layout.Mode,layout.Groups.Select(group=>new DetailLayoutGroup(group.Key,group.Name)).ToArray());
    }
    /// <summary>完整解析保存聚合，字段只能引用本次定义中的标签。</summary>
    internal static DetailLayoutSave LayoutSave(JsonElement request)
    {
        Object(request, "pageId", "revision", "detailLayout", "fields");
        var id = Id(request, "pageId"); var revision = Text(request, "revision"); Revision(revision);
        if (!request.TryGetProperty("detailLayout", out var layout)) throw new InvalidOperationException("缺少布局定义。");
        var definition = Layout(layout); var keys = definition.Groups.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        if (!request.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("缺少完整的布局字段集合。");
        var result = new Dictionary<long, string>();
        foreach (var field in fields.EnumerateArray())
        {
            Object(field, "pageColumnId", "detailGroupKey"); var fieldId = Id(field, "pageColumnId"); var key = Text(field, "detailGroupKey");
            if (key.Length > 0 && !keys.Contains(key)) throw new InvalidOperationException("字段引用了不存在的布局标签。");
            if (!result.TryAdd(fieldId, key)) throw new InvalidOperationException("布局字段标识不能重复。");
        }
        return new(id, revision, definition, result);
    }
    /// <summary>页面字段批量使用固定标志白名单。</summary>
    internal static IReadOnlyList<MetadataPatch> PageFieldPatches(JsonElement request) => Patches(request, false);
    /// <summary>字段排序只接受ID和整数顺序。</summary>
    internal static IReadOnlyList<MetadataPatch> ColumnOrderPatches(JsonElement request) => Patches(request, true);
    /// <summary>逐项校验后构造独立补丁，不修改原始请求对象。</summary>
    private static IReadOnlyList<MetadataPatch> Patches(JsonElement request, bool orderOnly)
    {
        if (request.ValueKind != JsonValueKind.Array || request.GetArrayLength() == 0) throw new InvalidOperationException("批量修改必须是非空数组。");
        var result = new List<MetadataPatch>(); var ids = new HashSet<long>();
        foreach (var row in request.EnumerateArray())
        {
            Object(row, orderOnly ? ["id", "sortNo"] : ["id", "detailGroupKey", .. PageFlags.Keys]);
            var id = Id(row, "id"); if (!ids.Add(id)) throw new InvalidOperationException("批量标识不能重复。");
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (orderOnly)
            {
                if (!row.TryGetProperty("sortNo", out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sort)) throw new InvalidOperationException("排序必须是有效32位整数，不能省略或清空。");
                values["sort_no"] = sort;
            }
            else foreach (var property in row.EnumerateObject())
            {
                if (property.Name == "id") continue;
                if (property.Name == "detailGroupKey") { values["detail_group_key"] = Text(row, property.Name); continue; }
                var text = property.Value.ValueKind == JsonValueKind.True ? "1" : property.Value.ValueKind == JsonValueKind.False ? "0" : property.Value.ToString();
                if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.String or JsonValueKind.Number) || text is not ("0" or "1")) throw new InvalidOperationException("字段标志只接受0或1，不能清空：" + property.Name);
                values[PageFlags[property.Name]] = int.Parse(text, CultureInfo.InvariantCulture);
            }
            result.Add(new(id, values));
        }
        return result;
    }
}
