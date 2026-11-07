using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Metarill.Testing;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>共享缓存名称必须在完整组上编译，普通角色和来源范围不能被共享标记吞掉。</summary>
public sealed class GlobalSharedCachedTargetTests
{
    /// <summary>四种键/父键及长短分支的R0名称来自同一缓存值，字段去重不丢仓库或城市。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",true)][InlineData("int64","string",false)][InlineData("int64","int64",true)]
    public void Shared_cached_names_compile_with_both_controls_and_original_scope(string key,string parent,bool shorter)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(0,key,parent,shorter);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"3",Outputs(shorter),Scopes(c));Assert.Contains("b.`yz_project_id`=@project_scope_number",Get<string>(plan,"Scope"));var from=Get<string>(plan,"From");Assert.Contains("LEFT JOIN `cached_global`",from);Assert.DoesNotContain("JOIN `lookup`",from);Assert.Contains("`yz_project_id`=@project_scope_number",from);Assert.Contains("`lookup_name`",Get<IReadOnlyDictionary<string,string>>(plan,"Expressions")["area_name"]);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>共同缓存只登记一次，所选定义仍携带完整控制列全局页图，原维护不能变成部分成功。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",true)][InlineData("int64","string",false)][InlineData("int64","int64",true)]
    public void Shared_redundant_cache_deduplicates_without_losing_global_pages(string key,string parent,bool shorter)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(1,key,parent,shorter);var defs=Definitions(c);var plan=RelationMutationPlan.Compile(c,defs);Assert.Equal(shorter?3:4,Get<IEnumerable>(plan,"Bindings").Cast<object>().Count());var maintenance=RelationMaintenanceCatalog.Compile(c,defs);var entry=Assert.Single(Get<IEnumerable>(maintenance,"Plans").Cast<object>(),x=>Get<string>(x,"Key")=="3");Assert.Null(Get<object?>(Get<object>(entry,"Value"),"Error"));Assert.Equal("10",Assert.Single(defs.Single(x=>x.ValueColumnId=="114").CrossTableGlobalPages!).Value);
    }
    /// <summary>丢失同伴或共同上级页/条件不一致时，不生成只处理一个控件的SQL。</summary>
    [Theory][InlineData("peer")][InlineData("page")][InlineData("condition")]
    public void Shared_read_requires_all_peers_and_same_cached_role_scope(string kind)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(0,"string","int64",true);var scopes=Scopes(c);if(kind=="peer")scopes=scopes[..1];else{var levels=scopes[1].Levels.ToArray();levels[1]=kind=="page"?levels[1] with{PageId="20"}:levels[1] with{Condition=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("status=2",["status"]),new Dictionary<string,string>{{"status","int32"}})};scopes[1]=new(scopes[1].SourceColumnId,levels);}Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"3",Outputs(true),scopes));
    }
    /// <summary>去重后的缓存不能依赖缺失、错表或普通层伪装的全局页映射。</summary>
    [Theory][InlineData("missing")][InlineData("wrong")][InlineData("extra")]
    public void Shared_cache_requires_complete_global_page_proof(string kind)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(1,"int64","string",true);var defs=Definitions(c);var index=Array.FindIndex(defs,x=>x.ValueColumnId=="114");var pages=new Dictionary<string,string>(defs[index].CrossTableGlobalPages!);if(kind=="missing")pages.Clear();if(kind=="wrong")pages["area"]="20";if(kind=="extra")pages["country"]="40";defs[index]=defs[index] with{CrossTableGlobalPages=pages,CrossTablePageId=null};Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,defs));
    }
    /// <summary>固定输出保留两条原分支；短控件没有城市字段。</summary>
    private static string[] Outputs(bool shorter)=>new[]{"country_name","area_name","warehouse_name"}.Concat(shorter?[]:new[]{"place_name"}).ToArray();
    /// <summary>每个控件的层页及常量仍从同一元数据获取，不接受请求覆盖。</summary>
    internal static NonRedundantCrossTableScope[] Scopes(MetadataCatalog c)=>c.ConfigColumns.Where(x=>x.ConfigTableId=="3"&&x.ControlKind=="CascadeTree").Select(control=>new NonRedundantCrossTableScope(control.Id,Levels(control).Select(level=>{var target=c.ConfigTables.Single(t=>t.TableName==level.GetProperty("table").GetString());var page=c.ConfigPages.Single(p=>p.ConfigTableId==target.Id);return new NonRedundantCrossTableTargetScope(page.Id,FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(page.WhereSql,c.ConfigColumns.Where(x=>x.ConfigTableId==target.Id).Select(x=>x.ColumnName)),c.ConfigColumns.Where(x=>x.ConfigTableId==target.Id).ToDictionary(x=>x.ColumnName,x=>x.LogicalType)));}).ToArray())).ToArray();
    /// <summary>实际定义按缓存字段去重，并保留代表控制列完整全局页图，避免用别的控件补页。</summary>
    internal static RelationCacheDefinition[] Definitions(MetadataCatalog c)
    {
        var source=c.ConfigTables.Single(t=>t.Id=="3");var columns=RelationStorageColumns.Expand(source,c.ConfigColumns.Where(x=>x.ConfigTableId=="3"));return c.ConfigColumns.Where(x=>x.ConfigTableId=="3"&&x.ControlKind=="CascadeTree").SelectMany(control=>{var levels=Levels(control);var pages=levels.Select(level=>c.ConfigTables.Single(t=>t.TableName==level.GetProperty("table").GetString())).Where(GlobalCrossTableTarget.Declared).ToDictionary(t=>levels.Single(level=>level.GetProperty("table").GetString()==t.TableName).GetProperty("key").GetString()!,t=>t.Id+"0");return levels.Select(level=>{var target=c.ConfigTables.Single(t=>t.TableName==level.GetProperty("table").GetString());var prefix=level.GetProperty("columnPrefix").GetString()!;return new RelationCacheDefinition("3",columns.Single(x=>x.ColumnName==prefix+"_id").Id,target.Id,c.ConfigColumns.Single(x=>x.ConfigTableId==target.Id&&x.ColumnName==level.GetProperty("nameField").GetString()).Id,columns.Single(x=>x.ColumnName==prefix+"_name").Id,false){CrossTableColumnId=control.Id,CrossTableLevelKey=level.GetProperty("key").GetString(),CrossTablePageId=GlobalCrossTableTarget.Declared(target)?target.Id+"0":null,CrossTableGlobalPages=pages};});}).GroupBy(x=>x.CacheColumnId).Select(group=>group.First()).ToArray();
    }
    /// <summary>复制定义层，不泄露JsonDocument生命周期。</summary>
    private static JsonElement[] Levels(ColumnMetadata control){using var json=JsonDocument.Parse(control.ControlDetail);return json.RootElement.GetProperty("levels").EnumerateArray().Select(x=>x.Clone()).ToArray();}
    /// <summary>只读实际冻结计划，不复刻生产算法。</summary>
    private static T Get<T>(object source,string name)=>(T)source.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(source)!;
}
