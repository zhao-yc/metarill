using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>已保存的旧List按原目标回退编译为实际候选、派生名称及写入范围。</summary>
public sealed class LegacyListTargetGenerationTests
{
    /// <summary>验证原页面、候选权限、派生查询及完整包构建，元数据不被改写。</summary>
    [Theory]
    [InlineData("joinTable", 0)]
    [InlineData("stem", 0)]
    [InlineData("linkedPage", 0)]
    [InlineData("joinTable", 1)]
    public async Task Legacy_list_compiles_into_its_published_target(string mode, int storage)
    {
        var catalog = Catalog(mode, storage);
        var before = JsonSerializer.Serialize(catalog);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-legacy-list-target");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            const string ns = "Example.LegacyList";
            Assert.True(new ApplicationPackager().Create(catalog, output, ns, NonRedundantApplicationFixture.Root()).FrontendIncluded);
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Controllers/LegacyListOrderPage101Controller.g.cs"));
            Assert.Contains("""[HttpPost("findNameIdCustomer15/page")]""", controller);
            Assert.Contains("""authorization.RequirePagePermission("201", "list");""", controller);
            var service = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Services/LegacyListOrderService.g.cs"));
            Assert.Contains("DerivedOptionsPlan101_15", service);
            Assert.Contains("ValidateRelations101Async", service);
            Assert.Contains("OptionsCondition101_15", service);
            Assert.Contains("ImportRelation101_15Async", service);
            Assert.Contains("DerivedOptionsRows101_15Async", service);
            var vue = await File.ReadAllTextAsync(Path.Combine(output, "frontend/pages/yzp/legacy_list_order-detail-101.vue"));
            Assert.Contains("<zxz-uni-data-select v-model='entity.customerId'", vue);
            foreach (var command in new[] { "restore", "build" }) await NonRedundantApplicationFixture.BuildAsync(output, command);

            var context = new AssemblyLoadContext("LegacyList-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                using var binary = File.OpenRead(Path.Combine(output, "backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
                var type = context.LoadFromStream(binary).GetType(ns + ".Generated.Services.LegacyListOrderService", true)!;
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            finally { context.Unload(); }
            Assert.Equal(before, JsonSerializer.Serialize(catalog));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>显式失效的表或关联页不能降级到旧同名目标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_explicit_target_does_not_use_the_legacy_alias(bool page)
    {
        var catalog = Catalog("joinTable", 0);
        var source = catalog.ConfigColumns.Single(column => column.Id == "15");
        if (page) source.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("999");
        else source.ControlDetail = """{"tableId":"999","nameField":"company_name"}""";
        var output = Path.Combine(Path.GetTempPath(), "metarill-legacy-list-reject-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.LegacyRejected", NonRedundantApplicationFixture.Root()));
        Assert.False(Directory.Exists(output));
    }

    /// <summary>保留真实派生名称、目标权限及候选条件，只切换旧目标声明方式。</summary>
    private static MetadataCatalog Catalog(string mode, int storage)
    {
        var catalog = DerivedRelationNamesTests.Catalog("legacy_list", false);
        catalog.ConfigTables.Single(table => table.Id == "1").IsRongyu = storage;
        var target = catalog.ConfigTables.Single(table => table.Id == "2");
        var source = catalog.ConfigColumns.Single(column => column.Id == "15");
        source.LinkConfigTableId = "";
        source.Extra.Remove("linkConfigPageId");
        source.Extra.Remove("joinTable");
        source.ControlDetail = """{"table":"","tableId":"","nameField":"company_name","whereSql":"status=1"}""";
        if (mode == "joinTable")
            source.Extra["joinTable"] = JsonSerializer.SerializeToElement(target.TableName);
        else if (mode == "stem")
            target.TableName = source.ColumnName[..^3];
        else
            source.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("201");
        return catalog;
    }
}
