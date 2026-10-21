using System.Globalization;

namespace Metarill.Core.Contracts;

/// <summary>固定条件及精确范围共用无静默舍入的数值转换，不通过double中转整数或十进制值。</summary>
public static class ExactNumericValue
{
    private readonly record struct Parts(string Digits,long Power,bool Negative);
    /// <summary>数值字面量允许十进制指数，但必须数学上为整数且完整落入int64范围。</summary>
    public static bool TryInt64(string? value,out long result)
    {
        result=0;if(!Normalize(value,out var parts))return false;
        if(parts.Digits=="0")return true;
        if(parts.Power<0||parts.Power+parts.Digits.Length>19)return false;
        return long.TryParse((parts.Negative?"-":"")+parts.Digits+new string('0',(int)parts.Power),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out result);
    }
    /// <summary>decimal.TryParse本身会舍入；再次比较规范化数值，拒绝超精度或下溢后变成其它数的输入。</summary>
    public static bool TryDecimal(string? value,out decimal result)
    {
        result=0;if(!Normalize(value,out var expected)||!decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)||!Normalize(parsed.ToString("G29",CultureInfo.InvariantCulture),out var actual)||expected!=actual)return false;
        result=parsed;return true;
    }
    /// <summary>逻辑double保留浮点语义，但拒绝非有限值及非零数下溢为零，避免条件意外匹配零记录。</summary>
    public static bool TryDouble(string? value,out double result)
    {
        result=0;if(!Normalize(value,out var expected)||!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)||!double.IsFinite(parsed)||parsed==0&&expected.Digits!="0")return false;
        result=parsed;return true;
    }
    /// <summary>只做有界字符串归一：去前导/尾随零后用十进制幂比较，不分配巨大整数或指数数组。</summary>
    private static bool Normalize(string? value,out Parts result)
    {
        result=default;if(value is null||value.Length>512)return false;var text=value.Trim();if(text.Length==0)return false;
        var negative=text[0]=='-';if(text[0] is '+' or '-')text=text[1..];if(text.Length==0)return false;
        var exponent=0;var index=text.IndexOfAny(['e','E']);
        if(index>=0){if(!int.TryParse(text[(index+1)..],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out exponent))return false;text=text[..index];}
        var point=text.IndexOf('.');if(point!=text.LastIndexOf('.'))return false;
        var fraction=point<0?0:text.Length-point-1;var digits=point<0?text:text.Remove(point,1);
        if(digits.Length==0||digits.Any(character=>!char.IsAsciiDigit(character)))return false;
        digits=digits.TrimStart('0');if(digits.Length==0){result=new("0",0,false);return true;}
        var end=digits.Length;while(end>0&&digits[end-1]=='0')end--;
        result=new(digits[..end],(long)exponent-fraction+digits.Length-end,negative);return true;
    }
}
