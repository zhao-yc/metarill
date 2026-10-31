using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record FixedOptionCatalogModel(ColumnModel Source)
{
    public string JsonName => "yzYzpVersionName";
    public string Property => "YzYzpVersionName";
    public string Endpoint => "findNameIdYzYzpVersion" + Source.Source.Id;
}
internal sealed record FixedOptionCatalogResult(bool Declared, FixedOptionCatalogModel? Definition, string? Reason);

/// <summary>只证明平台版本字段的固定目录；未知目录或夹带查询配置仍是能力缺口。</summary>
internal static class FixedOptionCatalogCompilation
{
    /// <summary>声明存放在既有control_detail真实列，损坏声明也不能退回普通表查询。</summary>
    internal static bool HasDeclaration(ColumnModel column) => HasDeclaration(column.Source);
    internal static bool HasDeclaration(ColumnMetadata column) => HasCatalog(column.ControlDetail);
    /// <summary>同一真实页字段的来源或覆盖声明均需受审，不能因虚拟列先过滤而漏掉覆盖。</summary>
    internal static bool HasDeclaration(ColumnMetadata column, PageColumnMetadata? field) => HasDeclaration(column) || HasCatalog(PageOverlay(field));
    private static string PageOverlay(PageColumnMetadata? field) => field?.Extra.GetValueOrDefault("controlDetail").ToString() ?? "";
    private static bool HasCatalog(string text)
    {
        try { using var document = JsonDocument.Parse(text); return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("catalog", out _); }
        catch (JsonException) { return text.Contains("\"catalog\"", StringComparison.Ordinal); }
    }

    /// <summary>只接受确切目录、来源字段和name映射；不接受table/whereSql/orderBy等动态查询键。</summary>
    internal static FixedOptionCatalogResult Resolve(GenerationModel model, PageModel page, ColumnModel source)
    {
        var field = model.Catalog.ConfigPageColumns.SingleOrDefault(item => item.ConfigPageId == page.Source.Id && item.ConfigColumnId == source.Source.Id);
        var overlay = PageOverlay(field);
        if (!HasDeclaration(source.Source, field)) return new(false, null, null);
        FixedOptionCatalogResult Gap(string reason) => new(true, null, "固定版本目录尚未证明：" + reason);
        if (source.Source.IsVirtualColumn != 0 || GenerationModel.Enabled(source.Source.Extra, "isJoinColumn")) return Gap("目录源必须为非联接的物理版本字段。");
        if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) || !string.IsNullOrWhiteSpace(source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString())) return Gap("固定目录不能同时声明关联表或关联页面。");
        if (source.Source.LogicalType != "int64") return Gap("目录源必须明确声明int64逻辑类型。");
        if (field?.Extra.TryGetValue("controlKind", out var kind) == true && kind.ToString() is not ("" or "List")) return Gap("页面控件覆盖不是原List。" );
        if (source.Source.ControlKind != "List" || source.Source.Id != TemplateVersionCatalog.VersionColumnId || source.Source.ConfigTableId != TemplateVersionCatalog.ProjectTableId || source.Source.ColumnName != "yz_yzp_version_id" || source.Type != "long?") return Gap("来源字段或类型不匹配。");
        bool Valid(string text)
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var properties = document.RootElement.EnumerateObject().ToArray();
            return properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length
                && properties.Length == 2 && properties.All(item => item.Value.ValueKind == JsonValueKind.String)
                && properties.All(item => item.Name is "catalog" or "nameField")
                && document.RootElement.GetProperty("catalog").GetString() == TemplateVersionCatalog.CatalogId
                && document.RootElement.GetProperty("nameField").GetString() == "name";
        }
        try
        {
            if (!Valid(source.Source.ControlDetail) || !string.IsNullOrWhiteSpace(overlay) && !Valid(overlay)) return Gap("仅允许受控catalog/nameField声明，不能夹带表、SQL或未知键。");
            if (model.Tables.Single(table => table.Source.Id == source.Source.ConfigTableId).Columns.Any(column => column.JsonName.Equals("yzYzpVersionName", StringComparison.OrdinalIgnoreCase))) return Gap("计算名称与物理字段冲突。");
            return new(true, new(source), null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { return Gap("目录声明格式无效。"); }
    }

    /// <summary>候选与VO只采用本页可读/可写字段，不把其它页面的目录字段顺带公开。</summary>
    internal static IReadOnlyList<FixedOptionCatalogModel> ForPage(GenerationModel model, PageModel page) => page.Readable.Concat(page.Writable).DistinctBy(column => column.Source.Id)
        .Select(column => Resolve(model, page, column).Definition).OfType<FixedOptionCatalogModel>().ToArray();
}
