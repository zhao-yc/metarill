using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>共享原Java对照向量，覆盖所有原运算符、分组、精度和空值，不使用前端浮点比较冒充后端。</summary>
public sealed class ButtonVisibilityRuleTests
{
    public static IEnumerable<object[]> Vectors()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(VectorsPath()));
        foreach(var value in json.RootElement.EnumerateArray())yield return new object[]{value.GetProperty("name").GetString()!,value.GetProperty("rule").GetRawText(),value.GetProperty("row").GetRawText(),value.GetProperty("expected").GetBoolean()};
    }
    [Theory][MemberData(nameof(Vectors))]
    public void Original_backend_vectors_match(string name,string definition,string row,bool expected)
    {
        _=name;using var record=JsonDocument.Parse(row);var values=record.RootElement.EnumerateObject().ToDictionary(value=>value.Name,value=>Value(value.Value));
        Assert.Equal(expected,ButtonVisibilityRule.Compile(definition).Matches(values));
    }
    [Theory][InlineData("")][InlineData(" ")][InlineData("null")][InlineData("{}")]
    public void Empty_published_rules_remain_true(string definition)=>Assert.True(ButtonVisibilityRule.Compile(definition).Matches(new Dictionary<string,object?>()));
    [Theory][InlineData("[")][InlineData("[]")][InlineData("true")][InlineData("{\"conditions\":{}}")][InlineData("{\"groups\":{}}")]
    [InlineData("{\"conditions\":[{\"operator\":\"eval\"}]}")][InlineData("{\"logic\":\"OR\",\"groups\":[{}, {\"conditions\":[{\"operator\":\"eval\"}]}]}")]
    [InlineData("{\"conditions\":[{\"operator\":\"eq\",\"operator\":\"ne\"}]}")][InlineData("{\"conditions\":[],\"conditions\":[]}")]
    public void Invalid_or_ambiguous_rules_never_become_empty_allow_rules(string definition)=>Assert.Throws<ArgumentException>(()=>ButtonVisibilityRule.Compile(definition));
    /// <summary>Java不把NBSP当空白；未知操作不能被.NET的空白判断改写为默认eq。</summary>
    [Fact]
    public void Nonbreaking_operator_is_invalid_instead_of_default_equality()
        =>Assert.Throws<ArgumentException>(()=>ButtonVisibilityRule.Compile("{\"conditions\":[{\"operator\":\"\u00a0\"}]}"));
    [Fact]
    public void Compiled_plan_copies_definition_and_fields_are_readonly()
    {
        var rule=ButtonVisibilityRule.Compile("{\"conditions\":[{\"field\":\"name\",\"value\":\"允许\"}]}");
        Assert.Throws<NotSupportedException>(()=>((IList<string>)rule.Fields).Add("other"));Assert.Equal(new[]{"name"},rule.Fields);
        Assert.True(rule.Matches(new Dictionary<string,object?>{{"name","允许"}}));Assert.False(rule.Matches(new Dictionary<string,object?>{{"name","拒绝"}}));
    }
    [Fact]
    public void Definitions_have_bounded_size_depth_and_node_count()
    {
        Assert.Throws<ArgumentException>(()=>ButtonVisibilityRule.Compile(new string(' ',65537)+"{}"));
        var nested="{}";for(var index=0;index<18;index++)nested="{\"groups\":["+nested+"]}";
        Assert.Throws<ArgumentException>(()=>ButtonVisibilityRule.Compile(nested));
        Assert.Throws<ArgumentException>(()=>ButtonVisibilityRule.Compile("{\"conditions\":["+string.Join(',',Enumerable.Repeat("{}",1025))+"]}"));
    }
    /// <summary>字段目录之外只提供空值，字符串不当作JSON重新解析，日期无区域或时区依赖。</summary>
    [Fact]
    public void Nested_strings_are_not_objects_and_timestamp_keeps_original_Jdbc_shape()
    {
        var missing=ButtonVisibilityRule.Compile("{\"conditions\":[{\"field\":\"value.name\",\"operator\":\"isBlank\"}]}");
        Assert.True(missing.Matches(new Dictionary<string,object?>{{"value","{\"name\":\"伪造\"}"}}));
        var timestamp=ButtonVisibilityRule.Compile("{\"conditions\":[{\"field\":\"value\",\"value\":\"2026-10-29 10:20:30.1234\"}]}");
        Assert.True(timestamp.Matches(new Dictionary<string,object?>{{"value",new DateTime(2026,10,29,10,20,30).AddTicks(1234000)}}));
    }
    /// <summary>还原共享样本的JDBC类型标签，生产规则编译器不解析这些测试标签。</summary>
    private static object? Value(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            if(value.TryGetProperty("$jdbcTimestamp",out var timestamp))return DateTime.Parse(timestamp.GetString()!,System.Globalization.CultureInfo.InvariantCulture);
            if(value.TryGetProperty("$jdbcDate",out var date))return DateOnly.Parse(date.GetString()!,System.Globalization.CultureInfo.InvariantCulture);
            if(value.TryGetProperty("$jdbcTime",out var time))return TimeOnly.Parse(time.GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        }
        return value.Clone();
    }
    private static string VectorsPath([CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../../tools/acceptance/button-visibility/vectors.json"));
}
