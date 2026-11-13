using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Runtime.Security;
using Xunit;
namespace Metarill.Security.Tests;

/// <summary>以实际JDK日历转换结果验证原账号窗口；不连接SQL或伪装真实登录/授权验收。</summary>
public sealed class AccountCalendarWindowTests
{
    /// <summary>真实Java向量覆盖常见偏移、跳时与重叠；仅日历字段解释时区，边界包含规则保持原实现。</summary>
    [Fact]
    public void Calendar_boundaries_follow_actual_java_at_zone_vectors()
    {
        foreach(var item in Vectors())
        {
            var calendar=DateTime.Parse(item.Calendar,CultureInfo.InvariantCulture,DateTimeStyles.None);Assert.Equal(DateTimeKind.Unspecified,calendar.Kind);
            var zone=TimeZoneInfo.FindSystemTimeZoneById(item.Zone);
            foreach(var field in new[]{"validity_time","login_stop_time","login_rise_time"})
            {
                // 日期范围两端只比较当前真实时刻，不伪造.NET不能构造的越界参考瞬时。
                var references=calendar.Year<1970||item.UtcTicks>DateTime.MaxValue.Ticks||item.UtcTicks<DateTime.MinValue.Ticks
                    ?new[]{DateTimeOffset.UtcNow}:new[]{new DateTimeOffset(item.UtcTicks-1,TimeSpan.Zero),new DateTimeOffset(item.UtcTicks,TimeSpan.Zero),new DateTimeOffset(item.UtcTicks+1,TimeSpan.Zero)};
                foreach(var now in references)
                {
                    var row=Row(field,calendar);var before=JsonSerializer.Serialize(row);var expected=field=="login_rise_time"?item.UtcTicks<=now.UtcTicks:item.UtcTicks>now.UtcTicks;
                    Assert.True(AuthenticationService.AccountActive(row,now,zone)==expected,$"日历窗口不符：{item.Zone}/{item.Calendar}/{field}/{now:O}");Assert.Equal(before,JsonSerializer.Serialize(row));
                }
            }
        }
    }
    /// <summary>原HTTP空格时间与库内Unspecified值都按日历解释，UTC与带偏移的服务端瞬时不重复偏移。</summary>
    [Theory][InlineData("UTC")][InlineData("Asia/Shanghai")][InlineData("Asia/Kathmandu")]
    public void Wire_calendar_and_explicit_instant_values_have_distinct_interpretation(string zoneId)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);var now=new DateTimeOffset(2026,10,29,12,0,0,TimeSpan.Zero);var calendar=TimeZoneInfo.ConvertTime(now,zone).DateTime;
        foreach(var field in new[]{"validity_time","login_stop_time","login_rise_time"})foreach(var past in new[]{true,false})
        {
            var expected=field=="login_rise_time"?past:!past;var delta=past?-15:15;
            foreach(var value in new object[]{calendar.AddMinutes(delta),calendar.AddMinutes(delta).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),now.AddMinutes(delta).UtcDateTime,now.AddMinutes(delta),now.AddMinutes(delta).LocalDateTime})
                Assert.Equal(expected,AuthenticationService.AccountActive(Row(field,value),now,zone));
        }
    }
    /// <summary>真实系统入口使用相同宿主日历规则，不能只让测试专用重载通过。</summary>
    [Fact]
    public void Public_entry_uses_host_calendar_without_reinterpreting_session_utc()
    {
        var calendar=DateTime.SpecifyKind(DateTime.Now,DateTimeKind.Unspecified);
        foreach(var field in new[]{"validity_time","login_stop_time","login_rise_time"})foreach(var past in new[]{true,false})
            Assert.Equal(field=="login_rise_time"?past:!past,AuthenticationService.AccountActive(Row(field,calendar.AddMinutes(past?-15:15))));
        Assert.True(AuthenticationService.AccountActive(new Dictionary<string,object?>{{"is_disabled",0}}));
        Assert.False(AuthenticationService.AccountActive(new Dictionary<string,object?>{{"is_disabled",1}}));
    }
    /// <summary>损坏日历值拒绝账号，且不改已确认的物理disabled含义或原始记录。</summary>
    [Theory][InlineData("validity_time")][InlineData("login_stop_time")][InlineData("login_rise_time")]
    public void Invalid_boundaries_fail_closed_without_leaking_parse_exceptions(string field)
    {
        foreach(var value in new object[]{"","not-a-date",42,new object()})Assert.False(AuthenticationService.AccountActive(Row(field,value),DateTimeOffset.UtcNow,TimeZoneInfo.Utc));
    }
    /// <summary>保持最小真实账号字段，不增加任何伪权限或数据库依赖。</summary>
    private static Dictionary<string,object?> Row(string field,object value)=>new(){{"is_disabled",0},{field,value}};
    /// <summary>向量由本任务实际JDK生成，读取原文件而非在.NET复制Java转换算法。</summary>
    private static IReadOnlyList<Vector> Vectors([CallerFilePath]string source="")
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!,"Fixtures/account-calendar-vectors.json")));
        return document.RootElement.GetProperty("vectors").EnumerateArray().Select(item=>new Vector(item.GetProperty("zone").GetString()!,item.GetProperty("calendar").GetString()!,long.Parse(item.GetProperty("utcTicks").GetString()!,CultureInfo.InvariantCulture))).ToArray();
    }
    private sealed record Vector(string Zone,string Calendar,long UtcTicks);
}
