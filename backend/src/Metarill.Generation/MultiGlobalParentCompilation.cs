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

    /// <summary>单值范围只接受实际目标页声明的全局物理父键。</summary>
    internal static MultiTargetParentScope? PhysicalSingleTarget(GenerationModel model,TableModel table,PageModel page)=>
        GlobalCrossTableTarget.PhysicalSingleParent(model.Catalog,table.Source,page.Source) is {Global:true} parent?parent:null;

    /// <summary>按目标页证明的实际全局父键，原多值引用保持已有同页校验。</summary>
    internal static PageModel? PhysicalTarget(GenerationModel model,TableModel table,PageModel page)
    {
        if(PhysicalSingleTarget(model,table,page) is not null)return page;
        if(!FixedPageConditionCompilation.HasMultiTargetScope(table,page))return null;
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

    /// <summary>独立物理来源父必须实际参与同父多值关系，且同一来源页的关系只能证明同一个目标父范围。</summary>
    internal static PageModel? PhysicalSourceTarget(GenerationModel model,TableModel table,PageModel page)
    {
        var scopes=page.Scopes.Where(scope=>scope.Source=="parent").ToArray();
        if(scopes.Length!=1||NonRedundantCrossParentCompilation.Enabled(model,table,page))return null;
        var source=scopes[0].Column;
        var targets=MultiSelectCompilation.ForService(model,table,page).Where(multi=>multi.GlobalParent&&multi.SameParent)
            .Select(multi=>multi.TargetPage).Concat(DatabaseCheckboxCompilation.ForService(model,table,page)
                .Where(checkbox=>checkbox.GlobalParent).Select(checkbox=>checkbox.LogicalTargetPage!))
            .Concat(SingleParentTargets(model,table,source).Select(binding=>binding.Target))
            .DistinctBy(target=>NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
                target.Source.ConfigTableId,target.Source.Id),StringComparer.Ordinal).ToArray();
        if(targets.Length>1)throw new NotSupportedException("同一来源页的全局父范围须绑定同一个实际父页面。");
        return targets.SingleOrDefault();
    }

    /// <summary>独立表单父键也须有唯一父页；写入只读取该字段已有的物理值。</summary>
    internal static (ColumnModel Foreign,PageModel Target)? SingleParentBinding(GenerationModel model,TableModel table,ColumnModel source)
    {
        var bindings=SingleParentTargets(model,table,source).ToArray();
        if(bindings.Select(binding=>NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
            binding.Target.Source.ConfigTableId,binding.Target.Source.Id)).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new NotSupportedException("同一物理父键须绑定同一个实际全局父页。");
        return bindings.Length==0?null:bindings[0];
    }

    /// <summary>只从表级单值证明取持久父键，避免在关系编译内递归解析生成后的候选。</summary>
    private static IEnumerable<(ColumnModel Foreign,PageModel Target)> SingleParentTargets(GenerationModel model,TableModel table,ColumnModel source)
    {
        if(ForeignValidationRenderer.Target(model,source) is not {IsGlobal:true} parentTable)yield break;
        foreach(var foreign in table.Columns.Where(column=>column.Source.ControlKind is "List" or "SelectPage"&&column.Source.Id!=source.Source.Id))
        {
            if(ForeignValidationRenderer.Target(model,foreign) is not {IsGlobal:true} target||
                !target.Pages.Any(page=>PhysicalSingleTarget(model,target,page)?.Parent.Id==parentTable.Source.Id))
                continue;
            var proof=table.Source.IsRongyu==1
                ?GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,foreign.Source,target.Source)
                :GlobalSingleRelationTarget.Require(model.Catalog,table.Source,foreign.Source,target.Source);
            if(proof.PhysicalParent is not {Global:true} parent||
                parent.Source(model.Catalog,table.Source,foreign.Source,physicalSingle:true).Column.Id!=source.Source.Id)
                continue;
            yield return (foreign,target.Pages.Single(page=>page.Source.Id==proof.Page.Id));
        }
    }

    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
