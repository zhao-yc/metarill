using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>原多选Excel单元格沿JSON名称数组，候选解析和写入均处于来源导入事务。</summary>
internal static class MultiSelectExcelRenderer
{
    /// <summary>名称逐项唯一定位目标，先编译整行物理父依赖，不按客户端任意列或SQL查询。</summary>
    internal static string Import(GenerationModel model,TableModel table,PageModel page,MultiSelectModel multi,ImportDependencyPlan dependency)
    {
        if(DerivedMultiNamesCompilation.Declared(model,multi.Target,multi.Name))return DerivedMultiNamesImportRenderer.Render(model,table,page,multi,dependency);
        var suffix=page.Source.Id+"_"+multi.Source.Source.Id;var result=new StringBuilder();
        result.AppendLine("    /// <summary>原JSON名称数组只在已授权来源import内反查，重名、缺失或越界让整批回滚。</summary>")
            .AppendLine($"    private async Task<JsonElement> ImportMulti{suffix}Async(JsonElement cell,string? parentId,RuntimeActor actor{(dependency.Contextual?", IReadOnlyDictionary<string,JsonElement> relationContext":"")},CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine("        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor||session.CurrentTransaction is null)throw new UnauthorizedAccessException(\"多选导入要求真实身份和来源事务。\");")
            .AppendLine($"        relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"import\");")
            .AppendLine("        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);")
            .AppendLine("        if(cell.ValueKind==JsonValueKind.String){try{cell=JsonSerializer.Deserialize<JsonElement>(cell.GetString()!);}catch(JsonException error){throw PageImportException.Input(PageImportError.Array,inner:error);}}")
            .AppendLine("        if(cell.ValueKind!=JsonValueKind.Array||cell.GetArrayLength()>200)throw PageImportException.Input(PageImportError.Array);")
            .AppendLine("        var selected=new List<string>();");
        var targetParent="parentId";
        if(multi.TargetParentField is not null)
        {
            var parent=dependency.Parents[multi.Source.Source.Id];
            result.AppendLine($"        if(!relationContext.TryGetValue({Literal(parent.JsonName)},out var parentValue)||parentValue.ValueKind==JsonValueKind.Null)throw PageImportException.Input(PageImportError.Relation);");targetParent="parentValue.ToString()";
        }
        if(multi.TargetParents.Count>0)result.AppendLine($"        var targetParentId={targetParent};if(!long.TryParse(targetParentId,NumberStyles.None,CultureInfo.InvariantCulture,out var parentKey)||parentKey<=0)throw PageImportException.Input(PageImportError.Relation);");
        result.AppendLine("        foreach(var item in cell.EnumerateArray())").AppendLine("        {")
            .AppendLine("            if(item.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))throw PageImportException.Input(PageImportError.Relation);")
            .AppendLine($"            var query=session.Select<{multi.Target.EntityType}>();");
        if(multi.Target.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("            query=query.Where(row=>row.DeleteNo==null);");
        if(multi.Target.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id") is {} project&&(!multi.Target.IsGlobal||multi.TargetPage.Scopes.Any(scope=>scope.Source=="project")))result.AppendLine($"            query=query.Where(row=>row.{project.Property}=={Scope(project,"actor.ProjectId")});");
        foreach(var scope in multi.TargetPage.Scopes.Where(scope=>scope.Source=="parent"))result.AppendLine($"            query=query.Where(row=>row.{scope.Column.Property}=={Scope(scope.Column,"targetParentId!")});");
        result.Append(FixedPageConditionCompilation.Apply(multi.TargetPage,"MultiPageCondition"+suffix,"multi_import_"+suffix));
        if(multi.Name.IsId)result.AppendLine("            if(!long.TryParse(item.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out var key)||key<=0)throw PageImportException.Input(PageImportError.Relation);query=query.Where(row=>row.Id==key);");
        else result.AppendLine($"            var name=item.ToString();query=query.Where(row=>row.{multi.Name.Property}==name);");
        result.AppendLine("            var matches=await query.OrderBy(row=>row.Id).ForUpdate().Limit(2).ToListAsync(row=>new{row.Id},cancellationToken);")
            .AppendLine("            if(matches.Count!=1)throw PageImportException.Input(PageImportError.Relation);")
            .AppendLine("            var value=matches[0].Id.ToString(CultureInfo.InvariantCulture);if(selected.Contains(value))throw PageImportException.Input(PageImportError.Relation);selected.Add(value);")
            .AppendLine("        }").AppendLine("        return JsonSerializer.SerializeToElement(selected);").AppendLine("    }");return result.ToString();
    }
    /// <summary>上下文值按声明物理类型绑定，不经浮点数。</summary>
    private static string Scope(ColumnModel column,string value)=>column.Type switch{"string?"=>value,"int?"=>$"int.Parse({value},CultureInfo.InvariantCulture)",_=>$"long.Parse({value},CultureInfo.InvariantCulture)"};
    /// <summary>固定配置只能成为代码字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
