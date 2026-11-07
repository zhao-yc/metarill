using System.Text.Json;
using Metarill.Core.Contracts;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>沿用旧下拉清空协议，不能将空白转换扩大到必填标识或普通字符串。</summary>
public sealed class OptionalCompatibilityTests
{
    [Fact]
    public void Blank_nullable_values_are_explicit_null_while_omission_is_unspecified()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var value = JsonSerializer.Deserialize<BlankRequest>("{\"id\":\" \",\"count\":\"\",\"amount\":\"  \",\"enabled\":\"\",\"date\":\"\",\"text\":\" \"}", options)!;
        Assert.True(value.Id.IsSpecified); Assert.Null(value.Id.Value);
        Assert.True(value.Count.IsSpecified); Assert.Null(value.Count.Value);
        Assert.True(value.Amount.IsSpecified); Assert.Null(value.Amount.Value);
        Assert.True(value.Enabled.IsSpecified); Assert.Null(value.Enabled.Value);
        Assert.True(value.Date.IsSpecified); Assert.Null(value.Date.Value);
        Assert.True(value.Text.IsSpecified); Assert.Equal(" ", value.Text.Value);
        Assert.False(JsonSerializer.Deserialize<BlankRequest>("{}", options)!.Id.IsSpecified);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Optional<int>>("\"\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Optional<int?>>("\"无效数字\"", options));
    }
    public sealed class BlankRequest
    {
        public Optional<long?> Id { get; set; }
        public Optional<int?> Count { get; set; }
        public Optional<decimal?> Amount { get; set; }
        public Optional<bool?> Enabled { get; set; }
        public Optional<DateTime?> Date { get; set; }
        public Optional<string?> Text { get; set; }
    }
}
