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
    /// <summary>文本父级由发布层决定，逻辑祖先按字节匹配且所有原值独立绑定。</summary>
    [Fact]
    public void Text_parent_matches_the_logical_ancestor_by_exact_bytes()
    {
        var catalog=NonRedundantCrossTableReadTests.Catalog("scope_text","string","string");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
        definition["levels"]![0]!["idField"]="business_key";
        control.ControlDetail=definition.ToJsonString();
        catalog.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="business_key",
            LogicalType="string",ColumnLength="50",IsDbNotNull=1});
        catalog.ConfigIndexes.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{id="210",configTableId="2",isUnique=1}));
        catalog.ConfigIndexColumns.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{configIndexId="210",configColumnId="29"}));
        var plan=NonRedundantReads.CompileWithCrossTables(catalog,"1",["id"],NonRedundantCrossTableReadTests.Scopes(catalog),["country_id"]);
        using var session=Session();
        var query=session.Select<Row>();
        Assert.Throws<ArgumentException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","text_parent","  ",catalog.YzProjectId));
        Assert.Throws<ArgumentException>(()=>NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","text_parent",1L,catalog.YzProjectId));
        var sql=NonRedundantCrossTableScopeSql.Apply(query,session,plan,"country_id","text_parent","001",catalog.YzProjectId).ToSql();
        Assert.Contains(")=BINARY @text_parent_value",sql);
        Assert.Contains("cross2.`business_key`",sql);
        Assert.Contains("b.`id`=`a`.`id`",sql);
        Assert.DoesNotContain("'001'",sql);
    }

    /// <summary>共享稀疏读取从两支恢复公共父键，比较实际文本并绑定用户提交的父值。</summary>
    [Fact]
    public void Shared_text_parent_matches_both_sparse_branches_by_exact_bytes()
    {
        var catalog=SharedCrossTableReadTests.Catalog("shared_text","string","string");
        foreach(var control in catalog.ConfigColumns.Where(column=>column.Id is "31" or "41"))
        {
            var definition=System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
            definition["levels"]![0]!["idField"]="business_key";
            control.ControlDetail=definition.ToJsonString();
        }
        catalog.ConfigColumns.Add(new(){Id="root_key",ConfigTableId="2",ColumnName="business_key",
            LogicalType="string",ColumnLength="50",IsDbNotNull=1,YzProjectId=catalog.YzProjectId});
        catalog.ConfigIndexes.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{id="root_index",configTableId="2",isUnique=1}));
        catalog.ConfigIndexColumns.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{configIndexId="root_index",configColumnId="root_key"}));
        var scopes=SharedCrossTableReadTests.Scopes(catalog);
        var fields=SharedCrossTableReadTests.Outputs(false);
        var read=MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"1",fields,scopes);
        Assert.Equal(2,read.Crosses.Count);
        Assert.All(read.Crosses,branch=>Assert.True(branch.Selection.TextKeys[0]));
        var validation=Assert.Single(read.Crosses,branch=>branch.RequireRowValidity).Validity;
        Assert.Contains(")=BINARY (",validation);
        Assert.DoesNotContain("AS SIGNED",read.Expressions["country_id"]);
        var plan=NonRedundantReads.CompileWithCrossTables(catalog,"1",["id"],scopes,["country_id"]);
        using var session=Session();
        var sql=NonRedundantCrossTableScopeSql.Apply(session.Select<Row>(),session,plan,"country_id","shared_root","001",catalog.YzProjectId).ToSql();
        Assert.Contains(")=BINARY @shared_root_value",sql);
        Assert.Contains("COALESCE",sql);
        Assert.Contains("shared_text_place",sql);
        Assert.Contains("shared_text_warehouse",sql);
        Assert.DoesNotContain("'001'",sql);
    }

    /// <summary>R1 使用物理父字段限制来源行，再按精确键验证当前文本节点及其整数祖先。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Redundant_text_parent_validates_physical_key_and_target_visibility(bool account)
    {
        using var session=Session();
        const long dealer=9007199254740993L;
        var rule=account?FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("dealer_id=@current_dealer_id",["dealer_id"]),
            new Dictionary<string,string>{{"dealer_id","string"}}):null;
        var root=new RedundantSharedParentLevel("root","country","2","country","201","22",null,account?null:"string",true,"",rule)
        {
            AlternateKey="business_key",TextKey=true
        };
        var region=new RedundantSharedParentLevel("region","area","3","area","301","32","country_id","int64",true,"",null);
        var leaf=new RedundantSharedParentLevel("leaf","place","4","place","401","42","area_id","string",true,"",null)
        {
            AlternateKey="business_key",TextKey=true
        };
        var plan=new RedundantSharedParentPlan("15","place_id","string","15",[root,region,leaf]);
        var actor=new Metarill.Core.Security.RuntimeActor(2,"测试",false,"9007199254740993","3"){DealerId=account?dealer:null};
        var clause=RedundantSharedParentScopeSql.Render(session.Provider,[plan],actor,"scope_test",
            field=>"`a`.`"+field+"`",true);
        Assert.Contains("ON BINARY rp1.`country_id`=BINARY rp0.`business_key`",clause.Sql);
        Assert.Contains("BINARY rp2.`business_key`=BINARY `a`.`place_id`",clause.Sql);
        Assert.Contains("rp2.`business_key` REGEXP '[^[:space:]]'",clause.Sql);
        Assert.Contains("rp1.`id`>0",clause.Sql);
        Assert.Contains("rp2.`delete_no` IS NULL",clause.Sql);
        Assert.Contains("FOR UPDATE",clause.Sql);
        if(account)
        {
            Assert.Contains("rp0.`dealer_id`=@scope_test_0_0_scope_",clause.Sql);
            Assert.DoesNotContain("scope_test_0_0_project",clause.Parameters.Keys);
            Assert.Contains("9007199254740993",clause.Parameters.Values);
            Assert.Throws<UnauthorizedAccessException>(()=>RedundantSharedParentScopeSql.Render(session.Provider,[plan],
                actor with{DealerId=null},"scope_test",field=>"`a`.`"+field+"`",true));
        }
        else Assert.Equal(actor.ProjectId,clause.Parameters["scope_test_0_0_project"]);
        Assert.Equal(9007199254740993L,clause.Parameters["scope_test_0_1_project"]);
        var query=session.Select<Row>();
        Assert.Throws<ArgumentException>(()=>NonRedundantReads.ApplyTextParent(query,session,"place_id","001"));
        Assert.Throws<ArgumentException>(()=>NonRedundantReads.ApplyTextParent(query,session,"place_id","  ",businessKey:true));
        var sql=NonRedundantReads.ApplyTextParent(query,session,"place_id","001",businessKey:true).ToSql();
        Assert.Contains("BINARY (`a`.`place_id`)=BINARY @mt_parent_place_id",sql);
        Assert.DoesNotContain("'001'",sql);
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
