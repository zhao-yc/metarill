using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>执行真正的基础设施参数归一，不用驱动包装掩盖入库之前的位型损失。</summary>
public sealed class InfrastructureDoublePrecisionTests
{
    /// <summary>typed double必须直接保留原位型，覆盖全部指数、相邻值及固定随机有限输入。</summary>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("fr-FR")]
    public void Typed_double_values_keep_their_bits_before_database_binding(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var values = new List<double> { 0d, -0d, double.MaxValue, -double.MaxValue, BitConverter.UInt64BitsToDouble(0x0410000000000000), BitConverter.UInt64BitsToDouble(0x040fffffffffffff) };
            for (var exponent = -1074; exponent <= 1023; exponent++) { var value = Math.ScaleB(1d, exponent); values.Add(value); values.Add(-value); }
            var random = new Random(20261103); var bytes = new byte[8];
            for (var index = 0; index < 4096; index++) { random.NextBytes(bytes); var value = BitConverter.ToDouble(bytes); if (double.IsFinite(value)) values.Add(value); }
            foreach (var value in values) Assert.Equal(BitConverter.DoubleToUInt64Bits(value), BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(Coerce(value))));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>boxed float赋给逻辑double应作数值提升，不先缩成Single的短文本再回读。</summary>
    [Fact]
    public void Typed_single_values_are_widened_without_a_decimal_text_bridge()
    {
        var values = new List<float> { 0f, -0f, 0.1f, float.MaxValue, -float.MaxValue, float.Epsilon };
        for (var exponent = -149; exponent <= 127; exponent++) { var value = MathF.ScaleB(1f, exponent); values.Add(value); values.Add(-value); }
        foreach (var value in values) Assert.Equal(BitConverter.DoubleToUInt64Bits((double)value), BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(Coerce(value))));
    }

    /// <summary>保持原文本／JSON／空选择及有限数门禁，不以二进制快捷分支绕过非法值。</summary>
    [Fact]
    public void Text_json_null_and_invalid_inputs_keep_the_original_contract()
    {
        Assert.Null(Coerce(null)); Assert.Null(Coerce("")); Assert.Null(Coerce(JsonSerializer.SerializeToElement<object?>(null)));
        foreach (var input in new object[] { "4.1045368012983762E-289", JsonSerializer.Deserialize<JsonElement>("4.1045368012983762E-289"), JsonSerializer.SerializeToElement("4.1045368012983762E-289") })
            Assert.Equal(0x0410000000000000UL, BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(Coerce(input))));
        foreach (var input in new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, float.NaN, float.PositiveInfinity, float.NegativeInfinity, "1e309", "1e-999", "1,5", "NaN" })
        {
            var error = Assert.IsType<FormatException>(Assert.Throws<TargetInvocationException>(() => Coerce(input)).InnerException);
            Assert.Equal("浮点数必须是有效有限值，不能溢出或下溢为零。", error.Message);
        }
        Assert.Equal((double)123456789012345678.123456789m, Assert.IsType<double>(Coerce(123456789012345678.123456789m)));
        Assert.Equal(0.1d, Assert.IsType<double>(Coerce("0.1")));
    }

    /// <summary>复用发布元数据和实际归一入口，不另写近似解析算法。</summary>
    private static object? Coerce(object? value) => typeof(SqlRowStore).GetMethod("Coerce", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
        [value, new ColumnMetadata { Id = "measure", ConfigTableId = "table", ColumnName = "measure", LogicalType = "double" }]);
}