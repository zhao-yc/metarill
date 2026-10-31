namespace Metarill.Generation;

/// <summary>固定冗余物理链的纯来源eq筛选，不把读取/候选范围转成动态目标查询。</summary>
internal static class CrossTableFilterCompilation
{
    /// <summary>在最终模型中标记已证明的同步筛选；混合selfTree页仍保留原异步查询。</summary>
    internal static GenerationModel Prepare(GenerationModel model)=>model with{Tables=model.Tables.Select(table=>table with{Pages=table.Pages.Select(page=>page with{Searchable=page.Searchable.Select(column=>column with{IsFixedCrossTableFilter=column.Source.ControlKind=="CascadeTree"&&CrossTableWriteCompilation.Resolve(model,table,page,column).Definition is not null}).ToArray()}).ToArray()}).ToArray()};
    /// <summary>标记来自同一固定模型，编译计划丢失时拒绝生成而不是字符串LIKE回退。</summary>
    internal static IReadOnlyList<CrossTableWriteModel> ForPage(GenerationModel model,TableModel table,PageModel page)=>page.Searchable.Where(column=>column.IsFixedCrossTableFilter).Select(column=>CrossTableWriteCompilation.Resolve(model,table,page,column).Definition??throw new InvalidOperationException("跨表筛选固定计划已失效。")).ToArray();
}
