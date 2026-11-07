using System.Globalization;
using System.Numerics;

namespace Metarill.Core.Contracts;

/// <summary>以最小double单位累加整数，最后只舍入一次；抵消不会触发中间浮点溢出。</summary>
public sealed class BinaryDoubleSum
{
    private BigInteger total;private bool hasValues;
    /// <summary>按IEEE二进制位拆分有限输入，null不贡献值，拒绝NaN和无穷。</summary>
    public void Add(double? value)
    {
        if(value is null)return;var number=value.Value;
        if(!double.IsFinite(number))throw new InvalidOperationException("浮点统计必须是有限值。");
        hasValues=true;var bits=BitConverter.DoubleToInt64Bits(number);var storedExponent=(int)((bits>>52)&0x7ff);var mantissa=bits&0x000f_ffff_ffff_ffffL;
        if(storedExponent!=0)mantissa|=1L<<52;
        var units=new BigInteger(mantissa)<<Math.Max(0,storedExponent-1);total+=bits<0?-units:units;
    }
    /// <summary>数据库只传最多2047个指数桶；十进制整数系数不经double或decimal中转。</summary>
    public void AddBucket(int exponent,string coefficient)
    {
        if(exponent is < -1074 or > 971||coefficient.Length is 0 or > 66||!BigInteger.TryParse(coefficient,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var integer))throw new InvalidOperationException("浮点统计分桶系数或指数无效。");
        hasValues=true;total+=integer<<(exponent+1074);
    }
    /// <summary>按最近偶数舍入到double，空集合保留null，最终不可表示才拒绝。</summary>
    public double? Value()
    {
        if(!hasValues)return null;if(total.IsZero)return 0d;
        var magnitude=BigInteger.Abs(total);var shift=checked((int)Math.Max(0,magnitude.GetBitLength()-53));var significand=magnitude>>shift;
        if(shift>0)
        {
            var remainder=magnitude-(significand<<shift);var half=BigInteger.One<<(shift-1);
            if(remainder>half||remainder==half&&!significand.IsEven)significand++;
        }
        var result=Math.ScaleB((double)significand,shift-1074);if(total.Sign<0)result=-result;
        if(!double.IsFinite(result))throw new InvalidOperationException("浮点统计合计超出有限double范围。");return result;
    }
}
