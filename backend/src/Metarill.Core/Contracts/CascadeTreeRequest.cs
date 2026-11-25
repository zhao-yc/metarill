using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Metarill.Core.Contracts;

/// <summary>原级联请求；各模式按固定生成计划解析，目标和排序只能由生成期定义。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CascadeTreeOptionsRequest
{
    public string? LevelKey { get; set; }
    public JsonElement SelectedPath { get; set; }

    /// <summary>兼容原自树的空请求上下文；提供层标识或父对象时仍只接受固定结构。</summary>
    public long? ParseParent()=>CascadeTreeId.Parse(Parent(),true);

    /// <summary>业务文本键保留前导零和大小写，沿用原父对象结构。</summary>
    public string? ParseTextParent()=>CascadeTreeId.ParseText(Parent(),true);

    private JsonElement Parent()
    {
        if (LevelKey is {} level && (!level.StartsWith("selfTree-",StringComparison.Ordinal) ||
            !int.TryParse(level[9..],NumberStyles.None,CultureInfo.InvariantCulture,out var depth) || depth is <0 or >=64))
            throw new ArgumentException("自关联级联层标识无效或超过64层。");
        if (SelectedPath.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return default;
        if (SelectedPath.ValueKind != JsonValueKind.Object) throw new ArgumentException("selectedPath必须为对象。");
        var fields=SelectedPath.EnumerateObject().ToArray();
        if (fields.Length>1 || fields.Any(field=>field.Name!="parentId")) throw new ArgumentException("自关联级联只接受一个parentId。");
        return fields.Length==0 ? default : fields[0].Value;
    }
}

/// <summary>原路径回读只传叶子ID；空值返回空路径。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CascadeTreePathRequest
{
    public JsonElement LeafId { get; set; }
    public long? ParseLeaf() => CascadeTreeId.Parse(LeafId,true);
    public string? ParseTextLeaf() => CascadeTreeId.ParseText(LeafId,true);
}

/// <summary>JSON数字不经过浮点转换，保留高位字符串ID。</summary>
public static class CascadeTreeId
{
    /// <summary>文本业务键按原值匹配，JSON 数字保留原词法形式，不转换为整数。</summary>
    public static string? ParseText(JsonElement value,bool allowEmpty)
    {
        if(allowEmpty && value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)return null;
        if(value.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))
            throw new ArgumentException("级联ID必须为文本或数字。");
        var text=value.ToString();
        if(!string.IsNullOrWhiteSpace(text))return text;
        if(allowEmpty)return null;
        throw new ArgumentException("级联ID不能为空白。");
    }

    /// <summary>拒绝浮点、越界与非标量ID，允许原组件空选择。</summary>
    public static long? Parse(JsonElement value,bool allowEmpty)
    {
        if (allowEmpty && (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || value.ValueKind==JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))) return null;
        var text=value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
        if (!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id) || id<=0) throw new ArgumentException("级联ID必须为正整数文本。");
        return id;
    }
}
