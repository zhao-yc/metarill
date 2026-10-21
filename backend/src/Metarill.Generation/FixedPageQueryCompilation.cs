namespace Metarill.Generation;

/// <summary>实际固定查询的范围和有效默认排序；作用域只描述行约束，不替代身份、页面授权或真实父记录校验。</summary>
internal sealed record FixedPageQueryPlan(TableModel Table, PageModel Page, ColumnModel? SoftDeleteColumn,
    IReadOnlyList<(ColumnModel Column, string Source)> IdentityScopes,
    IReadOnlyList<(ColumnModel Column, bool Descending)> DefaultOrder);

/// <summary>生成查询和选择控件证明共用固定计划，避免仅比较原始WhereSql或漏掉生成器自动约束。</summary>
internal static class FixedPageQueryCompilation
{
    /// <summary>Mapper的软删除起点也由同一入口识别；字段不存在时保持原有无过滤行为。</summary>
    internal static ColumnModel? SoftDeleteColumn(TableModel table) => table.Columns.SingleOrDefault(column => column.Source.ColumnName == "delete_no");

    /// <summary>普通项目表自动绑定真实Actor项目；global只有显式项目范围才绑定。父范围始终独立绑定parentId。</summary>
    internal static FixedPageQueryPlan For(TableModel table, PageModel page)
    {
        if (!table.Pages.Any(candidate => ReferenceEquals(candidate, page))) throw new ArgumentException("固定查询计划必须属于当前表的真实页面。");
        var scopes = new List<(ColumnModel Column, string Source)>();
        var project = table.Columns.SingleOrDefault(column => column.Source.ColumnName == "yz_project_id");
        if (project is not null && (!table.IsGlobal || page.Scopes.Any(scope => scope.Source == "project"))) scopes.Add((project, "project"));
        scopes.AddRange(page.Scopes.Where(scope => scope.Source == "parent"));
        return new(table, page, SoftDeleteColumn(table), scopes, EffectiveOrder(table, page.Source.OrderBy));
    }

    /// <summary>与实际默认Order一致：已含主键则保留顺序，否则补id desc稳定分页；不解释请求排序。</summary>
    private static IReadOnlyList<(ColumnModel Column, bool Descending)> EffectiveOrder(TableModel table, string order)
    {
        var result = GenerationModel.ValidateOrderBy(order, table.Columns).ToList();
        if (!result.Any(item => item.Column.IsId)) result.Add((table.Columns.Single(column => column.IsId), true));
        return result;
    }

    /// <summary>只证明附加条件已被目标固定查询覆盖、排序与有效默认序列相同；保留原配置，不执行或透传SQL。</summary>
    internal static bool TryProveSelection(FixedPageQueryPlan target, SelectionControlDefinition definition, out string? reason)
    {
        try
        {
            var allowed = target.IdentityScopes.Select(scope => (scope.Column.Source.Id, scope.Source)).ToHashSet();
            // 附加项必须逐项已被目标页实际执行；不把来源控件的更窄条件偷换成宽目标页。
            var requested=Metarill.Core.Metadata.FixedConditionPlan.Compile(definition.WhereSql,target.Table.Columns.Select(column=>column.Source.ColumnName));
            var constants=target.Page.Condition?.Syntax.Terms.Select(FixedPageConditionCompilation.Term).ToHashSet(StringComparer.Ordinal)??new HashSet<string>(StringComparer.Ordinal);
            foreach(var term in requested.Terms)
            {
                if(term.Values.Any(value=>value.Kind==Metarill.Core.Metadata.FixedConditionValueKind.Variable))
                {
                    if(term.Comparison!=Metarill.Core.Metadata.FixedConditionComparison.Equal||term.Values.Count!=1)throw new InvalidOperationException("未证明的附加变量。");
                    var source=term.Values[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"project":term.Values[0].Text.Equals("parent_id",StringComparison.OrdinalIgnoreCase)||term.Values[0].Text.Equals("parentId",StringComparison.OrdinalIgnoreCase)?"parent":"";
                    var field=target.Table.Columns.Single(column=>column.Source.ColumnName==term.Field);
                    if(!allowed.Contains((field.Source.Id,source))){reason="选择控件附加范围未被目标固定查询约束。";return false;}
                }
                else if(!constants.Contains(FixedPageConditionCompilation.Term(term)))
                {reason="选择控件附加范围未被目标固定查询约束。";return false;}
            }
            if (!string.IsNullOrWhiteSpace(definition.OrderBy) &&
                !EffectiveOrder(target.Table, definition.OrderBy).Select(item => (item.Column.Source.Id, item.Descending))
                    .SequenceEqual(target.DefaultOrder.Select(item => (item.Column.Source.Id, item.Descending))))
            {
                reason = "选择控件排序与目标页有效默认排序不一致。"; return false;
            }
            reason = null; return true;
        }
        catch (Exception error) when(error is InvalidOperationException or ArgumentException)
        {
            reason = "选择控件附加范围或排序超出已支持的固定语法。"; return false;
        }
    }
}
