using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

public sealed partial class FixedConditionSqlTests
{
    /// <summary>固定常量入口必须保留SQL三值及原LIKE语义，不能把NULL等值改成IS NULL。</summary>
    [Fact]
    public void Constant_only_entry_preserves_sql_null_and_raw_like()
    {
        using var session=Session();var sql=Compile("name=NULL AND id IN(NULL,9007199254740993) AND name LIKE 'A_%'").ApplyConstants(session.Select<Row>(),session,"page").ToSql();
        Assert.Contains("=NULL",sql);Assert.DoesNotContain("IS NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("IN (NULL,@page_0)",sql);Assert.DoesNotContain("9007199254740993",sql);Assert.DoesNotContain("A_%",sql);
    }
    /// <summary>含变量的计划不能借无变量页面入口吞掉条件，失败前原查询保持不变。</summary>
    [Fact]
    public void Constant_only_entry_rejects_variables_before_mutating_query()
    {
        using var session=Session();var query=session.Select<Row>();var before=query.ToSql();Assert.Throws<InvalidOperationException>(()=>Compile("id=@actor").ApplyConstants(query,session,"page"));Assert.Equal(before,query.ToSql());
    }
}
