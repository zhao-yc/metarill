using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
namespace Metarill.Generation;

/// <summary>R0读取与R1缓存自树的派生名称，均须证明固定标量终点和物理结构／范围。</summary>
internal static class DerivedCascadeNamesCompilation
{
    /// <summary>生成候选、图和Excel共用目标读取计划，不能借名称联接放开拓扑或缓存维护。</summary>
    internal static (IReadOnlyList<string> Dependencies, IReadOnlyList<NonRedundantSingleTargetScope> Targets) Require(
        GenerationModel model,TableModel owner,CascadeTreeReadModel plan)
    {
        var target=plan.Target;if(owner.Source.IsRongyu is not(0 or 1)||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||plan.Name.Source.LogicalType!="string"&&!MultiValueLabel.IsTyped(plan.Name.Source.LogicalType)||plan.Name.IsJsonArray)throw new InvalidOperationException("派生自树名称要求已证明来源与普通R0目标的单值标量。");
        // R1读取不能独立于缓存发布：同一控制定义须取得完整字段与派生匹配证明，执行时统一进入维护队列。
        if((plan.DerivedName||plan.ScopedCondition)&&owner.Source.IsRongyu==1&&owner.Source.IsFunctionTable==0){var proof=SelfTreeCache.Compile(model.Catalog,owner.Source.Id,plan.Source.Source.Id,SelfTreePropagationCompilation.PageId(owner,plan));DerivedCrossTableCaches.Validate(model.Catalog,[proof],[]);}
        var fields=plan.TargetPage.Scopes.Select(scope=>scope.Column).Concat(plan.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Append(plan.Parent).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)&&
            plan.TargetPage.Condition?.CrossFields.Contains(column.Source.ColumnName)!=true&&
            plan.TargetPage.Condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true))
            throw new InvalidOperationException("自树的身份和条件必须为物理标量或固定级联路径。");
        // 全局目标名称由固定发布页证明，普通目标继续要求单值匹配计划。
        if(!target.IsGlobal)_=NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,plan.Name.Source.ColumnName);
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(plan.Order.Select(item=>item.Column.Source.ColumnName)).Concat(new[]{"id",plan.Name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var orderFields = JoinedOrderFields(model, plan);
        // 校验时证明排序列的实际类型，发布计划仍仅返回 ID、显示名称和父键。
        var outputs = new[]{ "id", plan.Name.Source.ColumnName, plan.Parent.Source.ColumnName }
            .Concat(orderFields.Select(column => column.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targets = OrderTargets(model, plan);
        if (plan.DerivedName && targets.Count > 0)
        {
            // 同一联接不能把新排序页的条件带入既有显示列，否则候选名称会和持久副本分叉。
            var labelTargets = NonRedundantReadCompilation.ProjectPathTargets(model, target, plan.TargetPage,
                [plan.Name.Source.ColumnName], multiValuePath: true);
            if (targets.Any(order => labelTargets.Any(label => label.ColumnId == order.ColumnId)))
                throw new NotSupportedException("带独立页面条件的排序联接不能改变树的显示名称范围。");
        }
        var read=plan.ScopedCondition
            ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,target.Source.Id,plan.TargetPage.Source.Id,outputs,dependencies,null,targets)
            :NonRedundantReadCompilation.HasGlobalRowProject(target)
            ?NonRedundantReads.CompileGlobalCandidate(model.Catalog,target.Source.Id,plan.TargetPage.Source.Id,outputs,dependencies,targets)
            :targets.Count>0?NonRedundantReads.CompileProjectRelation(model.Catalog,target.Source.Id,outputs,dependencies,targets)
            :NonRedundantReads.Compile(model.Catalog,target.Source.Id,outputs,dependencies);
        if(read.OutputTypes["id"]!="int64"||read.OutputTypes[plan.Name.Source.ColumnName]!=plan.Name.Source.LogicalType)
            throw new InvalidOperationException("自树名称未落到同类型的固定单值列。");
        if(orderFields.Any(column=>read.OutputTypes[column.Source.ColumnName]!=column.Source.LogicalType))
            throw new InvalidOperationException("自树排序列未落到同类型的固定单值列。");
        return (dependencies, targets);
    }

    /// <summary>复用实际目标页的单值联接范围；不从用户排序或名称猜测另一个页面。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> OrderTargets(GenerationModel model, CascadeTreeReadModel plan) =>
        NonRedundantReadCompilation.ProjectPathTargets(model, plan.Target, plan.TargetPage,
            JoinedOrderFields(model, plan).Select(column => column.Source.ColumnName), multiValuePath: true);

    private static ColumnModel[] JoinedOrderFields(GenerationModel model, CascadeTreeReadModel plan) =>
        !plan.DerivedOrder ? [] : plan.Order.Select(item => item.Column)
            .Where(column => column.Source.Id != plan.Name.Source.Id && !NonRedundantReadCompilation.Physical(model, plan.Target, column)).ToArray();
}
