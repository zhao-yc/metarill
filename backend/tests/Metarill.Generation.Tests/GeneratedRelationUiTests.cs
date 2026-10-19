using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>只让已编译 List 使用固定分页控件；未知双父不能猜测父键或恢复全量数组查询。</summary>
public sealed class GeneratedRelationUiTests
{
    [Fact]
    public async Task Same_parent_list_uses_remote_wrapper_and_fixed_selected_label_mixin()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-ui"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = RelationOptionsContractTests.Catalog(); catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
            new CSharpGenerator().Generate(catalog, output, "Example.RelationUi");
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            var list = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-list-201.vue"));
            Assert.Contains("<yzp-relation-select", detail); Assert.Contains("endpoint=\"/admin/auto/relation_source/201/findNameIdRelationTarget25\"", detail);
            Assert.Contains(":same-parent=\"true\"", detail); Assert.Contains("draftFilters.targetId", list); Assert.Contains("createRelationOptionsMixin", list);
            Assert.DoesNotContain("this._optionRequests.push(yzp.request", detail + list); Assert.DoesNotContain("options.targetId || []", detail + list);
            Assert.Contains("\"field\":\"targetId\"", list); Assert.Contains("\"sameParent\":true", list);
            catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
            new CSharpGenerator().Generate(catalog, output, "Example.RelationUi"); detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains(":readonly=\"true\"", detail); Assert.DoesNotContain("@update:modelValue=\"change('targetId'", detail);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    [Fact]
    public async Task Independent_target_parent_requires_a_unique_readable_declared_foreign_key()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-ui"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = RelationOptionsContractTests.Catalog();
            catalog.ConfigColumns.Add(new() { Id = "28", ConfigTableId = "2", ColumnName = "source_owner_id", LogicalType = "int64", LinkConfigTableId = "2" });
            catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = "source_owner_id=@parentId AND yz_project_id=@current_yz_project_id";
            new CSharpGenerator().Generate(catalog, output, "Example.IndependentUi");
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("关联目标父范围尚未明确", detail); Assert.DoesNotContain("<yzp-relation-select", detail);
            using (var capabilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "generation-capabilities.json"))))
                Assert.Contains(capabilities.RootElement.GetProperty("unsupportedControls").EnumerateArray(), item => item.GetProperty("reason").GetString()!.Contains("targetParentId", StringComparison.Ordinal));
            // 真实当前记录开放了唯一目标父外键之后，才能明确绑定 entity.parentId，不经过索引等跨层推测。
            catalog.ConfigPageColumns.Add(new() { Id = "2026", ConfigPageId = "201", ConfigColumnId = "26", IsShow = 1, IsEdit = 1 });
            new CSharpGenerator().Generate(catalog, output, "Example.IndependentUi"); detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains(":target-parent-id=\"entity.parentId\"", detail); Assert.Contains(":same-parent=\"false\"", detail);
            catalog.ConfigColumns.Add(new() { Id = "29", ConfigTableId = "2", ColumnName = "other_parent_id", LogicalType = "int64", LinkConfigTableId = "1" });
            catalog.ConfigPageColumns.Add(new() { Id = "2029", ConfigPageId = "201", ConfigColumnId = "29", IsShow = 1 });
            new CSharpGenerator().Generate(catalog, output, "Example.IndependentUi"); detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("关联目标父范围尚未明确", detail);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }
}
