using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>固定条件语法的离线验证；未代表候选、写入、文件或真实数据库已经接线。</summary>
public sealed class FixedConditionPlanTests
{
    private static readonly string[] Fields=["id","name","status","amount","delete_no","yz_project_id"];
    /// <summary>沿原V8条件集合，数值不经double，身份和表单变量保留各自独立名称。</summary>
    [Fact]
    public void Mixed_original_candidate_conditions_compile_without_losing_values_or_symbols()
    {
        var plan=FixedConditionPlan.Compile("name LIKE @formParameters.allowed AND status=1 AND id IN(9007199254740993,@parent_id) AND delete_no IS NULL AND yz_project_id=@current_yz_project_id",Fields);
        Assert.Equal(5,plan.Terms.Count);Assert.Equal(new[]{"formParameters.allowed","parent_id","current_yz_project_id"},plan.Variables);
        Assert.Equal("9007199254740993",plan.Terms[2].Values[0].Text);Assert.Equal(FixedConditionValueKind.Number,plan.Terms[2].Values[0].Kind);Assert.Equal(FixedConditionComparison.IsNull,plan.Terms[3].Comparison);
    }
    /// <summary>字符串中的AND、逗号、SQL标点及双单引号只属于字面量，不能制造额外谓词。</summary>
    [Fact]
    public void Quotes_groups_and_keyword_case_keep_the_original_literal_boundaries()
    {
        var plan=FixedConditionPlan.Compile("((`NAME`='Rock AND Roll, x; -- ''quoted''') aNd status IN(1,+2,-3,4.50,6e2)) AND name='C:\\data'",Fields);
        Assert.Equal(3,plan.Terms.Count);Assert.Equal("name",plan.Terms[0].Field);Assert.Equal("Rock AND Roll, x; -- 'quoted'",plan.Terms[0].Values[0].Text);Assert.Equal("C:\\data",plan.Terms[2].Values[0].Text);
        Assert.Equal(new[]{"1","+2","-3","4.50","6e2"},plan.Terms[1].Values.Select(value=>value.Text));
    }
    /// <summary>空值比较与IS NULL分开保留，不能在绑定时把原SQL的未知值条件改为匹配空记录。</summary>
    [Fact]
    public void Null_boolean_and_comparisons_keep_explicit_operator_identity()
    {
        var plan=FixedConditionPlan.Compile("name = NULL AND name <> NULL AND status != FALSE AND amount>=1.25 AND amount<9 AND delete_no IS NOT NULL AND status<=TRUE AND id>0",Fields);
        Assert.Equal(new[]{FixedConditionComparison.Equal,FixedConditionComparison.NotEqual,FixedConditionComparison.NotEqual,FixedConditionComparison.GreaterOrEqual,FixedConditionComparison.Less,FixedConditionComparison.IsNotNull,FixedConditionComparison.LessOrEqual,FixedConditionComparison.Greater},plan.Terms.Select(term=>term.Comparison));
        Assert.Equal(FixedConditionValueKind.Null,plan.Terms[0].Values[0].Kind);Assert.Equal(FixedConditionValueKind.Boolean,plan.Terms[2].Values[0].Kind);Assert.Empty(plan.Terms[5].Values);
    }
    /// <summary>变量按完整词读取，空白不混入名称，嵌套表单路径仍为一个发布符号。</summary>
    [Fact]
    public void Variables_are_exact_symbols_and_plan_snapshots_are_immutable()
    {
        var fields=Fields.ToList();var plan=FixedConditionPlan.Compile("id=@parentId AND name=@formParameters.copyTable.fromYzProjectId AND id=@parentId",fields);fields.Clear();
        Assert.Equal(new[]{"parentId","formParameters.copyTable.fromYzProjectId"},plan.Variables);Assert.Equal(new[]{"id","name"},plan.Fields);
        Assert.Throws<NotSupportedException>(()=>((IList<FixedConditionTerm>)plan.Terms).Clear());Assert.Throws<NotSupportedException>(()=>((IList<FixedConditionValue>)plan.Terms[0].Values).Clear());
        Assert.DoesNotContain("private-literal",JsonSerializer.Serialize(FixedConditionPlan.Compile("name='private-literal'",Fields)));
    }
    /// <summary>未知语句、表达式、注释、变量路径及截断输入不能通过忽略剩余文本变成成功计划。</summary>
    [Theory]
    [InlineData("name='x'; DELETE FROM t")][InlineData("name='x' OR id=1")][InlineData("id=1--x")][InlineData("id=1 /* x */")]
    [InlineData("id=1 UNION SELECT 1")][InlineData("id=(SELECT 1)")][InlineData("id=coalesce(@parent_id,id)")][InlineData("table.id=1")]
    [InlineData("unknown=1")][InlineData("id IN()")][InlineData("id IN(1,)")][InlineData("id IN(1")][InlineData("name='unterminated")]
    [InlineData("id=@formParameters.")][InlineData("id=@.invalid")][InlineData("id=@formParameters .id")][InlineData("id=@formParameters.[id]")]
    [InlineData("id=")][InlineData("id=1 AND")][InlineData("(id=1")][InlineData("id=1)")][InlineData("()")]
    [InlineData("id=1AND name='x'")][InlineData("name='x' ANDROID=1")][InlineData("id< =1")][InlineData("id! =1")][InlineData("`id `=1")]
    [InlineData("id=1e")][InlineData("id=1.")][InlineData("id=0x10")][InlineData("name=\"x\"")][InlineData("status BETWEEN 1 AND 2")]
    public void Unproven_syntax_fails_before_any_query_is_emitted(string source)=>Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile(source,Fields));
    /// <summary>工作量有界，配置内容和变量值不进入错误消息。</summary>
    [Fact]
    public void Limits_and_error_messages_do_not_echo_literal_contents()
    {
        Assert.Empty(FixedConditionPlan.Compile(null,Fields).Terms);Assert.Empty(FixedConditionPlan.Compile("  ",Fields).Terms);
        Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile(new string(' ',32769),Fields));
        Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile(string.Join(" AND ",Enumerable.Repeat("id=1",129)),Fields));
        Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile("id IN("+string.Join(',',Enumerable.Repeat("1",1025))+")",Fields));
        Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile(new string('(',18)+"id=1"+new string(')',18),Fields));
        Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile("",["id","ID"]));
        var error=Assert.Throws<ArgumentException>(()=>FixedConditionPlan.Compile("name='private-value' OR id=1",Fields));Assert.DoesNotContain("private-value",error.Message);
    }
}
