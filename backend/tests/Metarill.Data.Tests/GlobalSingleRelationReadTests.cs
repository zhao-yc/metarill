using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>单值全局别名仍由来源项目约束，只读取明确发布的非凭据物理值。</summary>
public sealed class GlobalSingleRelationReadTests
{
    /// <summary>字符串键使用二进制精确比较；可选目标项目列不改变共享范围或普通目标范围。</summary>
    [Theory][InlineData("string",false)][InlineData("string",true)][InlineData("int64",false)][InlineData("int64",true)]
    public void Global_scalar_join_keeps_source_and_other_target_scopes(string type,bool noProject)
    {
        var c=Catalog(type,noProject);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.Compile(c,"1",["shared_caption","shared_score","tenant_name"]);var sql=From(plan);Assert.DoesNotContain("r0.`yz_project_id`",sql);Assert.Contains("r0.`delete_no` IS NULL",sql);Assert.Contains("r1.`yz_project_id`=@project_scope_number",sql);if(type=="string")Assert.Contains("BINARY CAST(r0.`id` AS CHAR)=BINARY b.`shared_id`",sql);Assert.Equal("decimal",NonRedundantReads.Compile(c,"1",["shared_score"]).OutputTypes["shared_score"]);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>原项目中间节点可联到全局物理终点，普通读取和固定匹配都保留中间项目范围。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Nested_nonredundant_path_can_end_at_global_physical_value(bool noProject)
    {
        var c=Catalog("int64",noProject);c.ConfigTables.Add(new(){Id="4",YzProjectId=c.YzProjectId,TableName="single_read_mid",IsRongyu=0});c.ConfigColumns.AddRange([Col("41","4","id","int64",true),Col("42","4","yz_project_id","int64"),Col("43","4","delete_no","datetime"),new(){Id="44",YzProjectId=c.YzProjectId,ConfigTableId="4",ColumnName="shared_id",LogicalType="int64",LinkConfigTableId="2",ControlKind="List",ControlDetail="{\"tableId\":\"2\",\"nameField\":\"caption\",\"pageId\":\"21\"}"},new(){Id="18",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="mid_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"id\"}"},new(){Id="19",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="derived_caption",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("mid.shared.caption")}}]);
        var plan=MySqlNonRedundantReadCompiler.Compile(c,"1",["derived_caption"]);Assert.Contains("r0.`yz_project_id`=@project_scope_number",From(plan));Assert.DoesNotContain("r1.`yz_project_id`",From(plan));Assert.Equal("string",NonRedundantReads.CompileMatch(c,"1","derived_caption").OutputTypes["derived_caption"]);
    }
    /// <summary>未知页面、矛盾目标、非法全局形态与敏感／数组终点不能生成可执行计划。</summary>
    [Theory][InlineData("ownerGlobal")][InlineData("ownerUnpublished")][InlineData("sourceProject")][InlineData("targetR0")][InlineData("unpublishedTarget")][InlineData("unpublishedPage")][InlineData("variable")][InlineData("conflictTable")][InlineData("conflictPage")][InlineData("ambiguous")][InlineData("derivedName")][InlineData("credentialName")][InlineData("credentialOutput")][InlineData("arrayOutput")][InlineData("cachedOutput")]
    public void Unproven_global_paths_reject_before_connection(string kind)
    {
        var c=Catalog("int64",false);var foreign=c.ConfigColumns.Single(x=>x.Id=="15");var target=c.ConfigTables.Single(x=>x.Id=="2");var page=c.ConfigPages.Single(x=>x.Id=="21");var output="shared_caption";
        if(kind=="ownerGlobal")c.ConfigTables.Single(x=>x.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="ownerUnpublished")c.ConfigTables.Single(x=>x.Id=="1").YzProjectId="";if(kind=="sourceProject")c.ConfigColumns.RemoveAll(x=>x.Id=="12");if(kind=="targetR0")target.IsRongyu=0;if(kind=="unpublishedTarget")target.YzProjectId="";if(kind=="unpublishedPage")page.YzProjectId="";if(kind=="variable")page.WhereSql="yz_project_id=@current_yz_project_id";if(kind=="conflictTable")foreign.LinkConfigTableId="3";if(kind=="conflictPage")foreign.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("31");if(kind=="ambiguous"){foreign.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"caption\"}";c.ConfigPages.Add(new(){Id="22",YzProjectId=c.YzProjectId,ConfigTableId="2"});}if(kind=="derivedName")c.ConfigColumns.Single(x=>x.Id=="22").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="credentialName"){foreign.ControlDetail=foreign.ControlDetail.Replace("caption","password",StringComparison.Ordinal);output="shared_password";}if(kind=="credentialOutput"){c.ConfigColumns.Single(x=>x.Id=="16").Extra["columnFullName"]=JsonSerializer.SerializeToElement("shared.password");output="shared_score";}if(kind=="arrayOutput"){c.ConfigColumns.Single(x=>x.Id=="25").ControlKind="FileLibrary";c.ConfigColumns.Single(x=>x.Id=="25").LogicalType="string";output="shared_score";}
        if(kind=="cachedOutput"){c.ConfigColumns.Single(x=>x.Id=="25").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);output="shared_score";}
        Assert.ThrowsAny<Exception>(()=>NonRedundantReads.Compile(c,"1",[output]));
    }
    /// <summary>构造完整静态目录，普通目标与全局目标故意保留相同主键形态。</summary>
    private static MetadataCatalog Catalog(string type,bool noProject)
    {
        var c=new MetadataCatalog{YzProjectId="9007199254740993"};foreach(var(id,name,mode)in new[]{("1","source",0),("2","shared",1),("3","tenant",1)})c.ConfigTables.Add(new(){Id=id,YzProjectId=c.YzProjectId,TableName="single_read_"+name,IsRongyu=mode});c.ConfigTables[1].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        c.ConfigColumns=[Col("11","1","id","int64",true),Col("12","1","yz_project_id","int64"),Col("13","1","delete_no","datetime"),Col("14","1","name","string"),new(){Id="15",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="shared_id",LogicalType=type,ControlKind="List",LinkConfigTableId="2",ControlDetail="{\"tableId\":\"2\",\"pageId\":\"21\",\"nameField\":\"caption\"}"},new(){Id="16",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="shared_score",LogicalType="decimal",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("shared.score")}},new(){Id="17",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="tenant_id",LogicalType="int64",LinkConfigTableId="3",ControlKind="List",ControlDetail="{\"tableId\":\"3\",\"nameField\":\"name\"}"},Col("21","2","id","int64",true),Col("22","2","caption","string"),Col("23","2","yz_project_id","int64"),Col("24","2","delete_no","datetime"),Col("25","2","score","decimal"),Col("26","2","status","int32"),Col("27","2","password","string"),Col("31","3","id","int64",true),Col("32","3","name","string"),Col("33","3","yz_project_id","int64"),Col("34","3","delete_no","datetime")];if(noProject)c.ConfigColumns.RemoveAll(x=>x.Id=="23");c.ConfigPages=[new(){Id="21",ConfigTableId="2",YzProjectId=c.YzProjectId,WhereSql="status=1"},new(){Id="31",ConfigTableId="3",YzProjectId=c.YzProjectId,WhereSql="yz_project_id=@current_yz_project_id"}];return c;
    }
    /// <summary>只暴露SQL结构用于范围断言，不连接数据库。</summary>
    private static string From(MySqlNonRedundantReadPlan plan)=>(string)typeof(MySqlNonRedundantReadPlan).GetProperty("From",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plan)!;
    /// <summary>参数化测试元数据的公共物理列。</summary>
    private static ColumnMetadata Col(string id,string table,string name,string type,bool key=false)=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,IsDbNotNull=key?1:0,ColumnLength="100"};
}
