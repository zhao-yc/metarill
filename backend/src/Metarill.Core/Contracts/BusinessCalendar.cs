namespace Metarill.Core.Contracts;

/// <summary>业务审计采用原Java宿主日历；不用于会话、令牌或上传期限。</summary>
public static class BusinessCalendar
{
    /// <summary>去除.NET本地Kind，保持LocalDateTime/DATETIME无时区值；JSON不附加时区或再次偏移。</summary>
    public static DateTime Now => DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
}
