using Metarill.Data.Queries;
namespace Metarill.Generation;

/// <summary>数据库复选派生名称在发布时固定，不替换原来源授权、父及表单范围。</summary>
internal static class DerivedCheckboxNamesCompilation
{
    /// <summary>只签发普通R0目标的单值字符串，条件仍限物理标量；R1缓存同时取得完整维护证明。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,DatabaseCheckboxModel def)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||def.Target.Source.IsRongyu!=0||def.Target.IsGlobal||def.Target.Source.IsView!=0||def.Target.Source.IsFunctionTable!=0||def.Name.Type!="string?"||def.Name.IsJsonArray)throw new InvalidOperationException("派生复选名称要求已证明来源与普通R0目标的固定字符串。");
        var fields=def.Scopes.Select(scope=>scope.Column).Concat(def.Conditions?.Syntax.Fields.Select(field=>def.Target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,def.Target,column)))throw new InvalidOperationException("派生复选的身份、父及附加条件必须为物理标量。");
        var dependencies=fields.Select(column=>column.Source.ColumnName).Concat(def.Order.Select(item=>item.Column.Source.ColumnName)).Concat(new[]{"id",def.Name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan=NonRedundantReads.Compile(model.Catalog,def.Target.Source.Id,["id",def.Name.Source.ColumnName],dependencies);
        if(plan.OutputTypes["id"]!="int64"||plan.OutputTypes[def.Name.Source.ColumnName]!="string")throw new InvalidOperationException("复选名称必须落到固定单值字符串终点。");
        // R1不能只开放候选和写入：同一名称数组必须进入统一反向传播及原维护。
        if(owner.Source.IsRongyu==1)Metarill.Data.Relations.DerivedCrossTableCaches.Validate(model.Catalog,[new(owner.Source.Id,def.Source.Source.Id,def.Target.Source.Id,def.Name.Source.Id,def.Names.Source.Id,true)],[]);
        return dependencies;
    }
}
