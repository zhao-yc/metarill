using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Metadata.Tests;

/// <summary>实际字段转换与请求schema；不把纳秒承接或离线序列化冒充数据库精度/舍入验收。</summary>
public sealed class ClockTimeJsonTests
{
    /// <summary>保留原成功格式，并按已确认规则补齐微秒及合理完整无偏移时间。</summary>
    [Theory]
    [InlineData("12:34","12:34:00")][InlineData("00:00:00","00:00:00")]
    [InlineData("12:34:56.1","12:34:56.1")][InlineData("12:34:56.123456","12:34:56.123456")]
    [InlineData("2026-09-29 12:34:56","12:34:56")][InlineData("2026-09-29T12:34:56.123456","12:34:56.123456")]
    [InlineData("23:59:59.999999","23:59:59.999999")]
    public void Valid_clock_fields_parse_and_write_original_string_shape(string value,string expected)
    {
        var field=JsonSerializer.Deserialize<Value>(JsonSerializer.Serialize(new{clock=value}),JsonDefaults.Options)!;
        Assert.Equal(expected,ClockTimeValue.Format(field.Clock!.Value));
        using var output=JsonDocument.Parse(JsonSerializer.Serialize(field,JsonDefaults.Options));Assert.Equal(expected,output.RootElement.GetProperty("clock").GetString());
    }
    /// <summary>空串/null明确清空，省略不参与更新；不从日期或任意无效文本推导空值。</summary>
    [Fact]
    public void Optional_clock_keeps_omission_and_explicit_clear()
    {
        Assert.False(JsonSerializer.Deserialize<Request>("{}",JsonDefaults.Options)!.Clock.IsSpecified);
        foreach(var body in new[]{"{\"clock\":null}","{\"clock\":\"\"}"}){var request=JsonSerializer.Deserialize<Request>(body,JsonDefaults.Options)!;Assert.True(request.Clock.IsSpecified);Assert.Null(request.Clock.Value);}
        Assert.Equal(TimeSpan.FromHours(12)+TimeSpan.FromMinutes(34),JsonSerializer.Deserialize<Request>("{\"clock\":\"12:34\"}",JsonDefaults.Options)!.Clock.Value);
    }
    /// <summary>已确认修复原静默清空；数字/复合形状、跨日、偏移和非法日期都明确拒绝。</summary>
    [Theory]
    [InlineData("\" \"")][InlineData("0")][InlineData("true")][InlineData("{}")][InlineData("[]")]
    [InlineData("\"24:00\"")][InlineData("\"-01:00:00\"")][InlineData("\"1.02:03:04\"")]
    [InlineData("\"2026-09-29\"")][InlineData("\"12:34:56Z\"")][InlineData("\"12:34:56+08:00\"")]
    [InlineData("\"2026-02-30 12:34:56\"")][InlineData("\"12:34:56.1234567890\"")]
    public void Invalid_values_do_not_clear_or_reach_business(string input)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<Request>("{\"clock\":"+input+"}",JsonDefaults.Options));
    /// <summary>原九位输入仍可接收，记录.NET承接的100ns边界；普通持续时间配置完全不经过字段转换。</summary>
    [Fact]
    public void Nanosecond_input_and_unrelated_duration_have_explicit_boundaries()
    {
        var nanos=ClockTimeValue.ParseNanoseconds("12:34:56.123456789");Assert.Equal(45296123456789L,nanos);
        Assert.Equal(nanos/100,ClockTimeValue.ParseText("12:34:56.123456789")!.Value.Ticks);
        var duration=TimeSpan.FromHours(30);Assert.Equal(duration,JsonSerializer.Deserialize<TimeSpan>(JsonSerializer.Serialize(duration,JsonDefaults.Options),JsonDefaults.Options));
        Assert.Throws<JsonException>(()=>JsonSerializer.Serialize(new Value{Clock=duration},JsonDefaults.Options));
        using var schema=JsonDocument.Parse(JsonSerializer.Serialize(RequestJsonSchema.Create(typeof(Request))));Assert.Equal("object",schema.RootElement.GetProperty("type").GetString());
    }
    /// <summary>实际Excel时钟文本保持Java LocalTime的简短秒位和小数组。</summary>
    [Theory][InlineData("12:34","12:34")][InlineData("12:34:56.1","12:34:56.100")][InlineData("12:34:56.000001","12:34:56.000001")]
    public void Export_text_preserves_original_time_style(string input,string expected)=>Assert.Equal(expected,ClockTimeValue.FormatExport(ClockTimeValue.ParseText(input)!.Value));
    /// <summary>真实DTO字段schema兼容原picker及原生总小时，不使用会误拒绝这些输入的RFC3339格式。</summary>
    [Fact]
    public void Field_schema_matches_clock_and_native_input_shapes()
    {
        string Pattern(Type type)
        {
            using var schema=JsonDocument.Parse(JsonSerializer.Serialize(RequestJsonSchema.Create(type)));
            var field=schema.RootElement.GetProperty("properties").EnumerateObject().Single().Value;
            Assert.False(field.TryGetProperty("format",out _));Assert.Contains(field.GetProperty("type").EnumerateArray(),item=>item.GetString()=="null");return field.GetProperty("pattern").GetString()!;
        }
        var clock=Pattern(typeof(Request));var native=Pattern(typeof(SqlTimeJsonTests.Request));
        foreach(var value in new[]{"","12:34","12:34:56.123456","2026-09-29 12:34:56.123456789"})Assert.Matches(clock,value);
        foreach(var value in new[]{"25:00","2026-09-29","12:34:56Z","12:34:56.1234567890"})Assert.DoesNotMatch(clock,value);
        foreach(var value in new[]{"","-30:00","838:59:59","12:34:56.123456"})Assert.Matches(native,value);
        foreach(var value in new[]{"1.06:00:00","30:60","2026-09-29","12:34:56Z"})Assert.DoesNotMatch(native,value);
    }
    public sealed class Value{[JsonConverter(typeof(ClockTimeJsonConverter))]public TimeSpan? Clock{get;set;}}
    public sealed class Request{[JsonConverter(typeof(ClockTimeJsonConverter))]public Optional<TimeSpan?> Clock{get;set;}}
}
