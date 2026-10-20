using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>单值读取编译的失败边界；真实筛选及SQL行为在MySQL用例验证。</summary>
public sealed class NonRedundantReadTests
{
    /// <summary>只编译请求依赖；同表关系、自关联与不可变输出均不改源元数据。</summary>
    [Fact]
    public void Compiles_single_paths_and_freezes_source_metadata()
    {
        var catalog=Catalog("mt_read_root","mt_read_party");var plan=Plan(catalog);var before=plan.Outputs.ToArray();
        catalog.ConfigColumns.Single(column=>column.Id=="22").ColumnName="renamed";catalog.ConfigTables[1].TableName="renamed_table";
        Assert.Equal(before,plan.Outputs);Assert.Throws<NotSupportedException>(()=>((IList<string>)plan.Outputs).Add("rogue"));
    }
    /// <summary>实际递归字段循环被拒，普通自关联不是循环。</summary>
    [Fact]
    public void Rejects_real_derived_cycle()
    {
        var catalog=Catalog("mt_read_root","mt_read_party");var field=catalog.ConfigColumns.Single(column=>column.ColumnName=="grand_label");field.Extra["columnFullName"]=JsonSerializer.SerializeToElement("parent.grand_label");
        Assert.Throws<InvalidOperationException>(()=>Plan(catalog));
    }
    /// <summary>标识符、未知依赖、重复输出和未完成表达式不能变成原生SQL或NULL降级。</summary>
    [Theory]
    [InlineData("missing")][InlineData("name;DROP TABLE x")][InlineData("bill.missing")][InlineData("layout")]
    public void Rejects_unsupported_outputs(string name)=>Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.Compile(Catalog("mt_read_root","mt_read_party"),"1",[name]));
    /// <summary>同项目目标、物理主键和受支持单值关系是实际LEFT JOIN的前提。</summary>
    [Theory]
    [InlineData("foreign")][InlineData("key")][InlineData("multi")][InlineData("duplicate")][InlineData("provider")]
    public void Rejects_invalid_model(string kind)
    {
        var catalog=Catalog("mt_read_root","mt_read_party");
        switch(kind)
        {
            case "foreign":catalog.ConfigTables[1].YzProjectId="999";break;
            case "key":catalog.ConfigColumns.Single(column=>column.Id=="21").IsDbNotNull=0;break;
            case "multi":catalog.ConfigColumns.Single(column=>column.Id=="15").ControlKind="MultiSelectPage";break;
            case "duplicate":Assert.Throws<InvalidOperationException>(()=>MySqlNonRedundantReadCompiler.Compile(catalog,"1",["name","NAME"]));return;
            case "provider":catalog.DatabaseProvider="postgresql";break;
        }
        Assert.ThrowsAny<Exception>(()=>Plan(catalog));
    }
    /// <summary>非法字段、排序或值必须在连接数据库前失败，不先执行半次COUNT。</summary>
    [Theory]
    [InlineData("field")][InlineData("order")][InlineData("value")][InlineData("contains")][InlineData("operation")][InlineData("overflow")][InlineData("fractionalId")]
    public async Task Rejects_invalid_query_before_connecting(string kind)
    {
        using var session=new DatabaseSession(new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});
        var reader=new MySqlNonRedundantReader(session);var plan=Plan(Catalog("mt_read_root","mt_read_party"));
        FixedReadFilter[] filters=kind switch{"field"=>[new("rogue",FixedReadComparison.Equal,"x")],"value"=>[new("id",FixedReadComparison.Equal,new[]{1,2})],"contains"=>[new("id",FixedReadComparison.Contains,"x")],"operation"=>[new("name",(FixedReadComparison)999,"x")],"overflow"=>[new("id",FixedReadComparison.Equal,"9223372036854775808")],"fractionalId"=>[new("id",FixedReadComparison.Equal,1.5m)],_=>[]};
        FixedReadOrder[] order=kind=="order"?[new("rogue")]:[];
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadPageAsync(plan,filters,order,1,10));
    }
    /// <summary>文本或重复统计字段在连接前拒绝，不能让SUM隐式把业务名称转成零。</summary>
    [Theory]
    [InlineData("name")][InlineData("duplicate")]
    public async Task Rejects_invalid_statistics_before_connecting(string kind)
    {
        using var session=new DatabaseSession(new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});
        var reader=new MySqlNonRedundantReader(session);var fields=kind=="name"?new[]{"name"}:new[]{"bill_amount","BILL_AMOUNT"};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.ReadStatisticsAsync(Plan(Catalog("mt_read_root","mt_read_party")),[],fields));
    }
    /// <summary>固定用途字段包含同目标不同角色、自关联与跨级派生。</summary>
    internal static MySqlNonRedundantReadPlan Plan(MetadataCatalog catalog)=>MySqlNonRedundantReadCompiler.Compile(catalog,"1",["id","name","bill_name","ship_name","grand_label","parent_name","bill_amount"]);
    /// <summary>由元数据定义两个隔离实体表，派生名称不注册物理列。</summary>
    internal static MetadataCatalog Catalog(string source,string target)
    {
        var catalog=new MetadataCatalog{YzProjectId="901000000000000001",ConfigTables=[new(){Id="1",TableName=source,IsRongyu=0},new(){Id="2",TableName=target,IsRongyu=0}]};
        catalog.ConfigColumns=[Column("11","1","id","int64"),Column("12","1","name"),Column("13","1","yz_project_id","int64"),Column("14","1","delete_no","datetime"),Relation("15","1","bill_id","2"),Relation("16","1","ship_id","2"),Relation("17","1","parent_id","1"),Column("18","1","grand_label"),Column("19","1","layout"),Column("110","1","bill_amount","decimal"),Column("21","2","id","int64"),Column("22","2","name"),Column("23","2","yz_project_id","int64"),Column("24","2","delete_no","datetime"),Relation("25","2","parent_id","2"),Column("26","2","amount","decimal")];
        catalog.ConfigColumns.Single(column=>column.Id=="18").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("bill.parent.name")};
        catalog.ConfigColumns.Single(column=>column.Id=="110").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("bill.amount")};
        catalog.ConfigColumns.Single(column=>column.Id=="19").ControlKind="Title";catalog.ConfigColumns.Single(column=>column.Id=="23").LogicalType="string";return catalog;
    }
    /// <summary>统一逻辑类型和主键规则供正式SchemaService建立结构。</summary>
    private static ColumnMetadata Column(string id,string table,string name,string type="string")=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,ColumnLength="100",IsDbNotNull=name=="id"?1:0};
    /// <summary>目标ID是关系声明，自动显示字段保持原nameField约定。</summary>
    private static ColumnMetadata Relation(string id,string table,string name,string target){var column=Column(id,table,name,"int64");column.ControlKind="List";column.LinkConfigTableId=target;column.ControlDetail=JsonSerializer.Serialize(new{tableId=target,nameField="name"});return column;}
}
