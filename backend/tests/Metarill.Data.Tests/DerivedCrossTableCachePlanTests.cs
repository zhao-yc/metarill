using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;
/// <summary>R1跨表物理／R0派生层的完整静态闭包及拒绝边界。</summary>
public sealed class DerivedCrossTableCachePlanTests
{
    /// <summary>根、中、末及全部派生保留直接层，编译后不再依赖可变目录。</summary>
    [Theory][InlineData(1,"string")][InlineData(2,"int64")][InlineData(4,"string")][InlineData(7,"int64")]
    public void Derived_levels_form_complete_mixed_graph(int mask,string type)
    {
        var c=Catalog(mask,type);var (direct,derived)=Split(c);var before=JsonSerializer.Serialize(c);var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,direct,derived);Assert.Equal(before,JsonSerializer.Serialize(c));var source=Assert.Single(plan.MixedSourceColumns);Assert.Equal("cross_cache_source",source.Key);Assert.Equal(derived.Length,source.Value.Count);Assert.Contains("cross_dictionary",plan.MixedTargets);Assert.Equal(direct.Length,((IEnumerable)Read(Read(plan,"Propagation"),"Bindings")).Cast<object>().Count());Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,derived));var fingerprint=plan.Fingerprint;c.ConfigColumns.Clear();Assert.Equal(fingerprint,plan.Fingerprint);Assert.Contains("cross_dictionary",plan.MixedTargets);
    }
    /// <summary>R0物理名称仍由直接执行器处理，不粗略合并存储模式。</summary>
    [Fact]public void Physical_r0_name_keeps_direct_plan()
    {
        var c=CrossTableCachePlanTests.Catalog();foreach(var t in c.ConfigTables.Where(t=>t.Id!="source"))t.IsRongyu=0;var defs=CrossTableCachePlanTests.Definitions(c);var plan=RelationMaintenanceCatalog.Compile(c,defs);Assert.Empty(plan.MixedSourceColumns);Assert.Equal(3,((IEnumerable)Read(Read(plan,"Propagation"),"Bindings")).Cast<object>().Count());
    }
    /// <summary>缺层、错字段、跨项目、凭据、数组和坏同伴均不能发布部分计划。</summary>
    [Theory][InlineData("control")][InlineData("foreign")][InlineData("cache")][InlineData("global")][InlineData("project")][InlineData("virtual")][InlineData("credential")][InlineData("array")][InlineData("stringPath")][InlineData("missingProject")][InlineData("badPeer")][InlineData("conflict")]
    public void Invalid_derived_definition_rejects_before_connection(string kind)
    {
        var c=Catalog(1,"string");var (direct,derived)=Split(c);var d=derived[0];if(kind=="control")derived[0]=d with{CrossTableColumnId=null};if(kind=="foreign")derived[0]=d with{ForeignColumnId=direct[0].ForeignColumnId};if(kind=="cache")derived[0]=d with{CacheColumnId=direct[0].CacheColumnId};if(kind=="global")c.ConfigTables.Single(x=>x.Id=="country").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="project")c.ConfigTables.Single(x=>x.Id=="country").YzProjectId="999";if(kind=="virtual")c.ConfigColumns.Single(x=>x.Id=="country_label").IsVirtualColumn=1;if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="dictionary_name").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="country_label").Extra["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.password");}if(kind=="array")c.ConfigColumns.Single(x=>x.Id=="dictionary_name").ControlKind="FileLibrary";if(kind=="stringPath")c.ConfigColumns.Single(x=>x.Id=="country_dictionary").LogicalType="string";if(kind=="missingProject")c.ConfigColumns.RemoveAll(x=>x.Id=="dictionary_project");if(kind=="badPeer")c.ConfigColumns.Add(new(){Id="bad",ConfigTableId="source",ColumnName="bad_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("missing.name")}});if(kind=="conflict")direct=[..direct,d];Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,direct,derived));
    }
    /// <summary>两套各自无环的执行器合起来成环时仍拒绝，不能靠队列重复重算。</summary>
    [Fact]public void Combined_physical_and_mixed_cycle_rejects()
    {
        var c=Catalog(1,"int64");var (direct,derived)=Split(c);c.ConfigColumns.Single(x=>x.Id=="dictionary_name").ColumnName="source_country_name";c.ConfigColumns.Single(x=>x.Id=="country_label").Extra["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.source_country_name");c.ConfigColumns.Add(new(){Id="dict_back",ConfigTableId="dictionary",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="source"});direct=[..direct,new("dictionary","dict_back","source",derived[0].CacheColumnId,"dictionary_name",false)];Assert.NotNull(RelationMutationPlan.Compile(c,direct));Assert.Contains("循环",Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,direct,derived)).Message);
    }
    /// <summary>同一三层模型按掩码改为R0，其名称经int64单值关系落到真实字典。</summary>
    internal static MetadataCatalog Catalog(int mask,string type)
    {
        var c=CrossTableCachePlanTests.Catalog(type,"caption");c.ConfigTables.Add(new(){Id="dictionary",YzProjectId=c.YzProjectId,TableName="cross_dictionary",IsRongyu=1});foreach(var (id,name,logical) in new[]{("dictionary_id","id","int64"),("dictionary_project","yz_project_id","int64"),("dictionary_deleted","delete_no","datetime"),("dictionary_name","name","string")})c.ConfigColumns.Add(new(){Id=id,ConfigTableId="dictionary",ColumnName=name,LogicalType=logical,ColumnLength="100",IsDbNotNull=name=="id"?1:0});for(var i=0;i<3;i++)if((mask&(1<<i))!=0){var key=new[]{"country","area","place"}[i];c.ConfigTables.Single(t=>t.Id==key).IsRongyu=0;c.ConfigColumns.Add(new(){Id=key+"_dictionary",ConfigTableId=key,ColumnName="dictionary_id",LogicalType="int64",LinkConfigTableId="dictionary"});var name=c.ConfigColumns.Single(x=>x.Id==key+"_label");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.name");}return c;
    }
    /// <summary>稳定ID不变，按逻辑存储分流；普通六参数及JSON格式保持。</summary>
    internal static (RelationCacheDefinition[] Direct,RelationCacheDefinition[] Derived) Split(MetadataCatalog c)
    {
        var definitions=CrossTableCachePlanTests.Definitions(c);bool Derived(RelationCacheDefinition d){var table=c.ConfigTables.Single(t=>t.Id==d.TargetTableId);return !MetadataStoragePolicy.ForTable(c,table).IsPhysical(c.ConfigColumns.Single(x=>x.Id==d.ValueColumnId));}return(definitions.Where(d=>!Derived(d)).ToArray(),definitions.Where(Derived).ToArray());
    }
    /// <summary>测试只读内部计划，不增加HTTP或客户端改写入口。</summary>
    private static object Read(object value,string property)=>value.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(value)!;
}
