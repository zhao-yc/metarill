using System.Numerics;

namespace Metarill.Core.Contracts;

/// <summary>保留原Java列表百分比的float运算和两位四舍五入口径，不改成另一套小数计算结果。</summary>
public static class LegacyStatisticsMath
{
    /// <summary>浮点合计保持double；数据库空合计保留null，拒绝非有限值及文本溢出／下溢。</summary>
    public static double? DoubleSumResult(object? value)
    {
        if(value is null)return null;
        double result;
        if(value is string text)
        {
            if(!ExactNumericValue.TryDouble(text,out result))throw new InvalidOperationException("浮点统计必须是有限值，不能溢出或下溢为零。");
        }
        else if(value is double or float or decimal or int or long)result=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
        else throw new InvalidOperationException("浮点统计返回了非数值类型。");
        if(!double.IsFinite(result))throw new InvalidOperationException("浮点统计必须是有限值。");
        return result;
    }
    /// <summary>本页浮点合计不转decimal，null按零；精确抵消后只对最终不可表示结果拒绝。</summary>
    public static double SumDouble(IEnumerable<double?> values)
    {
        ArgumentNullException.ThrowIfNull(values);var sum=new BinaryDoubleSum();foreach(var value in values)sum.Add(value);return sum.Value()??0d;
    }
    /// <summary>按float的精确二进制有理数舍入，避免double转decimal时的有效位数损失；零分母由调用方省略键。</summary>
    public static decimal Percentage(long matching,long total)
    {
        if(matching<0||total<=0)throw new ArgumentOutOfRangeException(nameof(total),"统计数量及分母无效。");
        var value=(float)matching/(float)total*100f;
        if(value==0)return 0m;
        var bits=BitConverter.SingleToInt32Bits(value);
        var significand=(bits&0x7fffff)|0x800000;
        var exponent=((bits>>23)&0xff)-150;
        var cents=new BigInteger(significand)*100;
        if(exponent>=0)cents<<=exponent;
        else
        {
            var denominator=BigInteger.One<<-exponent;
            cents=BigInteger.DivRem(cents,denominator,out var remainder);
            if(remainder*2>=denominator)cents++;
        }
        // long输入的float百分比最大约9e20，舍入后的结果在decimal可表示范围内。
        return (decimal)cents/100m;
    }
}
