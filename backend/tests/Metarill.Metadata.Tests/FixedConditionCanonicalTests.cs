using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>规范化只保留已解析节点语义，生成器与文件模块不再各自按文本拼装条件。</summary>
public sealed class FixedConditionCanonicalTests
{
    /// <summary>比较、分组、原LIKE、字符串标点、空值、变量与精确数字都可无损重编译。</summary>
    [Theory]
    [InlineData("(name='A AND B,O''Neil' AND id>=9007199254740993) AND status IN(1,2,NULL)")]
    [InlineData("name LIKE 'A_%' AND name<>NULL AND name IS NOT NULL AND status IS NULL")]
    [InlineData("id=@current_yz_project_id AND status=@formParameters.order.status")]
    [InlineData("status=true AND id<-1e1 AND id>-9007199254740993")]
    public void Parsed_nodes_keep_the_same_semantics_after_canonical_roundtrip(string definition)
    {
        var fields=new[]{"name","id","status"};var original=FixedConditionPlan.Compile(definition,fields);
        var canonical=string.Join(" AND ",original.Terms.Select(term=>term.ToDefinition()));var reparsed=FixedConditionPlan.Compile(canonical,fields);
        Assert.Equal(original.Terms.Count,reparsed.Terms.Count);
        foreach(var pair in original.Terms.Zip(reparsed.Terms))
        {Assert.Equal(pair.First.Field,pair.Second.Field);Assert.Equal(pair.First.Comparison,pair.Second.Comparison);Assert.Equal(pair.First.Values.Select(v=>(v.Kind,v.Text)),pair.Second.Values.Select(v=>(v.Kind,v.Text)));}
        Assert.Equal(canonical,string.Join(" AND ",reparsed.Terms.Select(term=>term.ToDefinition())));
    }
}
