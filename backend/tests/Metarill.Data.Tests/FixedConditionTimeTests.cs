using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Data.Tests;

public sealed partial class FixedConditionSqlTests
{
    /// <summary>真实ORM固定条件支持微秒与总小时，值始终为TimeSpan参数，不能被插入SQL。</summary>
    [Theory][InlineData("12:34:56.123456")][InlineData("-30:00:00")][InlineData("838:59:59")]
    public void Time_fixed_conditions_are_typed_and_parameterized(string text)
    {
        var expected=SqlTimeValue.ParseText(text);
        Assert.Equal(expected,Metarill.Data.Queries.FixedConditionSqlPlan.BindScalar(text,"time"));
        using var session=Session();
        var sql=Compile("duration=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",text}},"time_contract").ToSql();
        Assert.Contains("@time_contract_0",sql);Assert.DoesNotContain(text,sql);
    }
    /// <summary>日数、偏移和超出MySQL范围在构造SQL前明确拒绝，不交给服务器截断。</summary>
    [Theory][InlineData("1.06:00:00")][InlineData("12:34:56Z")][InlineData("839:00:00")][InlineData("838:59:59.000001")]
    public void Invalid_time_fixed_conditions_fail_before_orm(string text)
    {
        using var session=Session();Assert.ThrowsAny<ArgumentException>(()=>Compile("duration=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",text}},"time_contract"));
    }
}
