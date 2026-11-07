using System.Data.Common;
using System.Reflection;
using FreeSql.DataAnnotations;
using Metarill.Data.Queries;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>原普通文本与已确认的名称字面搜索分离；SQL编译不打开连接。</summary>
public sealed class PlainTextLikeTests
{
    /// <summary>模式关键词只在参数中，不采用LOCATE或ESCAPE重写旧%/_及反斜杠规则。</summary>
    [Fact]public void Physical_pattern_uses_column_like_with_parameter_and_keeps_scope()
    {
        using var session=Session();var scoped=session.Select<Row>().As("plain_text").Where(row=>row.ProjectId==17);
        var sql=StringNameQuery.ApplyPattern(session,scoped,row=>row.Name,"Café'_%!\\").ToSql();
        Assert.Contains("plain_text.`name` LIKE @mt_text_pattern_",sql);Assert.Contains("ProjectId",sql);
        Assert.DoesNotContain("Café",sql);Assert.DoesNotContain("LOCATE",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("ESCAPE",sql);
        var literal=StringNameQuery.ApplyLike(session,session.Select<Row>(),row=>row.Name,"x_y").ToSql();Assert.Contains("ESCAPE '!'",literal);
    }
    /// <summary>同一固定计划区分原模式与字面Contains，绑定结构保持参数化且不丢项目范围。</summary>
    [Theory][InlineData(true)][InlineData(false)]public void Nonredundant_pattern_and_literal_have_distinct_binding(bool pattern)
    {
        var plan=NonRedundantReadTests.Plan(NonRedundantReadTests.Catalog("mt_pattern_root","mt_pattern_party"));
        var comparison=pattern?FixedReadComparison.LikePattern:FixedReadComparison.Contains;var filter=new FixedReadFilter("name",comparison,"x_y%!\\");
        var method=typeof(MySqlNonRedundantReader).GetMethod("Filter",BindingFlags.Static|BindingFlags.NonPublic)!;
        var sql=(string)method.Invoke(null,[plan,filter,0])!;Assert.Contains(" LIKE @filter_0",sql);Assert.Equal(!pattern,sql.Contains("ESCAPE '!'",StringComparison.Ordinal));
        using var command=new MySqlCommand();typeof(MySqlNonRedundantReader).GetMethod("Bind",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[command,"17",new[]{filter}]);
        Assert.Equal(pattern?"%x_y%!\\%":"%x!_y!%!!\\%",command.Parameters.Cast<DbParameter>().Single(p=>p.ParameterName.TrimStart('@')=="filter_0").Value);
    }
    /// <summary>模式不能借任意表达式／忽略列扩大查询，空值和空白继续由生成过滤入口跳过。</summary>
    [Fact]public void Pattern_rejects_unmapped_expression_and_blank_before_query_mutation()
    {
        using var session=Session();var query=session.Select<Row>();var before=query.ToSql();
        Assert.Throws<ArgumentException>(()=>StringNameQuery.ApplyPattern(session,query,row=>row.Name!.Trim(),"x"));
        Assert.Throws<ArgumentException>(()=>StringNameQuery.ApplyPattern(session,query,row=>row.Ignored,"x"));
        Assert.Throws<ArgumentException>(()=>StringNameQuery.ApplyPattern(session,query,row=>row.Name," "));Assert.Equal(before,query.ToSql());
    }
    /// <summary>枚举新增项仅允许文本，不以类型转换掩盖数值字段或null错误。</summary>
    [Theory][InlineData("id",true)][InlineData("name",false)]public async Task Pattern_invalid_type_is_rejected_before_connecting(string field,bool numeric)
    {
        using var session=Session();var reader=new MySqlNonRedundantReader(session);var plan=NonRedundantReadTests.Plan(NonRedundantReadTests.Catalog("mt_pattern_root","mt_pattern_party"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadPageAsync(plan,[new(field,FixedReadComparison.LikePattern,numeric?"1":null)],[],1,10));
    }
    /// <summary>只编译SQL，端口1及假凭据不能用于真实连接。</summary>
    private static DatabaseSession Session()=>new(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
    private sealed class Row
    {
        [Column(IsPrimary=true)]public long Id{get;set;}
        public long ProjectId{get;set;}
        [Column(Name="name")]public string? Name{get;set;}
        [Column(IsIgnore=true)]public string? Ignored{get;set;}
    }
}
