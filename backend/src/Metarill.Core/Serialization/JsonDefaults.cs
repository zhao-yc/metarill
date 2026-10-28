using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;

namespace Metarill.Runtime;

/// <summary>平台与独立宿主复用的既有JSON合同，保留原命名空间兼容调用。</summary>
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
    /// <summary>向宿主JSON选项追加相同的字符串ID和兼容时间转换器。</summary>
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
