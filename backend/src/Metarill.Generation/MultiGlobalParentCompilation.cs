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
        var parentCount=page.Scopes.Count(scope=>scope.Source=="parent");
        if(parentCount==0)return null;
        if(NonRedundantReadCompilation.HasGlobalRowProject(table)&&
            !NonRedundantReadCompilation.GlobalSelectionPage(table,page)&&page.Scopes.Any(scope=>scope.Source=="project"))
            return null;
        // 多父普通业务页仅在实际用作下拉或单选目标时接入单值候选证明。
        if(!table.IsGlobal&&parentCount>1&&!model.Tables.Any(owner=>owner.Columns.Any(column=>
            column.Source.ControlKind is "List" or "SelectPage"&&
            ForeignValidationRenderer.Target(model,column)?.Source.Id==table.Source.Id&&
            owner.Pages.Any(source=>ProjectSinglePage(model,source,column,table)?.Source.Id==page.Source.Id))))
            return null;
        var parent=DerivedRelationNamesCompilation.SingleParent(model,table,page);
        if(parent is not {Global:true})return null;
        if(!table.IsGlobal&&parent.AdditionalTargets.Count>0)
            _=NonRedundantReads.MultiGlobalParentPageId(model.Catalog,table.Source.Id,page.Source.Id);
        return parent;
    }

    /// <summary>实体范围只直接接入已证明的物理父；逻辑父走既有级联范围。</summary>
    internal static MultiTargetParentScope? PhysicalSingleTarget(GenerationModel model,TableModel table,PageModel page)=>
        SingleTarget(model,table,page) is {Logical:false} parent?parent:null;

    /// <summary>项目内目标页与原全局多选页可声明物理全局父，绑定仍由实际页面和外键证明。</summary>
    internal static MultiTargetParentScope? PhysicalPageParent(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Source.IsView!=0||table.Source.IsFunctionTable!=0||table.IsGlobal&&!SelectionPageRenderer.Enabled(page))return null;
        var parents=page.Scopes.Where(scope=>scope.Source=="parent").ToArray();
        if(parents.Length>1)
        {
            if(table.IsGlobal)
                return GlobalCrossTableTarget.PhysicalAccountParent(model.Catalog,table.Source,page.Source) is {Global:true} compound?compound:null;
            if(PublishedMultiParent(model,table,page) is not {Global:true,Logical:false,AdditionalTargets.Count:>0} compoundParent)
                return null;
            _=NonRedundantReads.MultiGlobalParentPageId(model.Catalog,table.Source.Id,page.Source.Id);
            return compoundParent;
        }
        if(parents.Length!=1)return null;
        var key=parents[0].Column;
        if(!model.Tables.Any(candidate=>candidate.IsGlobal&&candidate.Source.Id==key.Source.LinkConfigTableId)||
            CrossTableStorageColumns.IsLogicalPathField(table.Source,model.Catalog.ConfigColumns,key.Source.ColumnName))
            return null;
        // 已由共享级联冻结的 R1 父键沿完整祖先路径校验，不能再作为独立多选父键编译。
        if(!SelectionPageRenderer.Enabled(page)&&SharedRedundantParentCompilation.ForColumn(model,table,page,key).Count>0)
            return null;

        var condition=FixedConditionPlan.Compile(page.Source.WhereSql,table.Columns.Select(column=>column.Source.ColumnName));
        return MultiTargetParentScope.Compile(model.Catalog,table.Source,condition) is {Global:true,Logical:false} parent?parent:null;
    }

    /// <summary>多个父字段含逻辑级联时，逐个证明同一实际全局父页，再开放该页的父范围。</summary>
    internal static MultiTargetParentScope? LogicalCompoundTarget(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Source.IsRongyu!=0||page.Scopes.Count(scope=>scope.Source=="parent")<2||
            !page.Scopes.Any(scope=>scope.Source=="parent"&&
                CrossTableStorageColumns.IsLogicalPathField(table.Source,model.Catalog.ConfigColumns,scope.Column.Source.ColumnName)))
            return null;
        var parent=SingleTarget(model,table,page)??PublishedMultiParent(model,table,page);
        if(parent is not {Global:true,Logical:true,AdditionalTargets.Count:>0})return null;
        _=NonRedundantReads.MultiGlobalParentPageId(model.Catalog,table.Source.Id,page.Source.Id);
        return parent;
    }

    /// <summary>项目内多值目标仍使用实际项目归属，只开放明确发布的父范围。</summary>
    private static MultiTargetParentScope? PublishedMultiParent(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Source.IsRongyu!=0)return null;
        var checkbox=!SelectionPageRenderer.Enabled(page);
        if(checkbox&&(page.Source.Extra.GetValueOrDefault("type").ToString() is not ("" or "0" or "1")||
            !NonRedundantReads.IsScopedMultiTarget(model.Catalog,table.Source,page.Source.Id)))return null;
        var condition=FixedConditionPlan.Compile(page.Source.WhereSql,table.Columns.Select(column=>column.Source.ColumnName));
        if(table.IsGlobal)return MultiTargetParentScope.CompileGlobalPage(model.Catalog,table.Source,condition,checkbox);
        return checkbox?MultiTargetParentScope.CompileCheckboxPage(model.Catalog,table.Source,condition,allowGlobalParent:true):
            MultiTargetParentScope.CompileSelectionPage(model.Catalog,table.Source,condition,allowGlobalParent:true);
    }

    /// <summary>按目标页证明的实际全局父键，原多值引用保持已有同页校验。</summary>
    internal static PageModel? PhysicalTarget(GenerationModel model,TableModel table,PageModel page)
    {
        if(PhysicalSingleTarget(model,table,page) is not null||PhysicalPageParent(model,table,page) is not null)return page;
        if(LogicalCompoundTarget(model,table,page) is {} compound&&compound.AdditionalTargets.Any(scope=>!scope.Logical))return page;
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
        if(scopes.Length>1)return CompoundSourceTarget(model,table,page);
        if(scopes.Length!=1||NonRedundantCrossParentCompilation.Enabled(model,table,page))return null;
        var source=scopes[0].Column;
        var targets=MultiSelectCompilation.ForService(model,table,page).Where(multi=>multi.GlobalParent&&multi.SameParent)
            .Select(multi=>multi.TargetPage).Concat(DatabaseCheckboxCompilation.ForService(model,table,page)
                .Select(checkbox=>checkbox.GlobalParentPage).OfType<PageModel>())
            .Concat(SingleParentTargets(model,table,page,source).Select(binding=>binding.Target))
            .DistinctBy(target=>NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
                target.Source.ConfigTableId,target.Source.Id),StringComparer.Ordinal).ToArray();
        if(targets.Length>1)throw new NotSupportedException("同一来源页的全局父范围须绑定同一个实际父页面。");
        return targets.SingleOrDefault();
    }

    /// <summary>来源页有多个同父条件时，物理字段通过实际目标和全部来源条件证明。</summary>
    internal static PageModel? CompoundSourceTarget(GenerationModel model,TableModel table,PageModel page)
    {
        var scopes=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
        if(scopes.Length<2)return null;
        var physical=scopes.Where(column=>!CrossTableStorageColumns.IsLogicalPathField(
            table.Source,model.Catalog.ConfigColumns,column.Source.ColumnName)).ToArray();
        if(physical.Length==0)return null;
        var targets=physical.SelectMany(column=>SingleParentTargets(model,table,page,column,compoundOnly:true))
            .Select(binding=>binding.Target).Concat(CompoundMultiSourceTargets(model,table,page,physical))
            .DistinctBy(target=>NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
                target.Source.ConfigTableId,target.Source.Id),StringComparer.Ordinal).ToArray();
        if(targets.Length>1)throw new NotSupportedException("同一来源页的全局父范围须绑定同一个实际父页面。");
        return targets.SingleOrDefault();
    }

    /// <summary>从固定多值发布元数据证明来源父字段，避免在级联证明中递归编译写入或生成页面。</summary>
    private static IEnumerable<PageModel> CompoundMultiSourceTargets(GenerationModel model,TableModel table,PageModel page,
        IReadOnlyList<ColumnModel> physical)
    {
        if(table.Source.IsFunctionTable!=0||table.Source.IsView!=0)return [];
        var targets=new List<PageModel>();
        foreach(var column in table.Columns.Where(column=>column.Source.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"))
        {
            if(ForeignValidationRenderer.Target(model,column) is not {Source.IsRongyu:0} target||
                table.IsGlobal&&!target.IsGlobal||
                !target.Pages.Any(candidate=>candidate.Scopes.Any(scope=>scope.Source=="parent")))continue;
            var targetPage=MultiRelationTargetPage(model,table,page,column,target);
            if(targetPage is null||PublishedMultiParent(model,target,targetPage) is not {Global:true} parent||
                physical.Any(field=>ForeignValidationRenderer.Target(model,field)?.Source.Id!=parent.Parent.Id))continue;
            var owner=parent.Source(model.Catalog,table.Source,column.Source,page.Source);
            if(owner.AdditionalColumns.Count==0)continue;
            NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,targetPage.Source.Id,owner);
            targets.Add(targetPage);
        }
        return targets;
    }

    /// <summary>在级联生成前读取原控件发布绑定，项目内页面覆盖沿用正常候选的字段优先级。</summary>
    private static PageModel? MultiRelationTargetPage(GenerationModel model,TableModel table,PageModel page,ColumnModel column,TableModel target)
    {
        if(target.IsGlobal)
        {
            var proof=table.Source.IsRongyu==1
                ?GlobalMultipleRelationTarget.RequireCached(model.Catalog,table.Source,column.Source,target.Source)
                :GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,column.Source,target.Source);
            return target.Pages.Single(candidate=>candidate.Source.Id==proof.Page.Id);
        }
        var settings=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>
            field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id);
        var pageId=column.Source.ControlKind=="MultiSelectPage"
            ?SelectionControlDefinition.Resolve(column,settings).PageId
            :DatabaseCheckboxDefinition.Parse(column.Source.ControlDetail)
                .WithPageOverride(settings?.Extra.GetValueOrDefault("controlDetail").ToString()).PageId;
        if(pageId.Length==0)pageId=column.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        return target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==pageId);
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
    private static IEnumerable<(ColumnModel Foreign,PageModel Target)> SingleParentTargets(GenerationModel model,TableModel table,PageModel page,
        ColumnModel source,bool compoundOnly=false)
    {
        if(ForeignValidationRenderer.Target(model,source) is not {IsGlobal:true} parentTable)yield break;
        foreach(var foreign in table.Columns.Where(column=>column.Source.ControlKind is "List" or "SelectPage"&&column.Source.Id!=source.Source.Id))
        {
            if(ForeignValidationRenderer.Target(model,foreign) is not {} target)continue;
            if(target.IsGlobal)
            {
                if(!target.Pages.Any(candidate=>SingleTarget(model,target,candidate)?.Parent.Id==parentTable.Source.Id))
                    continue;
                var proof=RelationCompilation.GlobalTargetForPage(model,table,page,foreign,target);
                if(proof.Parent is {Global:true}&&proof.SourceParent is {} fixedSource)
                {
                    var direct=fixedSource.LogicalPages.Count==0&&fixedSource.Column.Id==source.Source.Id;
                    if(fixedSource.AdditionalColumns.Count>0&&
                        (direct||fixedSource.AdditionalColumns.Any(column=>column.Id==source.Source.Id)))
                    {
                        NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,proof.Page.Id,fixedSource);
                        yield return (foreign,target.Pages.Single(candidate=>candidate.Source.Id==proof.Page.Id));
                    }
                    else if(!compoundOnly&&direct)
                        yield return (foreign,target.Pages.Single(candidate=>candidate.Source.Id==proof.Page.Id));
                }
                continue;
            }
            if(table.IsGlobal||target.Source.IsRongyu!=0)continue;
            var targetPage=ProjectSinglePage(model,page,foreign,target);
            if(targetPage is null||SingleTarget(model,target,targetPage) is not {} parent||
                parent.Parent.Id!=parentTable.Source.Id)continue;
            var sourceBinding=table.Source.IsRongyu==0
                ?parent.SingleSource(model.Catalog,table.Source,foreign.Source,page.Source)
                :parent.SingleSource(model.Catalog,table.Source,foreign.Source);
            var primary=sourceBinding.LogicalPages.Count==0&&sourceBinding.Column.Id==source.Source.Id;
            if(sourceBinding.AdditionalColumns.Count>0&&
                (primary||sourceBinding.AdditionalColumns.Any(column=>column.Id==source.Source.Id)))
            {
                NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,targetPage.Source.Id,sourceBinding);
                yield return (foreign,targetPage);
            }
            else if(!compoundOnly&&primary)
                yield return (foreign,targetPage);
        }
    }

    /// <summary>项目内单选取当前页面的有效配置，下拉沿用原目标页回退规则。</summary>
    private static PageModel? ProjectSinglePage(GenerationModel model,PageModel page,ColumnModel column,TableModel target)
    {
        if(column.Source.ControlKind=="List")return RelationCompilation.ListTargetPage(model,target,page,column,requireExplicit:false);
        var configuration=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>
            field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id);
        var definition=SelectionControlDefinition.Resolve(column,configuration);
        return target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==definition.PageId);
    }

    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
