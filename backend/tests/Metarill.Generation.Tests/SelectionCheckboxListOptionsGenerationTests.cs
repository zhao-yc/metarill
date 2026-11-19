using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class SelectionCheckboxListOptionsGenerationTests
{
    /// <summary>实际生成原列表，核对多选目标页权限、分页、父范围及精确名称。</summary>
    [Theory]
    [InlineData("parent")]
    [InlineData("root")]
    [InlineData("independent")]
    [InlineData("unbound")]
    [InlineData("typed")]
    [InlineData("shared")]
    [InlineData("redundant")]
    public async Task Original_checkbox_reads_authorized_published_selection_options(string scenario)
    {
        var catalog = RedundantMultiNameCacheTests.Published(scenario == "redundant"
            ? GeneratedMultiSelectContractTests.Catalog() : NonRedundantMultiSelectionTests.Catalog());
        var field = catalog.ConfigColumns.Single(column => column.Id == "25");
        field.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
        catalog.ConfigPageColumns.Single(column => column.ConfigPageId == "201" && column.ConfigColumnId == "25")
            .Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        var targetPage = catalog.ConfigPages.Single(page => page.Id == "301");
        if (scenario == "root") targetPage.WhereSql = "yz_project_id=@current_yz_project_id";
        if (scenario is "independent" or "unbound")
        {
            sourcePage.WhereSql = "yz_project_id=@current_yz_project_id";
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "20126", ConfigPageId = sourcePage.Id, ConfigColumnId = "26", YzProjectId = catalog.YzProjectId, IsShow = 1,
                Extra = new()
                {
                    ["isSearch"] = JsonSerializer.SerializeToElement(scenario == "independent" ? 1 : 0),
                    ["queryDefaultValue"] = JsonSerializer.SerializeToElement("71")
                }
            });
        }
        if (scenario == "typed")
        {
            var name = catalog.ConfigColumns.Single(column => column.Id == "32");
            name.LogicalType = "decimal";
            name.Precision = 38;
            name.Scale = 10;
        }
        if (scenario == "shared")
        {
            catalog.ConfigColumns.AddRange([
                new() { Id = "56", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "backup_ids",
                    ColumnLength = "max", LogicalType = "string", ControlKind = field.ControlKind, ControlDetail = field.ControlDetail,
                    LinkConfigTableId = field.LinkConfigTableId, Extra = new(field.Extra) },
                new() { Id = "57", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "backup_names",
                    ColumnLength = "max", LogicalType = "string",
                    Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1),
                        ["columnFullName"] = JsonSerializer.SerializeToElement("backup.name") } }
            ]);
            catalog.ConfigPageColumns.AddRange([
                new() { Id = "20156", ConfigPageId = sourcePage.Id, ConfigColumnId = "56", YzProjectId = catalog.YzProjectId,
                    IsShow = 1, Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) } },
                new() { Id = "20157", ConfigPageId = sourcePage.Id, ConfigColumnId = "57", YzProjectId = catalog.YzProjectId, IsShow = 1 }
            ]);
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-selection-checkbox-list-options");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, "Example.SelectionCheckboxList",
                NonRedundantApplicationFixture.Root()).FrontendIncluded);
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Controllers/RelationTargetPage301Controller.g.cs"));
            Assert.Contains("[HttpPost(\"page\")]", controller);
            Assert.Contains("Actor(\"list\")", controller);
            if (scenario == "typed")
            {
                Assert.Contains("[HttpPost(\"selection/selected\")]", controller);
                Assert.Contains("item[\"name\"] =", controller);
            }
            await RunVue(output, scenario);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>挂载完整包的原 Vue，记录发给现有目标页的实际请求。</summary>
    private static async Task RunVue(string output, string scenario, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "SelectionCheckboxListOptionsProbe.mjs"));
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
