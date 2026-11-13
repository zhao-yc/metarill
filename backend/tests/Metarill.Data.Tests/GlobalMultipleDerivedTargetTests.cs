using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>全局R0多选名称必须实时读取真实终点；R1数组副本进入混合传播，不得读取不存在的物理名称。</summary>
public sealed class GlobalMultipleDerivedTargetTests
{
    /// <summary>原两种数组控件和内层两种物理键保持序号、缺失位置、精确ID及来源项目；联接不扩张根行。</summary>
    [Theory][InlineData("MultiSelectPage","int64")][InlineData("MultiSelectPage","string")][InlineData("MultiCheckBoxDatabase","int64")][InlineData("MultiCheckBoxDatabase","string")]
    public void Global_r0_arrays_read_current_names_inside_the_array_projection(string kind,string inner)
    {
        var c=Catalog(0,kind,inner);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_names"]);var sql=Read<IReadOnlyDictionary<string,string>>(plan,"Expressions")["global_source_global_target_names"];
        Assert.Contains("JSON_ARRAYAGG(multi_name_0.`name`)",sql);Assert.Contains("LEFT JOIN `global_source`",sql);Assert.Contains("LEFT JOIN `global_target`",sql);Assert.Contains("ORDER BY multi_ids.ordinal",sql);Assert.Contains("AS BINARY",sql);Assert.DoesNotContain("multi_value.`global_target_name`",sql);Assert.DoesNotContain("JOIN `global_source`",Read<string>(plan,"From"));Assert.Contains("b.`yz_project_id`=@project_scope_number",Read<string>(plan,"Scope"));
        Assert.Contains("LIKE {parameter} ESCAPE '!'",Read<IReadOnlyDictionary<string,string>>(plan,"ArrayNamePredicates")["global_source_global_target_names"]);Assert.DoesNotContain("multi_value.`yz_project_id`",sql);
        if(inner=="string")Assert.Contains("BINARY CAST(multi_name_0.`id` AS CHAR)=BINARY multi_value.`global_target_id`",sql);
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>两种R1数组缓存都登记中间与终点，保留原200/5000预算及目录身份，物理传播器仍拒绝逻辑值。</summary>
    [Theory][InlineData("MultiSelectPage","int64")][InlineData("MultiSelectPage","string")][InlineData("MultiCheckBoxDatabase","int64")][InlineData("MultiCheckBoxDatabase","string")]
    public async Task Global_r0_array_caches_keep_complete_maintenance_and_published_identity(string kind,string inner)
    {
        var c=Catalog(1,kind,inner);var before=JsonSerializer.Serialize(c);var definition=new RelationCacheDefinition("3","34","1","14","35",true);Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[definition]));
        var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[definition]);Assert.Contains("global_source",plan.MixedTargets);Assert.Contains("global_target",plan.MixedTargets);Assert.Equal(new[]{"global_source_global_target_names"},plan.MixedSourceColumns["cached_consumer"]);Assert.DoesNotContain("global_source",plan.MixedSourceColumns.Keys);
        using var database=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new ProjectRelationMutationPropagator(database).AfterMutationAsync(plan,"global_target",1,"999",new HashSet<string>{"name"},false,false));Assert.Null(database.CurrentTransaction);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>显式全局不能绕过目录归属、物理身份、单值路径、非凭据终点和固定候选范围。</summary>
    [Theory][InlineData("sourceProject")][InlineData("sourceGlobal")][InlineData("targetProject")][InlineData("virtualRowProject")][InlineData("targetKey")][InlineData("privateTerminal")][InlineData("privateProducer")][InlineData("wrongPage")][InlineData("dynamicCondition")][InlineData("cacheCondition")][InlineData("virtual")][InlineData("file")][InlineData("array")][InlineData("badPath")][InlineData("credential")][InlineData("cycle")][InlineData("innerArray")]
    public void Invalid_global_r0_array_proof_rejects_before_any_connection(string invalid)
    {
        foreach(var kind in new[]{"MultiSelectPage","MultiCheckBoxDatabase"})foreach(var mode in new[]{0,1})
        {
            var c=Catalog(mode,kind,"string");var middle=c.ConfigTables.Single(t=>t.Id=="1");var name=c.ConfigColumns.Single(f=>f.Id=="14");var inner=c.ConfigColumns.Single(f=>f.Id=="13");
            switch(invalid)
            {
                case "sourceProject":c.ConfigColumns.RemoveAll(f=>f.Id=="32");break;
                case "sourceGlobal":c.ConfigTables.Single(t=>t.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");break;
                case "targetProject":middle.YzProjectId="101";break;
                case "virtualRowProject":c.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64",IsVirtualColumn=1});break;
                case "targetKey":c.ConfigColumns.Single(f=>f.Id=="11").IsDbNotNull=0;break;
                case "privateTerminal":c.ConfigTables.Single(t=>t.Id=="2").Extra.Clear();break;
                case "privateProducer":middle.Extra.Clear();break;
                case "wrongPage":c.ConfigPages.Single(p=>p.Id=="10").YzProjectId="101";break;
                case "dynamicCondition":c.ConfigPages.Single(p=>p.Id=="10").WhereSql="id=@parent_id";break;
                case "cacheCondition":c.ConfigPages.Single(p=>p.Id=="10").WhereSql="global_target_name='甲'";break;
                case "virtual":name.IsVirtualColumn=1;break;
                case "file":name.ControlKind="FileLibrary";break;
                case "array":name.ControlKind="MultiSelectPage";break;
                case "badPath":name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");break;
                case "credential":c.ConfigColumns.Single(f=>f.Id=="22").ColumnName="password";inner.ControlDetail=inner.ControlDetail.Replace("nameField\":\"name","nameField\":\"password",StringComparison.Ordinal);break;
                case "cycle":inner.LinkConfigTableId="1";inner.ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"global_target_name\"}";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.global_target_name");break;
                case "innerArray":inner.ControlKind="MultiSelectPage";break;
            }
            if(mode==0)Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_names"]));
            else Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[new("3","34","1","14","35",true)]));
        }
    }
    /// <summary>新的全局多值能力不会将数组纳入标量精确管理，也不会产生物理R0名称引用。</summary>
    [Theory][InlineData("MultiSelectPage",200)][InlineData("MultiCheckBoxDatabase",5000)]
    public void Array_budget_and_physical_dependencies_are_complete(string kind,int expected)
    {
        var c=Catalog(1,kind,"string");var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[new("3","34","1","14","35",true)]);var entry=Assert.Single(Read<IEnumerable>(plan,"Plans").Cast<object>(),v=>Read<object>(v,"Key").ToString()=="3");var value=Read<object>(entry,"Value");Assert.Null(Read<object?>(value,"Error"));var multiple=Assert.Single(Read<IEnumerable>(value,"Multiples").Cast<object>());Assert.Equal(expected,Read<int>(multiple,"MaximumItems"));
        var read=Read<object>(value,"Read");Assert.DoesNotContain(Read<IEnumerable>(read,"PhysicalReferences").Cast<object>(),v=>Read<string>(v,"Table")=="global_source"&&Read<string>(Read<object>(v,"Column"),"Name")=="global_target_name");Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileMatch(Catalog(0,kind,"string"),"3","global_source_global_target_names"));
    }
    /// <summary>R0实体上的真实物理标签继续采用直接数组读取／物理传播，不能误作逻辑列或丢失发布身份。</summary>
    [Theory][InlineData(0,"MultiSelectPage")][InlineData(0,"MultiCheckBoxDatabase")][InlineData(1,"MultiSelectPage")][InlineData(1,"MultiCheckBoxDatabase")]
    public async Task Physical_label_on_global_r0_preserves_direct_array_path(int mode,string kind)
    {
        var c=Catalog(mode,kind,"string");c.ConfigColumns.Add(new(){Id="16",ConfigTableId="1",YzProjectId="100",ColumnName="caption",LogicalType="string",ColumnLength="100"});c.ConfigPageColumns.Add(new(){Id="1016",ConfigPageId="10",ConfigColumnId="16",YzProjectId="100",IsShow=1,IsEdit=1});var foreign=c.ConfigColumns.Single(f=>f.Id=="34");foreign.ControlDetail=foreign.ControlDetail.Replace("global_target_name","caption",StringComparison.Ordinal);var cache=c.ConfigColumns.Single(f=>f.Id=="35");cache.ColumnName="global_source_captions";cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_source.caption");
        if(mode==0){var read=MySqlNonRedundantReadCompiler.Compile(c,"3",[cache.ColumnName]);Assert.Contains("JSON_ARRAYAGG(multi_value.`caption`)",Read<IReadOnlyDictionary<string,string>>(read,"Expressions")[cache.ColumnName]);}
        else
        {
            var definition=new RelationCacheDefinition("3","34","1","16","35",true);Assert.Single(Read<IEnumerable>(RelationMutationPlan.Compile(c,[definition]),"Bindings").Cast<object>());var plan=RelationMaintenanceCatalog.Compile(c,[definition]);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new ProjectRelationMutationPropagator(session).AfterMutationAsync(plan,"global_source",1,"999",new HashSet<string>{"caption"},false,false));Assert.Null(session.CurrentTransaction);
        }
    }
    /// <summary>复用已验数组元数据，仅让中间名称采用R0并公开原type=2目标字段。</summary>
    internal static MetadataCatalog Catalog(int mode,string kind,string inner)
    {
        var c=GlobalMultipleCachedTargetTests.Catalog(mode,kind,inner);c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=0;c.ConfigPages.Single(p=>p.Id=="10").Extra["type"]=JsonSerializer.SerializeToElement(2);
        if(!c.ConfigPageColumns.Any(f=>f.ConfigPageId=="10"&&f.ConfigColumnId=="14"))c.ConfigPageColumns.Add(new(){Id="1014",ConfigPageId="10",ConfigColumnId="14",YzProjectId=c.YzProjectId,IsShow=1,IsEdit=0});return c;
    }
    /// <summary>只检查实际不可变计划，不复制SQL或维护算法。</summary>
    private static T Read<T>(object value,string name)=>(T)value.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(value)!;
}
