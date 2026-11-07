using System.Numerics;
using Metarill.Core.Contracts;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>覆盖IEEE全指数范围、合法抵消及最近偶数舍入，避免原生SUM或中间溢出吞掉值。</summary>
public sealed class BinaryDoubleSumTests
{
    /// <summary>每个double指数及固定随机位型单值都能精确往返，不经decimal。</summary>
    [Fact]
    public void Every_binary_exponent_and_finite_bit_pattern_round_trips()
    {
        for(var exponent=-1074;exponent<=1023;exponent++)
        {
            var value=Math.ScaleB(1d,exponent);Assert.Equal(value,LegacyStatisticsMath.SumDouble([value]));
            var sum=new BinaryDoubleSum();var bucket=Math.Min(exponent,971);sum.AddBucket(bucket,(BigInteger.One<<(exponent-bucket)).ToString(System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(value,sum.Value());
        }
        var random=new Random(53417);var bytes=new byte[8];for(var index=0;index<4096;index++){random.NextBytes(bytes);var value=BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes));if(double.IsFinite(value))Assert.Equal(value,LegacyStatisticsMath.SumDouble([value]));}
    }
    /// <summary>最终有限的抵消按任意顺序均保留，不能用保守拒绝代替正确求和。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Large_cancellation_preserves_the_true_final_value_in_every_order(bool tiny)
    {
        double[] values=tiny?[double.MaxValue,-double.MaxValue,double.Epsilon]:[double.MaxValue,double.MaxValue,-double.MaxValue];
        foreach(var permutation in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})Assert.Equal(tiny?double.Epsilon:double.MaxValue,LegacyStatisticsMath.SumDouble(permutation.Select(index=>(double?)values[index])));
    }
    /// <summary>最近偶数舍入、负数、非正规值及空集合保留明确语义。</summary>
    [Fact]
    public void Rounding_null_zero_and_subnormal_boundaries_are_explicit()
    {
        var half=Math.ScaleB(1d,-53);Assert.Equal(1d,LegacyStatisticsMath.SumDouble([1d,half]));Assert.Equal(Math.BitIncrement(Math.BitIncrement(1d)),LegacyStatisticsMath.SumDouble([Math.BitIncrement(1d),half]));
        Assert.Equal(-1d,LegacyStatisticsMath.SumDouble([-1d,-half]));Assert.Equal(Math.BitDecrement(Math.BitDecrement(-1d)),LegacyStatisticsMath.SumDouble([Math.BitDecrement(-1d),-half]));
        Assert.Equal(double.Epsilon,LegacyStatisticsMath.SumDouble([double.Epsilon,double.Epsilon,-double.Epsilon]));Assert.Equal(0d,LegacyStatisticsMath.SumDouble([]));Assert.Null(new BinaryDoubleSum().Value());
        Assert.Equal(double.MaxValue,LegacyStatisticsMath.SumDouble([double.MaxValue,Math.ScaleB(1d,969)]));Assert.Throws<InvalidOperationException>(()=>LegacyStatisticsMath.SumDouble([double.MaxValue,Math.ScaleB(1d,970)]));
    }
    /// <summary>真正溢出、非有限输入及非法桶不进入结果。</summary>
    [Theory][InlineData(-1075,"1")][InlineData(972,"1")][InlineData(0,"NaN")][InlineData(0,"1.5")][InlineData(0,"")]
    public void Invalid_buckets_are_rejected(int exponent,string coefficient)=>Assert.Throws<InvalidOperationException>(()=>new BinaryDoubleSum().AddBucket(exponent,coefficient));
}
