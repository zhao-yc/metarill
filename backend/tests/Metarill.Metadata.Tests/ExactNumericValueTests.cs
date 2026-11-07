using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>共用数值边界与既有精确范围消费；不以单位转换代替真实权限验收。</summary>
public sealed class ExactNumericValueTests
{
    [Theory]
    [InlineData("9007199254740993",9007199254740993L)][InlineData("9223372036854775807",long.MaxValue)][InlineData("-9223372036854775808",long.MinValue)]
    [InlineData("001.2300e2",123L)][InlineData("1000e-3",1L)][InlineData("-0.000",0L)]
    /// <summary>高位ID、指数及边界整数完整保值。</summary>
    public void Integer_literals_are_mathematically_integral_without_float_conversion(string source,long expected)
    {Assert.True(ExactNumericValue.TryInt64(source,out var value));Assert.Equal(expected,value);}
    [Theory]
    [InlineData("1e-100")][InlineData("9223372036854775808")][InlineData("9223372036854775807.0000000000000000000000000000001")][InlineData("1.0000000000000000000000000000001")]
    /// <summary>不可表示的小数或超界数不能匹配整数。</summary>
    public void Fraction_and_range_cannot_be_rounded_into_integer(string source)=>Assert.False(ExactNumericValue.TryInt64(source,out _));
    [Theory]
    [InlineData("001.2300","1.23")][InlineData("1e-28","0.0000000000000000000000000001")][InlineData("79228162514264337593543950335","79228162514264337593543950335")][InlineData("1.00e2","100")]
    /// <summary>精确值不依赖输入尾随零或指数书写方式。</summary>
    public void Exact_decimals_keep_scale_independent_value(string source,string expected)
    {Assert.True(ExactNumericValue.TryDecimal(source,out var value));Assert.Equal(decimal.Parse(expected,CultureInfo.InvariantCulture),value);}
    [Theory]
    [InlineData("1e-100")][InlineData("0.1000000000000000000000000000000001")][InlineData("1.0000000000000000000000000000001")][InlineData("79228162514264337593543950336")]
    /// <summary>超出decimal表示能力时明确拒绝。</summary>
    public void Decimal_overprecision_and_underflow_are_rejected(string source)=>Assert.False(ExactNumericValue.TryDecimal(source,out _));
    [Theory]
    [InlineData("NaN")][InlineData("Infinity")][InlineData("1e999")][InlineData("1e-5000")]
    /// <summary>非有限值和下溢零不进入条件。</summary>
    public void Floating_nonfinite_and_nonzero_underflow_are_rejected(string source)=>Assert.False(ExactNumericValue.TryDouble(source,out _));
    [Fact]
    /// <summary>最小非零double及真正的零保持可用，输入长度有界。</summary>
    public void Floating_minimum_and_zero_remain_representable()
    {Assert.True(ExactNumericValue.TryDouble("5e-324",out var minimum));Assert.Equal(double.Epsilon,minimum);Assert.True(ExactNumericValue.TryDouble("-0.00",out var zero));Assert.Equal(0d,zero);Assert.False(ExactNumericValue.TryDecimal("1"+new string('0',600),out _));}
    [Theory]
    [InlineData("decimal","10.0000000000000000000000000000001")][InlineData("decimal","1e-100")][InlineData("double","1e-5000")]
    /// <summary>既有范围消费者必须同样拒绝错误匹配。</summary>
    public void Existing_exact_scope_rejects_rounding_instead_of_granting_other_values(string type,string source)
    {var scope=new ExactDataManageValues(true,JsonSerializer.SerializeToElement(source));Assert.Throws<ArgumentException>(()=>scope.ReadLogical(type));}
    [Fact]
    /// <summary>合法范围数组保持原接口及精确值。</summary>
    public void Existing_exact_scope_keeps_valid_decimal_arrays()
    {var scope=new ExactDataManageValues(true,JsonSerializer.SerializeToElement("[\"1.2300\",\"1.23e1\"]"));Assert.Equal(new[]{1.23m,12.3m},scope.Read<decimal>());}
}
