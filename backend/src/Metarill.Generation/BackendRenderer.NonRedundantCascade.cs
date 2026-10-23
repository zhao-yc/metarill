using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>非冗余自树复用原固定图，分页前筛选和分页后投影均在来源读取快照内。</summary>
internal static partial class BackendRenderer
{
    /// <summary>无树筛选页面保留原同步签名；实际请求先完成固定图过滤再进入SQL。</summary>
    internal static string NonRedundantFilterCall(GenerationModel model,TableModel table,PageModel page,string request="request",string actor="actor",string? operation=null)
    {
        var async=page.Searchable.Any(column=>column.Source.ControlKind=="CascadeTree");
        var extra=operation is null?"":ExactDataManageCompilation.Argument(page,operation);
        return async?$"(await ReadCascadeFilters{page.Source.Id}Async({request},{actor},cancellationToken{extra}))":$"ReadFilters{page.Source.Id}({request},{actor}{extra})";
    }
    /// <summary>原节点对象仅解释固定ID，名称回显不成为SQL或拓扑依据。</summary>
    private static string NonRedundantCascadeFilters(GenerationModel model,TableModel table,PageModel page)
    {
        if(!page.Searchable.Any(column=>column.Source.ControlKind=="CascadeTree"))return "";
        var awaits=page.Searchable.Any(column=>column.Source.ControlKind=="CascadeTree"&&!column.IsFixedCrossTableFilter);
        var result=new StringBuilder().AppendLine("    /// <summary>树范围在SQL分页／统计之前解析，未知节点键不能扩大候选。</summary>")
            .AppendLine($"    private {(awaits?"async ":"")}Task<List<FixedReadFilter>> ReadCascadeFilters{page.Source.Id}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken{ExactDataManageCompilation.Parameter(page,"list")})")
            .AppendLine("    {").AppendLine($"        var result=ReadFilters{page.Source.Id}(request,actor{ExactDataManageCompilation.Argument(page,"authorizationOperation")});");
        foreach(var source in page.Searchable.Where(column=>column.Source.ControlKind=="CascadeTree"))
        {
            if(CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is{} cross)
            {result.Append(NonRedundantCrossTableCompilation.Filter(cross,page));continue;}
            var plan=CascadeTreeReadCompilation.Resolve(model,table,page,source).Definition??throw new InvalidOperationException("非冗余树筛选缺少固定定义。");
            var key=JsonSerializer.Serialize(source.JsonName);var name=JsonSerializer.Serialize(GenerationModel.Camel(source.Source.ColumnName[..^3]+"_name"));
            result.AppendLine($"        if(request.Filters.TryGetValue({key},out var value{source.Source.Id})&&!(value{source.Source.Id}.ValueKind==JsonValueKind.Array&&value{source.Source.Id}.GetArrayLength()==0))")
                .AppendLine("        {").AppendLine($"            var selected=Metarill.Core.Cascades.CascadeTreeGraphReader.FilterId(value{source.Source.Id},{key},{name});")
                .AppendLine($"            if(selected is not null){{var ids=await CascadeGraph{page.Source.Id}_{source.Source.Id}(actor).FilterIdsAsync(selected.Value,{plan.Exact.ToString().ToLowerInvariant()},cancellationToken);result.Add(new({JsonSerializer.Serialize(source.Source.ColumnName)},FixedReadComparison.In,ids.Select(id=>(object?){(source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"id")}).ToArray()));}}")
                .AppendLine("        }");
        }
        return result.AppendLine(awaits?"        return result;":"        return Task.FromResult(result);").AppendLine("    }").ToString();
    }
    /// <summary>仅从来源已授权ID构造最小实体，不能从名称反推节点或增加普通可读列。</summary>
    internal static string NonRedundantProjectionCall(GenerationModel model,TableModel table,PageModel page,string rows,string currentRead,bool one=false)
    {
        if(CascadeTreeViewRenderer.ForPage(model,table,page).Count==0)return one?$"{page.Name}Vo.FromReadRow({rows}[0])":$"{rows}.Select({page.Name}Vo.FromReadRow).ToArray()";
        var result=$"await ProjectRead{page.Source.Id}Async({rows},actor,cancellationToken,{currentRead})";return one?$"({result})[0]":result;
    }
    /// <summary>同页多行共用祖先缓存，写回执由私有入口选择当前锁读。</summary>
    private static string NonRedundantCascadeProjection(GenerationModel model,TableModel table,PageModel page)
    {
        var plans=CascadeTreeViewRenderer.ForPage(model,table,page);if(plans.Count==0)return "";
        var assignments=string.Join(",",plans.Select(plan=>$"{plan.Source.Property}=NonRedundantReads.Value<{plan.Source.Type}>(row,{JsonSerializer.Serialize(plan.Source.Source.ColumnName)})"));
        return $$"""
    /// <summary>名称和路径来自同一固定图，坏目标不使来源记录丢失。</summary>
    private Task<IReadOnlyList<{{page.Name}}Vo>> ProjectRead{{page.Source.Id}}Async(IReadOnlyList<IReadOnlyDictionary<string,object?>> rows,RuntimeActor actor,CancellationToken cancellationToken,bool currentRead=false)
    {
        var entities=rows.Select(row=>new {{table.Name}}{Id=NonRedundantReads.Value<long>(row,"id"),{{assignments}}}).ToArray();
        return DecorateCascade{{page.Source.Id}}Async(rows.Select({{page.Name}}Vo.FromReadRow).ToArray(),entities,actor,cancellationToken,currentRead);
    }
""";
    }
}
