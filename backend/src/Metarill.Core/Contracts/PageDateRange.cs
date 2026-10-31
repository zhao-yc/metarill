using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>原日期范围的纯值合同：仅严格日期格式的终点覆盖整天，完整时间终点保持精确包含。</summary>
public sealed record PageDateRange
{
    public DateTime Start { get; }
    public DateTime End { get; }
    public bool EndExclusive { get; }
    /// <summary>不可变边界仅由解析器构造，后端查询不再自行猜测日期含义。</summary>
    private PageDateRange(DateTime start, DateTime end, bool endExclusive) { Start = start; End = end; EndExclusive = endExclusive; }

    /// <summary>解析仍使用原Invariant日期时间语义，拒绝缺失/非字符串/无效/倒序；不把任意午夜扩成一天。</summary>
    public static PageDateRange Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) throw new ArgumentException("日期范围要求起止两个值。");
        // 先限制值类型与长度，再解析日期，错误不回显原请求内容。
        static string Text(JsonElement element) => element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 and <= 100 } text ? text : throw new ArgumentException("日期范围要求有效起止日期。");
        var startText = Text(value[0]); var endText = Text(value[1]);
        if (!DateTime.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateTime.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) throw new ArgumentException("日期范围要求有效起止日期。");
        var exclusive = false;
        if (DateTime.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            // 最大合法日期没有可表示的次日，改用整个DateTime上限，不溢出也不漏当天。
            if (date == DateTime.MaxValue.Date) end = DateTime.MaxValue;
            else { end = date.AddDays(1); exclusive = true; }
        }
        if (exclusive ? start >= end : start > end) throw new ArgumentException("日期范围起点不能晚于终点。");
        return new(start, end, exclusive);
    }
}
