using Metarill.Core.Metadata;

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

    /// <summary>全局冗余单选直接分页、详情及导出同样须校验真实物理父记录。</summary>
    internal static bool HasStoredGlobalSelectionParent(MetadataCatalog catalog,TableModel table,PageModel page)=>
        table.IsGlobal&&table.Source.IsRongyu==1&&
        (GlobalCrossTableTarget.DeclaresExactSingle(table.Source,page.Source)
            ?GlobalCrossTableTarget.ExactSingleParent(catalog,table.Source,page.Source)
            :GlobalCrossTableTarget.SingleParent(catalog,table.Source,page.Source)) is {Logical:false};

    /// <summary>有真实项目列的全局 R1 自树来源按项目管理自己的行，目标候选仍使用已发布的全局树。</summary>
    internal static ColumnModel? GlobalSelfTreeProject(TableModel table)=>
        table.IsGlobal&&table.Source.IsRongyu==1&&table.Columns.Any(column=>
            column.Source.ControlKind=="CascadeTree"&&!NonRedundantCrossTableSubset.Declared(column))
            ? table.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id") : null;

    /// <summary>全局树与固定单值缓存来源保留实际行项目隔离，独立全局候选共用原发布条件。</summary>
    internal static ColumnModel? ManagedGlobalProject(TableModel table)=>
        table.HasScopedGlobalCache
            ? table.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id")
            : GlobalSelfTreeProject(table);

    /// <summary>无条件字典只有在原详情控件实际引用且选档协议能编译时才发布全局候选。</summary>
    internal static GenerationModel PublishUnfilteredStoredPages(GenerationModel model)
    {
        var candidates=model.Tables
            .Where(table=>ManagedGlobalProject(table) is not null)
            .SelectMany(table=>table.Pages.Where(page=>(page.Source.Extra.GetValueOrDefault("type").ToString() is "1" or "2")&&
                page.Condition is null&&page.Scopes.Count==0&&!ExactDataManageCompilation.Declared(page)))
            .Select(page=>page.Source.Id).ToHashSet(StringComparer.Ordinal);
        if(candidates.Count==0)return model;

        var published=new HashSet<string>(StringComparer.Ordinal);
        var scoped=new HashSet<string>(StringComparer.Ordinal);
        foreach(var table in model.Tables)
        foreach(var page in table.Pages)
        foreach(var source in page.DetailColumns.Where(column=>column.Source.ControlKind is "SelectPage" or "MultiSelectPage"))
        {
            try
            {
                var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==source.Source.Id);
                var definition=SelectionControlDefinition.Resolve(source,field);
                if(!candidates.Contains(definition.PageId))continue;

                var target=source.Source.ControlKind switch
                {
                    "MultiSelectPage"=>MultiSelectCompilation.Resolve(model,table,page,source).Definition?.Target,
                    _ when table.Source.IsFunctionTable!=0=>FunctionTableSelectionCompilation.Resolve(model,table,page,source).Target,
                    _=>NonRedundantSelectionCompilation.Resolve(model,table,page,source).Target
                };
                if(target is null)continue;
                var condition=FixedConditionPlan.Compile(definition.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                // 原弹窗由目标页共享。已经使用当前项目限定的来源不能因另一个引用放宽。
                if(condition.Variables.Any(variable=>variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)))
                    scoped.Add(definition.PageId);
                else published.Add(definition.PageId);
            }
            catch(Exception error) when(error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // 无效引用由来源生成诊断报告，本步骤不为它授予全局候选范围。
            }
        }
        published.ExceptWith(scoped);
        if(published.Count==0)return model;
        return model with{Tables=model.Tables.Select(table=>table.Pages.Any(page=>published.Contains(page.Source.Id))
            ?table with{Pages=table.Pages.Select(page=>published.Contains(page.Source.Id)
                ?page with{PublishedUnfilteredSelectionCandidate=true}:page).ToArray()}
            :table).ToArray()};
    }

    /// <summary>原冗余选择页按已发布条件查询候选，详情、写入及导出继续限制行项目。</summary>
    internal static bool UsesStoredGlobalSelectionRows(MetadataCatalog catalog,TableModel table,PageModel page)
    {
        var pageType=page.Source.Extra.GetValueOrDefault("type").ToString();
        // 原单选和多选可按已发布的固定条件选档。无条件字典须有真实详情引用。
        var fixedSelection=page.PublishedUnfilteredSelectionCandidate||
            page.Condition?.Syntax is {Terms.Count:>0,Variables.Count:0};
        return table.IsGlobal&&table.Source.IsRongyu==1&&ManagedGlobalProject(table) is not null&&
            pageType is "1" or "2"&&
            !ExactDataManageCompilation.Declared(page)&&!FixedPageConditionCompilation.HasLogicalFields(page)&&
            !page.Scopes.Any(scope=>scope.Source=="project")&&
            (page.Scopes.Count==0&&(fixedSelection||FixedPageConditionCompilation.HasAccountVariables(page))||
                HasStoredGlobalSelectionParent(catalog,table,page));
    }

    /// <summary>带真实行归属的全局普通业务同样绑定当前项目，候选使用独立计划。</summary>
    internal static FixedPageQueryPlan For(TableModel table, PageModel page)
    {
        if (!table.Pages.Any(candidate => ReferenceEquals(candidate, page))) throw new ArgumentException("固定查询计划必须属于当前表的真实页面。");
        var scopes = new List<(ColumnModel Column, string Source)>();
        var project = table.Columns.SingleOrDefault(column => column.Source.ColumnName == "yz_project_id");
        if (project is not null && (!table.IsGlobal || NonRedundantReadCompilation.HasGlobalRowProject(table) ||
            ManagedGlobalProject(table) is not null || page.Scopes.Any(scope => scope.Source == "project"))) scopes.Add((project, "project"));
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
    internal static bool TryProveSelection(MetadataCatalog catalog, FixedPageQueryPlan target, SelectionControlDefinition definition, out string? reason)
    {
        try
        {
            var globalCandidate=NonRedundantReadCompilation.GlobalSelectionPage(target.Table,target.Page)||
                UsesStoredGlobalSelectionRows(catalog,target.Table,target.Page);
            var requested=FixedConditionPlan.Compile(definition.WhereSql,target.Table.Columns.Select(column=>column.Source.ColumnName));
            // 全局候选只有在发布页明确执行项目条件时，才能覆盖控件中的同一约束。
            var explicitProject=globalCandidate&&requested.Terms.Any(GlobalMultipleRelationTarget.IsProjectScope)&&
                (FixedPageConditionCompilation.HasGlobalProjectMultiScope(target.Table,target.Page)||
                 HasPublishedProjectScope(catalog,target.Table,target.Page));
            var allowed = target.IdentityScopes.Where(scope=>!globalCandidate||scope.Source!="project"||explicitProject)
                .Select(scope => (scope.Column.Source.Id, scope.Source)).ToHashSet();
            // 附加项必须逐项已被目标页实际执行；不把来源控件的更窄条件偷换成宽目标页。
            var constants=target.Page.Condition?.Syntax.Terms.Select(FixedPageConditionCompilation.Term).ToHashSet(StringComparer.Ordinal)??new HashSet<string>(StringComparer.Ordinal);
            foreach(var term in requested.Terms)
            {
                if(term.Values.Any(value=>value.Kind==Metarill.Core.Metadata.FixedConditionValueKind.Variable))
                {
                    if(term.Comparison!=Metarill.Core.Metadata.FixedConditionComparison.Equal||term.Values.Count!=1)throw new InvalidOperationException("未证明的附加变量。");
                    var source=term.Values[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"project":term.Values[0].Text.Equals("parent_id",StringComparison.OrdinalIgnoreCase)||term.Values[0].Text.Equals("parentId",StringComparison.OrdinalIgnoreCase)?"parent":"";
                    var field=target.Table.Columns.Single(column=>column.Source.ColumnName==term.Field);
                    if(Metarill.Core.Security.AccountConditionVariables.IsSupported(term.Values[0].Text)&&constants.Contains(FixedPageConditionCompilation.Term(term)))continue;
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
        catch (Exception error) when(error is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            reason = "选择控件附加范围或排序超出已支持的固定语法。"; return false;
        }
    }

    /// <summary>只有实际字典页执行的项目范围才可覆盖原控件的同一条件。</summary>
    private static bool HasPublishedProjectScope(MetadataCatalog catalog,TableModel table,PageModel page)
    {
        if(FixedPageConditionCompilation.HasGlobalProjectSingleScope(table,page))
        {
            GlobalCrossTableTarget.RequireProjectSingleCandidate(catalog,table.Source,page.Source);
            return true;
        }
        var syntax=FixedConditionPlan.Compile(page.Source.WhereSql,table.Columns.Select(column=>column.Source.ColumnName));
        // 精确非冗余多选的项目过滤由实际发布字典查询执行，可覆盖控件的同一条件。
        if(table.Source.IsRongyu==0&&GlobalCrossTableTarget.DeclaresExactMulti(table.Source,page.Source)&&
            (GlobalMultipleRelationTarget.IsProjectPage(table.Source,page.Source,syntax,allowExact:true)||
             GlobalMultipleRelationTarget.IsProjectParentPage(table.Source,page.Source,syntax,allowExact:true)))
        {
            _=GlobalCrossTableTarget.ExactPageParent(catalog,table.Source,page.Source);
            return true;
        }
        if(GlobalCrossTableTarget.IsSingleProjectParentPage(page.Source,syntax))
            return GlobalCrossTableTarget.PhysicalSingleParent(catalog,table.Source,page.Source) is not null;
        var checkbox=page.Source.Extra.GetValueOrDefault("type").ToString()!="2";
        if(FixedPageConditionCompilation.HasStoredGlobalMultiParentScope(table,page))
            return GlobalCrossTableTarget.StoredProjectMultiParent(catalog,table.Source,page.Source,checkbox,allowExact:true) is not null;
        if(!NonRedundantReadCompilation.GlobalAccountMultiPage(table,page)||
            !GlobalMultipleRelationTarget.IsProjectParentPage(table.Source,page.Source,syntax))return false;
        // 同一发布证明会核对真实项目列及全部物理父字段。
        GlobalCrossTableTarget.RequireMultiCandidate(catalog,table.Source,page.Source);
        return true;
    }
}
