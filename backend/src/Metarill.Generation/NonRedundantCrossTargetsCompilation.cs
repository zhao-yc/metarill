using Metarill.Data.Queries;
namespace Metarill.Generation;

/// <summary>跨表目标按每层真实物理结构及固定单值名称证明，不能只放开存储模式门禁。</summary>
internal static class NonRedundantCrossTargetsCompilation
{
    /// <summary>目标读取共用单值证明；R0不物化名称，R1副本另由完整维护／传播校验。</summary>
    internal static void Require(GenerationModel model,TableModel owner,CrossTableReadModel plan)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||owner.IsGlobal&&owner.Source.IsRongyu!=0||plan.Levels.Any(level=>level.Target.Source.IsView!=0||level.Target.Source.IsFunctionTable!=0))throw new InvalidOperationException("跨表目标要求普通实体来源及目标，R1缓存由独立完整依赖证明。");
        if(owner.IsGlobal)Metarill.Core.Metadata.GlobalDerivedRelationSource.RequireTable(model.Catalog,owner.Source);
        foreach(var level in plan.Levels.Where(level=>level.Target.IsGlobal))RequireGlobal(model,level.Target,level.Page,level.Name);
        foreach(var level in plan.Levels.Where(level=>level.Target.Source.IsRongyu==0))_=Plans(model,level);
    }
    /// <summary>全局候选只从已发布目标取名称，逻辑叶名需要单独证明。</summary>
    internal static void RequireGlobal(GenerationModel model,TableModel target,PageModel page,ColumnModel name)
    {
        if(FixedPageConditionCompilation.HasSelfTreeFields(page))
            Metarill.Core.Metadata.GlobalCrossTableTarget.RequireScopedSelfTreeName(model.Catalog,target.Source,page.Source,name.Source);
        else Metarill.Core.Metadata.GlobalCrossTableTarget.RequireCrossTableCandidate(model.Catalog,target.Source,page.Source,name.Source);
    }

    /// <summary>名称、父键、账号归属及逻辑条件分别走实际固定字段证明。</summary>
    internal static (IReadOnlyList<string> Dependencies, IReadOnlyList<string> SortDependencies,
        IReadOnlyList<NonRedundantSingleTargetScope> Targets,IReadOnlyList<NonRedundantSingleTargetScope> SortTargets) Plans(GenerationModel model,CrossTableLevelModel level)
    {
        var target=level.Target;var fields=level.Page.Scopes.Select(scope=>scope.Column).Concat(level.Page.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Concat(level.Parent is{} parent?new[]{parent}:[]).DistinctBy(column=>column.Source.Id).ToArray();
        var condition=level.Page.Condition;
        if(fields.Any(column=>column.IsJsonArray||
            !NonRedundantReadCompilation.Physical(model,target,column)&&condition?.CrossFields.Contains(column.Source.ColumnName)!=true&&
                condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true||
            GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")&&condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true))
            throw new InvalidOperationException("非冗余跨表父键和身份必须为物理标量，逻辑条件须来自已证明的级联。");
        // 项目内层位沿实际发布页证明下游关系，保留全局字典的页级覆盖。
        if(!target.IsGlobal)
            _=NonRedundantReads.CompilePublishedScalarMatch(model.Catalog,target.Source.Id,level.Page.Source.Id,level.Name.Source.ColumnName);
        if (level.DerivedOrder)
            return CascadeTargetOrderCompilation.Require(model, target, level.Page, level.Name, level.Parent,
                level.DerivedName, level.DerivedOrder, level.Order, fields);
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(level.Order.Select(item=>item.Column.Source.ColumnName))
            .Append("id").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputs=new[]{"id",level.Name.Source.ColumnName}.Concat(level.Parent is{} p?new[]{p.Source.ColumnName}:[]).ToArray();
        // 级联候选及运行时共用实际目标页的名称路径和条件。
        var targets=NonRedundantReadCompilation.ProjectPathTargets(model,target,level.Page,outputs.Concat(dependencies));
        var read=FixedPageConditionCompilation.HasLogicalFields(level.Page)
            ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,target.Source.Id,level.Page.Source.Id,outputs,dependencies,null,targets)
            :NonRedundantReadCompilation.HasGlobalRowProject(target)
            ?NonRedundantReads.CompileGlobalCandidate(model.Catalog,target.Source.Id,level.Page.Source.Id,outputs,dependencies,targets)
            :targets.Count>0?NonRedundantReads.CompileProjectRelation(model.Catalog,target.Source.Id,outputs,dependencies,targets)
            :NonRedundantReads.Compile(model.Catalog,target.Source.Id,outputs,dependencies);
        if(read.OutputTypes["id"]!="int64"||read.OutputTypes[level.Name.Source.ColumnName]!=level.Name.Source.LogicalType)
            throw new InvalidOperationException("跨表名称必须落到同类型的固定单值。");
        return (dependencies, [], targets, []);
    }
}
