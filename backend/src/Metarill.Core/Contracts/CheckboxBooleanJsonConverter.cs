using System.Text.Json;
using System.Text.Json.Serialization;

namespace Metarill.Core.Contracts;

/// <summary>只标注已发布CheckBox布尔字段，兼容原0/1模型；不注册为全局bool转换器。</summary>
public sealed class CheckboxBooleanJsonConverter : JsonConverterFactory
{
    /// <summary>字段属性转换只接受可空bool与其明确存在性包装。</summary>
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(bool?) || typeToConvert == typeof(Optional<bool?>);
    /// <summary>VO保持CLR bool，输出原数字模型；DTO仍记录省略与null。</summary>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => typeToConvert == typeof(bool?) ? new NullableBooleanConverter() : typeToConvert == typeof(Optional<bool?>) ? new OptionalBooleanConverter() : throw new NotSupportedException("CheckBox转换器只用于可空布尔字段。");

    /// <summary>筛选与DTO使用同一数值规则，禁止2、小数、指数或精度舍入冒充0/1。</summary>
    public static bool? Parse(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.GetRawText() == "0" => false,
        JsonValueKind.Number when value.GetRawText() == "1" => true,
        JsonValueKind.String => ParseText(value.GetString()),
        _ => throw new JsonException("复选框只接受0、1、布尔值或明确清空。")
    };
    /// <summary>原空白清空合同保留；字符串本身不经过数值解析或trim后冒充数字。</summary>
    private static bool? ParseText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value switch { "0" => false, "1" => true, _ => throw new JsonException("复选框只接受0、1、布尔值或明确清空。") };
    /// <summary>跨分段读取同样只接受单字节整数词元，不经double转换。</summary>
    private static bool? Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.True) return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.String) return ParseText(reader.GetString());
        if (reader.TokenType == JsonTokenType.Number && (reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length) == 1 && reader.TryGetInt32(out var number) && number is 0 or 1) return number == 1;
        throw new JsonException("复选框只接受0、1、布尔值或明确清空。");
    }
    /// <summary>已声明字段回显始终使用原控件的数字值，null仍为空。</summary>
    private static void Write(Utf8JsonWriter writer, bool? value) { if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value ? 1 : 0); }
    private sealed class NullableBooleanConverter : JsonConverter<bool?>
    {
        /// <summary>VO读取兼容原数值和已有bool。</summary>
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => CheckboxBooleanJsonConverter.Read(ref reader);
        /// <summary>只对标注字段写0/1，普通bool不经过这里。</summary>
        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options) => CheckboxBooleanJsonConverter.Write(writer, value);
    }
    private sealed class OptionalBooleanConverter : JsonConverter<Optional<bool?>>
    {
        public override bool HandleNull => true;
        /// <summary>显式null与空串仍标记为提交，省略由默认Optional保留。</summary>
        public override Optional<bool?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(CheckboxBooleanJsonConverter.Read(ref reader));
        /// <summary>写入机器合同的明确值时沿用相同数字编码。</summary>
        public override void Write(Utf8JsonWriter writer, Optional<bool?> value, JsonSerializerOptions options) => CheckboxBooleanJsonConverter.Write(writer, value.Value);
    }
}
