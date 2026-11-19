using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原复选所有名称用途复用同一固定读取；当前写入沿来源事务锁读。</summary>
internal static class DerivedCheckboxNamesRenderer
{
    /// <summary>静态编译名称联接、排序和物理条件，运行时只绑定实际身份与已声明表单值。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page,DatabaseCheckboxModel def)
    {
        var suffix=DatabaseCheckboxRenderer.Suffix(page,def);var dependencies=DerivedCheckboxNamesCompilation.Require(model,table,def);
        var scopes=def.Scopes.Select(scope=>"        filters.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+Value(scope.Column,scope.Source=="project"?"actor.ProjectId":"parentId!")+"));");
        var variables=def.Conditions is{} condition?RelationConditionsCompilation.Variables(condition,"parentId"):"";
        var pageCondition=(def.PhysicalTargetPage??DerivedCheckboxNamesCompilation.ExactMembershipPage(model,def)) is {Condition:not null} filterPage?
            "        var publishedCondition="+FixedPageConditionCompilation.Bound(filterPage,"CheckboxTargetCondition"+suffix)+
            "; condition=condition is null?publishedCondition:condition.And(publishedCondition);":"";
        var call=DerivedCheckboxNamesCompilation.PlanCall(model,def,dependencies);
        var exact=def.ExactTarget is {} scope?ExactDataManageCompilation.ReadFilter(def.NameTargetPage!,scope,
            Literal("list"),"checkboxExact","filters"):"";
        return $$"""
    /// <summary>只在类型初始化时编译，候选、保存和 Excel 共用固定目标与实际页条件。</summary>
    private static readonly NonRedundantReadPlan DerivedCheckboxPlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.{{call}};
    private sealed record DerivedCheckboxRow{{suffix}}(long Id,{{def.Name.Type}} Name);
    /// <summary>父存在性和来源身份沿原入口验证；名称条件不读取IsIgnore实体属性。</summary>
    private async Task<IReadOnlyList<DerivedCheckboxRow{{suffix}}>> DerivedCheckboxRows{{suffix}}Async(string? parentId,RuntimeActor actor,IReadOnlyDictionary<string,object?> formValues,long[]? ids,{{def.Name.Type}} name,bool matchName,int limit,bool locking,CancellationToken cancellationToken)
    {
        _=await CheckboxScope{{suffix}}Async(parentId,actor,formValues,locking,cancellationToken);

        var filters=new List<FixedReadFilter>();
{{string.Join("\n",scopes)}}
{{exact}}        if(ids is not null){if(ids.Length==0)return [];filters.Add(new("id",FixedReadComparison.In,ids.Select(id=>(object?)id).ToArray()));}
        if(matchName)filters.Add(new({{Literal(def.Name.Source.ColumnName)}},FixedReadComparison.Equal,name));
        var condition={{(def.Conditions is null?"(FixedConditionSqlPlan?)null":"CheckboxCondition"+suffix+".BindVariables(new Dictionary<string,object?>{"+variables+"})")}};
{{pageCondition}}
        var rows=await new NonRedundantReader(session).PageAsync(DerivedCheckboxPlan{{suffix}}.BindAccount(actor),filters,[{{string.Join(",",def.Order.Select(item=>"new("+Literal(item.Column.Source.ColumnName)+","+(item.Descending?"true":"false")+")"))}}],1,limit,cancellationToken,projectId:actor.ProjectId,condition:condition,currentRead:locking);
        return rows.Records.Select(row=>new DerivedCheckboxRow{{suffix}}(NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<{{def.Name.Type}}>(row,{{Literal(def.Name.Source.ColumnName)}}))).ToArray();
    }

""";
    }
    /// <summary>生成字面值先转义，SQL操作符与字段不接受请求扩展。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
    /// <summary>项目／父值按真实物理列类型绑定，避免长ID经过浮点。</summary>
    private static string Value(ColumnModel column,string value)=>column.Type switch{"string?"=>value,"int?"=>$"int.Parse({value},CultureInfo.InvariantCulture)",_=>$"long.Parse({value},CultureInfo.InvariantCulture)"};
}
