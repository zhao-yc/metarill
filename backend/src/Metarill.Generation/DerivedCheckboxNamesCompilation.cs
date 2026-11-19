namespace Metarill.Generation;

/// <summary>数据库复选派生名称在发布时固定，不替换原来源授权、父及表单范围。</summary>
internal static class DerivedCheckboxNamesCompilation
{
    /// <summary>只签发普通R0目标的单值名称，条件仍限物理标量；R1缓存同时取得完整维护证明。</summary>
    internal static IReadOnlyList<string> Require(GenerationModel model,TableModel owner,DatabaseCheckboxModel def)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||def.Target.Source.IsRongyu!=0||def.Target.Source.IsView!=0||def.Target.Source.IsFunctionTable!=0||def.Name.Type!="string?"&&!Metarill.Core.Contracts.MultiValueLabel.IsTyped(def.Name.Source.LogicalType)||def.Name.IsJsonArray)throw new InvalidOperationException("派生复选名称要求已证明来源与普通R0目标的固定单值名称。");
        var fields=def.Scopes.Select(scope=>scope.Column).Concat(def.Conditions?.Syntax.Fields.Select(field=>def.Target.Columns.Single(column=>column.Source.ColumnName==field))??[]).DistinctBy(column=>column.Source.Id).ToArray();
        if(fields.Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,def.Target,column)))throw new InvalidOperationException("派生复选的身份、父及附加条件必须为物理标量。");
        var pageFields=def.PhysicalTargetPage?.Condition?.Syntax.Fields??[];
        var dependencies=fields.Select(column=>column.Source.ColumnName)
            .Concat(def.Order.Select(item=>item.Column.Source.ColumnName)).Concat(pageFields)
            .Concat(new[]{"id",def.Name.Source.ColumnName}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var field=def.Name.Source.ColumnName;
        var plan=NonRedundantReadCompilation.CompileMultiLabelPlan(model,def.Target,def.LogicalTargetPage??def.GlobalPage,
            def.NameTargetPage,field,["id",field],dependencies);
        if(plan.OutputTypes["id"]!="int64"||plan.OutputTypes[def.Name.Source.ColumnName]!=def.Name.Source.LogicalType)
            throw new InvalidOperationException("复选名称与固定物理终点类型不一致。");
        // R1不能只开放候选和写入：同一名称数组必须进入统一反向传播及原维护。
        if(owner.Source.IsRongyu==1)Metarill.Data.Relations.DerivedCrossTableCaches.Validate(model.Catalog,[new(owner.Source.Id,def.Source.Source.Id,def.Target.Source.Id,def.Name.Source.Id,def.Names.Source.Id,true)],[]);
        return dependencies;
    }
}
