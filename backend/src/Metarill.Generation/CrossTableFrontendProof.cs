using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation;

/// <summary>两种存储模式共用原跨表组件及页面字段映射证明，不改模板或元数据。</summary>
internal static class CrossTableFrontendProof
{
    /// <summary>固定key、前缀、分隔符和显式属性必须与原组件／页面及后端JSON同时一致。</summary>
    internal static string? Reason(CrossTableWriteModel plan)
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
        return null;
    }
    /// <summary>沿原组件只替换下划线后小写字母的算法，不能假定与C#大小写归一化等价。</summary>
    private static string ComponentProperty(string prefix,string suffix)=>Regex.Replace(prefix+suffix,"_([a-z])",match=>match.Groups[1].Value.ToUpperInvariant());
    /// <summary>null沿原详情默认值，显式属性必须为同一个JSON字段；空名和非文本不视为默认。</summary>
    private static bool PageProperty(JsonElement entry,string property,string prefix,string suffix,string expected)=>entry.TryGetProperty(property,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ValueKind==JsonValueKind.String&&value.GetString()==expected:GenerationModel.Camel(prefix+suffix)==expected;

}
