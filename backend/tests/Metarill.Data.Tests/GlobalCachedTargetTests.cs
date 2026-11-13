using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>全局名称缓存不能仅靠isJoin标记取得读取资格，必须证明全局物理来源与完整的固定路径。</summary>
public sealed class GlobalCachedTargetTests
{
    /// <summary>普通来源的全局目标缓存名称由上游同目录全局物理字典确定，不增加新的跨项目数据来源。</summary>
    [Theory][InlineData(0,"int64","int64")][InlineData(0,"string","int64")][InlineData(0,"int64","string")][InlineData(0,"string","string")]
    [InlineData(1,"int64","int64")][InlineData(1,"string","int64")][InlineData(1,"int64","string")][InlineData(1,"string","string")]
    public void Valid_global_cached_target_has_recursive_name_proof(int mode,string sourceKey,string innerKey)
    {
        var c=Catalog(mode,sourceKey,innerKey);var before=JsonSerializer.Serialize(c);var owner=c.ConfigTables.Single(table=>table.Id=="3");var foreign=c.ConfigColumns.Single(column=>column.Id=="34");var target=c.ConfigTables.Single(table=>table.Id=="1");
        var proof=mode==0?GlobalSingleRelationTarget.Require(c,owner,foreign,target):GlobalSingleRelationTarget.RequireCached(c,owner,foreign,target);
        Assert.Equal("14",proof.Name.Id);Assert.Equal("10",proof.Page.Id);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>非冗余普通读取依旧约束来源项目，目标缓存只按固定全局别名读物理值，不扩大根行基数。</summary>
    [Theory][InlineData("int64","int64")][InlineData("string","int64")][InlineData("int64","string")][InlineData("string","string")]
    public void Valid_global_cached_target_read_keeps_source_scope_and_exact_foreign_key(string sourceKey,string innerKey)
    {
        var c=Catalog(0,sourceKey,innerKey);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["id","global_source_global_target_name"]);var sql=(string)Property(plan,"From");
        Assert.Contains("LEFT JOIN `global_source` r0",sql);Assert.Contains("r0.`delete_no` IS NULL",sql);Assert.DoesNotContain("r0.`yz_project_id`",sql);Assert.DoesNotContain("JOIN `global_target`",sql);
        Assert.Contains("b.`yz_project_id`=@project_scope_number",(string)Property(plan,"Scope"));if(sourceKey=="string")Assert.Contains("BINARY CAST(r0.`id` AS CHAR)=BINARY b.`global_source_id`",sql);
        Assert.Equal("string",NonRedundantReads.Compile(c,"3",["global_source_global_target_name"]).OutputTypes["global_source_global_target_name"]);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>既有精确单值用途沿真实int64来源键读取字符串终点，不把内层字符串外键当作运行期比较表达式。</summary>
    [Theory][InlineData("int64")][InlineData("string")]
    public void Cached_global_name_can_be_compiled_for_fixed_scalar_match(string innerKey)
    {
        var c=Catalog(0,"int64",innerKey);var plan=NonRedundantReads.CompileMatch(c,"3","global_source_global_target_name");Assert.Equal("string",plan.OutputTypes["global_source_global_target_name"]);
    }
    /// <summary>普通冗余消费者的固定传播目标允许已证明的全局名称缓存，来源仍有真实项目而目标不借项目过滤。</summary>
    [Theory][InlineData("int64")][InlineData("string")]
    public void Valid_global_cached_target_enters_existing_redundant_propagation(string type)
    {
        var c=Catalog(1,type,"int64");var plan=RelationMutationPlan.Compile(c,[new("3","34","1","14","35",false)]);var binding=Assert.Single(((IEnumerable)Property(plan,"Bindings")).Cast<object>());
        Assert.Equal("int64",Property(Property(binding,"Source"),"ProjectType"));Assert.Equal("",Property(Property(binding,"Target"),"ProjectType"));Assert.Equal("global_target_name",Property(binding,"Value"));Assert.Equal("global_source_global_target_name",Property(binding,"Cache"));
    }
    /// <summary>非法的缓存中间节点、普通私有终点、凭据或动态全局范围仍在连接之前拒绝。</summary>
    [Theory][InlineData("sourceProject")][InlineData("ordinaryTerminal")][InlineData("terminalVariable")][InlineData("middleVariable")][InlineData("unpublishedTerminal")][InlineData("wrongTarget")][InlineData("wrongPath")][InlineData("credentialTerminal")][InlineData("jsonTerminal")][InlineData("fileTerminal")][InlineData("virtualTerminal")][InlineData("virtualCache")][InlineData("wrongType")][InlineData("multiForeign")][InlineData("selectForeign")][InlineData("cacheCondition")][InlineData("cycle")]
    public void Invalid_global_cache_chain_never_becomes_a_published_read(string kind)
    {
        var c=Catalog(0,"int64","int64");var cache=c.ConfigColumns.Single(column=>column.Id=="14");var inner=c.ConfigColumns.Single(column=>column.Id=="13");var leaf=c.ConfigColumns.Single(column=>column.Id=="22");
        switch(kind)
        {
            case "sourceProject":c.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64",YzProjectId="100"});break;
            case "ordinaryTerminal":c.ConfigTables.Single(table=>table.Id=="2").Extra.Remove("scopeKind");break;
            case "terminalVariable":c.ConfigPages.Single(page=>page.Id=="20").WhereSql="id=@current_yz_project_id";break;
            case "middleVariable":c.ConfigPages.Single(page=>page.Id=="10").WhereSql="id=@current_yz_project_id";break;
            case "unpublishedTerminal":c.ConfigTables.Single(table=>table.Id=="2").YzProjectId="";break;
            case "wrongTarget":inner.LinkConfigTableId="3";break;
            case "wrongPath":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.missing");break;
            case "credentialTerminal":leaf.ColumnName="password";inner.ControlDetail=inner.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"password\"",StringComparison.Ordinal);break;
            case "jsonTerminal":leaf.LogicalType="json";break;
            case "fileTerminal":leaf.ControlKind="FileLibrary";break;
            case "virtualTerminal":leaf.IsVirtualColumn=1;break;
            case "virtualCache":cache.IsVirtualColumn=1;break;
            case "wrongType":cache.LogicalType="int64";break;
            case "multiForeign":inner.ControlKind="MultiSelectPage";break;
            case "selectForeign":inner.ControlKind="SelectPage";break;
            case "cacheCondition":c.ConfigPages.Single(page=>page.Id=="10").WhereSql="global_target_name='未证明条件'";break;
            case "cycle":inner.LinkConfigTableId="1";inner.ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"global_target_name\"}";cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.global_target_name");break;
        }
        var before=JsonSerializer.Serialize(c);Assert.ThrowsAny<Exception>(()=>NonRedundantReads.Compile(c,"3",["global_source_global_target_name"]));Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>递归证明不把物理终点API泛化为派生API，未接线的跨表/多值用途仍能使用原严格检查。</summary>
    [Fact]
    public void Physical_terminal_policy_stays_strict_for_a_valid_global_name_cache()
    {
        var c=Catalog(0,"int64","int64");var target=c.ConfigTables.Single(table=>table.Id=="1");var cache=c.ConfigColumns.Single(column=>column.Id=="14");var storage=MetadataStoragePolicy.ForTable(c,target);
        Assert.False(GlobalPhysicalRelationValue.IsScalar(storage,target,cache));Assert.True(GlobalPhysicalRelationValue.IsPublishedScalar(c,storage,target,cache));
    }
    /// <summary>多层全局缓存保留每层完整发布与精确名称，最多允许16层，超限不能造成栈耗尽。</summary>
    [Theory][InlineData(1,true)][InlineData(2,true)][InlineData(15,true)][InlineData(16,false)]
    public void Recursive_global_cache_has_a_fixed_depth_bound(int added,bool accepted)
    {
        var c=ExpandedCatalog(added);var owner=c.ConfigTables.Single(table=>table.Id=="3");var foreign=c.ConfigColumns.Single(column=>column.Id=="34");var target=c.ConfigTables.Single(table=>table.Id==foreign.LinkConfigTableId);
        if(accepted)Assert.NotNull(GlobalSingleRelationTarget.Require(c,owner,foreign,target));
        else Assert.Contains("16层",Assert.Throws<NotSupportedException>(()=>GlobalSingleRelationTarget.Require(c,owner,foreign,target)).Message,StringComparison.Ordinal);
    }
    /// <summary>每层都声明新的全局物理List来源，不复用表/列ID或跳过上游名称副本。</summary>
    private static MetadataCatalog ExpandedCatalog(int count)
    {
        var c=Catalog(0,"int64","int64");var targetId="1";var targetTable="global_source";var pageId="10";var name="global_target_name";
        for(var index=0;index<count;index++)
        {
            var id=(40+index).ToString(System.Globalization.CultureInfo.InvariantCulture);var page=(4000+index).ToString(System.Globalization.CultureInfo.InvariantCulture);var table="cached_global_"+index.ToString(System.Globalization.CultureInfo.InvariantCulture);var stem=10000+index*10;
            c.ConfigTables.Add(new(){Id=id,YzProjectId=c.YzProjectId,TableName=table,IsRongyu=1,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
            c.ConfigColumns.AddRange([new(){Id=stem.ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=(stem+1).ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="p_id",LogicalType="int64",ControlKind="List",LinkConfigTableId=targetId,ControlDetail=JsonSerializer.Serialize(new{tableId=targetId,table=targetTable,pageId,nameField=name})},new(){Id=(stem+2).ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="p_"+name,LogicalType="string",ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("p."+name)}}]);
            c.ConfigPages.Add(new(){Id=page,YzProjectId=c.YzProjectId,ConfigTableId=id,YzAdminTypeId="9"});targetId=id;targetTable=table;pageId=page;name="p_"+name;
        }
        var foreign=c.ConfigColumns.Single(column=>column.Id=="34");foreign.LinkConfigTableId=targetId;foreign.ControlDetail=JsonSerializer.Serialize(new{tableId=targetId,table=targetTable,pageId,nameField=name});return c;
    }
    /// <summary>最小三表图复用原全局来源夹具，仅新增真实项目消费者，不删除任何范围或名称副本。</summary>
    internal static MetadataCatalog Catalog(int mode,string sourceKey,string innerKey)
    {
        var c=GlobalCachedSourceProofTests.Catalog(innerKey);var cache=c.ConfigColumns.Single(column=>column.Id=="14");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.name");
        c.ConfigTables.Add(new(){Id="3",YzProjectId=c.YzProjectId,TableName="cached_consumer",IsRongyu=mode});
        c.ConfigColumns.AddRange([Col("31","id","int64",true),Col("32","yz_project_id","int64"),Col("33","delete_no","datetime"),new(){Id="34",YzProjectId=c.YzProjectId,ConfigTableId="3",ColumnName="global_source_id",LogicalType=sourceKey,ColumnLength="50",ControlKind="List",LinkConfigTableId="1",ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"global_target_name\"}"},new(){Id="35",YzProjectId=c.YzProjectId,ConfigTableId="3",ColumnName="global_source_global_target_name",LogicalType="string",ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("global_source.global_target_name")}}]);
        c.ConfigPages.Add(new(){Id="30",YzProjectId=c.YzProjectId,ConfigTableId="3",YzAdminTypeId="9"});return c;
        ColumnMetadata Col(string id,string name,string type,bool key=false)=>new(){Id=id,ConfigTableId="3",YzProjectId=c.YzProjectId,ColumnName=name,LogicalType=type,ColumnLength="100",IsDbNotNull=key?1:0};
    }
    /// <summary>只读计划/绑定的实际冻结字段作范围断言，不连接数据库或复制计划计算实现。</summary>
    private static object Property(object source,string name)=>source.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(source)!;
}
