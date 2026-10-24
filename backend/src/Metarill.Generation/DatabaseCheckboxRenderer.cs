using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>原嵌入式复选的候选与最终写入共用固定范围，不借用弹层目标页或客户端SQL。</summary>
internal static class DatabaseCheckboxRenderer
{
    /// <summary>原候选入口按来源页查询／新增／修改任一开放动作授权，保留四字段数组。</summary>
    internal static string Controller(GenerationModel model,TableModel table,PageModel page)=>string.Concat(DatabaseCheckboxCompilation.ForPage(model,table,page).Select(def=>$$"""
    /// <summary>完整有界候选用于原全选，指定空ID只回读空集合。</summary>
    [HttpPost("{{def.Endpoint}}")] [RequestSizeLimit(262144)]
    public async Task<R<IReadOnlyList<DatabaseCheckboxOption>>> {{def.Endpoint}}([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] System.Text.Json.JsonElement? body, CancellationToken cancellationToken)
    {
        var actor=RelationOptionAuthorization.Require(authorization,"{{page.Source.Id}}",{{Bool(page.Source.IsNew==1)}},{{Bool(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source))}});
        if(Request.Query.Count!=0)throw new ArgumentException("数据库复选候选不接受query参数。");
        var request=DatabaseCheckboxOptionsRequest.Read(body);
        return R<IReadOnlyList<DatabaseCheckboxOption>>.Success(await service.CheckboxOptions{{Suffix(page,def)}}Async(request,actor,cancellationToken));
    }

"""));

