using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>证明原根／中层部分选择可被无损区分，基础存储证据不替代生成Service或实库验收。</summary>
public sealed class CrossTableSparseSelectionTests
{
    /// <summary>同一长ID出现在不同目标表时，各层选择仍有不同持久化表示；只存一个ID，不重复保存祖先。</summary>
    [Theory][InlineData("string",false)][InlineData("string",true)][InlineData("int64",false)][InlineData("int64",true)]
    public void Each_partial_selection_has_one_distinct_storage_slot_without_caching_ancestors(string type,bool explicitColumns)
    {
        var catalog=Catalog(type,explicitColumns);var table=catalog.ConfigTables[0];var saved=JsonSerializer.Serialize(catalog);var plan=CrossTableSparseSelection.Compile(table,"3",catalog.ConfigColumns);
        const long id=9007199254740993;var encodings=new HashSet<string>(StringComparer.Ordinal);
        for(var deepest=0;deepest<3;deepest++)
        {
            var path=Enumerable.Repeat(id,deepest+1).ToArray();var stored=plan.PackValidatedPath(path);path[0]=1;
            Assert.Equal(3,stored.Count);Assert.Single(stored,pair=>pair.Value is not null);Assert.True(encodings.Add(JsonSerializer.Serialize(stored)));
            Assert.Equal(new CrossTableSelectedNode(deepest,id),plan.ReadStored(stored));Assert.Equal(type=="string"?typeof(string):typeof(long),stored.Values.Single(value=>value is not null)!.GetType());
            Assert.Throws<NotSupportedException>(()=>((IDictionary<string,object?>)stored).Add("secret",1L));
        }
        Assert.Null(plan.ReadStored(plan.PackValidatedPath([])));Assert.Equal(saved,JsonSerializer.Serialize(catalog));
        Assert.Equal(new[]{"country_id","area_id","place_id"},plan.Slots.Select(slot=>slot.ColumnName));
        catalog.ConfigColumns.Single(column=>column.Id=="3").ColumnName="changed_after_compile";Assert.Equal("place_id",plan.Slots[2].ColumnName);
    }

