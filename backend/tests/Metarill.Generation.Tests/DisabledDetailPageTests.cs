using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class DisabledDetailPageTests
{
    private const string RootNamespace = "Example.DisabledDetail";
    private const string Customer = "pages/yzp/customer";
    private const string Child = "pages/yzp/customer_note";

    /// <summary>三种移动布局和原桌面列表都保留数据入口；开关关闭后不输出详情及调用。</summary>
    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "\"1\"")]
    [InlineData(3, "true")]
    public async Task Published_list_respects_the_existing_detail_switch(int layout, string switchValue)
    {
        var workspace = Workspace(out var boundary);
        try
        {
            var catalog = RedundantMultiNameCacheTests.Published(GeneratorTests.Catalog());
            var page = Assert.Single(catalog.ConfigPages);
            page.IsNew = 1;
            page.IsCopy = 1;
            page.IsImport = 1;
            page.Extra["isNotGenerateDetailPage"] = JsonSerializer.Deserialize<JsonElement>(switchValue);
            page.Extra["mobileListPageStyleType"] = JsonSerializer.SerializeToElement(layout);
            page.Extra["isListEdit"] = JsonSerializer.SerializeToElement(1);
            var root = NonRedundantApplicationFixture.Root();
            Assert.True(new ApplicationPackager().Create(catalog, workspace, RootNamespace, root).FrontendIncluded);

            CheckDisabled(workspace, Customer, "101");
            var list = File.ReadAllText(Path.Combine(workspace, "frontend", Customer + "-list-101.vue"));
            Assert.Contains("<uni-table", list);
            Assert.Contains("@click='initImport'", list);
            Assert.Contains("filters.name", list);
            var layoutClass = layout switch { 1 => "<uni-list-item v-for", 2 => "class='yz-row' v-for", _ => "class='yz-row3' v-for" };
            Assert.Contains(layoutClass, list);
            Assert.False(File.Exists(Path.Combine(workspace, "backend/generated/frontend", Customer + "-detail-101.vue")));
            var controller = File.ReadAllText(Path.Combine(workspace, "backend/generated/Controllers/CustomerPage101Controller.g.cs"));
            Assert.Contains("[HttpPost(\"page\")]", controller);
            Assert.Contains("[HttpGet(\"detail/{id}\")]", controller);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(workspace, boundary); }
    }

    /// <summary>从已发布项目关闭详情时清理受管产物，重新打开可恢复列表、详情和主子关系。</summary>
    [Fact]
    public async Task Updating_a_published_child_page_removes_and_restores_only_its_detail()
    {
        var workspace = Workspace(out var boundary);
        try
        {
            var target = Path.Combine(workspace, "target");
            var candidate = Path.Combine(workspace, "candidate");
            var enabled = Path.Combine(workspace, "enabled");
            var catalog = RedundantMultiNameCacheTests.Published(GeneratedChildrenTests.Catalog());
            var child = catalog.ConfigPages.Single(page => page.Id == "201");
            child.Extra["isListEdit"] = JsonSerializer.SerializeToElement(1);
            var root = NonRedundantApplicationFixture.Root();
            var packager = new ApplicationPackager();
            packager.Create(catalog, target, RootNamespace, root);
            var path = Path.Combine(target, "frontend", Child + "-detail-201.vue");
            var generatedDetail = Path.Combine(target, "backend/generated/frontend", Child + "-detail-201.vue");
            var original = File.ReadAllText(path);

            child.Extra["isNotGenerateDetailPage"] = JsonSerializer.SerializeToElement(1);
            packager.Create(catalog, candidate, RootNamespace, root);
            var updater = new LocalGenerationUpdater();
            var plan = updater.Preview(target, candidate, ["generate-project-config", "generate-all-page"]);
            Assert.True(plan.CanApply);
            Assert.Equal("committed", updater.Apply(plan, plan.PlanSha256).Status);
            CheckDisabled(target, Child, "201");
            Assert.False(File.Exists(generatedDetail));
            Assert.True(File.Exists(Path.Combine(target, "frontend", Child + "-detail-202.vue")));
            var parentDetail = File.ReadAllText(Path.Combine(target, "frontend", Customer + "-detail-101.vue"));
            Assert.Contains(Child + "-list-201.vue", parentDetail);
            var childList = File.ReadAllText(Path.Combine(target, "frontend", Child + "-list-201.vue"));
            Assert.Contains("parent_id", childList);
            Assert.Contains("default:true", childList);

            child.Extra["isNotGenerateDetailPage"] = JsonSerializer.SerializeToElement(0);
            packager.Create(catalog, enabled, RootNamespace, root);
            plan = updater.Preview(target, enabled, ["generate-project-config", "generate-all-page"]);
            Assert.True(plan.CanApply);
            Assert.Equal("committed", updater.Apply(plan, plan.PlanSha256).Status);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.True(File.Exists(generatedDetail));
            Assert.Contains(Child + "-detail-201.vue", File.ReadAllText(Path.Combine(target, "frontend", Child + "-list-201.vue")));
            Assert.Contains(Child + "-detail-201", RoutePaths(target, "pages.json"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(workspace, boundary); }
    }

    /// <summary>详情关掉不会产生孤儿资源，完整包路由与列表一致。</summary>
    private static void CheckDisabled(string output, string stem, string page)
    {
        var detail = stem + "-detail-" + page;
        var list = stem + "-list-" + page;
        foreach (var routeFile in new[] { "pages.generated.json", "pages.json" })
        {
            var paths = RoutePaths(output, routeFile).ToArray();
            Assert.Contains(list, paths);
            Assert.DoesNotContain(detail, paths);
        }
        Assert.False(File.Exists(Path.Combine(output, "frontend", detail + ".vue")));
        var source = File.ReadAllText(Path.Combine(output, "frontend", list + ".vue"));
        Assert.DoesNotContain(detail, source);
        Assert.DoesNotContain("toDetail(", source);
        Assert.DoesNotContain("openChildDetailPopup", source);
    }

    private static IEnumerable<string?> RoutePaths(string output, string routeFile)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "frontend", routeFile)));
        return json.RootElement.GetProperty("pages").EnumerateArray().Select(route => route.GetProperty("path").GetString()).ToArray();
    }

    private static string Workspace(out string boundary)
    {
        boundary = Path.Combine(Path.GetTempPath(), "metarill-disabled-detail");
        return Path.Combine(boundary, Guid.NewGuid().ToString("N"));
    }
}
