using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

internal sealed record ListConfigurationTarget(TableMetadata Table, string NameField);

/// <summary>原下拉配置在保存时核对目标绑定，附加条件仍由生成器按发布范围编译。</summary>
internal static class ConfigurationListRules
{
    /// <summary>解析表级及页面覆盖。仅受信任的模板版本目录无需业务目标表。</summary>
    internal static async Task<ListConfigurationTarget?> ValidateAsync(SqlRowStore store, long project,
        ColumnMetadata source, string? pageDetail, CancellationToken token)
    {
        JsonElement original;
        JsonElement overlay;
        string tableId, tableName, pageId, nameField, order;
        var linkedPage = source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        try
        {
            original = Read(source.ControlDetail);
            overlay = Read(pageDetail);
            if (original.TryGetProperty("catalog", out _) || overlay.TryGetProperty("catalog", out _))
            {
                RequireTemplateCatalog(source, original, overlay, pageDetail);
                return null;
            }

            string Effective(string key, string fallback = "") => First(Scalar(overlay, key), Scalar(original, key), fallback);
            tableId = Effective("tableId", source.LinkConfigTableId);
            tableName = Effective("table");
            pageId = First(Scalar(overlay.TryGetProperty("pageId", out _) ? overlay : original, "pageId"), linkedPage);
            nameField = Effective("nameField", "name");
            // 原固定候选允许显式清空控件排序，数据库字段缺省时仍沿用目标页。
            order = Scalar(overlay.TryGetProperty("orderBy", out _) ? overlay : original, "orderBy");
            _ = Effective("whereSql");
        }
        catch (Exception error) when (error is JsonException || error is InvalidOperationException and not BusinessRuleException)
        {
            throw new BusinessRuleException("下拉列表控件明细必须为合法的 JSON 对象，已知配置项须为有效标量。");
        }

        if (!MetadataValidation.IsIdentifier(nameField))
            throw new BusinessRuleException("下拉列表显示字段必须为有效的数据库字段名。");
        if (!MetadataValidation.IsSortExpression(order))
            throw new BusinessRuleException("下拉列表排序只能填写字段名及可选的 ASC 或 DESC。");

        long? pageTable = null;
        if (pageId.Length > 0)
        {
            var page = (await store.ReadForUpdateAsync("config_page", new Dictionary<string, object?>
            {
                ["id"] = Id(pageId, "目标页面"), ["yz_project_id"] = project
            }, token)).SingleOrDefault();
            if (page is null) throw new BusinessRuleException("下拉列表目标页面不存在或不属于当前项目。");
            pageTable = SqlRowStore.Id(page, "config_table_id");
        }

        var filters = new Dictionary<string, object?> { ["yz_project_id"] = project };
        if (tableId.Length > 0) filters["id"] = Id(tableId, "目标数据表");
        else if (tableName.Length == 0 && pageTable is long id) filters["id"] = id;
        else
        {
            var targetName = First(tableName, MetadataNames.LegacySingleTargetName(source));
            if (!MetadataValidation.IsIdentifier(targetName))
                throw new BusinessRuleException("下拉列表配置缺少有效目标数据表。");
            filters["table_name"] = targetName;
        }

        var targets = await store.ReadForUpdateAsync("config_table", filters, token);
        if (targets.Count != 1)
            throw new BusinessRuleException("下拉列表目标数据表不存在、不属于当前项目或无法唯一确定。");

        var target = MetadataRepository.DecodeRow<TableMetadata>(targets[0]);
        var targetId = SqlRowStore.Id(targets[0]);
        if ((tableName.Length > 0 && tableName != target.TableName) ||
            (source.LinkConfigTableId.Length > 0 && source.LinkConfigTableId != target.Id))
            throw new BusinessRuleException("下拉列表目标数据表与关联声明不一致。");
        if (pageTable is not null && pageTable != targetId)
            throw new BusinessRuleException("下拉列表目标页面不属于已声明的目标数据表。");

        await ConfigurationSelectionRules.RequireDisplayFieldAsync(store, project, target, targetId, nameField, "下拉列表", token);
        return new(target, nameField);
    }

    /// <summary>版本目录须使用平台版本字段的固定声明，不按普通关系查库。</summary>
    private static void RequireTemplateCatalog(ColumnMetadata source, JsonElement original, JsonElement overlay, string? pageDetail)
    {
        static bool Valid(JsonElement detail) =>
            detail.EnumerateObject().Count() == 2 &&
            Scalar(detail, "catalog") == TemplateVersionCatalog.CatalogId &&
            Scalar(detail, "nameField") == "name";
        var joined = source.Extra.GetValueOrDefault("isJoinColumn");
        if (source.Id != TemplateVersionCatalog.VersionColumnId || source.ConfigTableId != TemplateVersionCatalog.ProjectTableId ||
            source.ColumnName != "yz_yzp_version_id" || source.LogicalType != "int64" || source.IsVirtualColumn != 0 ||
            joined.ValueKind == JsonValueKind.True || joined.ToString() == "1" || source.LinkConfigTableId.Length > 0 ||
            source.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length > 0 ||
            !Valid(original) || !string.IsNullOrWhiteSpace(pageDetail) && !Valid(overlay))
            throw new BusinessRuleException("下拉列表固定版本目录与平台版本字段声明不一致。");
    }

    private static long Id(string value, string label) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 &&
        id.ToString(CultureInfo.InvariantCulture) == value
            ? id : throw new BusinessRuleException("下拉列表" + label + "标识无效。");

    private static string First(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static JsonElement Read(string? text)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().DistinctBy(property => property.Name, StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
            throw new InvalidOperationException("控件明细必须为无重复键的对象。");
        return root.Clone();
    }

    private static string Scalar(JsonElement detail, string key)
    {
        if (!detail.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            throw new InvalidOperationException("已知配置必须为标量。");
        return value.ToString();
    }
}
