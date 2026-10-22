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
        if(CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is not null)return CrossTableExcelRenderer.Import(page,source);
        var plan=CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition;
        if(plan is null)return $"    private Task<JsonElement> ImportCascade{page.Source.Id}_{source.Source.Id}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)=>throw new NotSupportedException(\"级联导入范围尚未证明。\");\n";
        var read=plan.Read;var suffix=page.Source.Id+"_"+source.Source.Id;var target=read.Target;var parent=read.Parent;var name=read.Name;
        var projection=$"row=>new {target.Name}{{Id=row.Id,{name.Property}=row.{name.Property},{parent.Property}=row.{parent.Property}}}";
        var parentOutput=parent.Type=="string?"?"row."+parent.Property:"row."+parent.Property+"?.ToString(CultureInfo.InvariantCulture)";
        return $$"""
    /// <summary>按根至叶完整路径逐层唯一反查；锁住名称/归属，后续并发变化不能偷换Excel所指节点。</summary>
    private async Task<JsonElement> ImportCascade{{suffix}}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)
    {
        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);
        if(cell.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))throw PageImportException.Input(PageImportError.Relation);
        if(session.CurrentTransaction is null)throw new InvalidOperationException("级联导入要求来源事务。");
        var id=await Metarill.Core.Cascades.CascadeTreeExcelPath.ResolveAsync(cell.ToString(),{{Literal(read.Joiner)}},async(text,parentId,ct)=>
        {
            var query=CascadeScope{{suffix}}(actor,false,false).Where(row=>row.{{name.Property}}==text);
            var parentKey={{(parent.Type=="string?"?"parentId?.ToString(CultureInfo.InvariantCulture)":"parentId")}};
            query=parentId is null?query.Where(row=>row.{{parent.Property}}==null{{(parent.Type=="string?"?$"||row.{parent.Property}==\"\"":"")}}):query.Where(row=>row.{{parent.Property}}==parentKey);
            var rows=await query.OrderBy(row=>row.Id).ForUpdate().Limit(2).ToListAsync({{projection}},ct);
            return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{{name.Property}},{{parentOutput}})).ToArray();
        },cancellationToken);
        return JsonSerializer.SerializeToElement({{(source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"(long?)id")}},JsonOptions);
    }

""";
    }
    /// <summary>复用批量祖先缓存，在同一导出快照生成原完整路径，坏关联不伪装为空值。</summary>
    internal static string Export(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<ColumnModel> columns)
    {
        var result=new StringBuilder();
        foreach(var source in columns.Where(column=>column.Source.ControlKind=="CascadeTree"))
        {
            var plan=CascadeTreeReadCompilation.Resolve(model,table,page,source).Definition;var suffix=page.Source.Id+"_"+source.Source.Id;
            if(plan is null||source.Type is not("string?" or "long?")){result.AppendLine($"        string? ExcelCascade{source.Property}({table.Name} entity)=>throw new NotSupportedException(\"级联导出范围尚未证明。\");");continue;}
            result.AppendLine($"        var excelIds{source.Property}=entities.Select(entity=>Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture)).Where(text=>!string.IsNullOrWhiteSpace(text)).Select(text=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0?id:throw new InvalidOperationException(\"级联ID无效，未导出。\")).Distinct().ToArray();")
                .AppendLine($"        var excelPaths{source.Property}=await CascadeGraph{suffix}(actor).PathsAsync(excelIds{source.Property},cancellationToken);")
                .AppendLine($"        string? ExcelCascade{source.Property}({table.Name} entity){{var text=Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture);if(string.IsNullOrWhiteSpace(text))return null;var id=long.Parse(text,CultureInfo.InvariantCulture);return Metarill.Core.Cascades.CascadeTreeExcelPath.Format(excelPaths{source.Property}.GetValueOrDefault(id),{Literal(plan.Joiner)});}}");
        }
        return result.ToString();
    }
    /// <summary>元数据文本只作为转义后的字面量，不执行SQL或脚本。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
