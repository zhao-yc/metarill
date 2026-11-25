using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
namespace Metarill.Generation;

/// <summary>R0读取与R1缓存自树的派生名称，均须证明固定标量终点和物理结构／范围。</summary>
internal static class DerivedCascadeNamesCompilation
{
    /// <summary>生成候选、图和Excel共用目标读取计划，不能借名称联接放开拓扑或缓存维护。</summary>
    internal static (IReadOnlyList<string> Dependencies, IReadOnlyList<string> SortDependencies,
        IReadOnlyList<NonRedundantSingleTargetScope> Targets,IReadOnlyList<NonRedundantSingleTargetScope> SortTargets) Require(
        GenerationModel model,TableModel owner,CascadeTreeReadModel plan)
    {
        var target=plan.Target;if(owner.Source.IsRongyu is not(0 or 1)||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||plan.Name.Source.LogicalType!="string"&&!MultiValueLabel.IsTyped(plan.Name.Source.LogicalType)||plan.Name.IsJsonArray)throw new InvalidOperationException("派生自树名称要求已证明来源与普通R0目标的单值标量。");
        // R1读取不能独立于缓存发布：同一控制定义须取得完整字段与派生匹配证明，执行时统一进入维护队列。
        if((plan.DerivedName||plan.ScopedCondition)&&owner.Source.IsRongyu==1&&owner.Source.IsFunctionTable==0){var proof=SelfTreeCache.Compile(model.Catalog,owner.Source.Id,plan.Source.Source.Id,SelfTreePropagationCompilation.PageId(owner,plan));DerivedCrossTableCaches.Validate(model.Catalog,[proof],[]);}
        var fields=plan.TargetPage.Scopes.Select(scope=>scope.Column).Concat(plan.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Append(plan.Parent).Append(plan.Key).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)&&
            plan.TargetPage.Condition?.CrossFields.Contains(column.Source.ColumnName)!=true&&
            plan.TargetPage.Condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true))
            throw new InvalidOperationException("自树的身份和条件必须为物理标量或固定级联路径。");
        // 项目内的树也要用实际页面证明各段名称，否则无法继承全局字典的发布条件。
        if(!target.IsGlobal)
            _=NonRedundantReads.CompilePublishedScalarMatch(model.Catalog,target.Source.Id,plan.TargetPage.Source.Id,plan.Name.Source.ColumnName);
        return CascadeTargetOrderCompilation.Require(model, target, plan.TargetPage, plan.Name, plan.Parent,
            plan.DerivedName, plan.DerivedOrder, plan.Order, fields);
    }

    /// <summary>保留生成服务排序所需的实际范围页。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> OrderTargets(GenerationModel model, CascadeTreeReadModel plan) =>
        CascadeTargetOrderCompilation.OrderTargets(model, plan.Target, plan.TargetPage, plan.Name, plan.Order, plan.DerivedOrder);
}
