using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Metadata.Tests;

/// <summary>同一数值边界覆盖平台HTTP、完整宿主、可选更新及分片JSON；不连接数据库。</summary>
public sealed class FiniteDoubleJsonTests
{
    /// <summary>有限double不经decimal，字符串数字沿原选项兼容。</summary>
    [Theory]
    [InlineData("1.5",1.5)][InlineData("-12.5",-12.5)][InlineData("1.25e100",1.25e100)][InlineData("5e-324",double.Epsilon)]
    public void Finite_numbers_and_strings_keep_double_range(string text,double expected)
    {
        foreach(var body in new[]{text,JsonSerializer.Serialize(text)})Assert.Equal(expected,JsonSerializer.Deserialize<double>(body,JsonDefaults.Options));
        var json=JsonSerializer.Serialize(expected,JsonDefaults.Options);Assert.Equal(expected,JsonSerializer.Deserialize<double>(json,JsonDefaults.Options));
        var copied=new JsonSerializerOptions(JsonSerializerDefaults.Web);JsonDefaults.CopyTo(copied);Assert.Equal(expected,JsonSerializer.Deserialize<double>(text,copied));
    }
    /// <summary>溢出、非零下溢、非数值和NaN不能在调用业务前变为无穷大或零。</summary>
    [Theory]
    [InlineData("1e309")][InlineData("-1e309")][InlineData("1e-999")][InlineData("\"NaN\"")][InlineData("\"Infinity\"")][InlineData("\"1,5\"")][InlineData("true")][InlineData("[]")][InlineData("{}")]
    public void Invalid_numeric_inputs_are_rejected(string json)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<double>(json,JsonDefaults.Options));
    /// <summary>手写业务输出也不能把非有限值放入HTTP响应。</summary>
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)]
    public void Nonfinite_output_is_rejected(double value)=>Assert.Throws<JsonException>(()=>JsonSerializer.Serialize(value,JsonDefaults.Options));
    /// <summary>字段省略与主动清空仍由原Optional协议处理，整型ID仍为字符串。</summary>
    [Fact]
    public void Optional_presence_null_and_blank_keep_existing_contract()
    {
        Assert.False(JsonSerializer.Deserialize<Body>("{}",JsonDefaults.Options)!.Value.IsSpecified);
        foreach(var json in new[]{"{\"value\":null}","{\"value\":\"\"}","{\"value\":\"  \"}"}){var value=JsonSerializer.Deserialize<Body>(json,JsonDefaults.Options)!.Value;Assert.True(value.IsSpecified);Assert.Null(value.Value);}
        var positive=JsonSerializer.Deserialize<Body>("{\"value\":\"1.5e100\"}",JsonDefaults.Options)!.Value;Assert.True(positive.IsSpecified);Assert.Equal(1.5e100,positive.Value);
        Assert.Equal("\"9007199254740993\"",JsonSerializer.Serialize(9007199254740993L,JsonDefaults.Options));
    }
    /// <summary>尊重独立调用者的数字字符串选项，不擅自改变输出协议。</summary>
    [Fact]
    public void Explicit_number_handling_options_are_preserved()
    {
        var strict=new JsonSerializerOptions();strict.Converters.Add(new FiniteDoubleJsonConverter());Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<double>("\"1.5\"",strict));
        strict=new JsonSerializerOptions{NumberHandling=JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString};strict.Converters.Add(new FiniteDoubleJsonConverter());Assert.Equal("\"1.5\"",JsonSerializer.Serialize(1.5,strict));Assert.Equal(1.5,JsonSerializer.Deserialize<double>("\"1.5\"",strict));
    }
    /// <summary>跨网络缓冲片段的数字仍走同一有界解析，不能只覆盖连续内存。</summary>
    [Fact]
    public void Segmented_numeric_token_is_supported_and_oversize_is_rejected()
    {
        Assert.Equal(1.25e100,Read("1.25e","100"));Assert.Throws<JsonException>(()=>Read("1"+new string('0',510),"00"));
    }
    private sealed class Body{public Optional<double?> Value{get;set;}}
    /// <summary>构造两段真实Utf8JsonReader输入，不复制解析器实现。</summary>
    private static double Read(string left,string right)
    {
        var first=new Segment(Encoding.UTF8.GetBytes(left));var last=first.Append(Encoding.UTF8.GetBytes(right));var sequence=new ReadOnlySequence<byte>(first,0,last,last.Memory.Length);var reader=new Utf8JsonReader(sequence,true,default);Assert.True(reader.Read());Assert.True(reader.HasValueSequence);return new FiniteDoubleJsonConverter().Read(ref reader,typeof(double),JsonDefaults.Options);
    }
    private sealed class Segment:ReadOnlySequenceSegment<byte>
    {
        internal Segment(byte[] memory)=>Memory=memory;
        internal Segment Append(byte[] memory){var next=new Segment(memory){RunningIndex=RunningIndex+Memory.Length};Next=next;return next;}
    }
}
