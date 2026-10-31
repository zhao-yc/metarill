using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>通用数据库复选的精确ID数组；全选预算与候选5000项一致，不沿用弹层多选的200项限制。</summary>
public static class DatabaseCheckboxValue
{
    public const int MaximumItems=5000;
    /// <summary>保留字符串数组文本；旧数值数组按精确long规范化为字符串，绝不经浮点数中转。</summary>
    public static string? ReadWrite(JsonElement? value)
    {
        if(value is null||value.Value.ValueKind==JsonValueKind.Null)return null;
        var text=value.Value.ValueKind switch{JsonValueKind.Array=>value.Value.GetRawText(),JsonValueKind.String=>value.Value.GetString()!,_=>throw new ArgumentException("数据库复选只接受数组、已编码数组文本或null。")};
        var ids=Parse(text);using var doc=JsonDocument.Parse(text);
        return doc.RootElement.EnumerateArray().All(item=>item.ValueKind==JsonValueKind.String)?text:JsonSerializer.Serialize(ids.Select(Id).ToArray());
    }
    /// <summary>缺省空集合用于内部校验；非空必须是有界、唯一、规范正整数，不默默截断或去重。</summary>
    public static long[] Parse(string? text)
    {
        if(text is null)return [];
        if(string.IsNullOrWhiteSpace(text)||text.Length>MaximumItems*24+2)throw new ArgumentException("数据库复选文本必须为有界JSON数组。");
        try
        {
            using var document=JsonDocument.Parse(text);var value=document.RootElement;
            if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>MaximumItems)throw new ArgumentException("数据库复选最多5000项，不能截断全选结果。");
            var ids=value.EnumerateArray().Select(ReadId).ToArray();if(ids.Distinct().Count()!=ids.Length)throw new ArgumentException("数据库复选ID不能重复。");return ids;
        }
        catch(JsonException error){throw new ArgumentException("数据库复选JSON格式无效。",error);}
    }
    /// <summary>公开VO输出精确字符串ID；null与空数组仍保持不同，旧坏值明确拒绝而不修补源存储。</summary>
    public static JsonElement? ProjectIds(string? text)=>text is null?null:JsonSerializer.SerializeToElement(Parse(text).Select(Id).ToArray());
    /// <summary>候选只读回显允许原数组或单值ID；显式空值返回空集合，不回退全部候选。</summary>
    public static long[] ReadSelected(JsonElement? value)
    {
        if(value is null||value.Value.ValueKind==JsonValueKind.Null||value.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(value.Value.GetString()))return [];
        var item=value.Value;
        if(item.ValueKind==JsonValueKind.String&&item.GetString()!.TrimStart().StartsWith('['))return Parse(item.GetString());
        return item.ValueKind==JsonValueKind.Array?Parse(item.GetRawText()):[ReadId(item)];
    }
    /// <summary>原下拉筛选的单ID按成员相等解释，1不能误匹配11。</summary>
    public static long ReadId(JsonElement value)
    {
        var text=value.ValueKind switch{JsonValueKind.String=>value.GetString(),JsonValueKind.Number when value.TryGetInt64(out _)=>value.GetRawText(),_=>null};
        if(text is null||!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=Id(id))throw new ArgumentException("数据库复选ID必须为精确规范正整数。");return id;
    }
    /// <summary>长整数标识固定以十进制字符串输出。</summary>
    private static string Id(long id)=>id.ToString(CultureInfo.InvariantCulture);
}
