using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>跨表辅助名称由明确控制列／层位证明，标量缓存不冒充多值回填。</summary>
public sealed class CrossTableCachePlanTests
{
    /// <summary>字符串或整数槽及默认／自定义物理名称共享冻结传播和整表回填。</summary>
    [Theory][InlineData("string","name")][InlineData("int64","name")][InlineData("string","caption")][InlineData("int64","caption")]
    public async Task Physical_cross_names_compile_scalar_maintenance_without_arrays(string type,string field)
    {
        var c=Catalog(type,field);var before=JsonSerializer.Serialize(c);var bindings=Definitions(c);var p=RelationMaintenanceCatalog.Compile(c,bindings);Assert.Equal(64,RelationMutationPlan.Compile(c,bindings).Fingerprint.Length);Assert.Contains(p.Tables,x=>x.Id=="source");Assert.DoesNotContain("source",p.MixedSourceColumns.Keys);Assert.Equal(before,JsonSerializer.Serialize(c));using var db=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused"});using var ct=new CancellationTokenSource();ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new RelationMaintenanceExecutor(db).BackfillAsync(p,"source",ct.Token));
    }
    /// <summary>跨表缓存不能通过伪造字段、表、层、额外SQL或派生终点放宽普通标量合同。</summary>
    [Theory][InlineData("noControl")][InlineData("noLevel")][InlineData("wrongLevel")][InlineData("wrongTarget")][InlineData("wrongCache")][InlineData("multiple")][InlineData("extra")][InlineData("duplicateKey")][InlineData("global")][InlineData("derived")][InlineData("credential")][InlineData("parent")][InlineData("parentSystem")][InlineData("foreignControl")][InlineData("targetR0")]
    public void Invalid_cross_proofs_fail_before_connection(string kind)
    {
        var c=Catalog("string","caption");var definitions=Definitions(c);var first=definitions[0];var control=c.ConfigColumns.Single(x=>x.Id=="control");var json=JsonNode.Parse(control.ControlDetail)!;
        if(kind=="noControl")definitions[0]=first with{CrossTableColumnId=null};if(kind=="noLevel")definitions[0]=first with{CrossTableLevelKey=null};if(kind=="wrongLevel")definitions[0]=first with{CrossTableLevelKey="missing"};if(kind=="wrongTarget")definitions[0]=first with{TargetTableId="area",ValueColumnId="area_label"};if(kind=="wrongCache")definitions[0]=first with{CacheColumnId=definitions[1].CacheColumnId};if(kind=="multiple")definitions[0]=first with{Multiple=true};
        if(kind=="extra")json["whereSql"]="1=1";if(kind=="duplicateKey")json["levels"]![1]!["key"]="country";if(kind=="global")c.ConfigTables.Single(x=>x.Id=="country").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="derived"){var name=c.ConfigColumns.Single(x=>x.Id=="country_label");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");}if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="country_label").ColumnName="password";json["levels"]![0]!["nameField"]="password";}if(kind=="parent")c.ConfigColumns.Single(x=>x.Id=="area_parent").LinkConfigTableId="place";if(kind=="parentSystem")json["levels"]![1]!["parentField"]="yz_project_id";if(kind=="foreignControl"){var foreign=new ColumnMetadata{Id="bad_foreign",ConfigTableId="source",ColumnName="country_id",LogicalType="string",ControlKind="List",LinkConfigTableId="country"};c.ConfigColumns.Add(foreign);definitions[0]=definitions[0] with{ForeignColumnId="bad_foreign"};}if(kind=="targetR0")c.ConfigTables.Single(x=>x.Id=="country").IsRongyu=0;control.ControlDetail=json.ToJsonString();Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,definitions));
    }
    /// <summary>原六参数构造及JSON不增加空扩展字段，普通标量名称约束仍然存在。</summary>
    [Fact]
    public void Ordinary_definitions_keep_shape_and_cannot_claim_custom_cross_aliases()
    {
        var c=Catalog("string","caption");var plain=Definitions(c)[0] with{CrossTableColumnId=null,CrossTableLevelKey=null};var json=JsonSerializer.Serialize(plain);Assert.DoesNotContain("CrossTable",json);Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[plain]));var copy=JsonSerializer.Deserialize<RelationCacheDefinition>(JsonSerializer.Serialize(Definitions(c)[0]));Assert.Equal(Definitions(c)[0],copy);
    }
    /// <summary>目录以稳定字段ID描述，不写源元数据或手动创建物理结构。</summary>
    internal static RelationCacheDefinition[] Definitions(MetadataCatalog c)
    {
        var source=c.ConfigTables.Single(x=>x.Id=="source");var columns=RelationStorageColumns.Expand(source,c.ConfigColumns.Where(x=>x.ConfigTableId==source.Id));return new[]{"country","area","place"}.Select(key=>new RelationCacheDefinition("source",columns.Single(x=>x.ColumnName==key+"_id").Id,key,key+"_label",columns.Single(x=>x.ColumnName==key+"_name").Id,false){CrossTableColumnId="control",CrossTableLevelKey=key}).ToArray();
    }
    /// <summary>三层固定普通R1目标，名称可使用caption，来源缓存仍沿原prefix_name。</summary>
    internal static MetadataCatalog Catalog(string type="string",string field="name")
    {
        var c=new MetadataCatalog{YzProjectId="54"};foreach(var id in new[]{"source","country","area","place"}){c.ConfigTables.Add(new(){Id=id,YzProjectId=c.YzProjectId,TableName="cross_cache_"+id,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id=id+"_id",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=id+"_project",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},new(){Id=id+"_deleted",ConfigTableId=id,ColumnName="delete_no",LogicalType="datetime"}]);if(id!="source")c.ConfigColumns.Add(new(){Id=id+"_label",ConfigTableId=id,ColumnName=field,LogicalType="string",ColumnLength="100"});}c.ConfigColumns.AddRange([new(){Id="area_parent",ConfigTableId="area",ColumnName="parent_id",LogicalType=type,LinkConfigTableId="country"},new(){Id="place_parent",ConfigTableId="place",ColumnName="parent_id",LogicalType=type,LinkConfigTableId="area"},new(){Id="control",ConfigTableId="source",ColumnName="place_id",LogicalType=type,ColumnLength="100",ControlKind="CascadeTree",LinkConfigTableId="place",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{new{key="country",columnPrefix="country",table="cross_cache_country",nameField=field},new{key="area",columnPrefix="area",table="cross_cache_area",nameField=field,parentField="parent_id"},new{key="place",columnPrefix="place",table="cross_cache_place",nameField=field,parentField="parent_id"}}})}]);return c;
    }
}
