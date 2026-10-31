using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Metadata.Tests;

/// <summary>原生TIME不能被日内picker范围或.NET持续时间天数形状误改。</summary>
public sealed class SqlTimeJsonTests
{
    /// <summary>普通TIME保持符号和总小时，微秒沿文本往返。</summary>
    [Theory][InlineData("30:00:00","30:00:00")][InlineData("-25:01:02.123456","-25:01:02.123456")]
    [InlineData("838:59:59.999999","838:59:59.999999")][InlineData("-838:59:59.999999","-838:59:59.999999")]
    [InlineData("00:00","00:00:00")][InlineData("-00:00:00","00:00:00")]
    public void Native_time_preserves_signed_total_hours(string text,string expected)
    {
        var body=JsonSerializer.Serialize(new{time=text});var dto=JsonSerializer.Deserialize<Request>(body,JsonDefaults.Options)!;
        Assert.True(dto.Time.IsSpecified);Assert.Equal(expected,SqlTimeValue.Format(dto.Time.Value!.Value));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(dto,JsonDefaults.Options));Assert.Equal(expected,json.RootElement.GetProperty("time").GetString());
    }
    /// <summary>不接收.NET天数、偏移、数字或SQL表达式；非法文本不会成为清空。</summary>
    [Theory][InlineData("\"1.06:00:00\"")][InlineData("\"30:60:00\"")][InlineData("\"-\"")][InlineData("\"+30:00\"")]
    [InlineData("\"CURRENT_TIME()\"")][InlineData("123")][InlineData("{}")]
    public void Invalid_native_time_is_rejected(string value)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<Request>("{\"time\":"+value+"}",JsonDefaults.Options));
    /// <summary>字段存在性与其他可选合同相同；全局持续时间仍使用原转换器。</summary>
    [Fact]
    public void Native_time_preserves_optional_semantics()
    {
        Assert.False(JsonSerializer.Deserialize<Request>("{}",JsonDefaults.Options)!.Time.IsSpecified);
        foreach(var value in new[]{"null","\"\""}){var dto=JsonSerializer.Deserialize<Request>("{\"time\":"+value+"}",JsonDefaults.Options)!;Assert.True(dto.Time.IsSpecified);Assert.Null(dto.Time.Value);}
        Assert.Equal(3_600_000_000_000L*30+1,SqlTimeValue.ParseNanoseconds("30:00:00.000000001"));
    }
    public sealed class Request{[JsonConverter(typeof(SqlTimeJsonConverter))]public Optional<TimeSpan?> Time{get;set;}}
}