    /// <summary>显式祖先关联是稀疏位的一部分，列顺序不改变派生名称归属或扩大普通别名豁免。</summary>
    [Theory][InlineData("string",false)][InlineData("string",true)][InlineData("int64",false)][InlineData("int64",true)]
    public void Explicit_slot_links_share_cascade_names_but_ordinary_alias_conflicts_stay_rejected(string type,bool reverse)
    {
        var catalog=Catalog(type,true);foreach(var column in catalog.ConfigColumns.Where(column=>column.ColumnName is "country_id" or "area_id"))column.LinkConfigTableId="target_"+column.ColumnName;if(reverse)catalog.ConfigColumns.Reverse();var before=JsonSerializer.Serialize(catalog);var plan=MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables[0]);Assert.Equal(3,plan.PhysicalColumns.Count(column=>column.ColumnName.EndsWith("_id",StringComparison.Ordinal)));Assert.DoesNotContain(plan.PhysicalColumns,column=>column.ColumnName.EndsWith("_name",StringComparison.Ordinal));Assert.Equal(MetadataFieldStorage.RelationDisplay,plan.Kind(catalog.ConfigColumns.Single(column=>column.ColumnName=="country_name")));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        catalog.ConfigColumns.Add(new(){Id="ordinary",ConfigTableId="1",ColumnName="customer_id",LogicalType="int64",LinkConfigTableId="target_customer"});catalog.ConfigColumns.Add(new(){Id="ordinary_name",ConfigTableId="1",ColumnName="customer_name",LogicalType="string"});Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables[0]));
    }

    /// <summary>显式各级不同ID类型按自身物理定义编解码，不能把字符串和整数绕过类型检查混用。</summary>
    [Fact]
    public void Mixed_declared_id_types_are_preserved_exactly()
    {
        var catalog=Catalog("string",true);catalog.ConfigColumns.Single(column=>column.ColumnName=="area_id").LogicalType="int64";var plan=CrossTableSparseSelection.Compile(catalog.ConfigTables[0],"3",catalog.ConfigColumns);
        Assert.IsType<string>(plan.PackValidatedPath([10])["country_id"]);Assert.IsType<long>(plan.PackValidatedPath([10,20])["area_id"]);Assert.IsType<string>(plan.PackValidatedPath([10,20,30])["place_id"]);
        var wrong=plan.PackValidatedPath([10,20]).ToDictionary();wrong["area_id"]="20";Assert.Throws<InvalidOperationException>(()=>plan.ReadStored(wrong));
    }

    /// <summary>已有来源只有末级列时，只预览增加两个可空ID位；不写数据库、不删除历史显示列。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Schema_preview_adds_only_nullable_id_slots_and_keeps_original_catalog(string type)
    {
        var catalog=Catalog(type,false);var saved=JsonSerializer.Serialize(catalog);var desired=MySqlSchemaPlanner.Compile(catalog,"sparse_test");var source=Assert.Single(desired.Tables);
        Assert.Equal(new[]{"area_id","country_id","id","name","place_id"},source.Columns.Select(column=>column.Name));Assert.All(source.Columns.Where(column=>column.Name.EndsWith("_id",StringComparison.Ordinal)),column=>Assert.True(column.Nullable));
        var old=desired with{Tables=[source with{Columns=source.Columns.Where(column=>column.Name is not("country_id" or "area_id")).Append(new ColumnSchema("country_name","varchar(64)",true,false,null)).ToArray()}]};
        var changes=MySqlSchemaPlanner.Compare(old,desired);Assert.Equal(2,changes.Count);Assert.All(changes,change=>{Assert.Equal("addColumn",change.Kind);Assert.DoesNotContain("DROP",change.Sql);Assert.DoesNotContain("UPDATE",change.Sql);Assert.DoesNotContain("_name",change.Sql);});
        Assert.Empty(MySqlSchemaPlanner.Compare(desired,desired));Assert.Equal(saved,JsonSerializer.Serialize(catalog));
        var storage=MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables[0]);Assert.Equal(3,storage.PhysicalColumns.Count(column=>column.ColumnName.EndsWith("_id",StringComparison.Ordinal)));Assert.DoesNotContain(storage.PhysicalColumns,column=>column.ColumnName.EndsWith("_name",StringComparison.Ordinal));
    }

    /// <summary>旧数据多位有值或非规范ID明确拒绝，不能选择最深位掩盖歧义，也不自动回填或清理。</summary>
    [Theory][InlineData("01")][InlineData("+1")][InlineData(" 1")][InlineData("1 ")][InlineData("1.0")][InlineData("0")][InlineData("-1")][InlineData("9223372036854775808")]
    public void Ambiguous_or_noncanonical_stored_values_are_never_silently_repaired(string invalid)
    {
        var catalog=Catalog("string",false);var plan=CrossTableSparseSelection.Compile(catalog.ConfigTables[0],"3",catalog.ConfigColumns);var row=plan.PackValidatedPath([1]).ToDictionary();row["country_id"]=invalid;var before=JsonSerializer.Serialize(row);Assert.Throws<InvalidOperationException>(()=>plan.ReadStored(row));Assert.Equal(before,JsonSerializer.Serialize(row));
        row=plan.PackValidatedPath([1]).ToDictionary();row["place_id"]="1";Assert.Throws<InvalidOperationException>(()=>plan.ReadStored(row));
        row=plan.PackValidatedPath([]).ToDictionary();row["country_id"]="";Assert.Null(plan.ReadStored(row));
    }

    /// <summary>缺少位、未知位、错误运行时类型及无效路径均拒绝，明确区分完整空值与未提交。</summary>
    [Fact]
    public void Missing_fields_unknown_fields_wrong_types_and_invalid_paths_are_rejected()
    {
        var catalog=Catalog("int64",false);var plan=CrossTableSparseSelection.Compile(catalog.ConfigTables[0],"3",catalog.ConfigColumns);var row=plan.PackValidatedPath([]).ToDictionary();row.Remove("country_id");Assert.Throws<ArgumentException>(()=>plan.ReadStored(row));row["unknown_id"]=1L;Assert.Throws<ArgumentException>(()=>plan.ReadStored(row));
        row=plan.PackValidatedPath([]).ToDictionary();foreach(var value in new object[]{1,1.0m,"1",-1L,0L}){row["place_id"]=value;Assert.Throws<InvalidOperationException>(()=>plan.ReadStored(row));}
        Assert.Throws<ArgumentException>(()=>plan.PackValidatedPath([1,2,3,4]));Assert.Throws<ArgumentException>(()=>plan.PackValidatedPath([1,0]));
    }

    /// <summary>显式错误列与共享位在结构预览前停止，不能用自动扩展覆盖原有业务约束。</summary>
    [Theory][InlineData("virtual")][InlineData("join")][InlineData("notNull")][InlineData("default")][InlineData("type")][InlineData("control")][InlineData("foreign")][InlineData("duplicate")][InlineData("shared")]
    public void Conflicting_metadata_cannot_become_a_silent_physical_storage_change(string conflict)
    {
        var catalog=Catalog("string",true);var root=catalog.ConfigColumns.Single(column=>column.ColumnName=="country_id");
        switch(conflict)
        {
            case "virtual":root.IsVirtualColumn=1;break;case "join":root.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);break;case "notNull":root.IsDbNotNull=1;break;case "default":root.DefaultValue="1";break;case "type":root.LogicalType="decimal";break;case "control":root.ControlKind="List";break;case "foreign":root.ConfigTableId="other";break;case "duplicate":root.ColumnName="area_id";break;
            case "shared":var definition=System.Text.Json.Nodes.JsonNode.Parse(catalog.ConfigColumns.Single(column=>column.Id=="3").ControlDetail)!;definition["levels"]![2]!["columnPrefix"]="another_place";catalog.ConfigColumns.Add(new(){Id="99",ConfigTableId="1",ColumnName="another_place_id",LogicalType="string",ControlKind="CascadeTree",ControlDetail=definition.ToJsonString()});break;
        }
        var before=JsonSerializer.Serialize(catalog);Assert.Throws<InvalidOperationException>(()=>CrossTableSparseSelection.Compile(catalog.ConfigTables[0],"3",catalog.ConfigColumns));Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }

    /// <summary>存储位存在不代表完整路径已接线；底层读取仍拒绝把空祖先位当逻辑值返回。</summary>
    [Theory][InlineData("country_id")][InlineData("area_id")][InlineData("place_id")][InlineData("country_name")][InlineData("place_name")]
    public void Logical_path_outputs_remain_gated_before_fixed_projection_is_implemented(string field)
    {
        var catalog=Catalog("string",false);Assert.Throws<NotSupportedException>(()=>NonRedundantReads.Compile(catalog,"1",["id",field]));Assert.Equal("int64",NonRedundantReads.Compile(catalog,"1",["id"]).OutputTypes["id"]);
    }

    /// <summary>固定三层元数据复用原columnPrefix命名；目标业务范围由后续正式生成计划证明。</summary>
    private static MetadataCatalog Catalog(string type,bool explicitColumns)
    {
        var result=new MetadataCatalog{YzProjectId="1",ConfigTables=[new(){Id="1",TableName="sparse_source",IsRongyu=0}],ConfigColumns=[new(){Id="1",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="2",ConfigTableId="1",ColumnName="name",LogicalType="string",ColumnLength="64"},new(){Id="3",ConfigTableId="1",ColumnName="place_id",LogicalType=type,ColumnLength=type=="string"?"64":"",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{new{table="country"},new{table="area",parentField="country_id"},new{table="place",parentField="area_id"}}})}]};
        if(explicitColumns)foreach(var name in new[]{"country_id","country_name","area_id","area_name","place_name"})result.ConfigColumns.Add(new(){Id="declared_"+name,ConfigTableId="1",ColumnName=name,LogicalType=name.EndsWith("_id",StringComparison.Ordinal)?type:"string",ColumnLength="64"});return result;
    }
}
