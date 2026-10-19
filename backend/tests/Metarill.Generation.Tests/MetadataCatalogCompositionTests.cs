using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>物理模型组合必须在输出或连接数据库前拒绝保留表占用，并支持平台自举去重。</summary>
public sealed class MetadataCatalogCompositionTests
{
    [Fact]
    public void Merge_preserves_inputs_and_deduplicates_identical_platform_physical_definitions()
    {
        var infrastructure = Infrastructure();
        var application = GeneratorTests.Catalog();
        application.ConfigTables.AddRange(infrastructure.ConfigTables);
        application.ConfigColumns.AddRange(infrastructure.ConfigColumns);
        application.ConfigIndexes.AddRange(infrastructure.ConfigIndexes);
        application.ConfigIndexColumns.AddRange(infrastructure.ConfigIndexColumns);
        var before = JsonSerializer.Serialize(application);
        var original = JsonSerializer.Serialize(infrastructure);
        var merged = MetadataCatalogComposition.MergePhysical(application, infrastructure);
        Assert.Equal(2, merged.ConfigTables.Count);
        Assert.Equal(application.ConfigColumns.Count, merged.ConfigColumns.Count);
        Assert.Single(merged.ConfigIndexes);
        merged.ConfigTables[0].Name = "只修改副本";
        merged.ConfigColumns[0].Extra["test"] = JsonSerializer.SerializeToElement("副本扩展");
        Assert.Equal(before, JsonSerializer.Serialize(application));
        Assert.Equal(original, JsonSerializer.Serialize(infrastructure));
    }

    [Fact]
    public void Reserved_name_requires_same_id_fields_indexes_and_no_business_page()
    {
        var infrastructure = Infrastructure();
        var occupied = GeneratorTests.Catalog();
        occupied.ConfigTables[0].TableName = "yz_file_library";
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(occupied, infrastructure));
        var changed = Infrastructure();
        changed.ConfigColumns[1].ColumnLength = "200";
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(changed, infrastructure));
        var exposed = Infrastructure();
        exposed.ConfigPages.Add(new() { Id = "700", ConfigTableId = "500" });
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(exposed, infrastructure));
        var differentIndex = Infrastructure();
        differentIndex.ConfigIndexColumns[0] = JsonSerializer.SerializeToElement(new { id = "504", configIndexId = "503", configColumnId = "501", columnName = "id", sortNo = 0, sortDirection = "ASC" });
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(differentIndex, infrastructure));
    }

    [Fact]
    public void Unrelated_table_column_index_and_cross_kind_id_collisions_are_rejected()
    {
        var infrastructure = Infrastructure();
        var tableCollision = GeneratorTests.Catalog();
        tableCollision.ConfigTables[0].Id = "500";
        foreach (var column in tableCollision.ConfigColumns) column.ConfigTableId = "500";
        tableCollision.ConfigPages[0].ConfigTableId = "500";
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(tableCollision, infrastructure));
        var columnCollision = GeneratorTests.Catalog();
        columnCollision.ConfigColumns[0].Id = "501";
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(columnCollision, infrastructure));
        var indexCollision = GeneratorTests.Catalog();
        indexCollision.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new { id = "503", configTableId = "1", indexName = "business_index" }));
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(indexCollision, infrastructure));
        var crossKind = GeneratorTests.Catalog();
        crossKind.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new { id = "502", configTableId = "1", indexName = "cross_kind" }));
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(crossKind, infrastructure));
    }

    [Fact]
    public void Missing_or_duplicate_physical_ids_and_infrastructure_pages_are_rejected()
    {
        var duplicate = Infrastructure(); duplicate.ConfigIndexes.Add(duplicate.ConfigIndexes[0]);
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(GeneratorTests.Catalog(), duplicate));
        var exposed = Infrastructure(); exposed.ConfigPages.Add(new() { Id = "700", ConfigTableId = "500" });
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(GeneratorTests.Catalog(), exposed));
    }

    /// <summary>最小文件基础模型包含完整索引闭包，字段定义只用于组合契约测试。</summary>
    private static MetadataCatalog Infrastructure() => new()
    {
        ConfigTables = [new() { Id = "500", TableName = "yz_file_library" }],
        ConfigColumns = [new() { Id = "501", ConfigTableId = "500", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 }, new() { Id = "502", ConfigTableId = "500", ColumnName = "name", LogicalType = "string", ColumnLength = "255" }],
        ConfigIndexes = [JsonSerializer.SerializeToElement(new { id = "503", configTableId = "500", indexName = "ix_file_name", isUnique = 0, isEnabled = 1, indexType = "INDEX" })],
        ConfigIndexColumns = [JsonSerializer.SerializeToElement(new { id = "504", configIndexId = "503", configColumnId = "502", columnName = "name", sortNo = 0, sortDirection = "ASC" })]
    };
}
