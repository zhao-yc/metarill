using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Metarill.Core.Contracts;

/// <summary>原十进制输入须能精确表示为 decimal，避免反序列化时将金额变成其他值。</summary>
public sealed class ExactDecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text;
        if (reader.TokenType == JsonTokenType.String && (options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0)
            text = reader.GetString();
        else if (reader.TokenType == JsonTokenType.Number)
        {
            if ((reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length) > 512)
                throw new JsonException("小数文本超过允许长度。");
            text = reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray()) :
                Encoding.UTF8.GetString(reader.ValueSpan);
        }
        else
            throw new JsonException("小数只接受数字或允许的数值字符串。");

        return ExactNumericValue.TryDecimal(text, out var value) ? value :
            throw new JsonException("小数必须能被精确表示，不能舍入或下溢为零。");
    }

    /// <summary>沿用原数值输出及调用方明确开启的字符串输出。</summary>
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        if ((options.NumberHandling & JsonNumberHandling.WriteAsString) != 0)
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        else
            writer.WriteNumberValue(value);
    }
}
