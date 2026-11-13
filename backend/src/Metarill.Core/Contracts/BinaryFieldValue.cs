using System.Globalization;
using System.Text.Json;
namespace Metarill.Core.Contracts;

/// <summary>显式binary使用标准JSON Base64文本；null、空字节数组和省略由各入口分别保留。</summary>
public static class BinaryFieldValue
{
    /// <summary>配置文本以 JSON 空字符串字面量表达零字节，空白配置仍没有默认值。</summary>
    public const string EmptyDefault = "\"\"";

    /// <summary>反查还原配置文本，确保零字节与未配置默认可分别保存。</summary>
    public static string EncodeDefault(byte[] bytes) => bytes.Length == 0 ? EmptyDefault : Convert.ToBase64String(bytes);
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
    /// <summary>识别配置中的零字节字面量，其余默认值沿原 Base64 和长度校验。</summary>
    public static byte[]? DefaultBytes(string text,string length)
    {
        if(text.Length==0)return null;
        try
        {
            var bytes = text == EmptyDefault ? [] : ParseText(text)!;
            Validate(bytes, MaximumLength(length));
            if (bytes.Length == 0 && text != EmptyDefault) throw new ArgumentException("零字节默认值必须使用明确的空字符串字面量。");
            return bytes;
        }
        catch(Exception error)when(error is JsonException or ArgumentException){throw new InvalidOperationException("二进制默认值必须是长度范围内的 Base64 或空字符串字面量。",error);}
    }
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
