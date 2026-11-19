using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
namespace Metarill.Generation;

/// <summary>R0读取与R1缓存自树的派生名称，均须证明固定标量终点和物理结构／范围。</summary>
internal static class DerivedCascadeNamesCompilation
{
    /// <summary>生成候选、图和Excel共用目标读取计划，不能借名称联接放开拓扑或缓存维护。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,CascadeTreeReadModel plan)
    {
        var target=plan.Target;if(owner.Source.IsRongyu is not(0 or 1)||target.Source.IsRongyu!=0||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||plan.Name.Source.LogicalType!="string"&&!MultiValueLabel.IsTyped(plan.Name.Source.LogicalType)||plan.Name.IsJsonArray)throw new InvalidOperationException("派生自树名称要求已证明来源与普通R0目标的单值标量。");
        // R1读取不能独立于缓存发布：同一控制定义须取得完整字段与派生匹配证明，执行时统一进入维护队列。
        if(owner.Source.IsRongyu==1&&owner.Source.IsFunctionTable==0){var proof=SelfTreeCache.Compile(model.Catalog,owner.Source.Id,plan.Source.Source.Id,SelfTreePropagationCompilation.PageId(owner,plan));DerivedCrossTableCaches.Validate(model.Catalog,[proof],[]);}
        var fields=plan.TargetPage.Scopes.Select(scope=>scope.Column).Concat(plan.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Append(plan.Parent).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)&&
            plan.TargetPage.Condition?.CrossFields.Contains(column.Source.ColumnName)!=true&&
            plan.TargetPage.Condition?.SelfTreeFields.Contains(column.Source.ColumnName)!=true))
            throw new InvalidOperationException("自树的身份和条件必须为物理标量或固定级联路径。");
        // 全局目标名称由固定发布页证明，普通目标继续要求单值匹配计划。
        if(!target.IsGlobal)_=NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,plan.Name.Source.ColumnName);
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(plan.Order.Select(item=>item.Column.Source.ColumnName)).Concat(new[]{"id",plan.Name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] outputs=["id",plan.Name.Source.ColumnName,plan.Parent.Source.ColumnName];
        var read=plan.ScopedCondition
            ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,target.Source.Id,plan.TargetPage.Source.Id,outputs,dependencies)
            :NonRedundantReadCompilation.HasGlobalRowProject(target)
            ?NonRedundantReads.CompileGlobalCandidate(model.Catalog,target.Source.Id,plan.TargetPage.Source.Id,outputs,dependencies)
            :NonRedundantReads.Compile(model.Catalog,target.Source.Id,outputs,dependencies);
        if(read.OutputTypes["id"]!="int64"||read.OutputTypes[plan.Name.Source.ColumnName]!=plan.Name.Source.LogicalType)
            throw new InvalidOperationException("自树名称未落到同类型的固定单值列。");
        return dependencies;
    }
}
