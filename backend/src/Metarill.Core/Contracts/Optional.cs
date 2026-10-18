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
        public override Optional<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options));
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
