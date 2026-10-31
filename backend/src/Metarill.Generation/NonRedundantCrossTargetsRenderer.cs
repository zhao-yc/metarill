using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原跨表读委托按固定层选择非冗余目标计划，候选与当前路径不读IsIgnore名称。</summary>
internal static class NonRedundantCrossTargetsRenderer
{
    /// <summary>名称、父键与排序在生成期证明；授权及固定范围沿原CrossScope。</summary>
    internal static string Service(GenerationModel model,TableModel owner,PageModel page,CrossTableReadModel plan,int index)
    {
        var level=plan.Levels[index];var suffix=page.Source.Id+"_"+plan.Source.Source.Id+"_"+index;var dependencies=NonRedundantCrossTargetsCompilation.Dependencies(model,level);var outputs=new[]{"id",level.Name.Source.ColumnName}.Concat(level.Parent is{} p?new[]{p.Source.ColumnName}:[]).ToArray();var parent=level.Parent is not{} parentColumn?"null":parentColumn.Type=="string?"?"NonRedundantReads.Value<string?>(row,"+Literal(parentColumn.Source.ColumnName)+")":"NonRedundantReads.Value<long?>(row,"+Literal(parentColumn.Source.ColumnName)+")?.ToString(CultureInfo.InvariantCulture)";var scopes=level.Page.Scopes.Select(scope=>"        bound.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+(scope.Column.Type=="string?"?"actor.ProjectId":"long.Parse(actor.ProjectId,CultureInfo.InvariantCulture)")+"));");
        return $$"""
    /// <summary>跨表本级计划只编译一次，不暴露外部字段、排序或SQL。</summary>
    private static readonly NonRedundantReadPlan CrossTargetPlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.CompileRelation({{Literal(level.Target.Source.Id)}},new string[]{ {{string.Join(",",outputs.Select(Literal))}} },new string[]{ {{string.Join(",",dependencies.Select(Literal))}} });
    private static readonly FixedReadOrder[] CrossTargetOrder{{suffix}}=[{{string.Join(",",level.Order.Select(item=>"new("+Literal(item.Column.Source.ColumnName)+","+(item.Descending?"true":"false")+")"))}}];
    /// <summary>公开授权由原逐层工厂执行；身份／项目／条件不能因名称派生而扩大。</summary>
    private async Task<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeNode>> CrossTargetRows{{suffix}}Async(RuntimeActor actor,IReadOnlyList<FixedReadFilter> filters,int? take,bool currentRead,CancellationToken ct)
    {
        _=CrossScope{{suffix}}(actor,false);if(currentRead&&take is null)throw new InvalidOperationException("跨表当前路径要求明确的有界读取。");var bound=new List<FixedReadFilter>(filters);
{{string.Join("\n",scopes)}}
        var reader=new NonRedundantReader(session);var rows=take is{} limit?(currentRead?await reader.CurrentRowsAsync(CrossTargetPlan{{suffix}},bound,CrossTargetOrder{{suffix}},limit,ct,actor.ProjectId,{{(level.Page.Condition is null?"null":"CrossCondition"+suffix)}}):await reader.RowsAsync(CrossTargetPlan{{suffix}},bound,CrossTargetOrder{{suffix}},limit,ct,actor.ProjectId,{{(level.Page.Condition is null?"null":"CrossCondition"+suffix)}})):await reader.AllRowsAsync(CrossTargetPlan{{suffix}},bound,CrossTargetOrder{{suffix}},ct,actor.ProjectId,{{(level.Page.Condition is null?"null":"CrossCondition"+suffix)}});
        return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(level.Name.Source.ColumnName)}}),{{parent}})).ToArray();
    }

""";
    }
    /// <summary>单节点路径仍由原CrossTableReader逐层核验。</summary>
    internal static string Find(PageModel page,CrossTableReadModel plan,int index,string overlay="")
    {
        var read="return(await CrossTargetRows"+page.Source.Id+"_"+plan.Source.Source.Id+"_"+index+"Async(actor,[new(\"id\",FixedReadComparison.Equal,id)],1,currentRead,ct)).SingleOrDefault();";
        return "        case "+index+":"+(overlay.Length==0?read:"{"+overlay+read+"}");
    }
    /// <summary>候选保持原完整数组，无COUNT／分页截断；父值仍按声明类型参数化。</summary>
    internal static string Children(PageModel page,CrossTableReadModel plan,int index)
    {
        var parent=plan.Levels[index].Parent;var filter=parent is null?"[]":"[new("+Literal(parent.Source.ColumnName)+",FixedReadComparison.Equal,"+(parent.Type=="string?"?"parent?.ToString(CultureInfo.InvariantCulture)":"parent")+")]";
        return "        case "+index+":return await CrossTargetRows"+page.Source.Id+"_"+plan.Source.Source.Id+"_"+index+"Async(actor,"+filter+",null,false,ct);";
    }
    /// <summary>配置标识符必须先成为C#字符串字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
