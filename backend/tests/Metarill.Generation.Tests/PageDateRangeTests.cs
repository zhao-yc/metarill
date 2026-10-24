using System.Text.Json;
using Metarill.Core.Contracts;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原日期值与完整时间值的边界不同，不能按解析后的午夜猜测用户意图。</summary>
public sealed class PageDateRangeTests
{
    /// <summary>保留请求字节中的日期/时间区别，闰月末覆盖整日，精确时间不扩日。</summary>
    [Theory]
    [InlineData("2024-02-01", "2024-02-29", "2024-03-01T00:00:00", true)]
    [InlineData("2024-02-29T12:00:00", "2024-02-29", "2024-03-01T00:00:00", true)]
    [InlineData("2024-02-29", "2024-02-29T00:00:00", "2024-02-29T00:00:00", false)]
    [InlineData("2024-02-29", "2024-02-29 00:00:00", "2024-02-29T00:00:00", false)]
    [InlineData("2024-02-29", "2024-02-29T00:00:00.0000000", "2024-02-29T00:00:00", false)]
    public void Date_end_is_exclusive_next_day_but_full_timestamp_is_inclusive(string start, string end, string expected, bool exclusive)
    {
        var range = PageDateRange.Parse(JsonSerializer.SerializeToElement(new[] { start, end }));
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), range.End); Assert.Equal(exclusive, range.EndExclusive);
    }
    /// <summary>最大日期保持合法而不AddDays溢出。</summary>
    [Fact]
    public void Maximum_date_covers_the_last_day_without_overflow()
    { var range = PageDateRange.Parse(JsonSerializer.SerializeToElement(new[] { "9999-12-31", "9999-12-31" })); Assert.Equal(DateTime.MaxValue, range.End); Assert.False(range.EndExclusive); }
    /// <summary>空数组由Query作为清除筛选处理，解析器自身只接受完整的两个边界。</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[\"2024-02-29\"]")]
    [InlineData("[\"2024-02-29\",\"2024-02-29\",\"2024-03-01\"]")]
    [InlineData("[null,\"2024-02-29\"]")]
    [InlineData("[true,\"2024-02-29\"]")]
    [InlineData("[{},\"2024-02-29\"]")]
    [InlineData("[\"\",\"2024-02-29\"]")]
    [InlineData("[\"2023-02-29\",\"2024-02-29\"]")]
    [InlineData("[\"2024-02-29\",\"2024-02-30\"]")]
    [InlineData("[\"2024-03-01\",\"2024-02-29\"]")]
    [InlineData("[\"2024-02-29T12:00:00\",\"2024-02-29T00:00:00\"]")]
    public void Invalid_partial_or_reversed_ranges_are_rejected(string json) => Assert.Throws<ArgumentException>(() => PageDateRange.Parse(JsonSerializer.Deserialize<JsonElement>(json)));
}
