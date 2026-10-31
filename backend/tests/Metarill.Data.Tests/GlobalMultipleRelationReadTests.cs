using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>全局多值名称仅改变目标范围，来源项目与普通目标范围必须保留。</summary>
public sealed class GlobalMultipleRelationReadTests
{
    /// <summary>两种原数组控件使用精确ID，保留顺序、null位置和分页前名称筛选。</summary>
    [Theory][InlineData("MultiSelectPage",false)][InlineData("MultiSelectPage",true)][InlineData("MultiCheckBoxDatabase",false)][InlineData("MultiCheckBoxDatabase",true)]
    public void Global_array_plan_keeps_tenant_source_and_optional_target_project(string kind,bool noProject)
    {
        var c=Catalog(kind,noProject);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.Compile(c,"1",["shared_names","tenant_names"]);var expressions=Property<IReadOnlyDictionary<string,string>>(plan,"Expressions");var sql=expressions["shared_names"];Assert.DoesNotContain("multi_value.`yz_project_id`",sql);Assert.Contains("multi_value.`delete_no` IS NULL",sql);Assert.Contains("AS BINARY",sql);Assert.Contains("ORDER BY multi_ids.ordinal",sql);Assert.Contains("multi_value.`yz_project_id`=@project_scope_number",expressions["tenant_names"]);Assert.Contains("b.`yz_project_id`=@project_scope_number",Property<string>(plan,"Scope"));Assert.Contains("LIKE {parameter}",Property<IReadOnlyDictionary<string,string>>(plan,"ArrayNamePredicates")["shared_names"]);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>原四段复选可通过明确页绑定使用全局目标，不把目标页条件替代原候选条件。</summary>
    [Fact]public void Legacy_checkbox_definition_keeps_explicit_global_binding()
    {
        var c=Catalog("MultiCheckBoxDatabase",true);var source=c.ConfigColumns.Single(x=>x.Id=="15");source.ControlDetail="旧第一段;旧第二段;status=1;name desc";source.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("21");Assert.Contains("JSON_ARRAYAGG",Property<IReadOnlyDictionary<string,string>>(MySqlNonRedundantReadCompiler.Compile(c,"1",["shared_names"]),"Expressions")["shared_names"]);
    }
    /// <summary>未发布、冲突声明、缓存／凭据名称与全局来源均不能得到SQL计划。</summary>
    [Theory][InlineData("ownerGlobal")][InlineData("ownerUnpublished")][InlineData("noSourceProject")][InlineData("targetR0")][InlineData("targetUnpublished")][InlineData("pageUnpublished")][InlineData("pageVariable")][InlineData("conflictTable")][InlineData("conflictPage")][InlineData("duplicate")][InlineData("ambiguous")][InlineData("cachedName")][InlineData("credentialName")][InlineData("arrayName")][InlineData("viewTarget")]
    public void Unproven_global_array_rejects_before_connection(string kind)
    {
        var c=Catalog("MultiSelectPage",false);var source=c.ConfigColumns.Single(x=>x.Id=="15");var target=c.ConfigTables.Single(x=>x.Id=="2");var page=c.ConfigPages.Single(x=>x.Id=="21");var name=c.ConfigColumns.Single(x=>x.Id=="22");
        if(kind=="ownerGlobal")c.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="ownerUnpublished")c.ConfigTables[0].YzProjectId="";if(kind=="noSourceProject")c.ConfigColumns.RemoveAll(x=>x.Id=="12");if(kind=="targetR0")target.IsRongyu=0;if(kind=="targetUnpublished")target.YzProjectId="";if(kind=="pageUnpublished")page.YzProjectId="";if(kind=="pageVariable")page.WhereSql="yz_project_id=@current_yz_project_id";if(kind=="conflictTable")source.LinkConfigTableId="3";if(kind=="conflictPage")source.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("31");if(kind=="duplicate")source.ControlDetail="{\"tableId\":\"2\",\"tableId\":\"3\"}";if(kind=="ambiguous"){source.ControlDetail="{\"tableId\":\"2\"}";c.ConfigPages.Add(new(){Id="22",YzProjectId=c.YzProjectId,ConfigTableId="2"});}if(kind=="cachedName")name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="credentialName"){name.ColumnName="password";source.ControlDetail=source.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"password\"",StringComparison.Ordinal);}if(kind=="arrayName")name.ControlKind="FileLibrary";if(kind=="viewTarget")target.IsView=1;
        Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.Compile(c,"1",["shared_names"]));
    }
    /// <summary>合成目录不写平台配置，普通目标刻意保留项目谓词。</summary>
    private static MetadataCatalog Catalog(string kind,bool noProject)
    {
        var c=new MetadataCatalog{YzProjectId="9007199254740993"};foreach(var(id,name,mode)in new[]{("1","source",0),("2","shared",1),("3","tenant",1)})c.ConfigTables.Add(new(){Id=id,YzProjectId=c.YzProjectId,TableName="global_multi_read_"+name,IsRongyu=mode});c.ConfigTables[1].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        c.ConfigColumns=[Col("11","1","id","int64",true),Col("12","1","yz_project_id","int64"),Col("13","1","delete_no","datetime"),Col("21","2","id","int64",true),Col("22","2","name","string"),Col("23","2","yz_project_id","int64"),Col("24","2","delete_no","datetime"),Col("25","2","status","int32"),Col("31","3","id","int64",true),Col("32","3","name","string"),Col("33","3","yz_project_id","int64"),Col("34","3","delete_no","datetime")];if(noProject)c.ConfigColumns.RemoveAll(x=>x.Id=="23");foreach(var(id,target,prefix)in new[]{("15","2","shared"),("16","3","tenant")}){var col=Col(id,"1",prefix+"_ids","string");col.ControlKind=kind;col.LinkConfigTableId=target;col.ControlDetail=JsonSerializer.Serialize(new{tableId=target,pageId=target=="2"?"21":"31",nameField="name",whereSql="status=1"});c.ConfigColumns.Add(col);}c.ConfigPages=[new(){Id="21",YzProjectId=c.YzProjectId,ConfigTableId="2",WhereSql="status=1",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}},new(){Id="31",YzProjectId=c.YzProjectId,ConfigTableId="3",WhereSql="yz_project_id=@current_yz_project_id"}];return c;
    }
    /// <summary>只用于检查已编译SQL结构，不连接数据库。</summary>
    private static T Property<T>(MySqlNonRedundantReadPlan plan,string name)=>(T)typeof(MySqlNonRedundantReadPlan).GetProperty(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plan)!;
    /// <summary>物理列必须显式声明，不依赖缺失字段推断全局。</summary>
    private static ColumnMetadata Col(string id,string table,string name,string type,bool key=false)=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,ColumnLength="max",IsDbNotNull=key?1:0};
}
