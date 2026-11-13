using System.Globalization;
using System.Text.Json;
namespace Metarill.Core.Contracts;

/// <summary>原生TIME的带符号总小时合同；不使用.NET天数格式，不改变其他持续时间配置。</summary>
public static class SqlTimeValue
{
    /// <summary>解析总小时、时分秒和1至9位小数；数据库取值范围交给对应适配器。</summary>
    public static long? ParseNanoseconds(string? value)
    {
        if(value is null||value.Length==0)return null;
        var text=value.AsSpan();var negative=text[0]=='-';if(negative)text=text[1..];
        var separator=text.IndexOf(':');if(separator is <2 or >6)throw Invalid();
        long hours=0;foreach(var ch in text[..separator]){if(ch is <'0' or >'9')throw Invalid();hours=hours*10+ch-'0';}
        var remainder=ClockTimeValue.ParseNanoseconds("00"+text[separator..].ToString())??throw Invalid();
        var nanos=hours*3_600_000_000_000L+remainder;
        return negative?-nanos:nanos;
    }
    /// <summary>保留100ns可表达部分，数据库微秒精度另由实库验收。</summary>
    public static TimeSpan? ParseText(string? value)=>ParseNanoseconds(value) is {} nanos?TimeSpan.FromTicks(nanos/100):null;
    /// <summary>数字和对象不能伪装成原生TIME文本。</summary>
    public static TimeSpan? Parse(JsonElement value)=>value.ValueKind switch{JsonValueKind.Null=>null,JsonValueKind.String=>ParseText(value.GetString()),_=>throw Invalid()};
    /// <summary>输出带符号的总小时，30小时仍为30:00:00而不是1.06:00:00。</summary>
    public static string Format(TimeSpan value)
    {
        if(value==TimeSpan.MinValue)throw Invalid();
        var negative=value.Ticks<0;var magnitude=negative?-value:value;
        var hours=magnitude.Ticks/TimeSpan.TicksPerHour;
        return (negative?"-":"")+hours.ToString("D2",CultureInfo.InvariantCulture)+ClockTimeValue.Format(TimeSpan.FromTicks(magnitude.Ticks%TimeSpan.TicksPerHour))[2..];
    }
    /// <summary>固定默认必须由声明精度精确表达，禁止SQL函数或静默截断。</summary>
    public static string DefaultText(string value,int precision)
    {
        if(precision is <0 or >6)throw new ArgumentOutOfRangeException(nameof(precision));
        var nanos=ParseNanoseconds(value)??throw Invalid();long quantum=1;for(var i=precision;i<9;i++)quantum*=10;
        if(nanos%quantum!=0)throw new JsonException("时间默认值超过声明的小数秒精度。");
        return Format(TimeSpan.FromTicks(nanos/100));
    }
    /// <summary>公开错误不包含原输入。</summary>
    private static JsonException Invalid()=>new("TIME只接受带可选负号的总小时HH:mm、HH:mm:ss及可选小数秒。");
}
