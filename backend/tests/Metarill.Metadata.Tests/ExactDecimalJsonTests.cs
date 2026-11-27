using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>原小数数字与文本边界在写入之前拒绝无法精确表示的数值。</summary>
public sealed class ExactDecimalJsonTests
{
    [Theory]
    [InlineData("1e-29")]
    [InlineData("-1e-29")]
    [InlineData("12345678901234567890.123456789123456789")]
    public void Inexact_numbers_fail_in_both_host_serializers(string value)
    {
        var host = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonDefaults.CopyTo(host);
        foreach (var options in new[] { JsonDefaults.Options, host })
        foreach (var json in new[] { value, JsonSerializer.Serialize(value) })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>(json, options));
    }

    [Fact]
    public void Exact_values_and_optional_presence_keep_the_original_json_contract()
    {
        const decimal amount = 9007199254740993.1250m;
        foreach (var json in new[] { "9007199254740993.1250", "\"9007199254740993.1250\"" })
            Assert.Equal(amount, JsonSerializer.Deserialize<decimal>(json, JsonDefaults.Options));
        Assert.Equal("9007199254740993.1250", JsonSerializer.Serialize(amount, JsonDefaults.Options));
        Assert.False(JsonSerializer.Deserialize<Request>("{}", JsonDefaults.Options)!.Amount.IsSpecified);
        foreach(var body in new[] { "{\"amount\":null}", "{\"amount\":\" \"}" })
        {
            var value = JsonSerializer.Deserialize<Request>(body, JsonDefaults.Options)!.Amount;
            Assert.True(value.IsSpecified);
            Assert.Null(value.Value);
        }
    }

    private sealed class Request
    {
        public Optional<decimal?> Amount { get; set; }
    }
}
