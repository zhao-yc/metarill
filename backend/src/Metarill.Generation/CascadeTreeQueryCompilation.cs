using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>只有需要目标图读取的级联筛选改为异步解析；其它页面保留原生成字节和调用协议。</summary>
internal static class CascadeTreeQueryCompilation
{
    /// <summary>固定跨表只匹配来源列，selfTree仍异步读取目标图。</summary>
    internal static bool Async(PageModel page)=>page.Searchable.Any(column=>column.Source.ControlKind=="CascadeTree"&&!column.IsFixedCrossTableFilter);
    /// <summary>分批图、固定物理父和原多选页的父校验须与对应候选查询共用读快照。</summary>
    internal static bool Snapshot(GenerationModel model,TableModel table,PageModel page)=>Async(page)||
        SelectionPageRenderer.HasParentScope(page)||
        ProjectScopedSingleRelationTarget.PhysicalSelectionParent(model.Catalog,table.Source,page.Source) is not null||
        GlobalCachedParentCompilation.ForSelfTree(model,table,page) is not null||CascadeTreeViewRenderer.ForPage(model,table,page).Count>0||table.Source.IsRongyu==0&&(DatabaseCheckboxCompilation.ForPage(model,table,page).Count>0||CrossTableReadCompilation.ForService(model,table,page).Count>0);
    /// <summary>列表、数量、统计、导出及选择回读共享同一Query调用，不能遗漏级联筛选。</summary>
    internal static string Call(PageModel page,string request="request",string actor="actor",string extra="")=>Async(page)?$"(await Query{page.Source.Id}({request}, {actor}, cancellationToken{extra}))":$"Query{page.Source.Id}({request}, {actor}{extra})";
}
