using FreeSql.DataAnnotations;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>逻辑父范围只能引用固定路径；输入错误必须在修改ORM查询或连接数据库前拒绝。</summary>
public sealed class NonRedundantCrossTableScopeSqlTests
{
    /// <summary>两个控件／常量条件的参数相互隔离，长ID和私有文本不进入SQL。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Independent_scopes_bind_values_and_all_target_constants(string type)
    {
        var catalog=NonRedundantCrossTableReadTests.Catalog("scope_fixed",type,"string",true);
        var plan=NonRedundantReads.CompileWithCrossTables(catalog,"1",["id"],NonRedundantCrossTableReadTests.Scopes(catalog),["country_id","secondary_country_id"]);
        using var session=Session();var query=session.Select<Row>().Where(row=>row.Id>0);
        object first=type=="string"?"9007199254740993":9007199254740993L;object second=type=="string"?"9007199254740994":9007199254740994L;
        NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","root",first,catalog.YzProjectId);
        NonRedundantCrossTableScopeSql.Apply(query,session,plan,"secondary_country_id","second",second,catalog.YzProjectId);
        var sql=query.ToSql();Assert.Contains("EXISTS",sql);Assert.Contains("@root_value",sql);Assert.Contains("@second_value",sql);
        Assert.Contains("@root_constant_mt_cross_scope_",sql);Assert.Contains("@second_constant_mt_cross_scope_",sql);
        Assert.DoesNotContain("@mt_cross_scope_",sql);Assert.DoesNotContain("@project_scope_",sql);
        Assert.DoesNotContain(NonRedundantCrossTableReadTests.BlockedTitle,sql);Assert.DoesNotContain("9007199254740993",sql);Assert.DoesNotContain("FOR UPDATE",sql);
        Assert.Contains("b.`id`=`a`.`id`",sql);Assert.Equal(new[]{"id"},plan.OutputTypes.Keys);
    }
    /// <summary>名称、未编译层位、错类型、非法／越界ID及项目不能被宽松转换成可匹配值。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Unproved_fields_types_ids_and_project_fail_without_changing_query(string type)
    {
        var catalog=NonRedundantCrossTableReadTests.Catalog("scope_reject",type,"int64");
        var plan=NonRedundantReads.CompileWithCrossTables(catalog,"1",["id"],NonRedundantCrossTableReadTests.Scopes(catalog),["country_id"]);
        using var session=Session();var query=session.Select<Row>().Where(row=>row.Id>0);var before=query.ToSql();
        foreach(var field in new[]{"name","country_name","area_id","id","x;DROP"})Assert.Throws<InvalidOperationException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,field,"p","1",catalog.YzProjectId));
        object[] bad=type=="string"?[1L,"01","0","-1","1.0","1 ","9223372036854775808","' OR 1=1 --"]:["1",1,1d,0L,-1L];
        foreach(var value in bad)Assert.Throws<ArgumentException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","p",value,catalog.YzProjectId));
        object valid=type=="string"?"1":1L;
        foreach(var project in new[]{"0","01","-1","1 ","9223372036854775808"})Assert.Throws<ArgumentException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","p",valid,project));
        Assert.Throws<InvalidOperationException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","p;DROP",valid,catalog.YzProjectId));
        Assert.Throws<InvalidOperationException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","p",valid,catalog.YzProjectId,true));
        Assert.Equal(before,query.ToSql());
    }
    /// <summary>只生成SQL，离线端口不具备建表或数据访问条件。</summary>
    private static DatabaseSession Session()=>new(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
    [Table(Name="scope_fixed_source",DisableSyncStructure=true)]
    private sealed class Row{[Column(Name="id",IsPrimary=true)]public long Id{get;set;}}
}
