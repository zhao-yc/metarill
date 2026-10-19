using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Metadata;

public sealed record ConfigurationPageReference(string PageId, string PageName, string PageColumnId, string PageColumnName, string ControlKind, bool ControlDetailOverridden, string DetailSummary);
public sealed record ConfigurationIndexReference(string IndexId, string IndexName, string IndexColumnId, string ColumnName, int? SortNo, string SortDirection, bool Enabled);
public sealed record ConfigurationControlReference(string SourceType, string ColumnId, string ColumnName, string ColumnLabel, string TableId, string TableName,
    string? PageId, string? PageName, string? PageColumnId, string ControlKind, string ReferencePath, string ReferencedField, string Description);
public sealed record ConfigurationColumnReferences(string ColumnId, string ColumnName, string ColumnLabel, string TableId, string TableName, string ProjectId,
    IReadOnlyList<ConfigurationPageReference> Pages, IReadOnlyList<ConfigurationIndexReference> Indexes, IReadOnlyList<ConfigurationControlReference> Controls, IReadOnlyList<string> Coverage);

/// <summary>字段引用是纯元数据精确映射扫描，不执行控件 SQL、不推断手写脚本，也不扩张关系生成能力。</summary>
internal static class ConfigurationReferenceRules
{
    /// <summary>所有对象来自同项目快照，输出仅定位信息，原配置和解析异常内容不得出现在响应。</summary>
    internal static ConfigurationColumnReferences Build(MetadataCatalog catalog, string columnId)
    {
        MetadataValidation.Validate(catalog);
        var target = catalog.ConfigColumns.SingleOrDefault(column => column.Id == columnId) ?? throw new UnauthorizedAccessException("字段不存在或不属于当前项目。");
        var tables = catalog.ConfigTables.Where(table => Owned(table, catalog)).ToDictionary(table => table.Id, StringComparer.Ordinal);
        if (!tables.TryGetValue(target.ConfigTableId, out var owner) || !Owned(target, catalog)) throw new UnauthorizedAccessException("字段所属配置不属于当前项目。");
        var columns = catalog.ConfigColumns.Where(column => Owned(column, catalog) && tables.ContainsKey(column.ConfigTableId)).OrderBy(column => Id(column.Id)).ToArray();
        var byId = columns.ToDictionary(column => column.Id, StringComparer.Ordinal);
        var pages = catalog.ConfigPages.Where(page => Owned(page, catalog) && tables.ContainsKey(page.ConfigTableId)).ToDictionary(page => page.Id, StringComparer.Ordinal);
        var fields = catalog.ConfigPageColumns.Where(field => Owned(field, catalog) && pages.ContainsKey(field.ConfigPageId) && byId.ContainsKey(field.ConfigColumnId)
            && pages[field.ConfigPageId].ConfigTableId == byId[field.ConfigColumnId].ConfigTableId).OrderBy(field => Id(field.Id)).ToArray();
        var coverage = new List<string>
        {
            "仅检查当前字段所属项目内的页面字段、索引字段、List/SelectPage/MultiSelectPage/CascadeTree 结构化映射及单层关联列。",
            "SQL、排序表达式、自定义控件脚本、动态规则和手写代码不做全文推断；无命中不代表不存在这些依赖。",
            "List/SelectPage 页面明细按已知映射字段合并，CascadeTree 页面明细整体覆盖；此扫描不表示相关运行控件已完成移植。",
            "显式目标标识越界时不按同名表回退；未知目标、无效配置和多层关联需打开原配置核对。"
        };
        var direct = fields.Where(field => field.ConfigColumnId == target.Id).Select(field =>
        {
            var overridden = !string.IsNullOrWhiteSpace(Extra(field, "controlDetail"));
            return new ConfigurationPageReference(field.ConfigPageId, pages[field.ConfigPageId].Name, field.Id, field.Name, target.ControlKind, overridden,
                overridden ? "含页面覆盖明细，按控件规则生效" : "继承表字段控件明细");
        }).ToArray();
        var controls = new List<ConfigurationControlReference>();
        foreach (var source in columns)
        {
            Scan(source, null); ScanJoin(source);
        }
        foreach (var field in fields) Scan(byId[field.ConfigColumnId], field);
        var indexes = catalog.ConfigIndexes.Where(index => Text(index, "configTableId") == owner.Id && JsonOwned(index, catalog)).ToDictionary(index => Text(index, "id"), StringComparer.Ordinal);
        var indexed = catalog.ConfigIndexColumns.Where(child => JsonOwned(child, catalog) && indexes.ContainsKey(Text(child, "configIndexId")))
            .Where(child => Text(child, "configColumnId") == target.Id || string.IsNullOrWhiteSpace(Text(child, "configColumnId")) && Text(child, "columnName") == target.ColumnName)
            .OrderBy(child => Integer(child, "sortNo") ?? 0).ThenBy(child => Id(Text(child, "id"))).Select(child =>
            {
                var index = indexes[Text(child, "configIndexId")];
                return new ConfigurationIndexReference(Text(index, "id"), Text(index, "indexName"), Text(child, "id"), Text(child, "columnName"), Integer(child, "sortNo"), Text(child, "sortDirection"), Flag(Text(index, "isEnabled")));
            }).ToArray();
        return new(target.Id, target.ColumnName, target.Name, owner.Id, owner.TableName, catalog.YzProjectId, direct, indexed, controls, coverage);

        // 只匹配明确目标表和完整物理字段名，输出映射路径而非控件明细原文。
        void Add(ColumnMetadata source, PageColumnMetadata? field, TableMetadata? referenced, string path, string? property)
        {
            if (referenced?.Id != owner.Id || string.IsNullOrWhiteSpace(property) || property != target.ColumnName) return;
            var sourceTable = tables[source.ConfigTableId]; var page = field is null ? null : pages[field.ConfigPageId];
            controls.Add(new(field is null ? "column" : "pageColumn", source.Id, source.ColumnName, source.Name, sourceTable.Id, sourceTable.TableName,
                page?.Id, page?.Name, field?.Id, source.ControlKind, path, property, "结构化映射 " + path + " 引用该字段"));
        }
        // 表名仅在项目内唯一匹配；显式标识不存在时绝不按名称寻找替代对象。
        TableMetadata? Table(string? id, string? name)
        {
            if (!string.IsNullOrWhiteSpace(id)) return tables.GetValueOrDefault(id);
            var matches = tables.Values.Where(table => table.TableName == name).Take(2).ToArray(); return matches.Length == 1 ? matches[0] : null;
        }
        // List 与选择页只合并非空映射字段，级联页面覆盖整体替换。
        void Scan(ColumnMetadata source, PageColumnMetadata? field)
        {
            if (source.ControlKind is not ("List" or "SelectPage" or "MultiSelectPage" or "CascadeTree")) return;
            try
            {
                var overrideText = field is null ? "" : Extra(field, "controlDetail");
                var original = source.ControlKind == "CascadeTree" && !string.IsNullOrWhiteSpace(overrideText) ? new JsonObject() : Definition(source.ControlDetail);
                var detail = string.IsNullOrWhiteSpace(overrideText) ? original : Definition(overrideText);
                string Mapped(string name, string fallback = "")
                {
                    var value = Value(detail, name); if (string.IsNullOrWhiteSpace(value)) value = Value(original, name); return string.IsNullOrWhiteSpace(value) ? fallback : value;
                }
                if (source.ControlKind == "List")
                {
                    var referenced = Table(Mapped("tableId", source.LinkConfigTableId), Mapped("table"));
                    Add(source, field, referenced, "nameField", Mapped("nameField", "name")); Add(source, field, referenced, "idField", "id");
                }
                else if (source.ControlKind is "SelectPage" or "MultiSelectPage")
                {
                    var selected = pages.GetValueOrDefault(Mapped("pageId")); var referenced = selected is null ? null : tables.GetValueOrDefault(selected.ConfigTableId);
                    Add(source, field, referenced, "nameField", Mapped("nameField", "name")); Add(source, field, referenced, "idField", "id");
                }
                else
                {
                    var mode = detail.ContainsKey("sourceMode") ? Value(detail, "sourceMode") : "crossTable";
                    if (mode is not ("selfTree" or "crossTable")) mode = detail["selfTree"] is JsonObject selectedTree && !string.IsNullOrWhiteSpace(Value(selectedTree, "table")) && (detail["levels"] is not JsonArray selectedLevels || selectedLevels.Count == 0) ? "selfTree" : "crossTable";
                    if (mode == "selfTree" && detail["selfTree"] is JsonObject tree)
                        Tree(source, field, tree, "selfTree.", true);
                    else if (mode == "crossTable" && detail["levels"] is JsonArray levels)
                        for (var index = 0; index < levels.Count; index++) if (levels[index] is JsonObject level) Tree(source, field, level, "levels[" + index + "].", false);
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
            { coverage.Add("未能解析" + (field is null ? "字段 " + source.Id : "页面字段 " + field.Id) + " 的控件明细，需打开原配置核对。"); }
        }
        // 自树保留 id/name 默认值，多级关系只使用各层显式字段映射。
        void Tree(ColumnMetadata source, PageColumnMetadata? field, JsonObject tree, string prefix, bool self)
        {
            var referenced = Table(null, Value(tree, "table"));
            Add(source, field, referenced, prefix + "idField", Default(Value(tree, "idField"), self ? "id" : ""));
            Add(source, field, referenced, prefix + "nameField", Default(Value(tree, "nameField"), self ? "name" : ""));
            Add(source, field, referenced, prefix + "parentField", Value(tree, "parentField"));
        }
        // 单层 relation.field 只通过本项目真实关系字段定位，不把 SQL 或同名前缀当作跨项目依据。
        void ScanJoin(ColumnMetadata source)
        {
            if (!Flag(Extra(source, "isJoinColumn"))) return;
            var path = Extra(source, "columnFullName").Split('.');
            if (path.Length != 2 || !Regex.IsMatch(path[0], "^[A-Za-z_][A-Za-z0-9_]*$") || path[1] != target.ColumnName) return;
            var relations = columns.Where(column => column.ConfigTableId == source.ConfigTableId && !Flag(Extra(column, "isJoinColumn")) && (column.ColumnName == path[0] + "_id" || column.ColumnName == path[0] + "_ids")).Take(2).ToArray();
            if (relations.Length != 1) return;
            var relation = relations[0]; TableMetadata? referenced = null;
            try
            {
                var joined = Extra(relation, "joinTable");
                if (!string.IsNullOrWhiteSpace(relation.LinkConfigTableId)) referenced = Table(relation.LinkConfigTableId, null);
                else if (!string.IsNullOrWhiteSpace(joined)) referenced = Table(null, joined);
                else if (relation.ControlKind == "List") { var detail = Definition(relation.ControlDetail); referenced = Table(Value(detail, "tableId"), Value(detail, "table")); }
                else if (relation.ControlKind is "SelectPage" or "MultiSelectPage")
                { var selected = pages.GetValueOrDefault(Value(Definition(relation.ControlDetail), "pageId")); referenced = selected is null ? null : tables.GetValueOrDefault(selected.ConfigTableId); }
                if (referenced is null && string.IsNullOrWhiteSpace(relation.LinkConfigTableId) && string.IsNullOrWhiteSpace(joined) && string.IsNullOrWhiteSpace(relation.ControlDetail)) referenced = Table(null, path[0]);
                Add(source, null, referenced, "columnFullName", path[1]);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            { coverage.Add("未能解析字段 " + source.Id + " 的关联列映射，需打开原配置核对。"); }
        }
    }

    /// <summary>结构化定义允许空值，重复键、数组或其它根节点不被解释为映射。</summary>
    private static JsonObject Definition(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        using var parsed = JsonDocument.Parse(text);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || parsed.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != parsed.RootElement.EnumerateObject().Count())
            throw new InvalidOperationException("控件配置结构无效。");
        return JsonNode.Parse(text)!.AsObject();
    }
    /// <summary>映射值只接受字符串或数值标识，复杂节点不会被转成可执行表达式。</summary>
    private static string Value(JsonObject source, string name) => source[name] is null ? "" : source[name]!.GetValueKind() is JsonValueKind.String or JsonValueKind.Number ? source[name]!.ToString() : throw new InvalidOperationException("映射字段类型无效。");
    /// <summary>只读取元数据扩展键的存储文本，不执行其中的表达式。</summary>
    private static string Extra(MetadataRecord row, string name) => row.Extra.TryGetValue(name, out var value) ? value.ToString() : "";
    /// <summary>索引引用快照仅按明确属性读取定位文本。</summary>
    private static string Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) ? value.ToString() : "";
    /// <summary>按原标志协议识别启用值。</summary>
    private static bool Flag(string value) => value is "1" or "true" or "True";
    /// <summary>排序只接受无符号十进制 ID，异常值排到默认位置。</summary>
    private static long Id(string id) => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    /// <summary>保留索引字段可空整数排序，不把未知值猜作有效排序。</summary>
    private static int? Integer(JsonElement row, string name) => int.TryParse(Text(row, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    /// <summary>对象归属来自当前项目快照，显式跨项目值不得进入扫描。</summary>
    private static bool Owned(MetadataRecord row, MetadataCatalog catalog) => string.IsNullOrEmpty(row.YzProjectId) || row.YzProjectId == catalog.YzProjectId;
    /// <summary>兼容旧索引快照未带项目键，显式冲突一律拒绝。</summary>
    private static bool JsonOwned(JsonElement row, MetadataCatalog catalog) => string.IsNullOrWhiteSpace(Text(row, "yzProjectId")) || Text(row, "yzProjectId") == catalog.YzProjectId;
    /// <summary>仅在未配置映射时采用控件约定默认值。</summary>
    private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
