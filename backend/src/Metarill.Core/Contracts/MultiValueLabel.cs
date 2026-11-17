using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>多选和复选的标量名称以可还原文本跨浏览器、Excel 和名称缓存传递。</summary>
public static class MultiValueLabel
{
    private static readonly string[] DateTimePatterns = ["yyyy-MM-dd HH:mm:ss.FFFFFF", "yyyy-MM-dd'T'HH:mm:ss.FFFFFF"];

    /// <summary>可确定为单值、可读且能由原名称反查的逻辑类型。</summary>
    public static bool IsTyped(string kind) => kind is "int32" or "int64" or "decimal" or "double" or "bool" or "date" or "datetime" or "time";
    /// <summary>输出精确数值和数据库日历时间。保留 null，不附加时区或区域格式。</summary>
    public static string? Text(string kind, object? value)
    {
        if (value is null) return null;
        return value switch
        {
            bool flag => flag ? "true" : "false",
            decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            double number => ExactNumericValue.FormatDoubleLabel(number),
            DateTime date => date.ToString(kind == "date" ? "yyyy-MM-dd" : DateTimePatterns[0], CultureInfo.InvariantCulture),
            TimeSpan time => SqlTimeValue.Format(time),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>导入时按实际数据库类型解析，溢出、精度丢失或非标量文本使用原关系错误。</summary>
    public static object Parse(string kind, string? text)
    {
        switch (kind)
        {
            case "decimal" when ExactNumericValue.TryDecimal(text, out var number): return number;
            case "double" when ExactNumericValue.TryDouble(text, out var floating): return floating;
            case "bool" when bool.TryParse(text, out var flag): return flag;
            case "bool" when text is "0" or "1": return text == "1";
            case "date" when DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date): return date;
            case "datetime" when DateTime.TryParseExact(text, DateTimePatterns, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time): return time;
            case "time":
                try
                {
                    if (SqlTimeValue.ParseNanoseconds(text) is { } nanos &&
                        nanos % 1000 == 0 && nanos is >= -3020399000000000L and <= 3020399000000000L)
                        return TimeSpan.FromTicks(nanos / 100);
                }
                catch (JsonException error)
                {
                    throw PageImportException.Input(PageImportError.Relation, inner: error);
                }
                break;
        }
        throw PageImportException.Input(PageImportError.Relation);
    }
}
