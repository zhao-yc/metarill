using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>多选派生不能借字符串类型开放任意JSON函数或读取凭据。</summary>
public sealed class NonRedundantMultiReadTests
{
    /// <summary>只有编译证明的多值名称才能使用元素筛选；错误在建立数据库连接前拒绝。</summary>
    [Fact]
    public async Task Array_predicate_rejects_plain_text_before_connecting()
    {
        using var session=new DatabaseSession(new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});
        var plan=MySqlNonRedundantReadCompiler.Compile(Catalog(),"1",["id","name","party_names"]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadPageAsync(plan,[new("name",FixedReadComparison.JsonArrayAnyContains,"x")],[],1,10));
    }
    /// <summary>当前读只能加入调用者真实事务，不能另开连接伪装为当前锁。</summary>
    [Fact]
    public async Task Current_projection_requires_actual_transaction_before_connecting()
    {
        using var session=new DatabaseSession(new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});
        var plan=MySqlNonRedundantReadCompiler.Compile(Catalog(),"1",["party_names"]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadPageAsync(plan,[],[],1,10,currentRead:true));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadCurrentRowsAsync(plan,[],[],10));
        var portable=NonRedundantReads.Compile(Catalog(),"1",["party_names"]);await Assert.ThrowsAsync<InvalidOperationException>(()=>new NonRedundantReader(session).CurrentRowsAsync(portable,[],[],10));
    }
    /// <summary>没有目标、非物理/数组名称、凭据以及矛盾目标声明不能产生半可用SQL。</summary>
    [Theory]
    [InlineData("missing")][InlineData("array")][InlineData("derived")][InlineData("credential")][InlineData("targetMismatch")][InlineData("physicalAlias")][InlineData("wrongAliasPath")]
    public void Rejects_unproven_multiple_name(string scenario)
    {
        var catalog=Catalog();var relation=catalog.ConfigColumns.Single(column=>column.Id=="120");var name=catalog.ConfigColumns.Single(column=>column.Id=="22");
        if(scenario=="missing")relation.ControlDetail="{\"tableId\":\"999\"}";
        if(scenario=="array")name.ControlKind="FileLibrary";
        if(scenario=="derived")name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(scenario=="credential"){name.ColumnName="password";relation.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"password\"}";}
        if(scenario=="targetMismatch")relation.LinkConfigTableId="1";
        if(scenario is "physicalAlias" or "wrongAliasPath")catalog.ConfigColumns.Add(new(){Id="121",ConfigTableId="1",ColumnName="party_names",LogicalType="string",Extra=scenario=="wrongAliasPath"?new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("ship.name")}:new()});
        Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.Compile(catalog,"1",["party_"+(scenario=="credential"?"passwords":"names")]));
    }
    /// <summary>原名称数组和元素筛选都读取当前页简称，保留ID顺序与范围。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Uses_page_label_for_array_and_element_filter(int targetStorage)
    {
        var catalog=Catalog();
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=targetStorage;
        var relation=catalog.ConfigColumns.Single(column=>column.Id=="120");
        relation.ControlDetail="""{"tableId":"2","pageId":"201","nameField":"name"}""";
        catalog.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="short_name",LogicalType="string"});
        catalog.ConfigPages.Add(new(){Id="101",ConfigTableId="1"});
        catalog.ConfigPages.Add(new(){Id="201",ConfigTableId="2",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
        catalog.ConfigPageColumns.Add(new(){Id="101120",ConfigPageId="101",ConfigColumnId="120",
            Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"short_name"}""")}});
        var binding=new NonRedundantMultiTargetScope("120","101","201",false);
        var plan=NonRedundantReads.CompileProjectPage(catalog,"1",["party_names"],["party_names"],[],[],[binding]).MySql;
        var sql=plan.Expression("party_names");
        Assert.Contains("multi_value.`short_name`",sql);
        Assert.Contains("multi_value.`yz_project_id`",sql);
        Assert.Contains("multi_value.`delete_no` IS NULL",sql);
        Assert.Contains("ORDER BY multi_ids.ordinal",sql);
        Assert.Contains("multi_value.`short_name` LIKE {parameter}",plan.ArrayNamePredicates["party_names"]);
        var baseline=MySqlNonRedundantReadCompiler.Compile(catalog,"1",["party_names"]);
        Assert.Contains("multi_value.`name`",baseline.Expression("party_names"));
        Assert.DoesNotContain("short_name",baseline.Expression("party_names"));
    }

    /// <summary>名称数组不占物理列；相同项目和主键约束来自正式目录。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog=NonRedundantReadTests.Catalog("mt_nrm_root","mt_nrm_party");
        catalog.ConfigColumns.Add(new(){Id="120",ConfigTableId="1",ColumnName="party_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="2",ControlDetail="{\"tableId\":\"2\",\"nameField\":\"name\"}"});return catalog;
    }
}
