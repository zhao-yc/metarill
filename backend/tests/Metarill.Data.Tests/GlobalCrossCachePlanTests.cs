using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>固定跨表全局层必须和控制列、目标页共同发布；来源及普通层不能跟随全局取消项目范围。</summary>
public sealed class GlobalCrossCachePlanTests
{
    /// <summary>根、中、末及整链全局，两种精确ID表示、有无目标项目列均编译传播与原维护。</summary>
    [Theory][InlineData(1,"string",false)][InlineData(2,"string",true)][InlineData(4,"int64",false)][InlineData(7,"int64",true)]
    public void Global_cross_propagation_and_maintenance_keep_source_project(int mask,string type,bool noProject)
    {
        var c=Catalog(mask,type,noProject);var defs=Definitions(c);var before=JsonSerializer.Serialize(c);var mutation=RelationMutationPlan.Compile(c,defs);var bindings=((IEnumerable)Read(mutation,"Bindings")!).Cast<object>().ToArray();Assert.Equal(3,bindings.Length);Assert.All(bindings,b=>Assert.Equal("int64",Read(Read(b,"Source")!,"ProjectType")));var maintenance=RelationMaintenanceCatalog.Compile(c,defs);var table=Table(maintenance);Assert.Null(Read(table,"Error"));var read=Read(table,"Read")!;Assert.Contains("b.`yz_project_id`=@project_scope_number",(string)Read(read,"Scope")!);Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Equal(3,bindings.Select(x=>Read(x,"Cache")).Distinct().Count());
        foreach(var binding in bindings){var name=(string)Read(Read(binding,"Target")!,"Name")!;var global=c.ConfigTables.Single(t=>t.TableName==name).Extra.GetValueOrDefault("scopeKind").ToString()=="global";Assert.Equal(global?"":"int64",Read(Read(binding,"Target")!,"ProjectType"));}
    }
    /// <summary>缺页、跨项目页、未发布来源、变动上下文、派生名称及断父链都在触库前拒绝。</summary>
    [Theory][InlineData("pageMissing")][InlineData("pageForeign")][InlineData("pageTable")][InlineData("pageVariable")][InlineData("targetUnpublished")][InlineData("sourceUnpublished")][InlineData("sourceGlobal")][InlineData("targetR0")][InlineData("cachedName")][InlineData("wrongParent")][InlineData("missingGlobal")][InlineData("ordinaryPage")][InlineData("pageConflict")][InlineData("otherControl")]
    public void Invalid_global_cross_proof_rejects_entire_chain(string kind)
    {
        var c=Catalog(1,"string",true);var d=Definitions(c);var page=c.ConfigPages.Single(x=>x.Id=="country_page");if(kind=="pageMissing")d[0]=d[0] with{CrossTablePageId=null};if(kind=="pageForeign")page.YzProjectId="999";if(kind=="pageTable")page.ConfigTableId="area";if(kind=="pageVariable")page.WhereSql="id=@parent_id";if(kind=="targetUnpublished")c.ConfigTables.Single(x=>x.Id=="country").YzProjectId="";if(kind=="sourceUnpublished")c.ConfigTables.Single(x=>x.Id=="source").YzProjectId="";if(kind=="sourceGlobal")c.ConfigTables.Single(x=>x.Id=="source").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="targetR0")c.ConfigTables.Single(x=>x.Id=="country").IsRongyu=0;if(kind=="cachedName")c.ConfigColumns.Single(x=>x.Id=="country_label").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="wrongParent")c.ConfigColumns.Single(x=>x.Id=="area_parent").LinkConfigTableId="place";if(kind=="missingGlobal")c.ConfigTables.Single(x=>x.Id=="country").Extra.Clear();if(kind=="ordinaryPage")d[1]=d[1] with{CrossTablePageId="area_page"};if(kind=="pageConflict")d=[..d,d[0] with{CrossTablePageId="area_page"}];if(kind=="otherControl")d[0]=d[0] with{CrossTableColumnId="unknown"};Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,d));
    }
    /// <summary>只有全局层增加非空页绑定，普通六参数定义和非全局跨表形状不变。</summary>
    [Fact]public void Global_page_extension_roundtrips_without_changing_plain_definition()
    {
        var plain=new RelationCacheDefinition("s","f","t","n","c",false);Assert.DoesNotContain("CrossTable",JsonSerializer.Serialize(plain));var c=Catalog(1,"int64",false);var global=Definitions(c)[0];Assert.Equal(global,JsonSerializer.Deserialize<RelationCacheDefinition>(JsonSerializer.Serialize(global)));Assert.Equal("country_page",global.CrossTablePageId);
    }
    /// <summary>完整三层仅增加发布页及明确全局标志，不手写标准实体或DDL。</summary>
    internal static MetadataCatalog Catalog(int mask,string type,bool noProject)
    {
        var c=CrossTableCachePlanTests.Catalog(type,"caption");for(var i=0;i<3;i++){var key=new[]{"country","area","place"}[i];var table=c.ConfigTables.Single(x=>x.Id==key);if((mask&(1<<i))!=0){table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(noProject)c.ConfigColumns.RemoveAll(x=>x.ConfigTableId==key&&x.ColumnName=="yz_project_id");}c.ConfigPages.Add(new(){Id=key+"_page",YzProjectId=c.YzProjectId,ConfigTableId=key,WhereSql="caption IS NOT NULL"});}return c;
    }
    /// <summary>精确目标页仅绑定同一控制的全局层，不从请求读取。</summary>
    internal static RelationCacheDefinition[] Definitions(MetadataCatalog c)=>CrossTableCachePlanTests.Definitions(c).Select(x=>GlobalCrossTableTarget.Declared(c.ConfigTables.Single(t=>t.Id==x.TargetTableId))?x with{CrossTablePageId=x.TargetTableId+"_page"}:x).ToArray();
    /// <summary>只读观察已编译整表计划。</summary>
    private static object Table(RelationMaintenanceCatalog c)=>((IEnumerable)Read(c,"Plans")!).Cast<object>().Where(x=>(string)Read(x,"Key")! =="source").Select(x=>Read(x,"Value")!).Single();
    /// <summary>测试不公开生产计划内部状态。</summary>
    private static object? Read(object value,string property)=>value.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(value);
}
