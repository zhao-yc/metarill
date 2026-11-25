using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原跨表读委托按固定层选择非冗余目标计划，候选与当前路径不读IsIgnore名称。</summary>
internal static class NonRedundantCrossTargetsRenderer
{
    /// <summary>名称、父键与排序在生成期证明；授权及固定范围沿原CrossScope。</summary>
    internal static string Service(GenerationModel model,TableModel owner,PageModel page,CrossTableReadModel plan,int index)
    {
        var level=plan.Levels[index];var suffix=page.Source.Id+"_"+plan.Source.Source.Id+"_"+index;var (dependencies,sortDependencies,targets,sortTargets)=NonRedundantCrossTargetsCompilation.Plans(model,level);var outputs=NonRedundantCrossTargetsCompilation.Outputs(level);var parent=level.Parent is not{} parentColumn?"null":parentColumn.Type=="string?"?"NonRedundantReads.Value<string?>(row,"+Literal(parentColumn.Source.ColumnName)+")":"NonRedundantReads.Value<long?>(row,"+Literal(parentColumn.Source.ColumnName)+")?.ToString(CultureInfo.InvariantCulture)";var scopes=level.Page.Scopes.Select(scope=>"        bound.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+(scope.Column.Type=="string?"?"actor.ProjectId":"long.Parse(actor.ProjectId,CultureInfo.InvariantCulture)")+"));");
        var label=MultiSelectExcelRenderer.LabelText(level.Name,$"NonRedundantReads.Value<{level.Name.Type}>(row,{Literal(level.Name.Source.ColumnName)})");
        var scoped=FixedPageConditionCompilation.HasLogicalFields(level.Page);
        var start=scoped?$"CompileCrossPageCandidate({Literal(level.Target.Source.Id)},{Literal(level.Page.Source.Id)}":
            NonRedundantReadCompilation.RelationPlanStart(level.Target,level.Page);
        var separateOrder = sortDependencies.Count > 0;
        var bindings = NonRedundantReadCompilation.SingleTargetArgument(targets, "singleTargets");
        var bindAccount = scoped || level.Target.IsGlobal && FixedPageConditionCompilation.HasAccountVariables(level.Page)
            || targets.Count > 0;
        var orderKey=plan.HasTextKeys?"id":level.KeyColumn;
        var baseOrder = separateOrder ? $"[new({Literal(orderKey)})]" : "CrossTargetOrder" + suffix;
        var keyLiteral=Literal(level.KeyColumn);
        var numberKey=$"NonRedundantReads.Value<long>(row,{keyLiteral})";
        var key=level.TextKey?$"NonRedundantReads.Value<string>(row,{keyLiteral})":numberKey+".ToString(CultureInfo.InvariantCulture)";
        var node=plan.HasTextKeys?"TextCascadeTreeNode":"CascadeTreeNode";
        var nodeData=plan.HasTextKeys?$"{key},NonRedundantReads.Value<long>(row,\"id\")":numberKey;
        var nodes = $$"""rows.Select(row=>new Metarill.Core.Cascades.{{node}}({{nodeData}},{{label}},{{parent}})).ToArray()""";
        var sortOutput = "new string[]{"+Literal(orderKey)+"}";
        var sortReader=plan.HasTextKeys?"TextSelfTreeReader":"CascadeTreeNode";
        var sortPlan = separateOrder ? $$"""
    /// <summary>排序的字典条件独立于原名称投影，候选排序只返回 ID。</summary>
    private static readonly NonRedundantReadPlan CrossTargetSortPlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.{{start}},{{sortOutput}},new string[]{ {{string.Join(",",sortDependencies.Select(Literal))}} }{{NonRedundantReadCompilation.SingleTargetArgument(sortTargets,"singleTargets")}});
""" : "";
        var result = separateOrder ? $$"""
        var labels = {{nodes}};
        if (take is not null) return labels;
        var sorted = await reader.AllRowsAsync(CrossTargetSortPlan{{suffix}}.BindAccount(actor),bound,CrossTargetOrder{{suffix}},ct,actor.ProjectId,condition);
        return Metarill.Core.Cascades.{{sortReader}}.OrderCandidates(labels,sorted.Select(row=>NonRedundantReads.Value<long>(row,{{Literal(orderKey)}})).ToArray());
""" : "        return " + nodes + ";";
        return $$"""
    /// <summary>跨表本级计划只编译一次，不暴露外部字段、排序或SQL。</summary>
    private static readonly NonRedundantReadPlan CrossTargetPlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.{{start}},new string[]{ {{string.Join(",",outputs.Select(Literal))}} },new string[]{ {{string.Join(",",dependencies.Select(Literal))}} }{{bindings}});
{{sortPlan}}
    private static readonly FixedReadOrder[] CrossTargetOrder{{suffix}}=[{{string.Join(",",level.Order.Select(item=>"new("+Literal(item.Column.Source.ColumnName)+","+(item.Descending?"true":"false")+")"))}}];
    /// <summary>公开授权由原逐层工厂执行；身份／项目／条件不能因名称派生而扩大。</summary>
    private async Task<IReadOnlyList<Metarill.Core.Cascades.{{node}}>> CrossTargetRows{{suffix}}Async(RuntimeActor actor,IReadOnlyList<FixedReadFilter> filters,int? take,bool currentRead,CancellationToken ct)
    {
        _=CrossScope{{suffix}}(actor,false);if(currentRead&&take is null)throw new InvalidOperationException("跨表当前路径要求明确的有界读取。");var bound=new List<FixedReadFilter>(filters);
{{string.Join("\n",scopes)}}
        var condition=(FixedConditionSqlPlan?){{(scoped?"null":FixedPageConditionCompilation.Bound(level.Page,"CrossCondition"+suffix))}};
        var plan=CrossTargetPlan{{suffix}}{{(bindAccount?".BindAccount(actor)":"")}};
        var reader=new NonRedundantReader(session);var rows=take is{} limit?(currentRead?await reader.CurrentRowsAsync(plan,bound,{{baseOrder}},limit,ct,actor.ProjectId,condition):await reader.RowsAsync(plan,bound,{{baseOrder}},limit,ct,actor.ProjectId,condition)):await reader.AllRowsAsync(plan,bound,{{baseOrder}},ct,actor.ProjectId,condition);
{{result}}
    }

""";
    }
    /// <summary>单节点路径仍由原CrossTableReader逐层核验。</summary>
    internal static string Find(PageModel page,CrossTableReadModel plan,int index,string overlay="")
    {
        var level=plan.Levels[index];var column=Literal(level.KeyColumn);
        var value=plan.HasTextKeys?"long.Parse(id,CultureInfo.InvariantCulture)":"id";
        var filter=level.TextKey?$"FixedReadFilter.ForTextTree({column},[id])":$"new({column},FixedReadComparison.Equal,{value})";
        var read="return(await CrossTargetRows"+page.Source.Id+"_"+plan.Source.Source.Id+"_"+index+"Async(actor,["+filter+"],1,currentRead,ct)).SingleOrDefault();";
        return "        case "+index+":"+(overlay.Length==0?read:"{"+overlay+read+"}");
    }
    /// <summary>候选保持原完整数组，无COUNT／分页截断；父值仍按声明类型参数化。</summary>
    internal static string Children(PageModel page,CrossTableReadModel plan,int index)
    {
        var filter=plan.Levels[index].Parent is null?"[]":"["+ParentFilter(plan,index)+"]";
        return "        case "+index+":return await CrossTargetRows"+page.Source.Id+"_"+plan.Source.Source.Id+"_"+index+"Async(actor,"+filter+",null,false,ct);";
    }
    /// <summary>文本父键使用精确字节比较，整数父值按目标物理列类型绑定。</summary>
    internal static string ParentFilter(CrossTableReadModel plan,int index)
    {
        var parent=plan.Levels[index].Parent!;
        var column=Literal(parent.Source.ColumnName);
        if(plan.Levels[index-1].TextKey)
            return $"FixedReadFilter.ForTextTree({column},[parent!])";
        var value=parent.Type=="string?"?(plan.HasTextKeys?"parent":"parent?.ToString(CultureInfo.InvariantCulture)"):
            plan.HasTextKeys?"parent is null?(long?)null:long.Parse(parent,CultureInfo.InvariantCulture)":"parent";
        return $"new({column},FixedReadComparison.Equal,{value})";
    }

    /// <summary>批读和 Excel 回显用同一键列及精确比较模式。</summary>
    internal static string BatchFilter(CrossTableReadModel plan,int index)
    {
        var level=plan.Levels[index];
        var column=Literal(level.KeyColumn);
        if(level.TextKey)return $"FixedReadFilter.ForTextTree({column},ids)";
        var key=plan.HasTextKeys?"long.Parse(id,CultureInfo.InvariantCulture)":"id";
        return $"new({column},FixedReadComparison.In,ids.Select(id=>(object?){key}).ToArray())";
    }

    /// <summary>配置标识符必须先成为C#字符串字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
