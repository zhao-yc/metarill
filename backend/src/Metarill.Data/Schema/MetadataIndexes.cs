using Metarill.Core.Metadata;
using System.Text.Json;

namespace Metarill.Data.Schema;

/// <summary>索引也来自配置元数据，禁止直接采纳客户端 SQL 片段。</summary>
internal static class MetadataIndexes
{
    public static IReadOnlyList<IndexSchema> Compile(MetadataCatalog catalog, TableMetadata table, IReadOnlyList<ColumnSchema> columns)
    {
        var indexes = new List<IndexSchema>();
        foreach (var record in catalog.ConfigIndexes.Where(x => Text(x, "configTableId") == table.Id))
        {
            var name = Text(record, "indexName");
            MetadataValidation.RequireIdentifier(name);
            if (name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("不能用普通索引配置覆盖主键。");
            var id = Text(record, "id");
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("索引元数据缺少 ID。");
            var references = catalog.ConfigIndexColumns.Where(x => Text(x, "configIndexId") == id)
                .OrderBy(x => Number(x, "sortNo")).ToArray();
            var names = references.Select(reference =>
            {
                var column = catalog.ConfigColumns.SingleOrDefault(c => c.Id == Text(reference, "configColumnId"));
                if (column is null || column.ConfigTableId != table.Id || !columns.Any(c => c.Name == column.ColumnName))
                    throw new InvalidOperationException("索引字段必须引用同一实体表中的物理字段。");
                return column.ColumnName;
            }).ToArray();
            if (names.Length is < 1 or > 16 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
                throw new InvalidOperationException("索引字段数量无效或包含重复字段。");
            if (indexes.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("同表索引名重复。");
            indexes.Add(new IndexSchema(name, Number(record, "isUnique") == 1, names));
        }
        return indexes.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
    }
    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";
    private static int Number(JsonElement element, string name)
    {
        var text = Text(element, name);
        if (string.IsNullOrEmpty(text)) return 0;
        if (text == "true") return 1;
        if (text == "false") return 0;
        return int.TryParse(text, out var result) ? result : throw new InvalidOperationException("索引排序或唯一标识无效。");
    }
}
