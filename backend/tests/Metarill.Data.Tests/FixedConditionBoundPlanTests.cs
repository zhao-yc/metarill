using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>关系名称读取复用固定条件：请求只绑定值，页面与控件范围不得相互覆盖。</summary>
public sealed class FixedConditionBoundPlanTests
{
    /// <summary>绑定之后修改调用字典不影响参数；长ID保留整数，两个范围都保留。</summary>
    [Fact]
    public void Binding_snapshots_values_and_ands_without_mutating_static_plans()
    {
        var scope=Plan("id=@parent_id",new(){["id"]="int64"});
        var page=Plan("status=1",new(){["status"]="int32"});
        var values=new Dictionary<string,object?>{["parent_id"]="9007199254740993"};
        var bound=scope.BindVariables(values).And(page);values["parent_id"]="2";
        Assert.Empty(bound.Variables);Assert.Single(scope.Variables);
        using var session=Session();using var command=new MySqlCommand();var sql=bound.BindConstants(command,session,"bound");
        Assert.Contains("AND",sql);Assert.Equal(2,command.Parameters.Count);
        Assert.Equal(9007199254740993L,command.Parameters[0].Value);Assert.Equal(1,command.Parameters[1].Value);
        Assert.DoesNotContain("9007199254740993",sql);
    }
    /// <summary>缺变量、错误类型和矛盾类型在发出数据库命令前拒绝；NULL仍沿SQL三值语义。</summary>
    [Fact]
    public void Missing_invalid_and_conflicting_values_fail_before_execution()
    {
        var plan=Plan("id=@parent_id",new(){["id"]="int64"});
        Assert.Throws<ArgumentException>(()=>plan.BindVariables(new Dictionary<string,object?>()));
        Assert.Throws<ArgumentException>(()=>plan.BindVariables(new Dictionary<string,object?>{["parent_id"]="1 OR 1=1"}));
        Assert.Throws<InvalidOperationException>(()=>plan.And(Plan("id='x'",new(){["id"]="string"})));
        using var session=Session();using var command=new MySqlCommand();
        var sql=plan.BindVariables(new Dictionary<string,object?>{["parent_id"]=null}).BindConstants(command,session,"empty");
        Assert.Contains("=NULL",sql);Assert.Empty(command.Parameters.Cast<MySqlParameter>());
    }
    /// <summary>只编译测试内的字段与类型，不连接服务器。</summary>
    private static FixedConditionSqlPlan Plan(string text,Dictionary<string,string> types)=>FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(text,types.Keys),types);
    /// <summary>只取适配器参数语法，不打开连接或执行SQL。</summary>
    private static DatabaseSession Session()=>new(new(){Provider="mysql",ExpectedDatabase="unit_test",ConnectionString="Server=127.0.0.1;Database=unit_test;User Id=unit;Password=unit"});
}
