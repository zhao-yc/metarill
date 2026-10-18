using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;

namespace Metarill.Runtime;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();
    /// <summary>统一配置文件、HTTP 和代码生成的字段及时间语义。</summary>
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
        options.Converters.Add(new StringLongJsonConverter());
        options.Converters.Add(new CompatibleDateTimeConverter());
        return options;
    }
    public static void CopyTo(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.Converters.Add(new StringLongJsonConverter());
        options.Converters.Add(new CompatibleDateTimeConverter());
    }
    private sealed class CompatibleDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
    }
}
