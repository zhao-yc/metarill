using System.Globalization;
using System.Text.Json;
namespace Metarill.Core.Contracts;

/// <summary>原时间控件的日内时刻合同；与持续时间、日期或UTC瞬时分开。</summary>
public static class ClockTimeValue
{
    private const long NanosecondsPerSecond=1_000_000_000;
    /// <summary>只接受时分、时分秒及1至9位小数；原完整无偏移日期时间提取时刻，空串/null明确清空。</summary>
    public static long? ParseNanoseconds(string? value)
    {
        if(value is null||value.Length==0)return null;
        if(value.Length>29)throw Invalid();
        var text=value.AsSpan();
        if(text.Length>=19&&(text[10]==' '||text[10]=='T'))
        {
            if(!DateOnly.TryParseExact(text[..10],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw Invalid();
            text=text[11..];
        }
        if(text.Length!=5&&text.Length!=8&&(text.Length<10||text.Length>18))throw Invalid();
        if(text[2]!=':')throw Invalid();
        var hour=Pair(text[..2]);var minute=Pair(text.Slice(3,2));var second=0;
        if(hour>23||minute>59)throw Invalid();
        if(text.Length>=8){if(text[5]!=':')throw Invalid();second=Pair(text.Slice(6,2));if(second>59)throw Invalid();}
        long nanos=0;
        if(text.Length>8)
        {
            if(text[8]!='.')throw Invalid();
            for(var i=9;i<text.Length;i++){var digit=text[i]-'0';if(digit is <0 or >9)throw Invalid();nanos=nanos*10+digit;}
            for(var i=text.Length-9;i<9;i++)nanos*=10;
        }
        return ((hour*60L+minute)*60+second)*NanosecondsPerSecond+nanos;
    }
    /// <summary>以.NET 100ns承接输入，不经浮点；低于一tick的余数不进入TimeSpan，数据库微秒/舍入另行验收。</summary>
    public static TimeSpan? ParseText(string? value)=>ParseNanoseconds(value) is {} nanos?TimeSpan.FromTicks(nanos/100):null;
    /// <summary>仅接受JSON文本和明确null；数字、布尔、数组及对象不是时钟输入。</summary>
    public static TimeSpan? Parse(JsonElement value)=>value.ValueKind switch{JsonValueKind.Null=>null,JsonValueKind.String=>ParseText(value.GetString()),_=>throw Invalid()};
    /// <summary>手写Service调用和响应同样不能把负数或跨日持续时间作为日内时刻。</summary>
    public static bool IsClock(TimeSpan value)=>value.Ticks>=0&&value.Ticks<TimeSpan.TicksPerDay;
    /// <summary>响应沿原Jackson字符串形状，固定秒位并只输出实际存在的小数，不附日期、时区或天数。</summary>
    public static string Format(TimeSpan value)
    {
        if(!IsClock(value))throw Invalid();
        var text=value.Hours.ToString("D2",CultureInfo.InvariantCulture)+":"+value.Minutes.ToString("D2",CultureInfo.InvariantCulture)+":"+value.Seconds.ToString("D2",CultureInfo.InvariantCulture);
        var fraction=value.Ticks%TimeSpan.TicksPerSecond;
        return fraction==0?text:text+"."+fraction.ToString("D7",CultureInfo.InvariantCulture).TrimEnd('0');
    }
    /// <summary>Excel保持原LocalTime.toString的时分及3/6/9位小数分组，仍是文本。</summary>
    public static string FormatExport(TimeSpan value)
    {
        var clock=Format(value);var fraction=value.Ticks%TimeSpan.TicksPerSecond;
        if(fraction==0)return value.Seconds==0?clock[..5]:clock;
        var nanos=fraction*100;var digits=nanos%1_000_000==0?3:nanos%1_000==0?6:9;
        return clock[..8]+"."+nanos.ToString("D9",CultureInfo.InvariantCulture)[..digits];
    }
    /// <summary>安全默认值必须能由声明的数据库精度精确表示，不静默截断或接受SQL表达式。</summary>
    public static string DefaultText(string value,int precision)
    {
        if(precision is <0 or >6)throw new ArgumentOutOfRangeException(nameof(precision));
        var nanos=ParseNanoseconds(value)??throw Invalid();long quantum=1;for(var i=precision;i<9;i++)quantum*=10;
        if(nanos%quantum!=0)throw new JsonException("时间默认值超过声明的小数秒精度。");
        return Format(TimeSpan.FromTicks(nanos/100));
    }
    /// <summary>两位ASCII数值避免当前区域和Unicode数字造成接口歧义。</summary>
    private static int Pair(ReadOnlySpan<char> value){if(value[0] is <'0' or >'9'||value[1] is <'0' or >'9')throw Invalid();return (value[0]-'0')*10+value[1]-'0';}
    /// <summary>固定公开错误，不回显原输入或内部解析异常。</summary>
    private static JsonException Invalid()=>new("时间只接受有效日内时刻HH:mm、HH:mm:ss及可选小数秒。");
}
