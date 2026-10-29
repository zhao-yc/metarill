using System.Globalization;
using System.Text.Json;
namespace Metarill.Core.Contracts;

/// <summary>显式binary使用标准JSON Base64文本；null、空字节数组和省略由各入口分别保留。</summary>
public static class BinaryFieldValue
{
    /// <summary>复用System.Text.Json实际byte[]解码，不接受JSON数值数组或任意对象。</summary>
    public static byte[]? Parse(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind!=JsonValueKind.String)throw new JsonException("二进制字段必须是Base64文本。");
        try{return value.GetBytesFromBase64();}
        catch(FormatException error){throw new JsonException("二进制字段必须是有效Base64文本。",error);}
    }
    /// <summary>手写基础设施与默认值检查沿同一JSON解码器，不自行采用不同的宽松语法。</summary>
    public static byte[]? ParseText(string? text)=>text is null?null:Parse(JsonSerializer.SerializeToElement(text));
    /// <summary>本机byte[]直接作为二进制参数；文本和JsonElement遵守公开Base64合同。</summary>
    public static byte[]? Coerce(object? value)=>value switch
    {
        null=>null,byte[] bytes=>bytes,JsonElement json=>Parse(json),string text=>ParseText(text),
        _=>throw new JsonException("二进制字段必须是字节数组或Base64文本。")
    };
    /// <summary>空长度表示不设逻辑上限；空白保持既有DDL的255回退，物理最大值仍由数据库适配器验证。</summary>
    public static int? MaximumLength(string length)
    {
        if(length.Length==0)return null;if(string.IsNullOrWhiteSpace(length))return 255;
        if(!int.TryParse(length,NumberStyles.None,CultureInfo.InvariantCulture,out var maximum)||maximum<=0)throw new InvalidOperationException("二进制字段长度必须是正整数或留空。");
        return maximum;
    }
    /// <summary>按实际字节数校验，不能按Base64字符数截断或把空数组改为null。</summary>
    public static void Validate(byte[]? bytes,int? maximum)
    {
        if(maximum is <=0)throw new ArgumentOutOfRangeException(nameof(maximum));
        if(bytes is not null&&maximum is{} length&&bytes.Length>length)throw new ArgumentException("二进制字段超过配置的字节长度。");
    }
}
