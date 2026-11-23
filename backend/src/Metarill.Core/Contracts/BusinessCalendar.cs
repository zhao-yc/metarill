namespace Metarill.Core.Contracts;

/// <summary>业务审计采用原Java宿主日历；不用于会话、令牌或上传期限。</summary>
public static class BusinessCalendar
{
    /// <summary>去除.NET本地Kind，保持LocalDateTime/DATETIME无时区值；JSON不附加时区或再次偏移。</summary>
    public static DateTime Now => DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

    /// <summary>沿原 Java 工作簿的日历文本，日期时间保留有效小数秒且不附加时区。</summary>
    public static string FormatExcel(DateTime value, bool dateOnly)
    {
        var date = CalendarColumnDefault.Format(value, dateOnly: true);
        return dateOnly ? date : date + "T" + ClockTimeValue.FormatExport(value.TimeOfDay);
    }
}
