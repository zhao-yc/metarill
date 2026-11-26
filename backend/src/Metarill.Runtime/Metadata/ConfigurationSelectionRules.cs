using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>保存原单选及多选配置时核对目标元数据，固定查询的条件和权限仍在生成阶段证明。</summary>
internal static class ConfigurationSelectionRules
{
    internal static bool Applies(string kind) => kind is "SelectPage" or "MultiSelectPage";

    /// <summary>在调用方的项目事务中检查活动目标页、目标表及显示字段，返回已核实的表归属。</summary>
    internal static async Task<TableMetadata> ValidateAsync(SqlRowStore store, long project,
        string definition, string? overlay, string linkedPageId, CancellationToken token)
    {
        SelectionControlDetail detail;
        try
        {
            detail = SelectionControlDetail.Resolve(definition, overlay, linkedPageId);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new BusinessRuleException("选择页控件明细必须为合法的 JSON 对象，已知配置项须为有效标量。");
        }

        if (!long.TryParse(detail.PageId, NumberStyles.None, CultureInfo.InvariantCulture, out var pageId) ||
            pageId <= 0 || detail.PageId != pageId.ToString(CultureInfo.InvariantCulture))
            throw new BusinessRuleException("选择页配置缺少有效目标页面。");
        if (!MetadataValidation.IsIdentifier(detail.NameField))
            throw new BusinessRuleException("选择页显示字段必须为有效的数据库字段名。");
        if (!MetadataValidation.IsSortExpression(detail.OrderBy))
            throw new BusinessRuleException("选择页排序只能填写字段名及可选的 ASC 或 DESC。");

        var page = (await store.ReadForUpdateAsync("config_page",
            new Dictionary<string, object?> { ["id"] = pageId, ["yz_project_id"] = project }, token)).SingleOrDefault();
        if (page is null)
            throw new BusinessRuleException("选择页目标页面不存在或不属于当前项目。");

        var tableId = SqlRowStore.Id(page, "config_table_id");
        var table = (await store.ReadForUpdateAsync("config_table",
            new Dictionary<string, object?> { ["id"] = tableId, ["yz_project_id"] = project }, token)).SingleOrDefault();
        if (table is null)
            throw new BusinessRuleException("选择页目标页面缺少当前项目内的活动目标表。");

        var columns = await store.ReadForUpdateAsync("config_column",
            new Dictionary<string, object?> { ["config_table_id"] = tableId, ["yz_project_id"] = project }, token);
        var target = MetadataRepository.DecodeRow<TableMetadata>(table);
        // 名称也可来自既有级联或多值辅助列，按生成器的相同扩展规则核对。
        if (!columns.Any(column => SqlRowStore.Text(column, "column_name") == detail.NameField) &&
            !RelationStorageColumns.Expand(target, columns.Select(MetadataRepository.DecodeRow<ColumnMetadata>))
                .Any(column => column.ColumnName == detail.NameField))
            throw new BusinessRuleException("选择页显示字段在目标表中不存在：" + detail.NameField);

        return target;
    }
}
