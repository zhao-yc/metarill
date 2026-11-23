using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class DatabaseCheckboxListOptionsGenerationTests
{
    /// <summary>生成完整包，执行原列表如何载入候选、冻结范围和丢弃迟到回执。</summary>
    [Theory]
    [InlineData("parent")]
    [InlineData("root")]
    [InlineData("form")]
    [InlineData("unbound")]
    [InlineData("redundant")]
    [InlineData("stored")]
    [InlineData("stored_global")]
    [InlineData("stored_dropdown")]
    [InlineData("stored_global_dropdown")]
    public async Task Original_checkbox_only_loads_candidates_with_its_published_scope(string scenario)
    {
        var stored = scenario.StartsWith("stored", StringComparison.Ordinal);
        var catalog = RedundantMultiNameCacheTests.Published(DatabaseCheckboxGenerationTests.Catalog(scenario != "redundant" && !stored));
        var checkbox = catalog.ConfigColumns.Single(column => column.Id == "25");
        var dropdown = scenario.EndsWith("_dropdown", StringComparison.Ordinal);
        if (!dropdown) checkbox.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
        if (stored)
        {
            ConfigureStoredCheckbox(catalog, scenario.StartsWith("stored_global", StringComparison.Ordinal));
            catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["queryDefaultValue"] =
                JsonSerializer.SerializeToElement(dropdown ? "9007199254740993" : """["9007199254740993"]""");
        }
        if (scenario == "root")
        {
            foreach (var page in catalog.ConfigPages)
                page.WhereSql = "yz_project_id=@current_yz_project_id";
            checkbox.ControlDetail = "原参数一;原参数二;;name desc";
        }

        if (scenario is "form" or "unbound")
        {
            checkbox.ControlDetail = "原参数一;原参数二;parent_id=@parentId AND category_id=@formParameters.relationSource.categoryId;name desc";
            catalog.ConfigColumns.AddRange([
                new() { Id = "61", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "category_id", LogicalType = "int64" },
                new() { Id = "62", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "category_id", LogicalType = "int64" }
            ]);
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "20161", ConfigPageId = "201", ConfigColumnId = "61", YzProjectId = catalog.YzProjectId, IsShow = 1,
                Extra = new()
                {
                    ["isSearch"] = JsonSerializer.SerializeToElement(scenario == "form" ? 1 : 0),
                    ["queryDefaultValue"] = JsonSerializer.SerializeToElement("9007199254740993")
                }
            });
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-checkbox-list-options");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, "Example.CheckboxListOptions",
                NonRedundantApplicationFixture.Root()).FrontendIncluded);
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Controllers/RelationSourcePage201Controller.g.cs"));
            Assert.Contains("[HttpPost(\"findNameIdTarget25\")]", controller);
            Assert.Contains("RelationOptionAuthorization.Require(authorization,\"201\"", controller);
            await RunVue(output, scenario);
            if (stored)
            {
                var service = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Services/RelationSourceService.g.cs"));
                Assert.Contains("CheckboxGlobalCondition201_25.BindAccount(actor)", service);
                Assert.Contains(dropdown ? "DatabaseCheckboxValue.ReadId(filter.Value)" : "DatabaseCheckboxValue.ReadSelected(filter.Value)", service);
                Assert.Contains(dropdown ? "ApplyJsonArrayHasId" : "ApplyJsonArrayHasAnyId", service);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>列表与生成查询使用同一合成门店字典，项目和门店分别存储。</summary>
    internal static void ConfigureStoredCheckbox(Metarill.Core.Metadata.MetadataCatalog catalog, bool globalSource)
    {
        var checkbox = catalog.ConfigColumns.Single(column => column.Id == "25");
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        if (globalSource)
            catalog.ConfigTables.Single(table => table.Id == "2").Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        // 全局字典候选按账号门店；记录仍以实际项目和门店存储及维护。
        foreach (var definition in new[] { (Table: "2", Column: "61", Page: "201"), (Table: "3", Column: "62", Page: "301") })
        {
            catalog.ConfigColumns.Add(new() { Id = definition.Column, ConfigTableId = definition.Table,
                YzProjectId = catalog.YzProjectId, ColumnName = "store_id", LogicalType = "int64" });
            catalog.ConfigPages.Single(page => page.Id == definition.Page).WhereSql = definition.Table == "2"
                ? "yz_project_id=@current_yz_project_id AND store_id=@current_store_id"
                : "store_id=@current_store_id";
        }
        checkbox.ControlDetail = """{"tableId":"3","pageId":"301","nameField":"name"}""";
    }

    /// <summary>真实挂载生成页面，捕获公共请求接口接收的参数。</summary>
    private static async Task RunVue(string output, string scenario, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "DatabaseCheckboxListOptionsProbe.mjs"));
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (Exception error) { await SharedSourceBuildGate.StopOwnProcessAsync(process, stdout, stderr, error); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
