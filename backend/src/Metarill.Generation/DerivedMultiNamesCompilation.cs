using Metarill.Data.Queries;
namespace Metarill.Generation;

/// <summary>多选派生名称只在完整目标标量计划和物理范围均已证明时开放。</summary>
internal static class DerivedMultiNamesCompilation
{
    /// <summary>实际物理名称继续走原实现，不因联接标记猜测存储模式。</summary>
    internal static bool Declared(GenerationModel model,TableModel target,ColumnModel name)=>!NonRedundantReadCompilation.Physical(model,target,name);
    /// <summary>发布时编译目标名称和范围，不能退化为查询IsIgnore实体属性。</summary>
    internal static void Require(GenerationModel model,TableModel owner,TableModel target,PageModel page,ColumnModel name)
    {
        if(owner.Source.IsRongyu is not(0 or 1)||target.Source.IsRongyu!=0||target.IsGlobal||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||name.Type!="string?"||name.IsJsonArray||ExactDataManageCompilation.Declared(page)||NonRedundantCrossParentCompilation.Enabled(model,target,page))throw new InvalidOperationException("派生多选名称要求已证明来源、普通R0目标和固定单值字符串。");
        if(page.Scopes.Select(scope=>scope.Column).Concat(page.Condition?.Syntax.Fields.Select(field=>target.Columns.Single(column=>column.Source.ColumnName==field))??[]).Any(column=>column.IsJsonArray||!NonRedundantReadCompilation.Physical(model,target,column)))throw new InvalidOperationException("派生多选名称的候选身份、父和条件必须是物理标量。");
        var plan=NonRedundantReads.CompileMatch(model.Catalog,target.Source.Id,name.Source.ColumnName);
        if(plan.OutputTypes[name.Source.ColumnName]!="string")throw new InvalidOperationException("派生多选名称未落到固定字符串标量。");
    }
    /// <summary>原Excel按固定名称精确反查，读计划仅静态初始化时编译。</summary>
    internal static string Declaration(GenerationModel model,PageModel page,MultiSelectModel multi)=>!Declared(model,multi.Target,multi.Name)?"":"    /// <summary>多选导入名称的固定目标计划，普通请求只绑定值。</summary>\n    private static readonly Metarill.Data.Queries.NonRedundantReadPlan MultiDerivedPlan"+page.Source.Id+"_"+multi.Source.Source.Id+"=global::"+model.Namespace+".Generated.GeneratedNonRedundantReadCatalog.CompileRelation("+System.Text.Json.JsonSerializer.Serialize(multi.Target.Source.Id)+",new string[]{\"id\","+System.Text.Json.JsonSerializer.Serialize(multi.Name.Source.ColumnName)+"},new string[]{"+string.Join(",",multi.TargetPage.Scopes.Select(scope=>scope.Column.Source.ColumnName).Concat(multi.TargetPage.Condition?.Syntax.Fields??[]).Distinct().Select(field=>System.Text.Json.JsonSerializer.Serialize(field)))+"});\n";
}
