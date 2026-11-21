using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原媒体字段的详情编辑与列表文本筛选是独立用途。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class OriginalMediaFilterPackageTests
{
    /// <summary>两种存储均生成完整包，沿用原文本控件和数据库查询，导出继续携带相同筛选。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Full_package_supports_text_search_on_original_media_fields(int redundant)
    {
        var catalog = redundant == 0 ? NonRedundantApplicationFixture.Catalog("mt_media_filter") : GeneratorTests.Catalog();
        var table = catalog.ConfigTables.Single(table => table.Id == "1");
        table.IsRongyu = redundant;
        var page = catalog.ConfigPages.Single(page => page.Id == "101");
        page.IsExport = 1;
        var fields = AddMediaFields(catalog, page);

        var model = GenerationModel.Create(catalog, "Example.MediaFilter");
        Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model), gap =>
            gap.PageId == page.Id && gap.Usage == "filter" && fields.Any(field => field.ColumnName == gap.Column));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-media-filter");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var package = new ApplicationPackager().Create(catalog, directory, model.Namespace, NonRedundantApplicationFixture.Root());
            Assert.True(package.FrontendIncluded);
            var list = await File.ReadAllTextAsync(Path.Combine(directory, $"frontend/pages/yzp/{table.TableName}-list-{page.Id}.vue"));
            var entity = model.Tables.Single(table => table.Source.Id == page.ConfigTableId).Name;
            var service = await File.ReadAllTextAsync(Path.Combine(directory, $"backend/generated/Services/{entity}Service.g.cs"));
            foreach (var column in fields)
            {
                Assert.Contains($"v-model='filters.{GenerationModel.Camel(column.ColumnName)}' confirm-type='search' @confirm='filterList'", list);
                Assert.Contains(redundant == 0
                    ? $"new(\"{column.ColumnName}\",FixedReadComparison.LikePattern,"
                    : $"StringNameQuery.ApplyPattern(session,query,entity => entity.{GenerationModel.Pascal(column.ColumnName)},value!)", service);
            }

            // 同一页的 Export 应在原过滤入口基础上读取数据，不能只在列表使用筛选。
            var exportStart = service.IndexOf("Export" + page.Id + "Async(PageRequest request", StringComparison.Ordinal);
            Assert.True(exportStart >= 0);
            Assert.Contains((redundant == 0 ? "ReadFilters" : "Query") + page.Id + "(request,actor", service[exportStart..].Replace(" ", ""));
            await NonRedundantApplicationFixture.BuildAsync(directory, "restore");
            await NonRedundantApplicationFixture.BuildAsync(directory, "build");
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>页级改为动态下拉时不借原媒体文本规则放行未接线的候选。</summary>
    [Fact]
    public void Media_filter_does_not_admit_unproven_list_override()
    {
        var catalog = GeneratorTests.Catalog();
        var page = catalog.ConfigPages.Single();
        var field = AddMediaFields(catalog, page).First();
        catalog.ConfigPageColumns.Single(entry => entry.ConfigColumnId == field.Id).Extra["controlKind"] =
            JsonSerializer.SerializeToElement("List");

        var model = GenerationModel.Create(catalog, "Example.MediaFilter");
        Assert.Contains(GenerationCapabilityAudit.Collect(model), gap =>
            gap.PageId == page.Id && gap.Column == field.ColumnName && gap.Usage == "filter");
    }

    /// <summary>使用原控件和实际可写字符串列生成合成元数据。</summary>
    private static List<ColumnMetadata> AddMediaFields(MetadataCatalog catalog, PageMetadata page)
    {
        var result = new List<ColumnMetadata>();
        foreach (var kind in new[] { "Image", "MultiImage", "Upload", "Video", "Base64Image", "IntenetImage" })
        {
            var column = new ColumnMetadata
            {
                Id = (160 + result.Count).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConfigTableId = page.ConfigTableId,
                YzProjectId = catalog.YzProjectId,
                ColumnName = "media_" + kind.ToLowerInvariant(),
                Name = kind,
                LogicalType = "string",
                ControlKind = kind
            };
            result.Add(column);
            catalog.ConfigColumns.Add(column);
            catalog.ConfigPageColumns.Add(new()
            {
                Id = page.Id + column.Id,
                YzProjectId = catalog.YzProjectId,
                ConfigPageId = page.Id,
                ConfigColumnId = column.Id,
                IsShow = 1,
                IsEdit = 1,
                Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) }
            });
        }
        return result;
    }
}
