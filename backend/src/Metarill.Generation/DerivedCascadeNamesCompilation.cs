using Metarill.Data.Queries;
namespace Metarill.Generation;

/// <summary>独立／同表R0自树派生名称只在固定字符串终点和物理结构／范围全部证明后开放。</summary>
internal static class DerivedCascadeNamesCompilation
{
    /// <summary>生成候选、图和Excel共用目标读取计划，不能借名称联接放开拓扑或缓存维护。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,CascadeTreeReadModel plan)
    {
        var target=plan.Target;if(owner.Source.IsRongyu!=0||target.Source.IsRongyu!=0||target.IsGlobal||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||plan.Name.Type!="string?"||plan.Name.IsJsonArray)throw new InvalidOperationException("派生自树名称要求R0来源与普通R0目标的单值字符串。");
        var fields=plan.TargetPage.Scopes.Select(scope=>scope.Column).Concat(plan.TargetPage.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Append(plan.Parent).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)))throw new InvalidOperationException("派生自树的父结构、身份和条件必须为物理标量。");
        _=NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,plan.Name.Source.ColumnName);
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(plan.Order.Select(item=>item.Column.Source.ColumnName)).Concat(new[]{"id",plan.Name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var read=NonRedundantReads.Compile(model.Catalog,target.Source.Id,["id",plan.Name.Source.ColumnName,plan.Parent.Source.ColumnName],dependencies);
        if(read.OutputTypes["id"]!="int64"||read.OutputTypes[plan.Name.Source.ColumnName]!="string")throw new InvalidOperationException("自树名称未落到已证明的固定单值字符串。");return dependencies;
    }
}
