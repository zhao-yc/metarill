using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>将原节点对象映射到固定来源列和强类型参数，绝不接收动态列名或SQL。</summary>
internal static class CrossTableFilterRenderer
{
    /// <summary>各页键计划只初始化一次，不在每个请求解释元数据或反射字段。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)=>string.Concat(CrossTableFilterCompilation.ForPage(model,table,page).Select(plan=>$"    private static readonly Metarill.Core.Cascades.CrossTableFilter CrossFilter{page.Source.Id}_{plan.Read.Source.Source.Id}=new([{string.Join(",",plan.Levels.Select(level=>"new("+Literal(level.Id.JsonName)+","+Literal(level.Name.JsonName)+")"))}]);\n"));
    /// <summary>最深级只选一个eq，原字符串不归一成别的字符串；int64按精确整数绑定。</summary>
    internal static string Filter(CrossTableWriteModel plan,PageModel page)
    {
        var output=new StringBuilder().AppendLine($"                    var selected=CrossFilter{page.Source.Id}_{plan.Read.Source.Source.Id}.Parse(filter.Value);if(selected is null)break;").AppendLine("                    switch(selected.Level)").AppendLine("                    {");
        for(var index=0;index<plan.Levels.Count;index++)
        {
            var column=plan.Levels[index].Id;var value=column.Type=="string?"?"selected.Id":"long.Parse(selected.Id,NumberStyles.None,CultureInfo.InvariantCulture)";
            output.AppendLine($"                        case {index}:{{var value={value};query=query.Where(entity=>entity.{column.Property}==value);break;}}");
        }
        return output.AppendLine("                        default:throw new InvalidOperationException(\"跨表筛选层级无效。\");").AppendLine("                    }").ToString();
    }
    /// <summary>固定JSON键只作为C#字面量，不能插入代码或SQL。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
