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
    /// <summary>实际联动时间进入 MySQL 参数，空值保留 SQL 三值逻辑，方言继续检查范围。</summary>
    [Theory]
    [InlineData(true, "2026-11-24T12:34:56.123456", "12:34:56.123456")]
    [InlineData(false, "-30:01:02.123456", "-30:01:02.123456")]
    public void Time_form_values_reach_the_mysql_plan_as_typed_parameters(bool clock, string input, string canonical)
    {
        var plan = Compile("duration >= @formParameters.allowed");
        var provider = new Metarill.Data.MySqlDatabaseProvider();
        var values = new Dictionary<string, object?>
        {
            ["formParameters.allowed"] = Metarill.Data.Queries.FixedConditionSqlPlan.BindTimeFormValue(
                System.Text.Json.JsonSerializer.SerializeToElement(input), clock)
        };
        var clause = plan.Render(provider, values, "time_form", provider.QuoteIdentifier);
        Assert.Equal("(`duration`>=@time_form_0)", clause.Sql);
        Assert.Equal(SqlTimeValue.ParseText(canonical), Assert.IsType<TimeSpan>(clause.Parameters["time_form_0"]));
        values["formParameters.allowed"] = Metarill.Data.Queries.FixedConditionSqlPlan.BindTimeFormValue(
            System.Text.Json.JsonSerializer.SerializeToElement(""), clock);
        clause = plan.Render(provider, values, "time_form", provider.QuoteIdentifier);
        Assert.Equal("(`duration`>=NULL)", clause.Sql);
        Assert.Empty(clause.Parameters);

        values["formParameters.allowed"] = Metarill.Data.Queries.FixedConditionSqlPlan.BindTimeFormValue(
            System.Text.Json.JsonSerializer.SerializeToElement("839:00:00"), false);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Render(provider, values, "time_form", provider.QuoteIdentifier));
    }

    /// <summary>物理和联接精确范围经原 FreeSql 入口绑定时间参数，驱动范围仍在命令前校验。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exact_time_is_parameterized_in_both_mysql_query_shapes(bool derived)
    {
        const string text="-30:01:02.123456";
        var value=SqlTimeValue.ParseText(text)!.Value;
        using var session=Session();
        var catalog=NonRedundantReadTests.Catalog("mt_exact_time","mt_exact_time_target");
        catalog.ConfigColumns.Single(column=>column.Id=="18").LogicalType="time";
        catalog.ConfigColumns.Single(column=>column.Id=="22").LogicalType="time";
        string Apply(params object?[] values)
        {
            if(!derived)return Metarill.Data.Queries.ExactDataManageSql.Apply(
                session.Select<Row>(),session,"duration","exact_time",values).ToSql();
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileMatch(catalog,"1","grand_label");
            return Metarill.Data.Queries.ExactDataManageSql.ApplyDerived(session.Select<Row>(),session,plan,
                "grand_label","exact_time",values,catalog.YzProjectId,locking:false).ToSql();
        }
        var sql=Apply(value);
        Assert.Contains(derived?"@exact_time_value_0":"@exact_time_0",sql);
        Assert.DoesNotContain(text,sql);
        Assert.Throws<ArgumentOutOfRangeException>(()=>Apply(TimeSpan.FromHours(839)));
        Assert.Throws<ArgumentException>(()=>Apply(value+TimeSpan.FromTicks(1)));
    }

    /// <summary>日数、偏移和超出MySQL范围在构造SQL前明确拒绝，不交给服务器截断。</summary>
    [Theory][InlineData("1.06:00:00")][InlineData("12:34:56Z")][InlineData("839:00:00")][InlineData("838:59:59.000001")]
    public void Invalid_time_fixed_conditions_fail_before_orm(string text)
    {
        using var session=Session();Assert.ThrowsAny<ArgumentException>(()=>Compile("duration=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",text}},"time_contract"));
    }
}
