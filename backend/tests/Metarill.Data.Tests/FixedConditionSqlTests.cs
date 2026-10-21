using FreeSql.DataAnnotations;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using System.Text.Json;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>实际ORM解析固定条件；仅验证编译和绑定，不能替代真实数据库或正式候选入口。</summary>
public sealed class FixedConditionSqlTests
{
    private static Dictionary<string,string> Types()=>new(){{"id","int64"},{"name","string"},{"status","int32"},{"amount","decimal"},{"enabled","bool"},{"at","datetime"},{"duration","time"},{"ratio","double"}};
    /// <summary>元数据和运行时值均为参数；外层已有约束继续AND，源字面量与高位ID不进入SQL文本。</summary>
    [Fact]
    public void Actual_orm_keeps_literals_and_context_out_of_sql_and_preserves_outer_constraints()
    {
        using var session=Session();var plan=Compile("id IN(9007199254740993,@parent_id) AND name LIKE @formParameters.allowed AND status>=1");
        var query=plan.Apply(session.Select<Row>().Where(row=>row.Id>0),session,new Dictionary<string,object?>{{"parent_id","9007199254740994"},{"formParameters.allowed","private%_! ' OR 1=1 --"}},"fixed_test");
        var sql=query.ToSql();Assert.Contains("@fixed_test_0",sql);Assert.Contains("@fixed_test_3",sql);Assert.DoesNotContain("9007199254740993",sql);Assert.DoesNotContain("private",sql);Assert.Contains("LIKE",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("AND",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[]{"parent_id","formParameters.allowed"},plan.Variables);
    }
    /// <summary>NULL比较不得被ORM路径改成IS NULL，IN中的NULL同样不匹配空列。</summary>
    [Fact]
    public void Sql_null_semantics_are_distinct_from_is_null()
    {
        using var session=Session();var query=Compile("name=NULL AND name<>@nullValue AND id IN(NULL,@id)").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"nullValue",null},{"id",1L}},"null_test");
        var sql=query.ToSql();Assert.DoesNotContain("IS NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("=NULL",sql);Assert.Contains("<>NULL",sql);Assert.Contains("IN (NULL,@null_test_0)",sql);
        var explicitNull=Compile("name IS NULL").Apply(session.Select<Row>(),session,new Dictionary<string,object?>(),"null_test").ToSql();Assert.Contains("IS NULL",explicitNull,StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>完整绑定失败不改变既有查询，错误信息不带输入值。</summary>
    [Fact]
    public void Failed_binding_is_atomic_and_does_not_echo_context()
    {
        using var session=Session();var query=session.Select<Row>().Where(row=>row.Id>0);var before=query.ToSql();
        var missing=Compile("status=1 AND id=@missing");Assert.Throws<ArgumentException>(()=>missing.Apply(query,session,new Dictionary<string,object?>(),"atomic"));Assert.Equal(before,query.ToSql());
        var invalid=Compile("id=@value");var error=Assert.Throws<ArgumentException>(()=>invalid.Apply(query,session,new Dictionary<string,object?>{{"value","private-value OR 1=1"}},"atomic"));Assert.DoesNotContain("private-value",error.Message);Assert.Equal(before,query.ToSql());
    }
    /// <summary>字段类型在编译时固定，后续元数据字典变化不改变已发布计划。</summary>
    [Fact]
    public void Logical_types_are_frozen_and_empty_conditions_keep_the_same_query()
    {
        using var session=Session();var types=Types();var syntax=FixedConditionPlan.Compile("id=@value",types.Keys);var plan=FixedConditionSqlPlan.Compile(syntax,types);types["id"]="string";
        plan.Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",9007199254740993L}},"snapshot");
        var empty=Compile("");var query=session.Select<Row>();Assert.Same(query,empty.Apply(query,session,new Dictionary<string,object?>(),"empty"));Assert.DoesNotContain("Literal",JsonSerializer.Serialize(plan));
    }
    /// <summary>各逻辑类型的变量走明确转换，JSON数值保持原始十进制文本。</summary>
    [Theory]
    [InlineData("id","\"9007199254740993\"")][InlineData("id","9007199254740993")][InlineData("status","2")]
    [InlineData("amount","12.125")][InlineData("ratio","1.25")][InlineData("enabled","true")][InlineData("enabled","0")]
    [InlineData("at","\"2026-10-21T12:30:00\"")][InlineData("duration","\"12:30:00\"")][InlineData("name","\"text\"")]
    public void Typed_variables_compile_without_opening_database(string field,string json)
    {
        using var session=Session();using var document=JsonDocument.Parse(json);var sql=Compile(field+"=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",document.RootElement}},"typed").ToSql();Assert.Contains("@typed_0",sql);
    }
    /// <summary>嵌套、数组、溢出、精度丢失及非有限值在连接前拒绝，不依赖数据库隐式转换。</summary>
    [Theory]
    [InlineData("id","1.5")][InlineData("id","9223372036854775808")][InlineData("status","2147483648")]
    [InlineData("enabled","2")][InlineData("ratio","\"NaN\"")][InlineData("ratio","\"Infinity\"")]
    [InlineData("amount","1e60")][InlineData("at","\"not-a-date\"")][InlineData("duration","\"bad-time\"")]
    [InlineData("id","[]")][InlineData("name","{}")][InlineData("name","1")]
    public void Invalid_variables_are_rejected_before_database_connection(string field,string json)
    {
        using var session=Session();using var document=JsonDocument.Parse(json);Assert.Throws<ArgumentException>(()=>Compile(field+"=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",document.RootElement}},"reject"));
    }
    /// <summary>极小数和超出十进制精度的值不能被舍入为另一个ID或可匹配的零。</summary>
    [Theory]
    [InlineData("id","1e-100")][InlineData("id","9223372036854775807.0000000000000000000000000000001")]
    [InlineData("enabled","1.0000000000000000000000000000001")][InlineData("amount","\"0.1000000000000000000000000000000001\"")][InlineData("ratio","1e-5000")]
    public void Numeric_rounding_does_not_change_condition_values(string field,string json)
    {
        using var session=Session();using var document=JsonDocument.Parse(json);Assert.Throws<ArgumentException>(()=>Compile(field+"=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",document.RootElement}},"exact"));
    }
    /// <summary>浮点ID、坏元数据和参数名前缀不得变成可执行查询。</summary>
    [Fact]
    public void Unproved_types_literals_and_parameter_names_fail_before_query()
    {
        using var session=Session();Assert.Throws<ArgumentException>(()=>Compile("id=@value").Apply(session.Select<Row>(),session,new Dictionary<string,object?>{{"value",9007199254740993d}},"invalid"));
        Assert.Throws<ArgumentException>(()=>Compile("id=1.5"));Assert.Throws<ArgumentException>(()=>Compile("name=1"));Assert.Throws<ArgumentException>(()=>Compile("id LIKE '1%'"));
        var types=Types();types["id"]="json";Assert.Throws<ArgumentException>(()=>FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("id=1",types.Keys),types));
        Assert.Throws<InvalidOperationException>(()=>Compile("id=1").Apply(session.Select<Row>(),session,new Dictionary<string,object?>(),"x;DROP"));
    }
    /// <summary>语法和类型都来自本测试已声明字段，不给运行时替换入口。</summary>
    private static FixedConditionSqlPlan Compile(string text){var types=Types();return FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(text,types.Keys),types);}
    /// <summary>离线会话的无效端口证明这些转换用例不依赖真实数据库。</summary>
    private static DatabaseSession Session()=>new(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
    [Table(Name="mt_offline_fixed",DisableSyncStructure=true)]
    private sealed class Row
    {
        [Column(Name="id",IsPrimary=true)]public long Id{get;set;}
        [Column(Name="name")]public string? Name{get;set;}
    }
}
