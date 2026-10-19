using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>候选请求没有 SQL/动态目标入口，编译期固定关系目标和范围。</summary>
public sealed class RelationOptionsContractTests
{
    [Fact]
    public void Dedicated_requests_reject_dynamic_query_fields_and_bound_every_input()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var text in new[] { "{\"sql\":\"1=1\"}", "{\"table\":\"secret\"}", "{\"projectId\":\"2\"}", "{\"orderBy\":\"rand()\"}" })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RelationOptionsLegacyRequest>(text, options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RelationOptionsPageRequest>(text, options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RelationOptionsSelectedRequest>(text, options));
        }
        foreach (var request in new[] { new RelationOptionsPageRequest { PageSize = 101 }, new() { PageNo = 0 }, new() { PageNo = 1000001 }, new() { Keyword = new string('字', 101) } })
            Assert.Throws<ArgumentException>(request.Validate);
        new RelationOptionsPageRequest { Keyword = "%'_ OR 1=1 --" }.Validate();
        JsonSerializer.Deserialize<RelationOptionsLegacyRequest>("{\"formParameters\":{\"id\":\"7\"},\"parentId\":\"8\"}", options)!.Validate();
        Assert.Equal(new[] { 9007199254740993L }, new RelationOptionsSelectedRequest { SelectedIds = ["9007199254740993", "9007199254740993"] }.ParseIds());
        foreach (var ids in new[] { new List<string> { "1 OR 1=1" }, ["-1"], ["0"], ["9223372036854775808"], Enumerable.Repeat("1", 201).ToList() })
            Assert.Throws<ArgumentException>(() => new RelationOptionsSelectedRequest { SelectedIds = ids }.ParseIds());
    }

    [Fact]
    public void Page_overlay_and_explicit_target_page_compile_without_mutating_the_original_definition()
    {
        var catalog = Catalog(); var column = catalog.ConfigColumns.Single(column => column.Id == "25");
        var saved = column.ControlDetail;
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"nameField\":\"alias\",\"futureOption\":{\"keep\":true}}");
        var generated = Compile(catalog);
        Assert.Contains("entity.Alias.Contains(keyword)", generated); Assert.Contains("session.Select<RelationParent>()", generated);
        Assert.Contains("OptionsPage201_25Async", generated);
        Assert.Equal(saved, column.ControlDetail);
        column.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("101");
        Assert.Throws<InvalidOperationException>(() => Compile(catalog));
        column.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("301");
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"whereSql\":\"name like '%foo%'\"}");
        Assert.Throws<InvalidOperationException>(() => Compile(catalog));
    }

    [Fact]
    public void Independent_parent_definitions_compile_while_hidden_name_columns_and_unowned_targets_are_rejected()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.ConfigTableId == "3" && column.ColumnName == "parent_id").LinkConfigTableId = "2";
        var independent = Compile(catalog); Assert.Contains("session.Select<RelationSource>()", independent); Assert.Contains("out var targetParentKey", independent);
        catalog = Catalog(); catalog.ConfigPageColumns.RemoveAll(field => field.ConfigPageId == "301" && field.ConfigColumnId == "32");
        Assert.Throws<InvalidOperationException>(() => Compile(catalog));
        catalog = Catalog(); catalog.ConfigColumns.Single(column => column.ConfigTableId == "3" && column.ColumnName == "yz_project_id").ColumnName = "project_unknown";
        Assert.Throws<InvalidOperationException>(() => Compile(catalog));
        catalog = Catalog(); catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail = "{\"tableId\":\"3\",\"tableId\":\"1\"}";
        Assert.Throws<InvalidOperationException>(() => Compile(catalog));
    }

    /// <summary>从真实元数据模型编译源页关系，不通过测试替身拼接范围。</summary>
    private static string Compile(MetadataCatalog catalog)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-contract"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try { new CSharpGenerator().Generate(catalog, output, "Example.Relation"); return File.ReadAllText(Path.Combine(output, "Services/RelationSourceService.g.cs")); }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    /// <summary>三表共用同一真实父定义，目标页与控件各自声明父范围。</summary>
    internal static MetadataCatalog Catalog(string prefix = "relation")
    {
        var catalog = new MetadataCatalog { YzProjectId = "9007199254740993" };
        foreach (var (id, suffix) in new[] { ("1", "parent"), ("2", "source"), ("3", "target") })
        {
            catalog.ConfigTables.Add(new() { Id = id, TableName = prefix + "_" + suffix });
            var start = int.Parse(id) * 10;
            catalog.ConfigColumns.AddRange([new() { Id = (start + 1).ToString(), ConfigTableId = id, ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
                new() { Id = (start + 2).ToString(), ConfigTableId = id, ColumnName = "name", LogicalType = "string" },
                new() { Id = (start + 3).ToString(), ConfigTableId = id, ColumnName = "yz_project_id", LogicalType = "int64" },
                new() { Id = (start + 4).ToString(), ConfigTableId = id, ColumnName = "delete_no", LogicalType = "datetime" }]);
            if (id != "1") catalog.ConfigColumns.Add(new() { Id = id + "6", ConfigTableId = id, ColumnName = "parent_id", LogicalType = "int64", LinkConfigTableId = "1" });
            var page = new PageMetadata { Id = id + "01", ConfigTableId = id, WhereSql = "yz_project_id=@current_yz_project_id" + (id == "1" ? "" : " AND parent_id=@parentId") };
            catalog.ConfigPages.Add(page);
            catalog.ConfigPageColumns.Add(new() { Id = id + "001", ConfigPageId = page.Id, ConfigColumnId = (start + 2).ToString() });
        }
        catalog.ConfigColumns.Add(new() { Id = "35", ConfigTableId = "3", ColumnName = "alias", LogicalType = "string" });
        catalog.ConfigPageColumns.Add(new() { Id = "3002", ConfigPageId = "301", ConfigColumnId = "35" });
        catalog.ConfigColumns.Add(new() { Id = "25", ConfigTableId = "2", ColumnName = "target_id", LogicalType = "int64", LinkConfigTableId = "3", ControlKind = "List",
            ControlDetail = "{\"tableId\":\"3\",\"whereSql\":\"parent_id=@parentId\",\"nameField\":\"name\",\"futureOption\":{\"keep\":true}}", Extra = new() { ["linkConfigPageId"] = JsonSerializer.SerializeToElement("301") } });
        catalog.ConfigPageColumns.Add(new() { Id = "2002", ConfigPageId = "201", ConfigColumnId = "25" });
        return catalog;
    }
}
