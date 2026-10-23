using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation;

/// <summary>完整包按已经配置的跨表用途证明，未确认的路径Excel不能借隐藏控件绕过。</summary>
internal static class NonRedundantCrossTableSubset
{
    /// <summary>坏定义也不能借隐藏控件绕过完整包审计。</summary>
    internal static bool Declared(ColumnModel source)
    {
        if(source.Source.ControlKind!="CascadeTree")return false;
        try{using var json=JsonDocument.Parse(source.Source.ControlDetail);return !json.RootElement.TryGetProperty("sourceMode",out var mode)||mode.ValueKind!=JsonValueKind.String||mode.GetString()!="selfTree";}
        catch(Exception invalid)when(invalid is JsonException or InvalidOperationException){return true;}
    }
    /// <summary>只开放固定R0来源与独立R1目标的完整读写链，元数据及原按钮保持原样。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(table.Source.IsRongyu!=0||source.Source.ControlKind!="CascadeTree"||CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is null)return false;
        return Reason(model,table,page) is null;
    }
    /// <summary>所有隐藏同伴与实际导入导出列共同检查，不能只验证可见末级。</summary>
    internal static string? Reason(GenerationModel model,TableModel table,PageModel page)
    {
        try
        {
            var companions=table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").ToArray();
            var trees=companions.Where(Declared).ToArray();
            if(table.Source.IsRongyu!=0||trees.Length==0)return "非冗余跨表完整包要求明确的R0来源。";
            // 自树同伴使用真实自树计划，不当成跨表解析失败；未知、全局或自关联目标仍不放行。
            if(companions.Where(tree=>!Declared(tree)).Any(tree=>!NonRedundantSelfTreeSubset.FixedTarget(model,table,page,tree)))return "混合自树同伴缺少独立R1目标的完整读写证明。";
            var plans=trees.Select(tree=>CrossTableWriteCompilation.Resolve(model,table,page,tree)).ToArray();
            if(plans.Any(plan=>plan.Definition is null))return "跨表同页完整写入证明缺失，不能忽略隐藏或混合级联。";
            // 原组件按前缀转换属性，原详情还读取显式属性；两者必须与同一后端JSON字段一致。
            foreach(var plan in plans.Select(plan=>plan.Definition!))
            {
                using var json=JsonDocument.Parse(plan.Read.Source.Source.ControlDetail);var definition=json.RootElement;var entries=definition.GetProperty("levels").EnumerateArray().ToArray();
                if(definition.TryGetProperty("displayJoiner",out var joiner)&&joiner.ValueKind!=JsonValueKind.Null&&(joiner.ValueKind!=JsonValueKind.String||joiner.GetString()!=plan.Read.Joiner))return "跨表级联分隔符在原页面与固定后端间不一致，完整包尚未支持该定义。";
                for(var index=0;index<plan.Levels.Count;index++)
                {
                    var entry=entries[index];var level=plan.Levels[index];var tableName=entry.GetProperty("table").GetString()!;
                    var rawPrefix=entry.TryGetProperty("columnPrefix",out var prefix)&&prefix.ValueKind!=JsonValueKind.Null?prefix.GetString()!:tableName;var componentPrefix=rawPrefix.Length==0?tableName:rawPrefix;
                    var key=entry.TryGetProperty("key",out var keyValue)&&keyValue.ValueKind!=JsonValueKind.Null?keyValue.GetString()!:tableName;
                    if((key.Length==0?tableName:key)!=level.Read.Key)return "跨表级联层标识在原组件与固定后端间不一致。";
                    if(ComponentProperty(componentPrefix,"_id")!=level.Id.JsonName||ComponentProperty(componentPrefix,"_name")!=level.Name.JsonName)return "跨表级联前缀在原组件与固定后端间映射为不同属性。";
                    if(!PageProperty(entry,"idPropertyName",rawPrefix,"_id",level.Id.JsonName)||!PageProperty(entry,"namePropertyName",rawPrefix,"_name",level.Name.JsonName))return "跨表级联属性映射与原页面／固定后端字段不一致，完整包尚未支持该映射。";
                }
            }
            _=NonRedundantReadCompilation.ForPage(model,table,page);_=NonRedundantSelfTreeSubset.ParentTables(model,table,page);
            if(ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>trees.Any(tree=>tree.Source.Id==column.Source.Id)))return "跨表级联非空路径导入策略尚待确认，完整包不能开放该导入用途。";
            if(plans.SelectMany(plan=>plan.Definition!.Levels).Any(level=>ExportRenderer.IncludesColumn(page,level.Id)||ExportRenderer.IncludesColumn(page,level.Name)))return "跨表级联路径单元格导出尚待完整往返证明，完整包不能开放该导出用途。";
            return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return "非冗余跨表完整用途未通过："+error.Message;}
    }
    /// <summary>沿原组件只替换下划线后小写字母的算法，不能假定与C#大小写归一化等价。</summary>
    private static string ComponentProperty(string prefix,string suffix)=>Regex.Replace(prefix+suffix,"_([a-z])",match=>match.Groups[1].Value.ToUpperInvariant());
    /// <summary>null沿原详情默认值，显式属性必须为同一个JSON字段；空名和非文本不视为默认。</summary>
    private static bool PageProperty(JsonElement entry,string property,string prefix,string suffix,string expected)=>entry.TryGetProperty(property,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ValueKind==JsonValueKind.String&&value.GetString()==expected:GenerationModel.Camel(prefix+suffix)==expected;

}
