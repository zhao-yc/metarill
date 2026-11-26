using System.Text.Json;
using Metarill.Core.Metadata;
using System.Text.RegularExpressions;
namespace Metarill.Generation;

/// <summary>两种存储模式共用原跨表组件及页面字段映射证明，不改模板或元数据。</summary>
internal static class CrossTableFrontendProof
{
    /// <summary>原详情优先采用页字段的控件配置，空配置沿用表字段定义。</summary>
    internal static string? PageOverride(GenerationModel model, PageModel page, ColumnModel source) =>
        model.Catalog.ConfigPageColumns.SingleOrDefault(field =>
            field.ConfigPageId == page.Source.Id && field.ConfigColumnId == source.Source.Id)
            ?.Extra.GetValueOrDefault("controlDetail").ToString();

    /// <summary>页面显示和排序沿原配置，节点、父字段及存储槽与传播定义一致。</summary>
    internal static bool KeepsStorageDefinition(GenerationModel model, PageModel page, ColumnModel source) =>
        CrossTableStorageColumns.KeepsDefinition(source.Source.ControlDetail, PageOverride(model, page, source));

    /// <summary>固定key、前缀、分隔符和显式属性必须与原组件／页面及后端JSON同时一致。</summary>
    internal static string? Reason(GenerationModel model, PageModel page, CrossTableWriteModel plan) =>
        Reason(model, page, plan.Read);

    /// <summary>字段映射来源于固定读取层位，也适用于无存储槽的功能表。</summary>
    internal static string? Reason(GenerationModel model, PageModel page, CrossTableReadModel plan)
    {
        var overlay = PageOverride(model, page, plan.Source);
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(overlay)
            ? plan.Source.Source.ControlDetail : overlay);
        var definition = json.RootElement;
        var entries = definition.GetProperty("levels").EnumerateArray().ToArray();
        if (definition.TryGetProperty("displayJoiner", out var joiner) && joiner.ValueKind != JsonValueKind.Null &&
            (joiner.ValueKind != JsonValueKind.String || (joiner.GetString() is "" ? "/" : joiner.GetString()) != plan.Joiner))
            return "跨表级联分隔符在原页面与固定后端间不一致，完整包尚未支持该定义。";
        for(var index=0;index<plan.Levels.Count;index++)
        {
            var entry=entries[index];var level=plan.Levels[index];var tableName=entry.GetProperty("table").GetString()!;
            var rawPrefix=entry.TryGetProperty("columnPrefix",out var prefix)&&prefix.ValueKind!=JsonValueKind.Null?prefix.GetString()!:tableName;var componentPrefix=rawPrefix.Length==0?tableName:rawPrefix;
            var key=entry.TryGetProperty("key",out var keyValue)&&keyValue.ValueKind!=JsonValueKind.Null?keyValue.GetString()!:tableName;
            if((key.Length==0?tableName:key)!=level.Key)return "跨表级联层标识在原组件与固定后端间不一致。";
            var id = GenerationModel.Camel(level.Prefix+"_id");
            var name = GenerationModel.Camel(level.Prefix+"_name");
            if(ComponentProperty(componentPrefix,"_id")!=id||ComponentProperty(componentPrefix,"_name")!=name)return "跨表级联前缀在原组件与固定后端间映射为不同属性。";
            if(!PageProperty(entry,"idPropertyName",rawPrefix,"_id",id)||!PageProperty(entry,"namePropertyName",rawPrefix,"_name",name))return "跨表级联属性映射与原页面／固定后端字段不一致，完整包尚未支持该映射。";
        }
        return null;
    }
    /// <summary>沿原组件只替换下划线后小写字母的算法，不能假定与C#大小写归一化等价。</summary>
    private static string ComponentProperty(string prefix,string suffix)=>Regex.Replace(prefix+suffix,"_([a-z])",match=>match.Groups[1].Value.ToUpperInvariant());
    /// <summary>null沿原详情默认值，显式属性必须为同一个JSON字段；空名和非文本不视为默认。</summary>
    private static bool PageProperty(JsonElement entry,string property,string prefix,string suffix,string expected)=>entry.TryGetProperty(property,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ValueKind==JsonValueKind.String&&value.GetString()==expected:GenerationModel.Camel(prefix+suffix)==expected;

}
