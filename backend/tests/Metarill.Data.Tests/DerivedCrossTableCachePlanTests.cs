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
    /// <summary>项目内派生名称刷新依赖真实目标键和字典，反查同时消费写前键与当前键。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Text_business_key_reverses_and_backfills_through_the_published_dictionary(bool global)
    {
        var catalog=Catalog(1,"string");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="control");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
        definition["levels"]![0]!["idField"]="business_key";
        control.ControlDetail=definition.ToJsonString();
        catalog.ConfigColumns.Add(new(){Id="country_key",ConfigTableId="country",ColumnName="business_key",
            LogicalType="string",ColumnLength="50",IsDbNotNull=1});
        catalog.ConfigColumns.Add(new(){Id="country_status",ConfigTableId="country",ColumnName="status",LogicalType="int32"});
        catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="country_unique",configTableId="country",isUnique=1}));
        if(!global)catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{configIndexId="country_unique",configColumnId="country_project",sortNo=1}));
        catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{configIndexId="country_unique",configColumnId="country_key",sortNo=global?1:2}));
        catalog.ConfigPages.Add(new(){Id="country_list",ConfigTableId="country",
            WhereSql=global?"status=1":"yz_project_id=@current_yz_project_id AND status=1"});
        catalog.ConfigPageColumns.Add(new(){Id="country_list_caption",ConfigPageId="country_list",ConfigColumnId="country_label",IsShow=1});

        if(global)
        {
            catalog.YzAdminTypes.Add(new(){Id="8",YzProjectId=catalog.YzProjectId});
            foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "country" or "dictionary"))
                table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            var page=catalog.ConfigPages.Single(item=>item.Id=="country_list");
            page.YzAdminTypeId="8";
            page.YzProjectId=catalog.YzProjectId;
            catalog.ConfigPages.Add(new(){Id="dictionary_list",ConfigTableId="dictionary",YzAdminTypeId="8",YzProjectId=catalog.YzProjectId});
            catalog.ConfigPageColumns.Add(new(){Id="dictionary_caption",ConfigPageId="dictionary_list",ConfigColumnId="dictionary_name",IsShow=1});
            var foreign=catalog.ConfigColumns.Single(column=>column.Id=="country_dictionary");
            foreign.ControlKind="List";
            foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="dictionary",pageId="dictionary_list",nameField="name"});
        }
        var (direct,derived)=Split(catalog);
        if(global)
        {
            var pages=new Dictionary<string,string>{{"country","country_list"}};
            direct=direct.Select(binding=>binding with {CrossTableGlobalPages=pages}).ToArray();
            derived=derived.Select(binding=>binding with {CrossTableGlobalPages=pages,CrossTablePageId="country_list"}).ToArray();
        }
        var catalogPlan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,direct,derived);
        var read=catalogPlan.Plans["source"].Read;
        Assert.NotNull(read);
        Assert.Matches(@"BINARY r\d+\.\x60business_key\x60=BINARY b\.\x60country_id\x60",read.From);
        Assert.Matches(@"r\d+\.\x60status\x60=@[A-Za-z0-9_]+",read.From);
        Assert.Contains("LEFT JOIN `cross_dictionary`",read.From);
        var consumer=Assert.Single(catalogPlan.Mixed.Consumers);
        var reverse=Assert.Single(consumer.Read.Reverse,entry=>entry.TargetTable=="cross_cache_country");
        Assert.Equal("business_key",reverse.NodeKey!.Column);
        Assert.Equal("string",reverse.NodeKey.LogicalType);
        Assert.Equal(global?"":"int64",reverse.NodeKey.ProjectType);
        Assert.Contains("BINARY b.`country_id`=BINARY @target_node_current",reverse.TargetPredicate);
        Assert.Contains("BINARY b.`country_id`=BINARY @target_node_previous",reverse.TargetPredicate);
        Assert.Contains("status",reverse.Outputs["country_name"]);
        Assert.Contains("business_key",reverse.Outputs["country_name"]);
        Assert.Contains(consumer.Read.Reverse,entry=>entry.TargetTable=="cross_dictionary"&&entry.Outputs["country_name"].Contains("name"));
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
