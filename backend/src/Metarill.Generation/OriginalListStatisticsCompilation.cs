using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

internal sealed record ListStatistic(ColumnModel Column,int Kind,string SqlValue);

/// <summary>将原列表统计声明编译为固定强类型查询，不在运行时解释字段或拉取全部实体。</summary>
internal static class OriginalListStatisticsCompilation
{
    /// <summary>只消费页面实际列表字段及其原sumKind，隐藏字段不因统计额外读出。</summary>
    internal static IReadOnlyList<ListStatistic>? ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(page))return null;
        var result=new List<ListStatistic>();
        foreach(var column in page.ListColumns)
        {
            var field=model.Catalog.ConfigPageColumns.Single(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==column.Source.Id);
            var kind=field.Extra.TryGetValue("sumKind",out var value)&&int.TryParse(value.ToString(),out var parsed)?parsed:0;
            if(kind is not (1 or 2 or 3))continue;
            var type=column.Type.TrimEnd('?');
            if(!table.Columns.Any(item=>item.Source.Id==column.Source.Id)||column.IsManagedMultiNames||column.Source.IsVirtualColumn!=0||
                type is not ("decimal" or "int" or "long"))
                throw new InvalidOperationException("列表统计字段必须是已支持的固定数值读取："+column.Source.ColumnName);
            // 启用翻转复用已有SQL显示表达式，不能把DTO显示值与物理值按不同口径统计。
            result.Add(new(column,kind,ManagerCredentialCompilation.Order(table,page,column)));
        }
        return result;
    }

    /// <summary>统计总量使用未分页的同一固定筛选和身份范围；本页统计只折叠已返回的VO。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        if(NonRedundantReadCompilation.Requires(model,table))return BackendRenderer.NonRedundantStatistics(model,table,page);
        var definitions=ForPage(model,table,page);if(definitions is null)return "";
        var suffix=page.Source.Id;
        var source=new StringBuilder().AppendLine("    /// <summary>沿原列表统计口径复用页范围和筛选，聚合不继承分页或排序。</summary>")
            .AppendLine($"    internal async Task<Dictionary<string,object?>> Statistics{suffix}Async(PageRequest request,RuntimeActor actor,PageResult<{page.Name}Vo> page,CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        var statistics=new Dictionary<string,object?>(StringComparer.Ordinal);");
        if(definitions.Count==0)source.AppendLine("        await Task.CompletedTask;");
        foreach(var definition in definitions)
        {
            var column=definition.Column;var key=JsonSerializer.Serialize(column.JsonName);
            var local="stat"+column.Property;
            if(definition.Kind==1)
            {
                var current=column.Type.EndsWith('?')?"(decimal)(row."+column.Property+" ?? 0)":"(decimal)row."+column.Property;
                source.AppendLine($"        var {local}Total=await Query{suffix}(request,actor).SumAsync(entity=>{definition.SqlValue},cancellationToken);")
                    .AppendLine($"        statistics[{key}]=new{{currentPage=page.Records.Sum(row=>{current}),total={local}Total}};");
            }
            else
            {
                var physical="("+definition.SqlValue+") == 2";
                var display="row."+column.Property+" == 2";
                source.AppendLine($"        var {local}=new Dictionary<string,object?>();")
                    .AppendLine($"        if(page.Records.Count>0){local}[\"currentPage\"]=LegacyStatisticsMath.Percentage(page.Records.LongCount(row=>{display}),page.Records.Count);")
                    .AppendLine("        if(page.Total>0)").AppendLine("        {")
                    .AppendLine($"            var matches=await Query{suffix}(request,actor).Where(entity=>{physical}).CountAsync(cancellationToken);")
                    .AppendLine($"            {local}[\"total\"]=LegacyStatisticsMath.Percentage(matches,page.Total);")
                    .AppendLine("        }").AppendLine($"        statistics[{key}]={local};");
            }
        }
        return source.AppendLine("        return statistics;").AppendLine("    }").ToString();
    }

    /// <summary>原分页路由仍返回R和PageResult，statistics只放原map，不增加请求或暴露单独统计入口。</summary>
    internal static string Controller(PageModel page)
    {
        var suffix=page.Source.Id;
        if(!Enabled(page))return $"    public async Task<R<PageResult<{page.Name}Vo>>> Page([FromBody] PageRequest request, CancellationToken cancellationToken) => R<PageResult<{page.Name}Vo>>.Success(await service.Page{suffix}Async(request, Actor(\"list\"), cancellationToken));\n";
        return $$"""
    public async Task<R<PageResult<{{page.Name}}Vo>>> Page([FromBody] PageRequest request,CancellationToken cancellationToken)
    {
        var actor=Actor("list");var page=await service.Page{{suffix}}Async(request,actor,cancellationToken);
        var response=R<PageResult<{{page.Name}}Vo>>.Success(page);
        response.Map["statistics"]=await service.Statistics{{suffix}}Async(request,actor,page,cancellationToken);
        return response;
    }

""";
    }
    /// <summary>与原前端isSum开关一致，未启用页面不增加查询或响应字段。</summary>
    private static bool Enabled(PageModel page)=>GenerationModel.Enabled(page.Source.Extra,"isSum");
}
