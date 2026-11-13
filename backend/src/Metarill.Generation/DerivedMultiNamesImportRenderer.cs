using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原多选Excel名称数组只替换目标读取方式，来源import事务、表头和权限保持。</summary>
internal static class DerivedMultiNamesImportRenderer
{
    /// <summary>固定目标范围按原父依赖绑定，名称当前读逐项唯一定位，任一失败整批撤销。</summary>
    internal static string Render(GenerationModel model,TableModel table,PageModel page,MultiSelectModel multi,ImportDependencyPlan dependency)
    {
        DerivedMultiNamesCompilation.Require(model,table,multi.Target,multi.TargetPage,multi.Name);
        var suffix=page.Source.Id+"_"+multi.Source.Source.Id;var parent=multi.TargetParentField is not null?dependency.Parents[multi.Source.Source.Id]:null;
        var scopes=multi.TargetPage.Scopes.Select(scope=>"            filters.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+Value(scope.Column,scope.Source=="project"?"actor.ProjectId":"targetParentId!")+"));");
        return $$"""
    /// <summary>名称数组使用固定联接计划，不查询忽略持久化的实体名称属性。</summary>
    private async Task<JsonElement> ImportMulti{{suffix}}Async(JsonElement cell,string? parentId,RuntimeActor actor{{(dependency.Contextual?",IReadOnlyDictionary<string,JsonElement> relationContext":"")}},CancellationToken cancellationToken)
    {
        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor||session.CurrentTransaction is null)throw new UnauthorizedAccessException("多选导入要求真实身份和来源事务。");
        relationAuthorization.RequirePagePermission({{Literal(page.Source.Id)}},"import");
        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);
        if(cell.ValueKind==JsonValueKind.String){try{cell=JsonSerializer.Deserialize<JsonElement>(cell.GetString()!);}catch(JsonException error){throw PageImportException.Input(PageImportError.Array,inner:error);} }
        if(cell.ValueKind!=JsonValueKind.Array||cell.GetArrayLength()>200)throw PageImportException.Input(PageImportError.Array);
{{(parent is null?"":"        if(!relationContext.TryGetValue("+Literal(parent.JsonName)+",out var parentValue)||parentValue.ValueKind==JsonValueKind.Null)throw PageImportException.Input(PageImportError.Relation);")}}
{{(multi.TargetParents.Count==0?"":"        var targetParentId="+(parent is null?"parentId":"parentValue.ToString()")+";if(!long.TryParse(targetParentId,NumberStyles.None,CultureInfo.InvariantCulture,out var parentKey)||parentKey<=0)throw PageImportException.Input(PageImportError.Relation);")}}
        var selected=new List<string>();
        foreach(var item in cell.EnumerateArray())
        {
            if(item.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))throw PageImportException.Input(PageImportError.Relation);
            var filters=new List<FixedReadFilter>{new({{Literal(multi.Name.Source.ColumnName)}},FixedReadComparison.Equal,item.ToString())};
{{string.Join("\n",scopes)}}
            var matches=await new NonRedundantReader(session).PageAsync(MultiDerivedPlan{{suffix}},filters,[new("id")],1,2,cancellationToken,projectId:actor.ProjectId{{(multi.TargetPage.Condition is null?"":",condition:MultiPageCondition"+suffix)}},currentRead:true);
            if(matches.Records.Count!=1)throw PageImportException.Input(PageImportError.Relation);
            var value=NonRedundantReads.Value<long>(matches.Records[0],"id").ToString(CultureInfo.InvariantCulture);if(selected.Contains(value))throw PageImportException.Input(PageImportError.Relation);selected.Add(value);
        }
        return JsonSerializer.SerializeToElement(selected);
    }

""";
    }
    /// <summary>只绑定物理列已发布的逻辑类型，长ID不经浮点。</summary>
    private static string Value(ColumnModel column,string value)=>column.Type switch{"string?"=>value,"int?"=>$"int.Parse({value},CultureInfo.InvariantCulture)",_=>$"long.Parse({value},CultureInfo.InvariantCulture)"};
    /// <summary>固定配置只生成转义字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
