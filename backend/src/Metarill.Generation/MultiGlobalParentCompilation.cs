using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>物理全局父校验共用实际发布页，并加入调用端的读快照或写事务。</summary>
internal static class MultiGlobalParentCompilation
{
    /// <summary>生成前校验父页和条件，宿主从冻结目录复用同一计划。</summary>
    internal static string Declaration(GenerationModel model,PageModel target,string name)
    {
        _=NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.ConfigTableId,target.Source.Id);
        return $"    private static readonly Metarill.Data.Queries.NonRedundantReadPlan {name}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileMultiGlobalParent({Literal(target.Source.ConfigTableId)},{Literal(target.Source.Id)});\n";
    }

    internal static string Check(string plan,string key,string currentRead)=>
        $"        if((await new Metarill.Data.Queries.NonRedundantReader(session).KeyRowsAsync({plan}.BindAccount(actor),[],[{key}],cancellationToken,actor.ProjectId,currentRead:{currentRead})).Count!=1)throw new InvalidOperationException(\"全局父记录不存在、已删除或不在固定父范围。\");";

    /// <summary>全局候选的父级按其实际发布页验证，普通项目页沿用自身的项目和父级范围。</summary>
    internal static MultiTargetParentScope? SingleTarget(GenerationModel model,TableModel table,PageModel page)
    {
        if(NonRedundantReadCompilation.HasGlobalRowProject(table)&&
            !NonRedundantReadCompilation.GlobalSelectionPage(table,page)&&page.Scopes.Any(scope=>scope.Source=="project"))
            return null;
        return DerivedRelationNamesCompilation.SingleParent(model,table,page) is {Global:true} parent?parent:null;
    }

    /// <summary>实体范围只直接接入已证明的物理父；逻辑父走既有级联范围。</summary>
    internal static MultiTargetParentScope? PhysicalSingleTarget(GenerationModel model,TableModel table,PageModel page)=>
        SingleTarget(model,table,page) is {Logical:false} parent?parent:null;

    /// <summary>按目标页证明的实际全局父键，原多值引用保持已有同页校验。</summary>
    internal static PageModel? PhysicalTarget(GenerationModel model,TableModel table,PageModel page)
    {
        if(PhysicalSingleTarget(model,table,page) is not null)return page;
        if(!FixedPageConditionCompilation.HasMultiTargetScope(model,table,page))return null;
        var parents=page.Scopes.Where(scope=>scope.Source=="parent").ToArray();
        if(parents.Length!=1)return null;
        var column=parents[0].Column.Source;
        if(column.LinkConfigTableId.Length==0||
            !model.Tables.Any(candidate=>candidate.Source.Id==column.LinkConfigTableId&&candidate.IsGlobal)||
            CrossTableStorageColumns.IsLogicalPathField(table.Source,model.Catalog.ConfigColumns,column.ColumnName)||
            !NonRedundantReads.IsScopedMultiTarget(model.Catalog,table.Source,page.Source.Id))return null;
        return page;
    }

    /// <summary>来源或目标页面的物理父校验只使用同一已发布父页。</summary>
    internal static PageModel? PhysicalScopeTarget(GenerationModel model,TableModel table,PageModel page)=>
        PhysicalTarget(model,table,page)??PhysicalSourceTarget(model,table,page);

    /// <summary>物理来源父必须参与已发布的同父关系，同一来源页使用唯一的全局父范围。</summary>
    internal static PageModel? PhysicalSourceTarget(GenerationModel model,TableModel table,PageModel page)
    {
        var scopes=page.Scopes.Where(scope=>scope.Source=="parent").ToArray();
        if(scopes.Length!=1||NonRedundantCrossParentCompilation.Enabled(model,table,page))return null;
        var source=scopes[0].Column;
        var targets=MultiSelectCompilation.ForService(model,table,page).Where(multi=>multi.GlobalParent&&multi.SameParent)
            .Select(multi=>multi.TargetPage).Concat(DatabaseCheckboxCompilation.ForService(model,table,page)
                .Where(checkbox=>checkbox.GlobalParent).Select(checkbox=>checkbox.LogicalTargetPage!))
            .Concat(SingleParentTargets(model,table,page,source).Select(binding=>binding.Target))
            .DistinctBy(target=>NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
                target.Source.ConfigTableId,target.Source.Id),StringComparer.Ordinal).ToArray();
        if(targets.Length>1)throw new NotSupportedException("同一来源页的全局父范围须绑定同一个实际父页面。");
        return targets.SingleOrDefault();
    }

    /// <summary>独立表单父键也须有唯一父页；写入只读取该字段已有的物理值。</summary>
    internal static (ColumnModel Foreign,PageModel Target)? SingleParentBinding(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        var bindings=SingleParentTargets(model,table,page,source).ToArray();
        if(bindings.Select(binding=>NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
            binding.Target.Source.ConfigTableId,binding.Target.Source.Id)).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new NotSupportedException("同一物理父键须绑定同一个实际全局父页。");
        return bindings.Length==0?null:bindings[0];
    }

    /// <summary>直接从当前页的单值配置取得持久父键，生成关系时无需递归调用候选编译。</summary>
    private static IEnumerable<(ColumnModel Foreign,PageModel Target)> SingleParentTargets(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(ForeignValidationRenderer.Target(model,source) is not {IsGlobal:true} parentTable)yield break;
        foreach(var foreign in table.Columns.Where(column=>column.Source.ControlKind is "List" or "SelectPage"&&column.Source.Id!=source.Source.Id))
        {
            if(ForeignValidationRenderer.Target(model,foreign) is not {} target)continue;
            if(target.IsGlobal)
            {
                if(!target.Pages.Any(candidate=>SingleTarget(model,target,candidate)?.Parent.Id==parentTable.Source.Id))
                    continue;
                var proof=table.Source.IsRongyu==1
                    ?GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,foreign.Source,target.Source)
                    :GlobalSingleRelationTarget.Require(model.Catalog,table.Source,foreign.Source,target.Source);
                if(proof.Parent is {Global:true}&&proof.SourceParent is {LogicalPages.Count:0} fixedSource&&
                    fixedSource.Column.Id==source.Source.Id)
                    yield return (foreign,target.Pages.Single(candidate=>candidate.Source.Id==proof.Page.Id));
                continue;
            }
            if(table.IsGlobal||target.Source.IsRongyu!=0)continue;
            var targetPage=ProjectSinglePage(model,page,foreign,target);
            if(targetPage is null||SingleTarget(model,target,targetPage) is not {Logical:true} parent||
                parent.Parent.Id!=parentTable.Source.Id)continue;
            var sourceBinding=table.Source.IsRongyu==0
                ?parent.SingleSource(model.Catalog,table.Source,foreign.Source,page.Source)
                :parent.SingleSource(model.Catalog,table.Source,foreign.Source);
            if(sourceBinding.LogicalPages.Count==0&&sourceBinding.Column.Id==source.Source.Id)
                yield return (foreign,targetPage);
        }
    }

    /// <summary>项目内单选取当前页面的有效配置，下拉沿用原目标页回退规则。</summary>
    private static PageModel? ProjectSinglePage(GenerationModel model,PageModel page,ColumnModel column,TableModel target)
    {
        if(column.Source.ControlKind=="List")return RelationCompilation.ListTargetPage(target,page,column,requireExplicit:false);
        var configuration=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>
            field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id);
        var definition=SelectionControlDefinition.Resolve(column,configuration);
        return target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==definition.PageId);
    }

    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