    /// <summary>全部关系字段含隐藏字段都保留内部校验，公开候选只为本页已开放字段生成。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();var exposed=DatabaseCheckboxCompilation.ForPage(model,table,page).Select(def=>def.Source.Source.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var def in DatabaseCheckboxCompilation.ForService(model,table,page))
        {
            var suffix=Suffix(page,def);var project=def.Target.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id");
            result.Append(Declaration(def,suffix)).Append(Reader(def,suffix));
            if(def.DerivedName)result.Append(DerivedCheckboxNamesRenderer.Service(model,table,page,def));
            result.AppendLine("    /// <summary>来源身份、真实父和发布条件始终进入数据库；写校验使用当前事务锁读。</summary>")
                .AppendLine($"    private async Task<ISelect<{def.Target.Name}>> CheckboxScope{suffix}Async(string? parentId,RuntimeActor actor,IReadOnlyDictionary<string,object?> formValues,bool locking,CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine("        await Task.CompletedTask;")
                .AppendLine("        ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"数据库复选要求真实当前身份。\");")
                .AppendLine("        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException(\"复选最终校验必须加入实际写事务。\");");
            if(def.SourceParent is null)result.AppendLine("        if(!string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前复选来源未声明父范围。\");");
            else
            {
                result.AppendLine("        if(!long.TryParse(parentId,NumberStyles.None,CultureInfo.InvariantCulture,out var parentKey)||parentKey<=0||parentId!=parentKey.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"来源父ID必须为规范正整数字符串。\");");
                foreach(var parent in def.SourceParents)
                {
                    var parentProject=parent.Columns.Single(column=>column.Source.ColumnName=="yz_project_id");
                    result.AppendLine($"        var parentQuery=session.Select<{parent.Name}>().Where(row=>row.Id==parentKey&&row.{parentProject.Property}=={Scope(parentProject,"actor.ProjectId")});");
                    if(parent.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("        parentQuery=parentQuery.Where(row=>row.DeleteNo==null);");
                    result.AppendLine("        if(locking)parentQuery=parentQuery.ForUpdate();")
                        .AppendLine("        if(!await parentQuery.AnyAsync(cancellationToken))throw new InvalidOperationException(\"复选来源父不存在、已删除或不属于当前项目。\");");
                }
            }
            result.AppendLine(def.Target.IsGlobal?$"        var query=session.Select<{def.Target.Name}>();":$"        var query=session.Select<{def.Target.Name}>().Where(row=>row.{project!.Property}=={Scope(project,"actor.ProjectId")});");
            if(def.Target.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("        query=query.Where(row=>row.DeleteNo==null);");
            foreach(var (column,source) in def.Scopes)result.AppendLine($"        query=query.Where(row=>row.{column.Property}=={Scope(column,source=="project"?"actor.ProjectId":"parentId!")});");
            if(def.Conditions is {} condition)
            {
                var variables=string.Join(", ",condition.Syntax.Variables.Select(variable=>"["+Literal(variable)+"] = "+(variable.StartsWith("formParameters.",StringComparison.Ordinal)?"Metarill.Core.Contracts.FixedConditionFormValue.Require(formValues,"+Literal(variable)+")":variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"actor.ProjectId":"parentId")));
                result.AppendLine($"        query=CheckboxCondition{suffix}.Apply(query,session,new Dictionary<string,object?> {{ {variables} }},\"checkbox_{suffix}\");");
            }
            result.AppendLine("        return locking?query.ForUpdate():query;").AppendLine("    }");
            if(exposed.Contains(def.Source.Source.Id))
            {
                result.AppendLine("    /// <summary>全选不得截断；父验证和候选在同一读快照，权限在服务入口再次核对。</summary>")
                    .AppendLine($"    public Task<IReadOnlyList<DatabaseCheckboxOption>> CheckboxOptions{suffix}Async(DatabaseCheckboxOptionsRequest request,RuntimeActor actor,CancellationToken cancellationToken)")
                    .AppendLine("    {")
                    .AppendLine($"        if(relationAuthorization is null||RelationOptionAuthorization.Require(relationAuthorization,{Literal(page.Source.Id)},{Bool(page.Source.IsNew==1)},{Bool(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source))})!=actor)throw new UnauthorizedAccessException(\"复选读取身份已变化。\");")
                    .AppendLine("        if(request.Selected is {} ids&&(ids.Length>DatabaseCheckboxValue.MaximumItems||ids.Any(id=>id<=0)||ids.Distinct().Count()!=ids.Length))throw new ArgumentException(\"复选指定ID无效。\");")
                    .AppendLine($"        var form=ReadCheckboxForm{suffix}(request.FormParameters);")
                    .AppendLine("        return session.InReadSnapshotAsync<IReadOnlyList<DatabaseCheckboxOption>>(async()=>")
                    .AppendLine("        {")
                    .Append(def.DerivedName?"":$"            var query=await CheckboxScope{suffix}Async(request.ParentId,actor,form,false,cancellationToken);\n")
                    .Append(def.DerivedName?"":"            if(request.Selected is {} selected){if(selected.Length==0)return [];query=query.Where(row=>selected.Contains(row.Id));}\n")
                    .AppendLine(def.DerivedName?$"            var rows=await DerivedCheckboxRows{suffix}Async(request.ParentId,actor,form,request.Selected,null,false,5001,false,cancellationToken);":$"            var rows=await query{string.Concat(def.Order.Select(item=>$".{(item.Descending?"OrderByDescending":"OrderBy")}(row=>row.{item.Column.Property})"))}.Limit(5001).ToListAsync(row=>new {{row.Id,Name=row.{def.Name.Property}}},cancellationToken);")
                    .AppendLine("            if(rows.Count>DatabaseCheckboxValue.MaximumItems)throw new InvalidOperationException(\"复选候选超过5000项，全选不能使用截断结果。\");")
                    .AppendLine("            return rows.Select(row=>{var id=row.Id.ToString(CultureInfo.InvariantCulture);return new DatabaseCheckboxOption(id,row.Name,id,row.Name);}).ToArray();")
                    .AppendLine("        },cancellationToken);").AppendLine("    }");
            }
            result.AppendLine("    /// <summary>来源依赖变化重新校验完整所选集合，冗余名称仅在本事务派生；未提交无关字段不刷新。</summary>")
                .AppendLine($"    private async Task ValidateCheckbox{suffix}Async({table.Name} entity,RuntimeActor actor,ISet<string> submitted,CancellationToken cancellationToken,RecordMutationOperation? operation=null)")
                .AppendLine("    {")
                .AppendLine("        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"复选保存要求真实当前身份。\");")
                .AppendLine($"        if(operation==RecordMutationOperation.Import)relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"import\");")
                .AppendLine($"        var selected=DatabaseCheckboxValue.Parse(entity.{def.Source.Property});");
            if(int.TryParse(def.Source.Source.ColumnLength,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var sourceLimit)&&sourceLimit>0)result.AppendLine($"        if(entity.{def.Source.Property} is {{Length:>{sourceLimit}}})throw new ArgumentException(\"复选ID数组超过已声明物理列长度。\");");
            result
                .Append(def.DerivedName?"":$"        var query=await CheckboxScope{suffix}Async({ParentValue(def,"entity")},actor,{EntityForm(def,"entity")},true,cancellationToken);\n")
                .AppendLine(def.DerivedName?$"        var rows=await DerivedCheckboxRows{suffix}Async({ParentValue(def,"entity")},actor,{EntityForm(def,"entity")},selected,null,false,DatabaseCheckboxValue.MaximumItems,true,cancellationToken);":$"        var rows=await query.Where(row=>selected.Contains(row.Id)).Limit(DatabaseCheckboxValue.MaximumItems).ToListAsync(row=>new {{row.Id,Name=row.{def.Name.Property}}},cancellationToken);")
                .AppendLine("        if(rows.Count!=selected.Length)throw new InvalidOperationException(\"复选项不存在、已删除或已离开当前固定候选范围，整笔未保存。\");");
            if(table.Source.IsRongyu==1)
                result.AppendLine("        var names=rows.ToDictionary(row=>row.Id,row=>row.Name);")
                    .AppendLine($"        entity.{def.Names.Property}=entity.{def.Source.Property} is null?null:JsonSerializer.Serialize(selected.Select(id=>names[id]).ToArray());submitted.Add({Literal(def.Names.Source.ColumnName)});");
            if(table.Source.IsRongyu==1&&int.TryParse(def.Names.Source.ColumnLength,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var nameLimit)&&nameLimit>0)result.AppendLine($"        if(entity.{def.Names.Property} is {{Length:>{nameLimit}}})throw new ArgumentException(\"复选名称数组超过已声明物理列长度。\");");
            result.AppendLine("    }");
        }
        return result.ToString();
    }
    /// <summary>已编译条件在类型加载时创建一次，候选请求只提供参数。</summary>
    private static string Declaration(DatabaseCheckboxModel def,string suffix)
    {
        if(def.Conditions is not {} condition)return "";
        var fields=string.Join(",",condition.Syntax.Fields.Select(Literal));var types=string.Join(",",condition.Types.Select(pair=>"["+Literal(pair.Key)+"]="+Literal(pair.Value)));
        return $"    private static readonly Metarill.Data.Queries.FixedConditionSqlPlan CheckboxCondition{suffix}=Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile({Literal(condition.Definition)},new string[]{{{fields}}}),new Dictionary<string,string>{{{types}}});\n";
    }
    /// <summary>只提取原表单槽中发布时声明的物理叶字段。</summary>
    private static string Reader(DatabaseCheckboxModel def,string suffix)=>$"    private static IReadOnlyDictionary<string,object?> ReadCheckboxForm{suffix}(JsonElement? formParameters)=>new Dictionary<string,object?>{{"+string.Join(",",(def.Conditions?.Forms??[]).Select(binding=>"["+Literal(binding.Variable)+"]=Metarill.Data.Queries.FixedConditionSqlPlan.BindScalar(Metarill.Core.Contracts.FixedConditionFormValue.Read(formParameters,new string[]{"+string.Join(",",binding.Path.Select(Literal))+"},new string[]{"+string.Join(",",binding.FallbackPath.Select(Literal))+"}),"+Literal(binding.Column.Source.LogicalType)+")"))+"};\n";
    internal static string EntityForm(DatabaseCheckboxModel def,string entity)=>"new Dictionary<string,object?>{"+string.Join(",",(def.Conditions?.Forms??[]).Select(binding=>"["+Literal(binding.Variable)+"]="+entity+"."+binding.Column.Property))+"}";
    internal static string ParentValue(DatabaseCheckboxModel def,string entity)=>def.SourceParent is null?"null":def.SourceParent.Type=="string?"?entity+"."+def.SourceParent.Property:entity+"."+def.SourceParent.Property+"?.ToString(CultureInfo.InvariantCulture)";
    internal static string Suffix(PageModel page,DatabaseCheckboxModel def)=>page.Source.Id+"_"+def.Source.Source.Id;
    private static string Scope(ColumnModel column,string value)=>column.Type switch{"string?"=>value,"int?"=>$"int.Parse({value},CultureInfo.InvariantCulture)",_=>$"long.Parse({value},CultureInfo.InvariantCulture)"};
    private static string Bool(bool value)=>value?"true":"false";
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
}
