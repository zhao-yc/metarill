using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Metarill.Testing;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>全局名称缓存位于根/中/末时仍保留原稀疏ID、父关系、来源项目和固定页面。</summary>
public sealed class GlobalCrossCachedTargetTests
{
    /// <summary>实际R0跨表SQL读已有物理名称缓存，不额外联接上游字典或取消普通角色范围。</summary>
    [Theory][InlineData(0,"string","string",false)][InlineData(1,"string","int64",false)][InlineData(2,"int64","string",false)][InlineData(0,"int64","int64",true)]
    public void Cached_global_role_compiles_r0_paths_with_original_scopes(int role,string key,string parent,bool allGlobal)
    {
        var c=GlobalCrossCachedTargetFixture.Create(0,role,key,parent,allGlobal);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"3",["country_name","area_name","place_name"],Scopes(c));
        var from=Get<string>(plan,"From");Assert.Contains("LEFT JOIN `cached_global`",from);Assert.DoesNotContain("JOIN `lookup`",from);Assert.Contains("b.`yz_project_id`=@project_scope_number",Get<string>(plan,"Scope"));Assert.Contains("`lookup_name`",Get<IReadOnlyDictionary<string,string>>(plan,"Expressions")[new[]{"country_name","area_name","place_name"}[role]]);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>完整三层定义必须共同进入R1物理传播与原维护，而不是只放宽候选读取。</summary>
    [Theory][InlineData(0,"string","string",false)][InlineData(1,"string","int64",false)][InlineData(2,"int64","string",false)][InlineData(0,"int64","int64",true)]
    public void Cached_global_role_compiles_complete_r1_propagation_and_maintenance(int role,string key,string parent,bool allGlobal)
    {
        var c=GlobalCrossCachedTargetFixture.Create(1,role,key,parent,allGlobal);var defs=Definitions(c);var mutation=RelationMutationPlan.Compile(c,defs);Assert.Equal(3,Get<IEnumerable>(mutation,"Bindings").Cast<object>().Count());var maintenance=RelationMaintenanceCatalog.Compile(c,defs);var pair=Assert.Single(Get<IEnumerable>(maintenance,"Plans").Cast<object>(),x=>Get<string>(x,"Key")=="3");var plan=Get<object>(pair,"Value");Assert.Null(Get<object?>(plan,"Error"));Assert.Contains("b.`yz_project_id`=@project_scope_number",Get<string>(Get<object>(plan,"Read"),"Scope"));
    }
    /// <summary>新全局缓存资格不能放宽来源、父键、其它全局层或缓存生产链的旧门槛。</summary>
    [Theory][InlineData("sourceProject")][InlineData("sourceGlobal")][InlineData("producerProject")][InlineData("privateTerminal")][InlineData("unpublished")][InlineData("pageForeign")][InlineData("pageVariable")][InlineData("cacheCondition")][InlineData("badPath")][InlineData("credential")][InlineData("virtual")][InlineData("file")][InlineData("arrayProducer")][InlineData("parentJoin")][InlineData("fakeOtherCache")]
    public void Unproven_cached_role_rejects_whole_read_and_write_chain(string invalid)
    {
        foreach(var mode in new[]{0,1})
        {
            var c=GlobalCrossCachedTargetFixture.Create(mode,1,"string","int64",true);var cache=c.ConfigColumns.Single(x=>x.Id=="114");
            switch(invalid)
            {
                case "sourceProject":c.ConfigColumns.RemoveAll(x=>x.Id=="303");break;
                case "sourceGlobal":c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");break;
                case "producerProject":c.ConfigColumns.Add(new(){Id="199",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64"});break;
                case "privateTerminal":c.ConfigTables.Single(x=>x.Id=="2").Extra.Clear();break;
                case "unpublished":c.ConfigTables.Single(x=>x.Id=="1").YzProjectId="";break;
                case "pageForeign":c.ConfigPages.Single(x=>x.Id=="10").YzProjectId="101";break;
                case "pageVariable":c.ConfigPages.Single(x=>x.Id=="10").WhereSql="id=@parent_id";break;
                case "cacheCondition":c.ConfigPages.Single(x=>x.Id=="10").WhereSql="lookup_name='甲'";break;
                case "badPath":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");break;
                case "credential":c.ConfigColumns.Single(x=>x.Id=="205").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="113").ControlDetail=c.ConfigColumns.Single(x=>x.Id=="113").ControlDetail.Replace("\"name\"","\"password\"",StringComparison.Ordinal);break;
                case "virtual":cache.IsVirtualColumn=1;break;
                case "file":cache.ControlKind="FileLibrary";break;
                case "arrayProducer":c.ConfigColumns.Single(x=>x.Id=="113").ControlKind="MultiSelectPage";break;
                case "parentJoin":c.ConfigColumns.Single(x=>x.Id=="p1").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);break;
                case "fakeOtherCache":c.ConfigColumns.Single(x=>x.Id=="405").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);break;
            }
            if(mode==0)Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"3",["country_name","area_name","place_name"],Scopes(c)));else Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,Definitions(c)));
        }
    }
    /// <summary>目标页绑定不能被换成另一表页面，即使名称生产链本身正确也拒绝。</summary>
    [Fact]
    public void Read_binding_cannot_replace_a_published_role_page()
    {var c=GlobalCrossCachedTargetFixture.Create(0,1,"int64","string");var scopes=Scopes(c);var levels=scopes[0].Levels.Select((x,i)=>i==1?x with{PageId="20"}:x).ToArray();Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"3",["area_name"],[new("334",levels)]));}
    /// <summary>全部目标绑定来自同一公开元数据及其固定常量，不能在读计划里丢弃页面条件。</summary>
    internal static NonRedundantCrossTableScope[] Scopes(MetadataCatalog c)=>[new("334",GlobalCrossCachedTargetFixture.Levels(c).Select(level=>{var table=c.ConfigTables.Single(x=>x.TableName==level.GetProperty("table").GetString());var page=c.ConfigPages.Single(x=>x.ConfigTableId==table.Id);return new NonRedundantCrossTableTargetScope(page.Id,FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(page.WhereSql,c.ConfigColumns.Where(x=>x.ConfigTableId==table.Id).Select(x=>x.ColumnName)),c.ConfigColumns.Where(x=>x.ConfigTableId==table.Id).ToDictionary(x=>x.ColumnName,x=>x.LogicalType,StringComparer.Ordinal)));}).ToArray())];
    /// <summary>定义只提供真实字段ID及完整层/页，不向生产API传SQL或重写物理列。</summary>
    internal static RelationCacheDefinition[] Definitions(MetadataCatalog c)
    {
        var source=c.ConfigTables.Single(x=>x.Id=="3");var cols=RelationStorageColumns.Expand(source,c.ConfigColumns.Where(x=>x.ConfigTableId=="3"));return GlobalCrossCachedTargetFixture.Levels(c).Select(level=>{var target=c.ConfigTables.Single(x=>x.TableName==level.GetProperty("table").GetString());var prefix=level.GetProperty("columnPrefix").GetString()!;var field=level.GetProperty("nameField").GetString();return new RelationCacheDefinition("3",cols.Single(x=>x.ColumnName==prefix+"_id").Id,target.Id,c.ConfigColumns.Single(x=>x.ConfigTableId==target.Id&&x.ColumnName==field).Id,cols.Single(x=>x.ColumnName==prefix+"_name").Id,false){CrossTableColumnId="334",CrossTableLevelKey=level.GetProperty("key").GetString(),CrossTablePageId=GlobalCrossTableTarget.Declared(target)?target.Id+"0":null};}).ToArray();
    }
    /// <summary>只读冻结计划的实际字段，不复制计划算法。</summary>
    private static T Get<T>(object source,string name)=>(T)source.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(source)!;
}
