using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>只编译原完整路径导入导出；源事务、Actor和元数据固定目标复用既有生成规则。</summary>
internal static class CascadeTreeExcelRenderer
{
    /// <summary>导入操作来自内部调用链，不能由请求指定以绕过普通写入权限。</summary>
    internal static bool OperationAware(GenerationModel model,TableModel table,PageModel page)=>ImportRenderer.Enabled(page)&&(CascadeTreeWriteCompilation.ForService(model,table,page).Count>0||CrossTableWriteCompilation.ForService(model,table,page).Count>0);
    /// <summary>解析原单元格并只返回叶子ID；受管名称留给最终Service当前读维护。</summary>
    internal static string Import(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is{} cross)return CrossTableExcelRenderer.Import(page,cross);
        var plan=CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition;
        if(plan is null)return $"    private Task<JsonElement> ImportCascade{page.Source.Id}_{source.Source.Id}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)=>throw new NotSupportedException(\"级联导入范围尚未证明。\");\n";
        var suffix=page.Source.Id+"_"+source.Source.Id;
        return $"    private Task<JsonElement> ImportCascade{suffix}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)=>ReadCascadeExcel{suffix}Async(cell,actor,true,cancellationToken);\n";
    }

    /// <summary>导入与导出复验共用相同的节点匹配，导出只读，不新增可调用的接口。</summary>
    internal static string Readers(GenerationModel model,TableModel table,PageModel page)
    {
        if(!ImportRenderer.Enabled(page)&&!ExportRenderer.Enabled(page))return "";
        var imported=ImportRenderer.Enabled(page)?ImportRenderer.Columns(page):[];
        return string.Concat(CascadeTreeReadCompilation.ForService(model,table,page)
            .Where(plan=>plan.Source.Type is "string?" or "long?")
            .Where(plan=>ExportRenderer.IncludesColumn(page,plan.Source)||
                imported.Any(column=>column.Source.Id==plan.Source.Source.Id)&&
                CascadeTreeWriteCompilation.Resolve(model,table,page,plan.Source).Definition is not null)
            .Select(plan=>ReadPath(page,plan)));
    }

    /// <summary>物理整数、文本业务键和固定 SQL 使用同一导入／导出调用边界。</summary>
    private static string ReadPath(PageModel page,CascadeTreeReadModel read)
    {
        var source=read.Source;
        if(read.UsesFixedRead)return DerivedCascadeNamesRenderer.ExcelPath(page,source,read);
        if(read.TextKey)return TextCascadeTreeRenderer.ExcelPath(page,source,read);
        var suffix=page.Source.Id+"_"+source.Source.Id;var target=read.Target;var parent=read.Parent;var name=read.Name;
        var key=read.Key.Property;
        var nodeId=read.Key.IsId?"row.Id":$"row.{key}!.Value";
        var projection=$"row=>new {target.EntityType}{{{key}=row.{key},{name.Property}=row.{name.Property},{parent.Property}=row.{parent.Property}}}";
        var parentOutput=parent.Type=="string?"?"row."+parent.Property:"row."+parent.Property+"?.ToString(CultureInfo.InvariantCulture)";
        var label=MultiSelectExcelRenderer.LabelText(name,"row."+name.Property);
        var lookup=name.Type=="string?"?"text":"key";
        var parse=name.Type=="string?"?"":MultiSelectExcelRenderer.ParseLabel(name,"text","key");
        return $$"""
    /// <summary>逐层唯一反查。导入时锁定名称和归属，导出时沿用只读快照。</summary>
    private async Task<JsonElement> ReadCascadeExcel{{suffix}}Async(JsonElement cell,RuntimeActor actor,bool currentRead,CancellationToken cancellationToken)
    {
        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);
        if({{MultiSelectExcelRenderer.InvalidLabelKind(name,allowNumber:true,input:"cell")}})throw PageImportException.Input(PageImportError.Relation);
        if(session.CurrentTransaction is null||currentRead&&!session.HasWriteTransaction)throw new InvalidOperationException("级联Excel路径要求来源事务。");
        var id=await Metarill.Core.Cascades.CascadeTreeExcelPath.ResolveAsync(cell.ToString(),{{Literal(read.Joiner)}},async(text,parentId,ct)=>
        {
            {{parse}}
            var query=CascadeScope{{suffix}}(actor,false,false).Where(row=>row.{{name.Property}}=={{lookup}});
            var parentKey={{(parent.Type=="string?"?"parentId?.ToString(CultureInfo.InvariantCulture)":"parentId")}};
            query=parentId is null?query.Where(row=>row.{{parent.Property}}==null{{(parent.Type=="string?"?$"||row.{parent.Property}==\"\"":"")}}):query.Where(row=>row.{{parent.Property}}==parentKey);
            if(currentRead)query=query.ForUpdate();
            var rows=await query.OrderBy(row=>row.{{key}}).Limit(2).ToListAsync({{projection}},ct);
            return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode({{nodeId}},{{label}},{{parentOutput}})).ToArray();
        },cancellationToken);
        return JsonSerializer.SerializeToElement({{(source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"(long?)id")}},JsonOptions);
    }

""";
    }
    /// <summary>名称单独导出时同样需要原所选ID和完整祖先证明，隐藏依赖不增添表头。</summary>
    internal static IReadOnlyList<CascadeTreeReadModel> ExportPlans(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<ColumnModel> columns)=>CascadeTreeReadCompilation.ForService(model,table,page).Where(plan=>columns.Any(column=>column.Source.Id==plan.Source.Source.Id||table.Source.IsRongyu==0&&column.Source.Id==CascadeTreeViewRenderer.Name(table,plan.Source)?.Source.Id)).ToArray();
    /// <summary>复用批量祖先缓存，在同一导出快照生成原完整路径，坏关联不伪装为空值。</summary>
    internal static string Export(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<ColumnModel> columns)
    {
        var result=new StringBuilder();
        foreach(var source in columns.Where(column=>column.Source.ControlKind=="CascadeTree").Concat(ExportPlans(model,table,page,columns).Select(plan=>plan.Source)).DistinctBy(column=>column.Source.Id))
        {
            if(CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is{} cross){result.Append(CrossTableExcelRenderer.Export(table,page,cross));continue;}
            var plan=CascadeTreeReadCompilation.Resolve(model,table,page,source).Definition;var suffix=page.Source.Id+"_"+source.Source.Id;
            if(plan is null||source.Type is not("string?" or "long?")){result.AppendLine($"        string? ExcelCascade{source.Property}({table.EntityType} entity)=>throw new NotSupportedException(\"级联导出范围尚未证明。\");");continue;}
            if(plan.TextKey)
            {
                result.AppendLine($"        var excelIds{source.Property}=entities.Select(entity=>entity.{source.Property}).Where(key=>!string.IsNullOrWhiteSpace(key)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();")
                    .AppendLine($"        var excelPaths{source.Property}=await CascadeGraph{suffix}(actor).PathsAsync(excelIds{source.Property},cancellationToken);")
                    .Append(VerifyExport(page,source,plan,columns))
                    .AppendLine(columns.Contains(source)?$"        string? ExcelCascade{source.Property}({table.EntityType} entity){{var key=entity.{source.Property};return string.IsNullOrWhiteSpace(key)?null:excelCells{source.Property}[key];}}":"");
                if(table.Source.IsRongyu==0&&CascadeTreeViewRenderer.Name(table,source) is{} textName&&columns.Any(column=>column.Source.Id==textName.Source.Id))
                    result.AppendLine($"        foreach(var entity in entities)entity.{textName.Property}=entity.{source.Property} is {{}} key&&excelPaths{source.Property}.TryGetValue(key,out var path)?path?.Path.LastOrDefault()?.Name:null;");
                continue;
            }
            result.AppendLine($"        var excelIds{source.Property}=entities.Select(entity=>Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture)).Where(text=>!string.IsNullOrWhiteSpace(text)).Select(text=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0?id:throw new InvalidOperationException(\"级联ID无效，未导出。\")).Distinct().ToArray();")
                .AppendLine($"        var excelPaths{source.Property}=await CascadeGraph{suffix}(actor).PathsAsync(excelIds{source.Property},cancellationToken);")
                .Append(VerifyExport(page,source,plan,columns))
                .AppendLine(columns.Contains(source)?$"        string? ExcelCascade{source.Property}({table.EntityType} entity){{var text=Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture);return string.IsNullOrWhiteSpace(text)?null:excelCells{source.Property}[long.Parse(text,CultureInfo.InvariantCulture)];}}":"");
            if(table.Source.IsRongyu==0&&CascadeTreeViewRenderer.Name(table,source) is{} managed&&columns.Any(column=>column.Source.Id==managed.Source.Id))
                result.AppendLine($"        foreach(var entity in entities){{var text=Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture);entity.{managed.Property}=long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&excelPaths{source.Property}.TryGetValue(id,out var path)?path?.Path.LastOrDefault()?.Name:null;}}");
        }
        return result.ToString();
    }
    /// <summary>只校验工作簿中的完整路径列，单独导出的辅助名称仍取叶名。</summary>
    private static string VerifyExport(PageModel page,ColumnModel source,CascadeTreeReadModel plan,IReadOnlyList<ColumnModel> columns)=>
        !columns.Contains(source)?"":
        $"        var excelCells{source.Property}=await Metarill.Core.Cascades.CascadeTreeExcelPath.FormatVerifiedAsync(excelPaths{source.Property},{Literal(plan.Joiner)},async(text,ct)=>(await ReadCascadeExcel{page.Source.Id}_{source.Source.Id}Async(JsonSerializer.SerializeToElement(text),actor,false,ct)).ToString(),cancellationToken);\n";

    /// <summary>元数据文本只作为转义后的字面量，不执行SQL或脚本。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
