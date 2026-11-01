using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>输出必须保留IEEE位型；当前R文本和默认JSON会把已知相邻值合并，不能以近似断言掩盖。</summary>
public sealed class DoubleRoundTripTests
{
    /// <summary>数字和显式数字字符串保持原Token类型，两个相邻有限值必须独立往返。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Adjacent_values_keep_their_bits_and_original_json_token_kind(bool asString)
    {
        var options=new JsonSerializerOptions(JsonDefaults.Options);if(asString)options.NumberHandling|=JsonNumberHandling.WriteAsString;
        var upper=BitConverter.Int64BitsToDouble(0x0410_0000_0000_0000L);var lower=BitConverter.Int64BitsToDouble(0x040f_ffff_ffff_ffffL);var outputs=new List<string>();
        foreach(var value in new[]{upper,lower,-upper,-lower})
        {
            var json=JsonSerializer.Serialize(value,options);using var document=JsonDocument.Parse(json);Assert.Equal(asString?JsonValueKind.String:JsonValueKind.Number,document.RootElement.ValueKind);
            Assert.Equal(BitConverter.DoubleToInt64Bits(value),BitConverter.DoubleToInt64Bits(JsonSerializer.Deserialize<double>(json,options)));outputs.Add(json);
        }
        Assert.Equal(4,outputs.Distinct(StringComparer.Ordinal).Count());
    }
    /// <summary>全二进制指数及固定随机有限位型在不同区域设置下仍逐位往返，包含负零。</summary>
    [Fact]
    public void All_exponents_random_finite_values_and_signed_zero_roundtrip_in_json()
    {
        var values=new List<double>{0d,-0d,double.MaxValue,-double.MaxValue};for(var exponent=-1074;exponent<=1023;exponent++){var value=Math.ScaleB(1d,exponent);values.Add(value);values.Add(-value);}
        var random=new Random(72519);var bytes=new byte[8];for(var index=0;index<4096;index++){random.NextBytes(bytes);var value=BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes));if(double.IsFinite(value))values.Add(value);}
        var previous=CultureInfo.CurrentCulture;
        try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");foreach(var value in values){var json=JsonSerializer.Serialize(value,JsonDefaults.Options);Assert.Equal(BitConverter.DoubleToInt64Bits(value),BitConverter.DoubleToInt64Bits(JsonSerializer.Deserialize<double>(json,JsonDefaults.Options)));}}
        finally{CultureInfo.CurrentCulture=previous;}
    }
    /// <summary>原本能正确往返的短文本保持，避免将0.1改成长小数或改变显式字符串合同。</summary>
    [Fact]
    public void Existing_correct_short_numeric_texts_are_preserved()
    {
        Assert.Equal("0.1",JsonSerializer.Serialize(0.1d,JsonDefaults.Options));Assert.Equal("1.5",JsonSerializer.Serialize(1.5d,JsonDefaults.Options));
        var options=new JsonSerializerOptions(JsonDefaults.Options){NumberHandling=JsonDefaults.Options.NumberHandling|JsonNumberHandling.WriteAsString};Assert.Equal("\"0.1\"",JsonSerializer.Serialize(0.1d,options));
    }
    /// <summary>有限浮点修复不改变字符串ID或精确decimal响应。</summary>
    [Fact]
    public void Mixed_response_keeps_long_id_and_decimal_exact()
    {
        var value=BitConverter.Int64BitsToDouble(0x0410_0000_0000_0000L);var json=JsonSerializer.Serialize(new{value,id=9007199254740993L,amount=9007199254740993.125m},JsonDefaults.Options);using var document=JsonDocument.Parse(json);
        Assert.Equal(BitConverter.DoubleToInt64Bits(value),BitConverter.DoubleToInt64Bits(document.RootElement.GetProperty("value").GetDouble()));Assert.Equal("9007199254740993",document.RootElement.GetProperty("id").GetString());Assert.Equal(9007199254740993.125m,document.RootElement.GetProperty("amount").GetDecimal());
    }
}
