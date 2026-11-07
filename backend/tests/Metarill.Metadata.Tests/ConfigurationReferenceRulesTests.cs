using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>精确结构扫描不推断 SQL，覆盖和越界目标不能制造虚假引用。</summary>
public sealed class ConfigurationReferenceRulesTests
{
    [Fact]
    public void List_and_select_page_merge_mapping_overrides_without_returning_raw_configuration()
    {
        var catalog = Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == "21");
        source.ControlKind = "List"; source.ControlDetail = "{\"tableId\":\"1\",\"nameField\":\"title\",\"whereSql\":\"SQL_PRIVATE_SENTINEL\"}";
        var field = catalog.ConfigPageColumns.Single(); field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"whereSql\":\"PAGE_PRIVATE_SENTINEL\"}");
        var result = ConfigurationReferenceRules.Build(catalog, "11");
        Assert.Contains(result.Controls, reference => reference.ColumnId == "21" && reference.SourceType == "column" && reference.ReferencePath == "nameField");
        Assert.Contains(result.Controls, reference => reference.PageColumnId == "31" && reference.SourceType == "pageColumn" && reference.ReferencePath == "nameField");
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("SQL_PRIVATE_SENTINEL", serialized); Assert.DoesNotContain("PAGE_PRIVATE_SENTINEL", serialized);
        source.ControlKind = "MultiSelectPage"; source.ControlDetail = "{\"pageId\":\"100\",\"nameField\":\"title\"}";
        Assert.Equal(2, ConfigurationReferenceRules.Build(catalog, "11").Controls.Count);
        field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"nameField\":\"other\"}");
        Assert.Single(ConfigurationReferenceRules.Build(catalog, "11").Controls);
    }

    [Fact]
    public void Explicit_foreign_targets_never_fall_back_to_a_same_named_current_project_table()
    {
        var catalog = Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == "21");
        source.ControlKind = "List"; source.ControlDetail = "{\"tableId\":\"999\",\"table\":\"target_table\",\"nameField\":\"title\"}";
        Assert.Empty(ConfigurationReferenceRules.Build(catalog, "11").Controls);
        source.ControlKind = "SelectPage"; source.ControlDetail = "{\"pageId\":\"999\",\"nameField\":\"title\"}";
        Assert.Empty(ConfigurationReferenceRules.Build(catalog, "11").Controls);
        Assert.Throws<UnauthorizedAccessException>(() => ConfigurationReferenceRules.Build(catalog, "999"));
    }

    [Fact]
    public void Cascade_page_override_replaces_the_entire_base_definition_and_masks_parse_errors()
    {
        var catalog = Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == "21");
        source.ControlKind = "CascadeTree"; source.ControlDetail = "MALFORMED_PRIVATE_SENTINEL";
        var field = catalog.ConfigPageColumns.Single(); field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"target_table\",\"nameField\":\"title\",\"parentField\":\"parent_id\"}}");
        var result = ConfigurationReferenceRules.Build(catalog, "11");
        var reference = Assert.Single(result.Controls); Assert.Equal("pageColumn", reference.SourceType); Assert.Equal("selfTree.nameField", reference.ReferencePath);
        Assert.Contains(result.Coverage, message => message.Contains("字段 21", StringComparison.Ordinal));
        Assert.DoesNotContain("MALFORMED_PRIVATE_SENTINEL", JsonSerializer.Serialize(result));
        source.ControlDetail = "{\"sourceMode\":\"crossTable\",\"levels\":[{\"table\":\"target_table\",\"idField\":\"id\",\"nameField\":\"title\"}]}";
        field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"sourceMode\":\"crossTable\",\"levels\":[{\"table\":\"source_table\",\"nameField\":\"relation_id\"}]}");
        Assert.Equal("column", Assert.Single(ConfigurationReferenceRules.Build(catalog, "11").Controls).SourceType);
    }

    [Fact]
    public void Direct_pages_indexes_and_single_join_mappings_preserve_precise_identifiers()
    {
        var catalog = Catalog();
        catalog.ConfigPageColumns.Add(new() { Id = "32", ConfigPageId = "100", ConfigColumnId = "11", Name = "目标字段", YzProjectId = "1" });
        catalog.ConfigIndexes = [JsonSerializer.SerializeToElement(new { id = "41", configTableId = "1", indexName = "yzp_idx_title", isEnabled = 1, yzProjectId = "1" })];
        catalog.ConfigIndexColumns = [JsonSerializer.SerializeToElement(new { id = "42", configIndexId = "41", configColumnId = "11", columnName = "title", sortNo = 10, sortDirection = "ASC", yzProjectId = "1" }),
            JsonSerializer.SerializeToElement(new { id = "43", configIndexId = "41", configColumnId = "999", columnName = "title", sortNo = 20, yzProjectId = "1" })];
        var relation = catalog.ConfigColumns.Single(column => column.Id == "21"); relation.LinkConfigTableId = "1";
        var joined = new ColumnMetadata { Id = "22", ConfigTableId = "2", ColumnName = "relation_title", YzProjectId = "1" };
        joined.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1); joined.Extra["columnFullName"] = JsonSerializer.SerializeToElement("relation.title"); catalog.ConfigColumns.Add(joined);
        var result = ConfigurationReferenceRules.Build(catalog, "11");
        Assert.Equal("32", Assert.Single(result.Pages).PageColumnId); Assert.Equal("42", Assert.Single(result.Indexes).IndexColumnId);
        Assert.Equal("columnFullName", Assert.Single(result.Controls).ReferencePath);
        joined.Extra["columnFullName"] = JsonSerializer.SerializeToElement("relation.deep.title");
        Assert.Empty(ConfigurationReferenceRules.Build(catalog, "11").Controls);
        catalog.ConfigTables.Single(table => table.Id == "1").YzProjectId = "2";
        Assert.Throws<UnauthorizedAccessException>(() => ConfigurationReferenceRules.Build(catalog, "11"));
    }

    /// <summary>只包含明确结构定义，名字相似的字段或原文 SQL 都不能成为命中。</summary>
    private static MetadataCatalog Catalog() => new()
    {
        YzProjectId = "1",
        ConfigTables = [new() { Id = "1", TableName = "target_table", YzProjectId = "1" }, new() { Id = "2", TableName = "source_table", YzProjectId = "1" }],
        ConfigColumns = [new() { Id = "10", ConfigTableId = "1", ColumnName = "id", LogicalType = "int64", YzProjectId = "1" }, new() { Id = "11", ConfigTableId = "1", ColumnName = "title", Name = "标题", YzProjectId = "1" },
            new() { Id = "20", ConfigTableId = "2", ColumnName = "id", LogicalType = "int64", YzProjectId = "1" }, new() { Id = "21", ConfigTableId = "2", ColumnName = "relation_id", YzProjectId = "1" }],
        ConfigPages = [new() { Id = "100", ConfigTableId = "1", YzProjectId = "1" }, new() { Id = "200", ConfigTableId = "2", YzProjectId = "1" }],
        ConfigPageColumns = [new() { Id = "31", ConfigPageId = "200", ConfigColumnId = "21", YzProjectId = "1" }]
    };
}
