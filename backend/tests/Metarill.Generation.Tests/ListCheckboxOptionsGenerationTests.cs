using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class ListCheckboxOptionsGenerationTests
{
    /// <summary>原关联下拉的显式复选只读取本页发布且经过原权限约束的候选。</summary>
    [Theory]
    [InlineData("same")]
    [InlineData("root")]
    [InlineData("nonredundant")]
    [InlineData("hidden")]
    [InlineData("independent")]
    [InlineData("unbound")]
    [InlineData("form")]
    [InlineData("catalog")]
    public async Task Original_list_checkbox_uses_legacy_array_scope(string scenario)
    {
        var fixedCatalog = scenario == "catalog";
        var catalog = fixedCatalog ? TemplateVersionCatalogTests.Catalog()
            : RedundantMultiNameCacheTests.Published(RelationOptionsContractTests.Catalog());
        var sourceColumn = catalog.ConfigColumns.Single(column => column.Id == (fixedCatalog ? TemplateVersionCatalog.VersionColumnId : "25"));
        var sourcePage = catalog.ConfigPages.Single(page => page.ConfigTableId == sourceColumn.ConfigTableId);
        var field = catalog.ConfigPageColumns.Single(column => column.ConfigPageId == sourcePage.Id && column.ConfigColumnId == sourceColumn.Id);
        sourceColumn.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
        field.Extra["isSearch"] = JsonSerializer.SerializeToElement(1);

        if (scenario == "root")
        {
            sourcePage.WhereSql = "yz_project_id=@current_yz_project_id";
            catalog.ConfigPages.Single(page => page.Id == "301").WhereSql = sourcePage.WhereSql;
            sourceColumn.ControlDetail = """{"tableId":"3","nameField":"name"}""";
        }
        if (scenario == "nonredundant") catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 0;
        if (scenario == "hidden") field.IsShow = field.IsEdit = 0;
        if (scenario is "independent" or "unbound")
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "28", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "source_owner_id",
                LogicalType = "int64", LinkConfigTableId = "2"
            });
            sourcePage.WhereSql = "source_owner_id=@parentId AND yz_project_id=@current_yz_project_id";
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "2026", ConfigPageId = sourcePage.Id, ConfigColumnId = "26", YzProjectId = catalog.YzProjectId,
                Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(scenario == "independent" ? 1 : 0) }
            });
        }
        if (scenario == "form")
        {
            sourceColumn.ControlDetail =
                """{"tableId":"3","nameField":"name","whereSql":"parent_id=@parentId AND category_id=@formParameters.relationSource.categoryId"}""";
            catalog.ConfigColumns.AddRange([
                new() { Id = "61", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "category_id", LogicalType = "int64" },
                new() { Id = "62", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "category_id", LogicalType = "int64" }
            ]);
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "20161", ConfigPageId = sourcePage.Id, ConfigColumnId = "61", YzProjectId = catalog.YzProjectId,
                Extra = new()
                {
                    ["isSearch"] = JsonSerializer.SerializeToElement(1),
                    ["queryDefaultValue"] = JsonSerializer.SerializeToElement("9007199254740993")
                }
            });
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-list-checkbox-options");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var root = NonRedundantApplicationFixture.Root();
            const string rootNamespace = "Example.ListCheckboxOptions";
            if (fixedCatalog)
            {
                // 平台目录属于自举配置，直接生成并装配原前端，避免当作普通业务表合并。
                var generated = Path.Combine(output, "backend/generated");
                new CSharpGenerator().Generate(catalog, generated, rootNamespace);
                new FrontendPackager().Create(catalog, Path.Combine(generated, "frontend"), Path.Combine(output, "frontend"), root);
            }
            else
            {
                Assert.True(new ApplicationPackager().Create(catalog, output, rootNamespace, root).FrontendIncluded);
            }
            var table = fixedCatalog ? "yz_project" : "relation_source";
            var controller = await File.ReadAllTextAsync(Path.Combine(output,
                $"backend/generated/Controllers/{GenerationModel.Pascal(table)}Page{sourcePage.Id}Controller.g.cs"));
            var name = fixedCatalog ? "findNameIdYzYzpVersion" + TemplateVersionCatalog.VersionColumnId : "findNameIdTarget25";
            Assert.Contains($"[HttpPost(\"{name}\")]", controller);
            Assert.Contains("Actor(\"list\")", controller);
            if (!fixedCatalog) Assert.Contains("authorization.RequirePagePermission(\"301\", \"list\")", controller);
            if (scenario == "hidden")
                foreach (var command in new[] { "restore", "build" })
                    await NonRedundantApplicationFixture.BuildAsync(output, command);
            await RunVue(output, scenario, table, sourcePage.Id, name);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>实际挂载完整包页面，记录原复选及权限候选请求。</summary>
    private static async Task RunVue(string output, string scenario, string table, string page, string endpoint,
        [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "ListCheckboxOptionsProbe.mjs"));
        foreach (var argument in new[] { output, scenario, table, page, endpoint }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (Exception error) { await SharedSourceBuildGate.StopOwnProcessAsync(process, stdout, stderr, error); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
