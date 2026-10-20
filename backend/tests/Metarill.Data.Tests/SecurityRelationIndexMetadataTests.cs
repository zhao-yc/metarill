using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>固定平台三关联索引的离线合同；只编译元数据/差异，不打开连接或执行DDL。</summary>
public sealed class SecurityRelationIndexMetadataTests
{
    private static readonly (string Table,string Index,string First,string StableId,string FirstReferenceId,string DeleteReferenceId)[] Expected=
    [
        ("yz_role_permission","yzp_idx_role_permission_role_active","yz_role_id","900235310779076608","900235310800048128","900235310821019648"),
        ("yz_role","yzp_idx_role_admin_active","yz_admin_type_id","900235316722405376","900235316734988288","900235316751765504"),
        ("yz_manager","yzp_idx_manager_admin_active","yz_admin_type_id","900235322581848064","900235322598625280","900235322615402496")
    ];
    /// <summary>读取真实平台目录编译，保证ID、列顺序、开关和非唯一属性全部随发布源声明。</summary>
    [Fact]
    public void Published_indexes_use_stable_ids_and_exact_two_physical_columns()
    {
        var (catalog,root)=Load();var schema=MySqlSchemaPlanner.Compile(catalog,"offline_security_preview");
        using var ids=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"backend/metadata/identifiers.json")));
        foreach(var expected in Expected)
        {
            var table=catalog.ConfigTables.Single(value=>value.TableName==expected.Table);
            var definition=Assert.Single(catalog.ConfigIndexes,value=>Text(value,"configTableId")==table.Id&&Text(value,"indexName")==expected.Index);
            Assert.Equal(expected.StableId,Text(definition,"id"));Assert.Equal("1",Text(definition,"isEnabled"));Assert.Equal("0",Text(definition,"isUnique"));
            Assert.Equal(expected.StableId,ids.RootElement.GetProperty("index:"+expected.Table+":"+expected.Index).GetString());
            var physical=Assert.Single(schema.Tables.Single(value=>value.Name==expected.Table).Indexes!,value=>value.Name==expected.Index);
            Assert.False(physical.Unique);Assert.Equal(new[]{expected.First,"delete_no"},physical.Columns);Assert.DoesNotContain("id",physical.Columns);
            var references=catalog.ConfigIndexColumns.Where(value=>Text(value,"configIndexId")==expected.StableId).OrderBy(value=>int.Parse(Text(value,"sortNo"))).ToArray();Assert.Equal(2,references.Length);Assert.Equal(new[]{expected.FirstReferenceId,expected.DeleteReferenceId},references.Select(value=>Text(value,"id")));
            Assert.All(references,value=>{Assert.Equal("ASC",Text(value,"sortDirection"));Assert.Equal(Text(value,"id"),ids.RootElement.GetProperty("indexfield:"+expected.StableId+":"+Text(value,"configColumnId")).GetString());});
        }
        var names=catalog.ConfigIndexes.Select(value=>(Table:Text(value,"configTableId"),Name:Text(value,"indexName"))).ToArray();Assert.Equal(names.Length,names.Distinct().Count());
        Assert.Single(schema.Tables.Single(value=>value.Name=="yz_manager").Indexes!,value=>value.Name=="ux_manager_name"&&value.Unique&&value.Columns.SequenceEqual(new[]{"name"}));
    }
    /// <summary>最终发布集合只留3个真实父键与6个真实子键；旧builder导入不得复活尚未公开的临时父别名。</summary>
    [Fact]
    public void Published_index_identifier_keys_have_no_provisional_parent_aliases()
    {
        var (_,root)=Load();using var ids=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"backend/metadata/identifiers.json")));
        var properties=ids.RootElement.EnumerateObject().ToArray();var provisional=new[]{"2200000000000101012","2200000000000101015","2200000000000101018"};
        var expected=Expected.Select(value=>"index:"+value.Table+":"+value.Index).Concat(Expected.SelectMany(value=>new[]{"indexfield:"+value.StableId+":"+(value.Table=="yz_role_permission"?"2063264600941453314":value.Table=="yz_role"?"1692533179766878209":"1679698329655750657"),"indexfield:"+value.StableId+":"+(value.Table=="yz_role_permission"?"2200000000000000249":value.Table=="yz_role"?"2200000000000000218":"2200000000000000008")})).ToHashSet(StringComparer.Ordinal);
        var actual=properties.Where(property=>expected.Contains(property.Name)||provisional.Any(parent=>property.Name.StartsWith("indexfield:"+parent+":",StringComparison.Ordinal))).Select(property=>property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(9,actual.Count);Assert.True(expected.SetEquals(actual));
        Assert.DoesNotContain(properties,property=>provisional.Contains(property.Value.GetString(),StringComparer.Ordinal));
    }
    /// <summary>只模拟既有目标列上缺这三索引，预览必须恰好三ADD INDEX；它不是现场结构反查验收。</summary>
    [Fact]
    public void Offline_preview_only_adds_three_indexes_and_repeated_preview_is_empty()
    {
        var (catalog,_)=Load();var desired=MySqlSchemaPlanner.Compile(catalog,"offline_security_preview");var names=Expected.Select(value=>value.Index).ToHashSet(StringComparer.Ordinal);
        var before=desired with{Tables=desired.Tables.Select(table=>table with{Indexes=table.Indexes!.Where(index=>!names.Contains(index.Name)).ToArray()}).ToArray()};
        var changes=MySqlSchemaPlanner.Compare(before,desired);Assert.Equal(3,changes.Count);Assert.All(changes,change=>Assert.Equal("addIndex",change.Kind));
        foreach(var expected in Expected)Assert.Contains(changes,change=>change.Table==expected.Table&&change.Sql=="ALTER TABLE `"+expected.Table+"` ADD INDEX `"+expected.Index+"` (`"+expected.First+"`, `delete_no`)");
        Assert.Empty(MySqlSchemaPlanner.Compare(desired,desired));Assert.Equal(MySqlSchemaPlanner.Fingerprint(before),MySqlSchemaPlanner.Fingerprint(before with{Tables=before.Tables.Reverse().ToArray()}));
        Assert.All(changes,change=>{Assert.DoesNotContain("DROP",change.Sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("UNIQUE",change.Sql,StringComparison.OrdinalIgnoreCase);});
    }
    /// <summary>已有同名索引若列顺序变化必须明确拒绝，不能静默重建或假装只需追加。</summary>
    [Fact]
    public void Existing_same_name_with_other_column_order_is_not_silently_replaced()
    {
        var (catalog,_)=Load();var desired=MySqlSchemaPlanner.Compile(catalog,"offline_security_preview");var target=Expected[0];
        var current=desired with{Tables=desired.Tables.Select(table=>table.Name!=target.Table?table:table with{Indexes=table.Indexes!.Select(index=>index.Name!=target.Index?index:index with{Columns=index.Columns.Reverse().ToArray()}).ToArray()}).ToArray()};
        Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compare(current,desired));
    }
    /// <summary>仓库自身是该冻结平台测试的输入；不依赖原D盘源码或私有数据库配置。</summary>
    private static (MetadataCatalog Catalog,string Root) Load()
    {
        for(var current=new DirectoryInfo(AppContext.BaseDirectory);current is not null;current=current.Parent)
        {
            var path=Path.Combine(current.FullName,"backend/metadata/platform.json");if(File.Exists(path)&&File.Exists(Path.Combine(current.FullName,"scripts/build-platform-metadata.mjs")))
                return(JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!,current.FullName);
        }
        throw new InvalidOperationException("未找到仓库平台元数据测试输入。");
    }
    /// <summary>只读取固定元数据键，不采纳客户端列名或SQL。</summary>
    private static string Text(JsonElement value,string key)=>value.GetProperty(key).ToString();
}