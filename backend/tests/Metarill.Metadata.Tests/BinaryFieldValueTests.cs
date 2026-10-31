using System.Text.Json;
using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Metadata.Tests;

/// <summary>显式binary沿实际JSON字节合同，不把整数数组或空字符串偷偷改为其它类型。</summary>
public sealed class BinaryFieldValueTests
{
    /// <summary>公开解析与System.Text.Json真正byte[]解码一致，包含零字节、Unicode文本和空数组。</summary>
    [Theory][InlineData("")][InlineData("AA==")][InlineData("AP8Afw==")][InlineData("5Lit5paH")][InlineData("AQIDBA==")]
    public void Base64_matches_builtin_and_preserves_empty(string text)
    {
        var json=JsonSerializer.SerializeToElement(text);var expected=JsonSerializer.Deserialize<byte[]>(json.GetRawText());
        Assert.Equal(expected,BinaryFieldValue.Parse(json));Assert.Equal(expected,BinaryFieldValue.ParseText(text));
        Assert.Equal(expected,BinaryFieldValue.Coerce(json));Assert.Equal(expected,BinaryFieldValue.Coerce(text));
        Assert.Equal(text,Convert.ToBase64String(BinaryFieldValue.Parse(json)!));
        if(text.Length==0){Assert.NotNull(BinaryFieldValue.Parse(json));Assert.Empty(BinaryFieldValue.Parse(json)!);}
    }
    /// <summary>错误Base64和非字符串类型均拒绝，不能把字节数组当Json数值列表。</summary>
    [Theory][InlineData("\"不是编码\"")][InlineData("\"%%%\"")][InlineData("\"A\"")][InlineData("[0,255]")][InlineData("{}")][InlineData("123")][InlineData("true")]
    public void Invalid_text_and_json_shapes_are_rejected(string raw)=>Assert.Throws<JsonException>(()=>BinaryFieldValue.Parse(JsonSerializer.Deserialize<JsonElement>(raw)));
    /// <summary>null、空、实际字节分别保留；长度按字节计算，不能按Base64文本或数据库截断处理。</summary>
    /// <summary>元数据默认与请求同解码，留空框保留无默认语义，结果每次独立分配。</summary>
    [Fact]
    public void Default_bytes_validate_length_and_do_not_share_mutable_arrays()
    {
        Assert.Null(BinaryFieldValue.DefaultBytes("","4"));
        var first=BinaryFieldValue.DefaultBytes("AP8=","4")!;var next=BinaryFieldValue.DefaultBytes("AP8=","4")!;
        Assert.Equal(new byte[]{0,255},first);Assert.NotSame(first,next);first[0]=99;Assert.Equal((byte)0,next[0]);
        foreach(var invalid in new[]{"不是Base64","AQIDBAU=","UNHEX('00FF')"," ","[0,255]"})Assert.Throws<InvalidOperationException>(()=>BinaryFieldValue.DefaultBytes(invalid,"4"));
    }
    [Fact]
    public void Null_and_byte_length_contracts_remain_distinct()
    {
        Assert.Null(BinaryFieldValue.Parse(JsonSerializer.Deserialize<JsonElement>("null")));Assert.Null(BinaryFieldValue.ParseText(null));Assert.Null(BinaryFieldValue.Coerce(null));
        var bytes=new byte[]{0,255,0,127};Assert.Same(bytes,BinaryFieldValue.Coerce(bytes));
        BinaryFieldValue.Validate(bytes,4);BinaryFieldValue.Validate([],1);BinaryFieldValue.Validate(null,1);
        Assert.Throws<ArgumentException>(()=>BinaryFieldValue.Validate(bytes,3));Assert.Throws<ArgumentOutOfRangeException>(()=>BinaryFieldValue.Validate(bytes,0));
        Assert.Null(BinaryFieldValue.MaximumLength(""));Assert.Equal(255,BinaryFieldValue.MaximumLength(" "));Assert.Equal(4,BinaryFieldValue.MaximumLength("4"));Assert.Throws<InvalidOperationException>(()=>BinaryFieldValue.MaximumLength("max"));
    }
    /// <summary>请求schema与实际序列化一致，普通数组仍保留原数组定义；Optional仍表示可显式null。</summary>
    [Fact]
    public void Schema_reports_Base64_string_and_Optional_nullability()
    {
        var direct=RequestJsonSchema.Create(typeof(byte[]));Assert.Equal("string",direct["type"]);Assert.Equal("byte",direct["format"]);Assert.Equal("base64",direct["contentEncoding"]);Assert.False(direct.ContainsKey("items"));
        var optional=RequestJsonSchema.Create(typeof(Optional<byte[]?>));Assert.Contains("null",Assert.IsType<string[]>(optional["type"]));Assert.Equal("base64",optional["contentEncoding"]);
        Assert.Equal("array",RequestJsonSchema.Create(typeof(int[]))["type"]);
    }
}
