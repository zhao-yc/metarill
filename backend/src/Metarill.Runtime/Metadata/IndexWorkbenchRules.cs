using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Runtime.Metadata;

/// <summary>工作台只接收受限声明，不采纳SQL、物理路径或客户端指定归属。</summary>
internal static partial class IndexWorkbenchRules
{
    internal sealed record Column(long? Id, long? ColumnId, string Name, int SortNo, string Direction);
    internal sealed record Index(long? Id, string Name, string Type, int Enabled, IReadOnlyList<Column> Columns);
    internal sealed record Save(long TableId, string Revision, IReadOnlyList<Index> Indexes);
    [GeneratedRegex("^(yzp_idx_|yzp_uk_)[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)] private static partial Regex ManagedName();
    internal static bool Managed(string name) => ManagedName().IsMatch(name);
    internal static bool SystemColumn(string name) => name is "id" or "import_no" or "delete_no" or "yz_project_id" or "insert_time" or "update_time" or "insert_manager_id" or "update_manager_id" or "insert_manager_real_name" or "update_manager_real_name" or "insert_user_name" or "update_user_name";

    /// <summary>完整列表必须显式存在，空数组才代表清空；字段ID可省略由已声明列名反查。</summary>
    internal static Save Parse(JsonElement request, string? suppliedRevision)
    {
        MetadataWorkbenchRules.Object(request, "configTableId", "indexes", "revision");
        var tableId = MetadataWorkbenchRules.Id(request, "configTableId");
        var revision = request.TryGetProperty("revision", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (suppliedRevision is not null && revision is not null && suppliedRevision != revision) throw new InvalidOperationException("请求体与请求头版本不一致。");
        revision ??= suppliedRevision; MetadataWorkbenchRules.Revision(revision);
        if (!request.TryGetProperty("indexes", out var indexes) || indexes.ValueKind != JsonValueKind.Array || indexes.GetArrayLength() > 64) throw new InvalidOperationException("索引必须是最多64项的完整数组，不能省略或null。");
        var result = new List<Index>();
        foreach (var index in indexes.EnumerateArray())
        {
            MetadataWorkbenchRules.Object(index, "id", "indexName", "indexType", "isEnabled", "columns");
            var id = OptionalId(index, "id"); var name = MetadataWorkbenchRules.Text(index, "indexName").Trim(); var type = MetadataWorkbenchRules.Text(index, "indexType").Trim().ToUpperInvariant();
            var prefix = type == "UNIQUE" ? "yzp_uk_" : "yzp_idx_";
            if (type is not ("NORMAL" or "UNIQUE") || name.Length > 64 || !Managed(name) || !name.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("索引类型、托管前缀或名称无效；名称最多64字符。");
            var enabled = Number(index, "isEnabled"); if (enabled is not (0 or 1)) throw new InvalidOperationException("索引启用状态只能是0或1。");
            if (!index.TryGetProperty("columns", out var columns) || columns.ValueKind != JsonValueKind.Array || columns.GetArrayLength() is < 1 or > 16) throw new InvalidOperationException("每个索引必须包含1至16个字段。");
            var references = new List<Column>(); var positions = new HashSet<int>();
            foreach (var column in columns.EnumerateArray())
            {
                MetadataWorkbenchRules.Object(column, "id", "configColumnId", "columnName", "sortNo", "sortDirection");
                var childId = OptionalId(column, "id"); var columnId = OptionalId(column, "configColumnId"); var columnName = MetadataWorkbenchRules.Text(column, "columnName").Trim();
                Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(columnName);
                var sortNo = Number(column, "sortNo"); var direction = MetadataWorkbenchRules.Text(column, "sortDirection").Trim().ToUpperInvariant();
                if (sortNo <= 0 || !positions.Add(sortNo) || direction is not ("ASC" or "DESC")) throw new InvalidOperationException("索引序号必须为唯一正整数，方向只能是ASC或DESC。");
                references.Add(new(childId, columnId, columnName, sortNo, direction));
            }
            result.Add(new(id, name, type, enabled, references.OrderBy(column => column.SortNo).Select((column, position) => column with { SortNo = (position + 1) * 10 }).ToArray()));
        }
        return new(tableId, revision!, result);
    }
    /// <summary>新节点ID必须空，由服务器分配；非空值只接受正整数，不接受表达式和科学记数。</summary>
    private static long? OptionalId(JsonElement row, string field)
    { if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.String && value.GetString() == "") return null; return MetadataWorkbenchRules.Id(row, field); }
    /// <summary>兼容原UI数字字符串但不接收浮点、布尔或隐式默认。</summary>
    private static int Number(JsonElement row, string field)
    { if (row.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.String && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return number; throw new InvalidOperationException("缺少有效整数字段：" + field); }
}
