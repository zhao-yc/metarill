using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>固定跨表Excel保留选择深度；名称反查、快照和目标范围复用原生成链。</summary>
internal static class CrossTableExcelRenderer
{
    /// <summary>隐式各级ID只作为读取依赖，不扩大原导出表头。</summary>
    internal static IReadOnlyList<ColumnModel> ExportDependencies(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<ColumnModel> columns)=>
        CrossTableWriteCompilation.ForService(model,table,page).Where(plan=>columns.Any(column=>column.Source.Id==plan.Read.Source.Source.Id)).SelectMany(plan=>plan.Levels.Select(level=>level.Id)).DistinctBy(column=>column.Source.Id).ToArray();

    /// <summary>每页每控件只发一次固定反查；开关来自内部调用，Excel不能指定SQL、表或权限动作。</summary>
    internal static string Helpers(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var plan in CrossTableWriteCompilation.ForService(model,table,page).Where(plan=>
            ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>column.Source.Id==plan.Read.Source.Source.Id)||ExportRenderer.IncludesColumn(page,plan.Read.Source)))
        {
            var suffix=page.Source.Id+"_"+plan.Read.Source.Source.Id;
            result.AppendLine("    /// <summary>按名称与明确父键有界反查，导入当前读持锁，导出加入同一只读快照。</summary>")
                .AppendLine($"    private async Task<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeNode>> CrossExcelFind{suffix}Async(int index,string text,long? parent,bool bindParent,bool currentRead,RuntimeActor actor,CancellationToken ct)")
                .AppendLine("    {switch(index){");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index].Read;var scope=suffix+"_"+index;
                result.AppendLine($"        case {index}:{{");
                if(level.Target.Source.IsRongyu==0)
                {
                    result.AppendLine($"            var filters=new List<FixedReadFilter>{{new({L(level.Name.Source.ColumnName)},FixedReadComparison.Equal,text)}};");
                    if(level.Parent is{} parent)result.AppendLine($"            if(bindParent)filters.Add(new({L(parent.Source.ColumnName)},FixedReadComparison.Equal,{(parent.Type=="string?"?"parent?.ToString(CultureInfo.InvariantCulture)":"parent")}));");
                    result.AppendLine($"            return await CrossTargetRows{scope}Async(actor,filters,2,currentRead,ct);");
                }
                else
                {
                    result.AppendLine($"            var query=CrossScope{scope}(actor,false).Where(row=>row.{level.Name.Property}==text);");
                    if(level.Parent is{} parent)result.AppendLine($"            if(bindParent){{var parentKey={(parent.Type=="string?"?"parent?.ToString(CultureInfo.InvariantCulture)":"parent")};query=query.Where(row=>row.{parent.Property}==parentKey);}}");
                    var parentValue=level.Parent is not{} p?"null":p.Type=="string?"?"row."+p.Property:"row."+p.Property+"?.ToString(CultureInfo.InvariantCulture)";
                    var projection=$"row=>new {level.Target.EntityType}{{Id=row.Id,{level.Name.Property}=row.{level.Name.Property}{(level.Parent is{} parentColumn?","+parentColumn.Property+"=row."+parentColumn.Property:"")}}}";
                    result.AppendLine($"            if(currentRead)query=query.ForUpdate();var rows=await query.OrderBy(row=>row.Id).Limit(2).ToListAsync({projection},ct);")
                        .AppendLine($"            return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{level.Name.Property},{parentValue})).ToArray();");
                }
                result.AppendLine("        }");
            }
            result.AppendLine("        default:throw new ArgumentException(\"跨表Excel层号无效。\");}}")
                .AppendLine("    /// <summary>完整路径及唯一旧简写复用同一固定层图，不把排序第一条当作唯一匹配。</summary>")
                .AppendLine($"    private Task<Metarill.Core.Cascades.CrossTablePath> CrossExcelResolve{suffix}Async(string text,bool currentRead,RuntimeActor actor,CancellationToken ct)=>Metarill.Core.Cascades.CrossTableExcelPath.ResolveAsync(text,{plan.Levels.Count},{L(plan.Read.Joiner)},(index,name,parent,bound,token)=>CrossExcelFind{suffix}Async(index,name,parent,bound,currentRead,actor,token),(index,id,token)=>CrossReader{suffix}(actor,currentRead,false).PathAsync(index,id,token),ct);");
        }
        return result.ToString();
    }

    /// <summary>返回全部级ID而非只有叶子；空值仍由整行合并保留同行显式祖先。</summary>
    internal static string Import(PageModel page,CrossTableWriteModel plan)
    {
        var source=plan.Read.Source;var suffix=page.Source.Id+"_"+source.Source.Id;
        var result=new StringBuilder().AppendLine("    /// <summary>部分路径恢复到明确深度，后段保持null；最终Service仍核对当前路径与实际存储。</summary>")
            .AppendLine($"    private async Task<IReadOnlyDictionary<string,JsonElement>> ImportCascade{suffix}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine("        cancellationToken.ThrowIfCancellationRequested();if(session.CurrentTransaction is null)throw new InvalidOperationException(\"跨表导入要求来源事务。\");")
            .AppendLine("        var values=new Dictionary<string,JsonElement>(StringComparer.Ordinal);");
        foreach(var level in plan.Levels)result.AppendLine($"        values[{L(level.Id.JsonName)}]=JsonSerializer.SerializeToElement<object?>(null);");
        result.AppendLine("        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return values;")
            .AppendLine("        if(cell.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))throw PageImportException.Input(PageImportError.Relation);")
            .AppendLine($"        var path=await CrossExcelResolve{suffix}Async(cell.ToString(),true,actor,cancellationToken);");
        for(var index=0;index<plan.Levels.Count;index++)
        {
            var level=plan.Levels[index];var value=level.Id.Type=="string?"?$"path.Path[{index}].Id":$"long.Parse(path.Path[{index}].Id,CultureInfo.InvariantCulture)";
            result.AppendLine($"        if(path.Path.Count>{index})values[{L(level.Id.JsonName)}]=JsonSerializer.SerializeToElement({value},JsonOptions);");
        }
        return result.AppendLine("        return values;").AppendLine("    }").ToString();
    }

    /// <summary>导出保留原表头，以固定ID读取实际路径并验证名称可唯一反查；按路径缓存同一快照内的重复选择。</summary>
    internal static string Export(TableModel table,PageModel page,CrossTableWriteModel plan)
    {
        var source=plan.Read.Source;var suffix=page.Source.Id+"_"+source.Source.Id;var name=source.Property;
        var result=new StringBuilder()
            .AppendLine($"        var crossExcel{name}=new Dictionary<long,string?>();var crossExcelCache{name}=new Dictionary<(int Depth,long Id),Metarill.Core.Cascades.CrossTablePath>();")
            .AppendLine("        foreach(var entity in entities){")
            .AppendLine($"            var ids=new string?[]{{{string.Join(",",plan.Levels.Select(level=>$"Convert.ToString(entity.{level.Id.Property},CultureInfo.InvariantCulture)"))}}};")
            .AppendLine("            var deepest=-1;for(var index=0;index<ids.Length;index++)if(!string.IsNullOrEmpty(ids[index]))deepest=index;")
            .AppendLine($"            if(deepest<0){{crossExcel{name}[entity.Id]=null;continue;}}")
            .AppendLine("            if(!long.TryParse(ids[deepest],NumberStyles.None,CultureInfo.InvariantCulture,out var selected)||selected<=0||ids[deepest]!=selected.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException(\"跨表导出ID无效。\");");
        if(table.Source.IsRongyu==1)result.AppendLine("            if(ids.Take(deepest+1).Any(string.IsNullOrEmpty))throw new InvalidOperationException(\"跨表导出路径存在空洞。\");");
        result.AppendLine($"            if(!crossExcelCache{name}.TryGetValue((deepest,selected),out var path)){{")
            .AppendLine($"                path=await CrossReader{suffix}(actor,false,false).PathAsync(deepest,selected,cancellationToken);")
            .AppendLine($"                var text=Metarill.Core.Cascades.CrossTableExcelPath.Format(path,{plan.Levels.Count},{L(plan.Read.Joiner)})!;")
            .AppendLine($"                var restored=await CrossExcelResolve{suffix}Async(text,false,actor,cancellationToken);")
            .AppendLine("                if(!path.Path.Select(node=>node.Id).SequenceEqual(restored.Path.Select(node=>node.Id)))throw new InvalidOperationException(\"跨表导出路径不能唯一还原原ID。\");")
            .AppendLine($"                crossExcelCache{name}[(deepest,selected)]=path;}}")
            .AppendLine("            for(var index=0;index<=deepest;index++)if(!string.IsNullOrEmpty(ids[index])&&ids[index]!=path.Path[index].Id)throw new InvalidOperationException(\"跨表导出祖先与实际路径不符。\");")
            .AppendLine($"            crossExcel{name}[entity.Id]=Metarill.Core.Cascades.CrossTableExcelPath.Format(path,{plan.Levels.Count},{L(plan.Read.Joiner)});")
            .AppendLine("        }")
            .AppendLine($"        string? ExcelCascade{name}({table.EntityType} entity)=>crossExcel{name}[entity.Id];");
        return result.ToString();
    }
    /// <summary>配置文本仅作为C#字面量，不成为代码或SQL。</summary>
    private static string L(string text)=>JsonSerializer.Serialize(text);
}
