using System.Text.Json;
using System.Text.Json.Serialization;

namespace Metarill.Core.Contracts;

/// <summary>显式记录 JSON 字段存在性，省略不修改，null 请求清空。</summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public bool IsSpecified { get; }
    public T? Value { get; }
    public Optional(T? value) { IsSpecified = true; Value = value; }
}
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);
    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(type.GetGenericArguments()[0]))!;

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public override bool HandleNull => true;
        /// <summary>旧下拉控件用空白字符串清空可空值，仍标记为明确提交；字符串本身保持原值。</summary>
        public override Optional<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Nullable.GetUnderlyingType(typeof(T)) is not null && string.IsNullOrWhiteSpace(reader.GetString()))
                return new(default);
            return new(JsonSerializer.Deserialize<T>(ref reader, options));
        }
        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}

/// <summary>整型标识始终作为字符串输出，同时兼容已有数字输入。</summary>
public sealed class StringLongJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? long.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : reader.GetInt64();
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>原分页与Count的计数使用JSON数字，不能套用整型标识的字符串输出规则。</summary>
public sealed class NumericLongJsonConverter : JsonConverter<long>
{
    /// <summary>兼容既有数字字符串输入，拒绝溢出或非整数，保持完整int64精度。</summary>
    public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number)) return number;
        if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)) return number;
        throw new JsonException("计数必须为int64整数。");
    }
    /// <summary>明确输出原接口的数字计数，标识字段仍使用StringLongJsonConverter。</summary>
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
