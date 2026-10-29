using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>静态JS只从已编译节点输出，数字规范化不能改变服务端解释结果。</summary>
public sealed class ReadOnlyConditionScriptTests
{
    [Theory][InlineData("locked","record.locked")][InlineData("entity.locked","record.locked")][InlineData("this.entity.locked","record.locked")]
    [InlineData("00012","12")][InlineData("-000.50","-0.50")][InlineData("١٢.٥","12.5")][InlineData("1.","1.0")][InlineData("null","null")][InlineData("false","false")]
    public void Static_expression_uses_bound_fields_and_legal_decimal_tokens(string source,string expected)
    {Assert.Equal(expected,ReadOnlyConditionRule.Compile(source).ToJavaScript(field=>"record."+field));}
    /// <summary>运算符和括号来自固定语法树；字符串按JSON转义，不能闭合表达式插入脚本。</summary>
    [Fact]
    public void Composite_expression_has_fixed_operators_and_escaped_literals()
    {
        var rule=ReadOnlyConditionRule.Compile("!locked || (entity.status === '已审' && amount < -0.5)");
        Assert.Equal(new[]{"locked","status","amount"},rule.Fields);
        Assert.Equal("((!record.locked) || ((record.status === "+System.Text.Json.JsonSerializer.Serialize("已审")+") && (record.amount < -0.5)))",rule.ToJavaScript(field=>"record."+field));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize("</script>'\n"),ReadOnlyConditionRule.Compile("\"</script>'\\n\"").ToJavaScript(_=>throw new InvalidOperationException()));
    }
}
