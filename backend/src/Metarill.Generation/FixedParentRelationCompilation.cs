using System.Text.Json;

namespace Metarill.Generation;

internal sealed record FixedParentRelationBinding(ColumnModel SourceParentColumn, TableModel SourceParent, ColumnModel OwnerColumn,
    TableModel TargetParent, ColumnModel TargetParentColumn);
internal sealed record FixedParentRelationResult(bool Declared, FixedParentRelationBinding? Binding, string? Reason);

/// <summary>只编译显式声明的一跳源父归属字段；不猜字段名，不生成任意路径解释器。</summary>
internal static class FixedParentRelationCompilation
{
    internal const string Declaration = "sourceParentOwnerColumnId";
    /// <summary>声明保存在已有control_detail中，数据库重载与重复生成均能保留，无需新增物理列。</summary>
    internal static bool HasDeclaration(ColumnModel column)
    {
        if (column.Source.ControlKind != "List") return false;
        try { using var definition = JsonDocument.Parse(column.Source.ControlDetail); return definition.RootElement.ValueKind == JsonValueKind.Object && definition.RootElement.TryGetProperty(Declaration, out _); }
        catch (JsonException) { return false; }
    }
    /// <summary>所有边都按稳定ID和显式FK核验；来源父与目标父是不同绑定，客户端只提供来源上下文。</summary>
    internal static FixedParentRelationResult Resolve(GenerationModel model, PageModel page, ColumnModel source,
        IReadOnlyList<TableModel> sourceParents, IReadOnlyList<TableModel> targetParents, IReadOnlyList<(ColumnModel Column, string Source)> targetScopes)
    {
        if (!HasDeclaration(source)) return new(false, null, null);
        FixedParentRelationResult Gap(string reason) => new(true, null, "固定源父归属关系尚未支持：" + reason);
        try
        {
            using var json = JsonDocument.Parse(source.Source.ControlDetail);
            var value = json.RootElement.GetProperty(Declaration);
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return Gap("必须声明归属字段的稳定ID。");
            var settings = model.Catalog.ConfigPageColumns.SingleOrDefault(field => field.ConfigPageId == page.Source.Id && field.ConfigColumnId == source.Source.Id);
            if (settings?.Extra.TryGetValue("controlDetail", out var overlayValue) == true && !string.IsNullOrWhiteSpace(overlayValue.ToString()))
            {
                using var overlay = JsonDocument.Parse(overlayValue.ToString());
                if (overlay.RootElement.TryGetProperty(Declaration, out var overridden) && (overridden.ValueKind != JsonValueKind.String || overridden.GetString() != value.GetString())) return Gap("页面不能替换字段固定归属声明。");
            }
            if (sourceParents.Count != 1 || targetParents.Count != 1) return Gap("必须具有唯一来源父与唯一目标父。");
            var parent = sourceParents[0]; var targetParent = targetParents[0];
            var sourceScope = page.Scopes.Where(scope => scope.Source == "parent").DistinctBy(scope => scope.Column.Source.Id).ToArray();
            var targetScope = targetScopes.Where(scope => scope.Source == "parent").DistinctBy(scope => scope.Column.Source.Id).ToArray();
            if (sourceScope.Length != 1 || targetScope.Length != 1) return Gap("父范围必须由一个固定字段绑定。");
            var sourceParentColumn = sourceScope[0].Column; var targetParentColumn = targetScope[0].Column;
            var owner = parent.Columns.SingleOrDefault(column => column.Source.Id == value.GetString());
            if (source.Source.LinkConfigTableId != targetParentColumn.Source.ConfigTableId || sourceParentColumn.Source.LinkConfigTableId != parent.Source.Id || targetParentColumn.Source.LinkConfigTableId != targetParent.Source.Id ||
                owner is null || owner.Source.LinkConfigTableId != targetParent.Source.Id) return Gap("来源父、归属字段或目标父外键没有完整显式声明。");
            if (new[] { sourceParentColumn, targetParentColumn, owner }.Any(column => column.Type is not ("long" or "long?") || column.IsJsonArray || GenerationModel.Enabled(column.Source.Extra, "isJoinColumn"))) return Gap("归属边必须是物理单值整数外键。");
            if (parent.Source.Id == targetParent.Source.Id || !parent.Columns.Any(column => column.Source.ColumnName == "yz_project_id") || !targetParent.Columns.Any(column => column.Source.ColumnName == "yz_project_id")) return Gap("归属表必须独立且具有真实项目字段。");
            if (parent.Source.Id == "2076977530983813120")
            {
                var target = model.Tables.Single(table => table.Source.Id == source.Source.LinkConfigTableId);
                if (targetParent.Columns.Count(column => column.Source.ColumnName is "is_view" or "is_function_table") != 2 ||
                    targetParent.Columns.Where(column => column.Source.ColumnName is "is_view" or "is_function_table").Any(column => column.Type is not ("int?" or "bool?"))) return Gap("索引归属缺少物理表判定字段。");
                if (target.Columns.Count(column => column.Source.ColumnName is "is_virtual_column" or "is_join_column") != 2 ||
                    target.Columns.Where(column => column.Source.ColumnName is "is_virtual_column" or "is_join_column").Any(column => column.Type is not ("int?" or "bool?")) ||
                    !target.Columns.Any(column => column.Source.ColumnName == "control_kind" && column.Type == "string?")) return Gap("索引候选缺少已声明物理列判定字段。");
            }
            return new(true, new(sourceParentColumn, parent, owner, targetParent, targetParentColumn), null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return Gap("声明无法安全编译。"); }
    }
}
