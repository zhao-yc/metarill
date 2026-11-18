using FreeSql.DataAnnotations;
using Metarill.Core.Metadata;
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
    /// <summary>当前对象匹配使用祖先与名称表达式，既不嵌入常量也不读取未经编译的物理名称列。</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int64")]
    public void Fixed_page_condition_binds_logical_ids_names_and_physical_terms(string type)
    {
        var catalog=NonRedundantCrossTableReadTests.Catalog("scope_page",type,"string");
        foreach(var page in catalog.ConfigPages)
            foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+"_"+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsShow=1});
        var types=new Dictionary<string,string>{{"country_id",type},{"place_name","string"},{"amount","decimal"}};
        var crossFields=new HashSet<string>(StringComparer.Ordinal){"country_id","place_name"};
        var pageScope=new PageMetadata(){Id="11",ConfigTableId="1",YzProjectId=catalog.YzProjectId};
        catalog.ConfigPages.Add(pageScope);
        var plan=NonRedundantReads.CompileCrossPageMembership(catalog,catalog.ConfigTables[0],pageScope,types.Keys.ToArray(),crossFields);
        var key=type=="string"?"'9007199254740993'":"9007199254740993";
        var condition=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(
            $"country_id={key} AND place_name LIKE '甲%乙' AND amount>=12.5",types.Keys),types);
        using var session=Session();
        var query=session.Select<Row>().Where(row=>row.Id>0);
        var before=query.ToSql();
        Assert.Throws<InvalidOperationException>(()=>NonRedundantCrossTableScopeSql.ApplyCondition(query,session,plan,condition,"b205",catalog.YzProjectId,true));
        Assert.Equal(before,query.ToSql());
        NonRedundantCrossTableScopeSql.ApplyCondition(query,session,plan,condition,"b205",catalog.YzProjectId);
        var sql=query.ToSql();
        Assert.Contains("EXISTS",sql);
        Assert.Contains("b.`id`=`a`.`id`",sql);
        Assert.Contains("CASE WHEN",sql);
        Assert.Contains("b.`amount`",sql);
        Assert.Contains("@b205_condition_",sql);
        Assert.Contains("@b205_constant_mt_cross_scope_",sql);
        Assert.DoesNotContain("b.`place_name`",sql);
        Assert.DoesNotContain("9007199254740993",sql);
        Assert.DoesNotContain("甲%乙",sql);
        Assert.Equal(new[]{"id"},plan.OutputTypes.Keys);

        // 关系候选多投影名称和排序字段时，须仍使用同一关联范围和逻辑谓词。
        var candidate=NonRedundantReads.CompilePageMembership(catalog,catalog.ConfigTables[0],pageScope,types.Keys.ToArray(),
            crossFields,new HashSet<string>(StringComparer.Ordinal),["id","name"],["name","amount"]);
        Assert.Equal(new[]{"id","name"},candidate.OutputTypes.Keys);
        var candidateQuery=session.Select<Row>().Where(row=>row.Id>0);
        NonRedundantCrossTableScopeSql.ApplyCondition(candidateQuery,session,candidate,condition,"b205",catalog.YzProjectId);
        Assert.Equal(sql,candidateQuery.ToSql());
    }

    /// <summary>只生成SQL，离线端口不具备建表或数据访问条件。</summary>
    private static DatabaseSession Session()=>new(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
    [Table(Name="scope_fixed_source",DisableSyncStructure=true)]
    private sealed class Row{[Column(Name="id",IsPrimary=true)]public long Id{get;set;}}
}
