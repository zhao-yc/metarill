using System.Globalization;

namespace Metarill.Core.Contracts;

/// <summary>DATE 与无时区 DATETIME 使用确定的日历常量，供结构、生成服务和原表单共用。</summary>
public static class CalendarColumnDefault
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFF";
    private static readonly string[] DateTimeFormats = [DateFormat, DateTimeFormat, "yyyy-MM-dd HH:mm:ss.FFFFFF"];

    /// <summary>保留最多六位小数秒。DATE 只接收日期，DATETIME 的纯日期默认表示当天零点。</summary>
    public static bool TryParse(string text, bool dateOnly, out DateTime value)
    {
        value = default;
        if (text.EndsWith(".", StringComparison.Ordinal)) return false;
        return dateOnly
            ? DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
            : DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>采用原接口可读的无时区文本，完整保留有效小数秒。</summary>
    public static string Format(DateTime value, bool dateOnly) =>
        value.ToString(dateOnly ? DateFormat : DateTimeFormat, CultureInfo.InvariantCulture);
}
