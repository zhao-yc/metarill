using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>同一公开向量由原Java方法与C#解释器分别执行，禁止用测试生成预期结果。</summary>
public sealed class ReadOnlyConditionRuleTests
{
    public static IEnumerable<object[]> Vectors()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(VectorsPath()));
        foreach(var value in json.RootElement.EnumerateArray())yield return [value.GetProperty("name").GetString()!,value.GetProperty("condition").GetString()!,value.GetProperty("row").GetRawText(),value.GetProperty("outcome").GetString()!];
    }
    [Theory][MemberData(nameof(Vectors))]
    public void Original_restricted_condition_vectors_match(string name,string condition,string row,string expected)
    {
        _=name;using var json=JsonDocument.Parse(row);var record=json.RootElement.EnumerateObject().ToDictionary(value=>value.Name,value=>(object?)value.Value.Clone());
        var actual="invalid";try{actual=ReadOnlyConditionRule.Compile(condition).IsReadOnly(record)?"readonly":"writable";}catch(ArgumentException){}
        Assert.Equal(expected,actual);
    }
    /// <summary>发布快照不可变，真值按当前原记录变化，字段列表不可被调用方修改。</summary>
    [Fact]
    public void Compiled_condition_is_immutable_and_uses_each_current_record()
    {
        var rule=ReadOnlyConditionRule.Compile("this.entity.flag && amount < 0");Assert.Equal(new[]{"flag","amount"},rule.Fields);Assert.Throws<NotSupportedException>(()=>((IList<string>)rule.Fields).Add("other"));
        Assert.True(rule.IsReadOnly(new Dictionary<string,object?>{{"flag",true},{"amount",-1m}}));Assert.False(rule.IsReadOnly(new Dictionary<string,object?>{{"flag",true},{"amount",1m}}));
        var error=Assert.Throws<ArgumentException>(()=>ReadOnlyConditionRule.Compile("amount < true").IsReadOnly(new Dictionary<string,object?>{{"amount","不得泄露的实际值"}}));Assert.DoesNotContain("不得泄露",error.Message);
    }
    private static string VectorsPath([CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../../tools/acceptance/read-only-condition/vectors.json"));
}
