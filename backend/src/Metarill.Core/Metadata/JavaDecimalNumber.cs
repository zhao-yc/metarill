using System.Globalization;
using System.Text.RegularExpressions;
namespace Metarill.Core.Metadata;

/// <summary>原Java规则共用BigDecimal数值语义；按有效数字/scale比较，不经浮点、decimal或巨大幂分配。</summary>
internal sealed partial record JavaDecimalNumber(int Sign,string Digits,long Order)
{
    /// <summary>Java BigDecimal接受Unicode十进制数字，限制最终scale而非单独限制指数为int32。</summary>
    internal static bool Try(string text,out JavaDecimalNumber result)
    {
        result=new(0,"0",0);
        if(text.Any(c=>char.IsDigit(c)&&!char.IsAsciiDigit(c)))text=new string(text.Select(c=>char.IsDigit(c)?(char)('0'+CharUnicodeInfo.GetDecimalDigitValue(c)):c).ToArray());
        var match=Pattern().Match(text);if(!match.Success)return false;
        var whole=match.Groups["whole"].Value;var fraction=match.Groups["fraction"].Value;if(whole.Length+fraction.Length==0)return false;
        long exponent=0;if(match.Groups["exponent"].Success&&!long.TryParse(match.Groups["exponent"].Value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out exponent))return false;
        // 先用最终scale约束指数，再做减法，避免long极端文本造成溢出；小数位可抵消超过int32的正指数。
        if(exponent>(long)fraction.Length-int.MinValue||exponent<(long)fraction.Length-int.MaxValue)return false;
        var scale=(long)fraction.Length-exponent;var digits=(whole+fraction).TrimStart('0');if(digits.Length==0)return true;
        result=new(match.Groups["sign"].Value=="-"?-1:1,digits.TrimEnd('0'),digits.Length-scale);return true;
    }
    /// <summary>同数量级时补虚拟尾零逐位比较，兼容超decimal精度及巨大正负指数。</summary>
    internal int CompareTo(JavaDecimalNumber other)
    {
        if(Sign!=other.Sign)return Sign.CompareTo(other.Sign);if(Sign==0)return 0;
        var order=Order.CompareTo(other.Order);if(order!=0)return Sign*order;
        for(var i=0;i<Math.Max(Digits.Length,other.Digits.Length);i++){var a=i<Digits.Length?Digits[i]:'0';var b=i<other.Digits.Length?other.Digits[i]:'0';if(a!=b)return Sign*a.CompareTo(b);}
        return 0;
    }
    [GeneratedRegex(@"^(?<sign>[+-]?)(?<whole>[0-9]*)(?:\.(?<fraction>[0-9]*))?(?:[eE](?<exponent>[+-]?[0-9]+))?$",RegexOptions.CultureInvariant)]private static partial Regex Pattern();
}
