using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原跨表展示拼接已存各级名称，不查询目标或把历史名称静默刷新成当前值。</summary>
internal static class CrossTableDisplayRenderer
{
    /// <summary>只扩展本页已读叶子且受管物理名称完整的固定链，别名不能覆盖业务属性。</summary>
    internal static IReadOnlyList<CrossTableWriteModel> ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        var plans=CrossTableWriteCompilation.ForService(model,table,page).Where(plan=>page.Readable.Any(column=>column.Source.Id==plan.Read.Source.Source.Id)).ToArray();
        foreach(var plan in plans)if(table.Columns.Any(column=>column.Property.Equals(plan.Read.Source.Property+"DisplayText",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("跨表显示别名与物理字段冲突。");
        return plans;
    }
    /// <summary>DisplayText仅由后端填充，不能加入写DTO。</summary>
    internal static string Vo(GenerationModel model,TableModel table,PageModel page)=>string.Concat(ForPage(model,table,page).Select(plan=>$"    [JsonPropertyName({Literal(plan.Read.Source.JsonName+"DisplayText")})] public string? {plan.Read.Source.Property}DisplayText {{ get; set; }}\n"));
    /// <summary>普通页和没有本页读取权的级联不增加装饰调用。</summary>
    internal static string Decorate(GenerationModel model,TableModel table,PageModel page,string expression,string entities)=>ForPage(model,table,page).Count==0?expression:$"DecorateCross{page.Source.Id}({expression}, {entities})";
    /// <summary>新增、更新、复制和详情均使用本次正常实体中的已存名称。</summary>
    internal static string One(GenerationModel model,TableModel table,PageModel page,string expression)=>ForPage(model,table,page).Count==0?expression:$"DecorateCross{page.Source.Id}(new[] {{ {expression} }}, new[] {{ entity }})[0]";
    /// <summary>原Map/Bean实现仅跳过空白名称，保留非空名称原文和层级顺序，无目标N+1查询。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var plans=ForPage(model,table,page);if(plans.Count==0)return "";var output=new StringBuilder();
        output.AppendLine("    /// <summary>原跨表显示只拼接来源行中已存名称；部分选择与历史名称保持原语义。</summary>")
            .AppendLine($"    private static IReadOnlyList<{page.Name}Vo> DecorateCross{page.Source.Id}(IReadOnlyList<{page.Name}Vo> values,IReadOnlyList<{table.EntityType}> entities)")
            .AppendLine("    {").AppendLine("        if(values.Count!=entities.Count)throw new InvalidOperationException(\"跨表展示记录数不符。\");");
        foreach(var plan in plans)output.AppendLine($"        for(var index=0;index<values.Count;index++)values[index].{plan.Read.Source.Property}DisplayText=string.Join({Literal(plan.Read.Joiner)},new[]{{{string.Join(",",plan.Levels.Select(level=>"entities[index]."+level.Name.Property))}}}.Where(text=>!string.IsNullOrWhiteSpace(text)));" );
        return output.AppendLine("        return values;").AppendLine("    }").ToString();
    }
    /// <summary>配置仅作为C#字符串字面量，不解释为代码。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
