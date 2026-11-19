using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>原单选弹窗范围由目标页决定，Excel 名称反查在原导入事务中叠加控件条件。</summary>
internal static class SingleSelectionExcelRenderer
{
    /// <summary>先复用发布页的真实父校验，再用类型化值唯一匹配目标记录。</summary>
    internal static string Import(GenerationModel model,RelationModel relation,PageModel page,ImportDependencyPlan dependency)
    {
        var condition=relation.ImportCondition;
        var suffix=page.Source.Id+"_"+relation.SourceColumn.Source.Id;
        var conditionName="SingleImportCondition"+suffix;
        var readName="SingleImportReadPlan"+suffix;
        var declaration=condition is null?"":RelationConditionsCompilation.Declaration(condition,conditionName);
        var fixedRead=condition is not null&&relation.Target.Source.IsRongyu==0;
        if(condition is not null&&fixedRead)
        {
            var target=relation.Target;var targetPage=relation.TargetPage;var field=relation.NameColumn.Source.ColumnName;
            var targets=NonRedundantReadCompilation.ProjectPathTargets(model,target,targetPage,condition.Syntax.Fields.Append(field));
            _=NonRedundantReads.CompileSingleImportLabel(model.Catalog,target.Source.Id,targetPage.Source.Id,field,condition.Syntax.Fields,targets);
            var fields="new string[]{"+string.Join(",",condition.Syntax.Fields.Select(Literal))+"}";
            var args=string.Join(",",new[]{target.Source.Id,targetPage.Source.Id,field}.Select(Literal));
            declaration+=$"    private static readonly NonRedundantReadPlan {readName}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileSingleImportLabel({args},{fields}{NonRedundantReadCompilation.SingleTargetArgument(targets)});\n";
        }
        var mapped=dependency.Parents.GetValueOrDefault(relation.SourceColumn.Source.Id);
        var same=ControlRenderer.RelationContext(page,relation,false).SameParent;
        var parent=mapped is not null
            ?$"        if(!relationContext.TryGetValue({Literal(mapped.JsonName)},out var parentValue)||parentValue.ValueKind==JsonValueKind.Null||string.IsNullOrWhiteSpace(parentValue.ToString()))throw PageImportException.Input(PageImportError.Relation);\n        string? targetParentId=parentValue.ToString();"
            :$"        string? targetParentId={(same?"parentId":"null")};";
        var filters=string.Join("\n",relation.TargetPage.Scopes.Select(scope=>
            "        filters.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+
            ScopeValue(scope.Column,scope.Source=="project"?"actor.ProjectId":"targetParentId!")+"));"));
        var name=relation.NameColumn;
        string Match(string field,string value)=>fixedRead
            ?"filters.Add(new("+Literal(field)+",FixedReadComparison.Equal,"+value+"));"
            :$"query=query.Where(row=>row.{(field=="id"?"Id":name.Property)}=={value});";
        var match=name.Type=="string?"?$$"""
        if(cell.ValueKind==JsonValueKind.Number)
        {
            long id;try{id=cell.GetInt64();}catch(Exception error)when(error is FormatException or OverflowException){throw PageImportException.Input(PageImportError.Number,inner:error);}
            {{Match("id","id")}}
        }
        else
        {
            var label=cell.ToString().Trim();
            {{Match(name.Source.ColumnName,"label")}}
        }
""":$$"""
        if({{MultiSelectExcelRenderer.InvalidLabelKind(name,true,"cell")}})throw PageImportException.Input(PageImportError.Relation);
        {{MultiSelectExcelRenderer.ParseLabel(name,"cell.ToString().Trim()","key")}}
        {{Match(name.Source.ColumnName,"key")}}
""";
        var read=fixedRead?$$"""
        var rows=await new NonRedundantReader(session).PageAsync({{readName}}.BindAccount(actor),filters,[new("id")],
            1,2,cancellationToken,projectId:actor.ProjectId,condition:importCondition,currentRead:true);
        if(rows.Records.Count!=1)throw PageImportException.Input(PageImportError.Relation);
        var idValue=NonRedundantReads.Value<long>(rows.Records[0],"id");
""":$$"""
{{(condition is null?"":"        query=importCondition.ApplyConstants(query,session,\"single_control_import_"+suffix+"\");")}}
        var rows=await query.ForUpdate().Limit(2).ToListAsync(row=>new{row.Id},cancellationToken);
        if(rows.Count!=1)throw PageImportException.Input(PageImportError.Relation);
        var idValue=rows[0].Id;
""";
        return declaration+$$"""
    /// <summary>只按原页面导入权限和已发布范围反查，匹配必须唯一。</summary>
    private async Task<JsonElement> ImportRelation{{suffix}}Async(JsonElement cell,string? parentId,RuntimeActor actor{{(dependency.Contextual?",IReadOnlyDictionary<string,JsonElement> relationContext":"")}},CancellationToken cancellationToken)
    {
        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor||session.CurrentTransaction is null)
            throw new UnauthorizedAccessException("单选导入要求来源真实身份和实际事务。");
        relationAuthorization.RequirePagePermission({{Literal(page.Source.Id)}},"import");
        if(cell.ValueKind==JsonValueKind.Null||string.IsNullOrWhiteSpace(cell.ToString()))return JsonSerializer.SerializeToElement<object?>(null);
{{parent}}
        {{(fixedRead?"_":"var query")}}=await OptionsScope{{suffix}}Async(parentId,targetParentId,actor,cancellationToken,true,false{{relation.ExcelScopeArgument}});
{{(condition is null?"":RelationConditionsCompilation.BindImport(condition,conditionName,"targetParentId"))}}{{(fixedRead?"        var filters=new List<FixedReadFilter>();\n"+filters:"")}}
{{match}}
{{read}}
        return JsonSerializer.SerializeToElement(idValue.ToString(CultureInfo.InvariantCulture));
    }

""";
    }

    /// <summary>实际父键由来源页证明，参数类型沿用目标列声明。</summary>
    private static string ScopeValue(ColumnModel column,string value)=>column.Type switch
    {
        "long?" or "long"=>$"long.Parse({value},CultureInfo.InvariantCulture)",
        "int?" or "int"=>$"int.Parse({value},CultureInfo.InvariantCulture)",
        _=>value
    };

    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
