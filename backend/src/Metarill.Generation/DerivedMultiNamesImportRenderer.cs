using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原多选Excel名称数组只替换目标读取方式，来源import事务、表头和权限保持。</summary>
internal static class DerivedMultiNamesImportRenderer
{
    /// <summary>固定目标范围按原父依赖绑定，名称当前读逐项唯一定位，任一失败整批撤销。</summary>
    internal static string Render(GenerationModel model,TableModel table,PageModel page,MultiSelectModel multi,ImportDependencyPlan dependency)
    {
        var logicalImport=multi.ImportCondition?.HasLogicalFields==true;
        if(!logicalImport)DerivedMultiNamesCompilation.Require(model,table,multi.Target,multi.TargetPage,multi.Name,multi.ExactTarget);
        var suffix=page.Source.Id+"_"+multi.Source.Source.Id;var parent=multi.TargetParentField is not null?dependency.Parents[multi.Source.Source.Id]:null;
        var typed=multi.TypedLabel||multi.Name.IsId;
        var canonicalParent=multi.PublishedPhysicalSource?"||targetParentId!=parentKey.ToString(CultureInfo.InvariantCulture)":"";
        var readPlan=(logicalImport?"MultiImportReadPlan":"MultiDerivedPlan")+suffix;
        var targetCondition=logicalImport||multi.ExactTarget is not null||multi.TargetPage.Condition is null||FixedPageConditionCompilation.HasMultiTargetScope(model,multi.Target,multi.TargetPage)
            ?null:FixedPageConditionCompilation.Bound(multi.TargetPage,"MultiPageCondition"+suffix);
        var condition=multi.ImportCondition is null?targetCondition:
            targetCondition is null?"importCondition":"importCondition.And("+targetCondition+")";
        var scopes=multi.TargetPage.Scopes.Select(scope=>
            $"            filters.Add({MultiSelectFieldRenderer.FixedScopeFilter(multi,scope.Column,scope.Source)});");
        var exact=multi.ExactTarget is {} proof
            ?ExactDataManageCompilation.ReadFilter(multi.TargetPage,proof,Literal("list"),"multiImportExact","filters"):"";
        var validateParent=multi.SharedTextParent is not null
            ?multi.LogicalSourceParent?NonRedundantCrossParentCompilation.ValidateTextParent(model,table,page,"parentId"):
                SharedRedundantParentCompilation.ValidateTextParent(model,table,page,"parentId")
            :multi.TargetParents.Count==0?"":"        var targetParentId="+(parent is null?"parentId":"parentValue.ToString()")+";if(!long.TryParse(targetParentId,NumberStyles.None,CultureInfo.InvariantCulture,out var parentKey)||parentKey<=0"+canonicalParent+")throw PageImportException.Input(PageImportError.Relation);";
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
{{validateParent}}
{{MultiSelectExcelRenderer.ImportConditionBinding(multi,suffix)}}        var selected=new List<string>();
        foreach(var item in cell.EnumerateArray())
        {
            if({{MultiSelectExcelRenderer.InvalidLabelKind(multi.Name,true)}})throw PageImportException.Input(PageImportError.Relation);
{{(typed?"            "+MultiSelectExcelRenderer.ParseLabel(multi.Name,"item.ToString()","name"):"")}}
{{(multi.Name.IsId?"            if(name<=0)throw PageImportException.Input(PageImportError.Relation);":"")}}
            var filters=new List<FixedReadFilter>{new({{Literal(multi.Name.Source.ColumnName)}},FixedReadComparison.Equal,{{(typed?"name":"item.ToString()")}})};
{{string.Join("\n",scopes)}}
{{exact}}            var matches=await new NonRedundantReader(session).PageAsync({{readPlan}}.BindAccount(actor),filters,[new("id")],1,2,cancellationToken,projectId:actor.ProjectId{{(condition is null?"":",condition:"+condition)}},currentRead:true);
            if(matches.Records.Count!=1)throw PageImportException.Input(PageImportError.Relation);
            var value=NonRedundantReads.Value<long>(matches.Records[0],"id").ToString(CultureInfo.InvariantCulture);if(selected.Contains(value))throw PageImportException.Input(PageImportError.Relation);selected.Add(value);
        }
        return JsonSerializer.SerializeToElement(selected);
    }

""";
    }
    /// <summary>固定配置只生成转义字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
