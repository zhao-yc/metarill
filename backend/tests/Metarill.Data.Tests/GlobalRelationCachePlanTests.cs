using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>全局只放宽目标别名；自动与原维护共用已证明的R1缓存、来源项目及精确终点。</summary>
public sealed class GlobalRelationCachePlanTests
{
    /// <summary>单值与两种多值均可没有目标项目列，来源和写回范围仍有真实项目。</summary>
    [Theory][InlineData("List",false)][InlineData("List",true)][InlineData("SelectPage",false)][InlineData("SelectPage",true)][InlineData("MultiSelectPage",false)][InlineData("MultiSelectPage",true)][InlineData("MultiCheckBoxDatabase",false)][InlineData("MultiCheckBoxDatabase",true)]
    public void Global_target_cache_and_maintenance_keep_source_scope(string kind,bool noProject)
    {
        var c=Catalog(kind,noProject);var before=JsonSerializer.Serialize(c);var definition=Definition(kind);var plan=RelationMutationPlan.Compile(c,[definition]);var binding=((IEnumerable)Field(plan,"Bindings")).Cast<object>().Single();Assert.Equal("int64",Field(Field(binding,"Source"),"ProjectType"));Assert.Equal("",Field(Field(binding,"Target"),"ProjectType"));var maintenance=RelationMaintenanceCatalog.Compile(c,[definition]);var source=Plan(maintenance,"1");Assert.Null(NullableField(source,"Error"));var read=Field(source,"Read");Assert.Contains("b.`yz_project_id`=@project_scope_number",(string)Field(read,"Scope"));if(kind is "List" or "SelectPage")Assert.DoesNotContain("r0.`yz_project_id`",(string)Field(read,"From"));else{var expressions=(IReadOnlyDictionary<string,string>)Field(read,"Expressions");Assert.DoesNotContain("multi_value.`yz_project_id`",expressions["shared_names"]);Assert.Contains("multi_value.`delete_no` IS NULL",expressions["shared_names"]);}Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>R0中间节点继续项目隔离，其派生字符串可作为R1物理缓存的全局终点路径。</summary>
    [Fact]public void Local_nonredundant_intermediate_keeps_global_terminal_and_reverse_paths()
    {
        var c=Catalog("List",true);c.ConfigTables.Add(new(){Id="3",YzProjectId=c.YzProjectId,TableName="global_cache_middle",IsRongyu=0});foreach(var(id,name,type,key)in new[]{("31","id","int64",true),("32","yz_project_id","int64",false),("33","delete_no","datetime",false)})c.ConfigColumns.Add(Column(id,"3",name,type,key));c.ConfigColumns.Add(new(){Id="34",YzProjectId=c.YzProjectId,ConfigTableId="3",ColumnName="shared_id",LogicalType="int64",LinkConfigTableId="2",ControlKind="List",ControlDetail="{\"tableId\":\"2\",\"pageId\":\"21\",\"nameField\":\"name\"}"});c.ConfigColumns.Add(new(){Id="35",ConfigTableId="3",YzProjectId=c.YzProjectId,ColumnName="label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("shared.name")}});var foreign=c.ConfigColumns.Single(x=>x.Id=="15");foreign.ColumnName="middle_id";foreign.LinkConfigTableId="3";foreign.ControlDetail="{\"tableId\":\"3\",\"nameField\":\"label\"}";var cache=c.ConfigColumns.Single(x=>x.Id=="16");cache.ColumnName="middle_label";cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("middle.label");var plan=RelationMaintenanceCatalog.Compile(c);Assert.Null(NullableField(Plan(plan,"1"),"Error"));Assert.Contains("global_cache_shared",plan.MixedTargets);Assert.Contains("global_cache_middle",plan.MixedTargets);Assert.Equal(new[]{"middle_label"},plan.MixedSourceColumns["global_cache_source"]);
    }
    /// <summary>全局消费者、未发布目标、缓存／凭据名称与表页冲突不能发布可执行传播计划。</summary>
    [Theory][InlineData("sourceGlobal")][InlineData("sourceR0")][InlineData("sourceProject")][InlineData("targetR0")][InlineData("targetUnpublished")][InlineData("pageUnpublished")][InlineData("pageVariable")][InlineData("targetCache")][InlineData("nameCredential")][InlineData("valueCredential")][InlineData("differentPage")][InlineData("differentTable")]
    public void Unproven_global_cache_rejects_before_database(string kind)
    {
        var c=Catalog("List",false);var source=c.ConfigTables[0];var target=c.ConfigTables[1];var foreign=c.ConfigColumns.Single(x=>x.Id=="15");var value=c.ConfigColumns.Single(x=>x.Id=="22");var cache=c.ConfigColumns.Single(x=>x.Id=="16");var definition=Definition("List");if(kind=="sourceGlobal")source.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="sourceR0")source.IsRongyu=0;if(kind=="sourceProject")c.ConfigColumns.RemoveAll(x=>x.Id=="12");if(kind=="targetR0")target.IsRongyu=0;if(kind=="targetUnpublished")target.YzProjectId="";if(kind=="pageUnpublished")c.ConfigPages[0].YzProjectId="";if(kind=="pageVariable")c.ConfigPages[0].WhereSql="yz_project_id=@current_yz_project_id";if(kind=="targetCache")value.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="nameCredential"){value.ColumnName="password";cache.ColumnName="shared_password";cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("shared.password");foreign.ControlDetail=foreign.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"password\"",StringComparison.Ordinal);}if(kind=="valueCredential"){c.ConfigColumns.Add(Column("29","2","password","string"));definition=definition with{ValueColumnId="29"};cache.ColumnName="shared_password";cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("shared.password");}if(kind=="differentPage")foreign.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("missing");if(kind=="differentTable")foreign.LinkConfigTableId="1";Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[definition]));
    }
    /// <summary>标量与数组均只使用元数据稳定字段ID，不能由请求替换SQL。</summary>
    internal static RelationCacheDefinition Definition(string kind)=>new("1","15","2","22","16",kind is "MultiSelectPage" or "MultiCheckBoxDatabase");
    /// <summary>合成完整目录；目标共享声明不改变来源或目标页的真实发布归属。</summary>
    internal static MetadataCatalog Catalog(string kind,bool noProject)
    {
        const string project="9007199254740993";var multiple=kind is "MultiSelectPage" or "MultiCheckBoxDatabase";var c=new MetadataCatalog{YzProjectId=project,ConfigTables=[new(){Id="1",YzProjectId=project,TableName="global_cache_source",IsRongyu=1},new(){Id="2",YzProjectId=project,TableName="global_cache_shared",IsRongyu=1,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}}]};c.ConfigColumns=[Column("11","1","id","int64",true),Column("12","1","yz_project_id","int64"),Column("13","1","delete_no","datetime"),Column("21","2","id","int64",true),Column("22","2","name","string"),Column("23","2","yz_project_id","int64"),Column("24","2","delete_no","datetime"),Column("25","2","status","int32")];if(noProject)c.ConfigColumns.RemoveAll(x=>x.Id=="23");c.ConfigColumns.Add(new(){Id="15",ConfigTableId="1",YzProjectId=project,ColumnName=multiple?"shared_ids":"shared_id",LogicalType="string",ColumnLength="max",ControlKind=kind,LinkConfigTableId="2",ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="21",nameField="name",whereSql="status=1"})});c.ConfigColumns.Add(new(){Id="16",YzProjectId=project,ConfigTableId="1",ColumnName=multiple?"shared_names":"shared_name",LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("shared.name")}});c.ConfigPages=[new(){Id="21",ConfigTableId="2",YzProjectId=project,WhereSql="status=1",Extra=new(){["type"]=JsonSerializer.SerializeToElement(kind=="MultiSelectPage"?2:kind=="SelectPage"?1:0)}}];return c;
    }
    /// <summary>公共物理列显式声明来源，不依赖缺列推断共享。</summary>
    private static ColumnMetadata Column(string id,string table,string name,string type,bool key=false)=>new(){Id=id,YzProjectId="9007199254740993",ConfigTableId=table,ColumnName=name,LogicalType=type,ColumnLength=type=="string"?"max":"",IsDbNotNull=key?1:0};
    /// <summary>只读观察已编译私有计划，不调用数据库或放宽生产可见性。</summary>
    private static object Plan(RelationMaintenanceCatalog plan,string id)=>((IEnumerable)Field(plan,"Plans")).Cast<object>().Where(x=>(string)Field(x,"Key")==id).Select(x=>Field(x,"Value")).Single();
    private static object Field(object target,string name)=>NullableField(target,name)??throw new InvalidOperationException("测试计划缺少字段："+name);
    private static object? NullableField(object target,string name)=>target.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target);
}
