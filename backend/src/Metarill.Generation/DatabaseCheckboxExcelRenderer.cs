using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>复选Excel保留原存储合同：冗余用ID数组，非冗余用名称数组并验证可逆性。</summary>
internal static class DatabaseCheckboxExcelRenderer
{
    /// <summary>非冗余名称在来源import事务和同一实际表单范围内唯一反查，不能复用候选成功缓存。</summary>
    internal static string Import(TableModel table,PageModel page,DatabaseCheckboxModel def,ImportDependencyPlan dependency)
    {
        var suffix=DatabaseCheckboxRenderer.Suffix(page,def);var result=new StringBuilder();
        result.AppendLine("    /// <summary>原单元格只接受有界JSON数组，任何名称歧义或越界使整批回滚。</summary>")
            .AppendLine($"    private async Task<JsonElement> ImportCheckbox{suffix}Async(JsonElement cell,string? parentId,RuntimeActor actor{(dependency.Contextual?",IReadOnlyDictionary<string,JsonElement> relationContext":"")},CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine("        await Task.CompletedTask;")
            .AppendLine("        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor||session.CurrentTransaction is null)throw new UnauthorizedAccessException(\"复选导入要求来源真实身份和实际事务。\");")
            .AppendLine($"        relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"import\");")
            .AppendLine("        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);");
        if(table.Source.IsRongyu==1)
        {
            result.AppendLine("        try{return DatabaseCheckboxValue.ProjectIds(DatabaseCheckboxValue.ReadWrite(cell))??JsonSerializer.SerializeToElement<object?>(null);}catch(ArgumentException error){throw PageImportException.Input(PageImportError.Array,inner:error);}");
        }
        else
        {
            result.AppendLine("        if(cell.ValueKind==JsonValueKind.String){try{cell=JsonSerializer.Deserialize<JsonElement>(cell.GetString()!);}catch(JsonException error){throw PageImportException.Input(PageImportError.Array,inner:error);}}")
                .AppendLine("        if(cell.ValueKind!=JsonValueKind.Array||cell.GetArrayLength()>DatabaseCheckboxValue.MaximumItems)throw PageImportException.Input(PageImportError.Array);");
            var form="new Dictionary<string,object?>{"+string.Join(",",(def.Conditions?.Forms??[]).Select(binding=>"["+Literal(binding.Variable)+"]=Metarill.Data.Queries.FixedConditionSqlPlan.BindScalar(relationContext["+Literal(binding.Column.JsonName)+"],"+Literal(binding.Column.Source.LogicalType)+")"))+"}";
            result.AppendLine($"        var scope=await CheckboxScope{suffix}Async(parentId,actor,{form},true,cancellationToken);")
                .AppendLine("        var selected=new List<string>();")
                .AppendLine("        foreach(var item in cell.EnumerateArray())")
                .AppendLine("        {")
                .AppendLine("            if(item.ValueKind!=JsonValueKind.String)throw PageImportException.Input(PageImportError.Relation);var name=item.GetString();")
                .AppendLine($"            var currentScope=await CheckboxScope{suffix}Async(parentId,actor,{form},true,cancellationToken);")
                .AppendLine(def.DerivedName?$"            var matches=await DerivedCheckboxRows{suffix}Async(parentId,actor,{form},null,name,true,2,true,cancellationToken);":$"            var matches=await currentScope.Where(row=>row.{def.Name.Property}==name).OrderBy(row=>row.Id).Limit(2).ToListAsync(row=>new{{row.Id}},cancellationToken);")
                .AppendLine("            if(matches.Count!=1)throw PageImportException.Input(PageImportError.Relation);")
                .AppendLine("            var id=matches[0].Id.ToString(CultureInfo.InvariantCulture);if(selected.Contains(id))throw PageImportException.Input(PageImportError.Relation);selected.Add(id);")
                .AppendLine("        }")
                .AppendLine("        return JsonSerializer.SerializeToElement(selected);");
        }
        return result.AppendLine("    }").ToString();
    }
    /// <summary>名称导出必须能在当前原条件下唯一回到同一ID，拒绝移动范围或同名重映射。</summary>
    internal static string Export(TableModel table,PageModel page,DatabaseCheckboxModel def)
    {
        var suffix=DatabaseCheckboxRenderer.Suffix(page,def);
        return $$"""
    /// <summary>源行及候选共用读取快照，名字按数据库真实比较规则唯一性核验。</summary>
    private async Task<IReadOnlyDictionary<long,string?>> ExportCheckbox{{suffix}}Async(IReadOnlyList<{{table.EntityType}}> entities,RuntimeActor actor,CancellationToken cancellationToken)
    {
        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException("复选导出要求真实身份。");
        relationAuthorization.RequirePagePermission("{{page.Source.Id}}","export");
        if(session.CurrentTransaction is null)throw new InvalidOperationException("非冗余复选名称导出要求实际读取快照。");
        var values=new Dictionary<long,string?>();
        foreach(var entity in entities)
        {
            var selected=DatabaseCheckboxValue.Parse(entity.{{def.Source.Property}});
            var scope=await CheckboxScope{{suffix}}Async({{DatabaseCheckboxRenderer.ParentValue(def,"entity")}},actor,{{DatabaseCheckboxRenderer.EntityForm(def,"entity")}},false,cancellationToken);
            {{(def.DerivedName?"var rows=await DerivedCheckboxRows"+suffix+"Async("+DatabaseCheckboxRenderer.ParentValue(def,"entity")+",actor,"+DatabaseCheckboxRenderer.EntityForm(def,"entity")+",selected,null,false,DatabaseCheckboxValue.MaximumItems,false,cancellationToken);":"var rows=await scope.Where(row=>selected.Contains(row.Id)).Limit(DatabaseCheckboxValue.MaximumItems).ToListAsync(row=>new{row.Id,Name=row."+def.Name.Property+"},cancellationToken);")}}
            if(rows.Count!=selected.Length||rows.Any(row=>row.Name is null))throw new InvalidOperationException("复选名称导出存在缺失、删除或离开候选范围的目标，不能生成会重映射的名称数组。");
            var names=rows.ToDictionary(row=>row.Id,row=>row.Name);
            foreach(var id in selected)
            {
                var name=names[id];var nameScope=await CheckboxScope{{suffix}}Async({{DatabaseCheckboxRenderer.ParentValue(def,"entity")}},actor,{{DatabaseCheckboxRenderer.EntityForm(def,"entity")}},false,cancellationToken);
                {{(def.DerivedName?"var matches=await DerivedCheckboxRows"+suffix+"Async("+DatabaseCheckboxRenderer.ParentValue(def,"entity")+",actor,"+DatabaseCheckboxRenderer.EntityForm(def,"entity")+",null,name,true,2,false,cancellationToken);":"var matches=await nameScope.Where(row=>row."+def.Name.Property+"==name).Limit(2).ToListAsync(row=>new{row.Id},cancellationToken);")}}
                if(matches.Count!=1||matches[0].Id!=id)throw new InvalidOperationException("复选名称导出存在歧义，名称数组不能唯一还原原ID。");
            }
            values[entity.Id]=entity.{{def.Source.Property}} is null?null:JsonSerializer.Serialize(selected.Select(id=>names[id]).ToArray());
        }
        return values;
    }

""";
    }
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
