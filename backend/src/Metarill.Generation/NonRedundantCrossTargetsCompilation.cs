using Metarill.Data.Queries;
namespace Metarill.Generation;

/// <summary>跨表目标按每层真实物理结构及固定单值名称证明，不能只放开存储模式门禁。</summary>
internal static class NonRedundantCrossTargetsCompilation
{
    /// <summary>目标读取共用单值证明；R0不物化名称，R1副本另由完整维护／传播校验。</summary>
    internal static void Require(GenerationModel model,TableModel owner,CrossTableReadModel plan)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||owner.IsGlobal||plan.Levels.Any(level=>level.Target.Source.IsView!=0||level.Target.Source.IsFunctionTable!=0))throw new InvalidOperationException("跨表目标要求普通实体来源及目标，R1缓存由独立完整依赖证明。");
        foreach(var level in plan.Levels.Where(level=>level.Target.IsGlobal))Metarill.Core.Metadata.GlobalCrossTableTarget.Require(model.Catalog,level.Target.Source,level.Page.Source);
        foreach(var level in plan.Levels.Where(level=>level.Target.Source.IsRongyu==0))_=Dependencies(model,level);
    }
    /// <summary>名称可派生，父键、身份、条件及其它排序必须是已证明物理标量。</summary>
    internal static IReadOnlyList<string> Dependencies(GenerationModel model,CrossTableLevelModel level)
    {
        var target=level.Target;var fields=level.Page.Scopes.Select(scope=>scope.Column).Concat(level.Page.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Concat(level.Parent is{} parent?new[]{parent}:[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)||GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")))throw new InvalidOperationException("非冗余跨表父键、身份和条件必须为物理标量。");
        _=NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,level.Name.Source.ColumnName);
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(level.Order.Select(item=>item.Column.Source.ColumnName)).Append("id").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var outputs=new[]{"id",level.Name.Source.ColumnName}.Concat(level.Parent is{} p?new[]{p.Source.ColumnName}:[]).ToArray();var read=NonRedundantReads.Compile(model.Catalog,target.Source.Id,outputs,dependencies);if(read.OutputTypes["id"]!="int64"||read.OutputTypes[level.Name.Source.ColumnName]!="string")throw new InvalidOperationException("跨表名称必须落到非凭据单值字符串。");return dependencies;
    }
}
