using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>仅组合结构同步模型；文件基础设施不能通过业务页面暴露或覆盖，输入对象保持不变。</summary>
public static class MetadataCatalogComposition
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>合并应用与基础设施物理定义；自举项目的相同定义去重，所有冲突在建表前失败。</summary>
    public static MetadataCatalog MergePhysical(MetadataCatalog application, MetadataCatalog infrastructure)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(infrastructure);
        MetadataValidation.Validate(application);
        MetadataValidation.Validate(infrastructure);
        if (application.DatabaseProvider != infrastructure.DatabaseProvider || application.BackendTarget != infrastructure.BackendTarget || application.SchemaVersion != infrastructure.SchemaVersion)
            throw new InvalidOperationException("应用与基础设施元数据版本、后端或数据库类型不一致。");
        if (infrastructure.ConfigPages.Count != 0 || infrastructure.ConfigPageColumns.Count != 0)
            throw new InvalidOperationException("基础设施物理模型不能携带业务页面。");
        RequireUniquePhysicalIds(application);
        RequireUniquePhysicalIds(infrastructure);
        var result = Clone(application);
        var reserved = infrastructure.ConfigTables.ToDictionary(table => table.TableName, StringComparer.OrdinalIgnoreCase);
        var duplicateTableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in application.ConfigTables)
        {
            if (!reserved.TryGetValue(table.TableName, out var expected)) continue;
            if (table.Id != expected.Id || TableDefinition(table) != TableDefinition(expected))
                throw new InvalidOperationException("业务表占用了基础设施保留表名。");
            if (application.ConfigPages.Any(page => page.ConfigTableId == table.Id))
                throw new InvalidOperationException("基础设施表不能通过业务页面开放。");
            RequireSame(Columns(application, table.Id), Columns(infrastructure, expected.Id), "基础设施字段物理定义不一致。");
            RequireSame(Indexes(application, table.Id), Indexes(infrastructure, expected.Id), "基础设施索引物理定义不一致。");
            duplicateTableIds.Add(table.Id);
        }
        var duplicateIndexIds = infrastructure.ConfigIndexes.Where(index => duplicateTableIds.Contains(Text(index, "configTableId"))).Select(index => Text(index, "id")).ToHashSet(StringComparer.Ordinal);
        var permittedIds = duplicateTableIds.Concat(infrastructure.ConfigColumns.Where(column => duplicateTableIds.Contains(column.ConfigTableId)).Select(column => column.Id))
            .Concat(duplicateIndexIds).Concat(infrastructure.ConfigIndexColumns.Where(column => duplicateIndexIds.Contains(Text(column, "configIndexId"))).Select(column => Text(column, "id"))).ToHashSet(StringComparer.Ordinal);
        var applicationObjects = PhysicalObjects(application).ToDictionary(item => item.Id, item => item.Identity, StringComparer.Ordinal);
        foreach (var item in PhysicalObjects(infrastructure))
            if (applicationObjects.TryGetValue(item.Id, out var identity) && (!permittedIds.Contains(item.Id) || identity != item.Identity))
                throw new InvalidOperationException("应用与基础设施物理对象标识冲突。");
        // ID 冲突只允许同一基础表中已经逐项证明一致的定义，不容许借用其他对象的标识。
        Append(result.ConfigTables, infrastructure.ConfigTables, table => table.Id, table => duplicateTableIds.Contains(table.Id));
        Append(result.ConfigColumns, infrastructure.ConfigColumns, column => column.Id, column => duplicateTableIds.Contains(column.ConfigTableId));
        Append(result.ConfigIndexes, infrastructure.ConfigIndexes, record => Text(record, "id"), record => duplicateTableIds.Contains(Text(record, "configTableId")));
        Append(result.ConfigIndexColumns, infrastructure.ConfigIndexColumns, record => Text(record, "id"), record => duplicateIndexIds.Contains(Text(record, "configIndexId")));
        MetadataValidation.Validate(result);
        return result;
    }

    /// <summary>仅按物理字段比较；显示文案、控件和项目标识不会改变数据库结构。</summary>
    private static string TableDefinition(TableMetadata table) => JsonSerializer.Serialize(new { table.TableName, table.IsFunctionTable, table.IsView, table.IsRongyu }, JsonOptions);
    private static string[] Columns(MetadataCatalog catalog, string tableId) => catalog.ConfigColumns.Where(column => column.ConfigTableId == tableId)
        .Select(column => JsonSerializer.Serialize(new { column.Id, column.ConfigTableId, column.ColumnName, column.LogicalType, column.ColumnLength, column.IsDbNotNull, column.IsVirtualColumn, column.Precision, column.Scale, column.DefaultValue }, JsonOptions)).Order(StringComparer.Ordinal).ToArray();
    /// <summary>索引名称、字段标识、顺序和方向均参与一致性检查，不按列表偶然顺序比较。</summary>
    private static string[] Indexes(MetadataCatalog catalog, string tableId) => catalog.ConfigIndexes.Where(index => Text(index, "configTableId") == tableId)
        .Select(index => JsonSerializer.Serialize(new { id = Text(index, "id"), tableId, name = Text(index, "indexName"), unique = Text(index, "isUnique"), type = Text(index, "indexType"), enabled = Text(index, "isEnabled"),
            columns = catalog.ConfigIndexColumns.Where(column => Text(column, "configIndexId") == Text(index, "id")).Select(column => new { id = Text(column, "id"), indexId = Text(column, "configIndexId"), columnId = Text(column, "configColumnId"), name = Text(column, "columnName"), order = Text(column, "sortNo"), direction = Text(column, "sortDirection") }).OrderBy(column => column.id, StringComparer.Ordinal).ToArray() }, JsonOptions)).Order(StringComparer.Ordinal).ToArray();
    private static void RequireSame(string[] actual, string[] expected, string message)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) throw new InvalidOperationException(message);
    }
    /// <summary>只向副本追加；重复标识必须属于已经核验一致的基础设施对象。</summary>
    private static void Append<T>(List<T> destination, IEnumerable<T> source, Func<T, string> id, Func<T, bool> mayDeduplicate)
    {
        var existing = destination.Select(id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in source)
        {
            if (!existing.Add(id(item)))
            {
                if (!mayDeduplicate(item)) throw new InvalidOperationException("应用与基础设施元数据标识冲突。");
                continue;
            }
            destination.Add(Clone(item));
        }
    }
    /// <summary>物理对象采用独立且明确的全局标识，禁止重复索引或跨对象借用标识。</summary>
    private static void RequireUniquePhysicalIds(MetadataCatalog catalog)
    {
        var ids = catalog.ConfigTables.Select(table => table.Id).Concat(catalog.ConfigColumns.Select(column => column.Id))
            .Concat(catalog.ConfigIndexes.Select(index => Text(index, "id"))).Concat(catalog.ConfigIndexColumns.Select(column => Text(column, "id"))).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("物理元数据标识缺失或重复。");
    }
    /// <summary>物理对象标识跨表、字段和索引核对，防止不同类别的碰撞被分表追加掩盖。</summary>
    private static IEnumerable<(string Id, string Identity)> PhysicalObjects(MetadataCatalog catalog) =>
        catalog.ConfigTables.Select(table => (table.Id, "table:" + table.Id))
            .Concat(catalog.ConfigColumns.Select(column => (column.Id, "column:" + column.ConfigTableId)))
            .Concat(catalog.ConfigIndexes.Select(index => (Text(index, "id"), "index:" + Text(index, "configTableId"))))
            .Concat(catalog.ConfigIndexColumns.Select(column => (Text(column, "id"), "index-column:" + Text(column, "configIndexId"))));
    private static string Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.ToString() : "";
    private static T Clone<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
}
