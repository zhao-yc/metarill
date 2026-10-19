using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>主子页使用真实元数据和生成 Vue，验证父范围、交互及写入隔离。</summary>
public sealed class GeneratedChildrenTests
{
    /// <summary>执行真正生成的页签、子列表、导航和弹层脚本，不以拼接字符串代替行为验证。</summary>
    [Fact]
    public async Task Generated_children_execute_saved_parent_popup_navigation_and_readonly_contracts()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-children"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = Catalog(); new CSharpGenerator().Generate(catalog, output, "Example.Children");
            var path = Path.Combine(output, "frontend/pages/yzp/customer-detail-101.vue"); var before = File.ReadAllText(path);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(before, "<uni-forms "));
            Assert.True(before.IndexOf("</uni-forms>", StringComparison.Ordinal) < before.IndexOf("<ChildList", StringComparison.Ordinal));
            Assert.Contains("v-show=\"currentChild === 0\"", before); Assert.Contains(":parent_id=\"id\"", before); Assert.Contains(":parentReadOnlyCondition=\"readOnlyCondition\"", before);
            catalog.ConfigChildren.Reverse(); new CSharpGenerator().Generate(catalog, output, "Example.Children"); Assert.Equal(before, File.ReadAllText(path));
            await RunProbe(output);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>已声明的非 Tab 位置沿用旧 v8 实际顺排行为，空打开方式默认为新页面。</summary>
    [Theory]
    [InlineData(null)] [InlineData("OneWithMany")] [InlineData("NewPage")]
    public async Task Legacy_non_tab_layout_stays_stacked(string? place)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-children"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = Catalog(); catalog.ConfigPages[0].Extra["childPlace"] = JsonSerializer.SerializeToElement(place);
            new CSharpGenerator().Generate(catalog, output, "Example.Children");
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/customer-detail-101.vue"));
            Assert.DoesNotContain("v-show=\"currentChild", detail); Assert.Contains(":child_page_open_type=\"1\"", detail);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(detail, "<ChildList").Count);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>未知配置、父范围错表、跨项目、循环和重复定义都在落盘前拒绝。</summary>
    [Theory]
    [InlineData("openType")] [InlineData("openFloat")] [InlineData("sort")] [InlineData("unknownProperty")]
    [InlineData("parentPage")] [InlineData("childPage")] [InlineData("noScope")] [InlineData("wrongTable")]
    [InlineData("project")] [InlineData("admin")] [InlineData("cycle")] [InlineData("duplicate")]
    [InlineData("placement")] [InlineData("readonlyExpression")] [InlineData("listEdit")] [InlineData("unknownType")]
    public async Task Invalid_child_definition_does_not_write_any_output(string scenario)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-children"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = Catalog(); var child = JsonNode.Parse(catalog.ConfigChildren[0].GetRawText())!;
            switch (scenario)
            {
                case "openType": child["childPageOpenType"] = 3; break;
                case "openFloat": child["childPageOpenType"] = 1.5; break;
                case "sort": child["sortNo"] = "bad"; break;
                case "unknownProperty": child["unknownFlag"] = true; break;
                case "parentPage": child["parentConfigPageId"] = "missing"; break;
                case "childPage": child["childConfigPageId"] = "missing"; break;
                case "noScope": catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = ""; break;
                case "wrongTable": catalog.ConfigColumns.Single(column => column.Id == "203").LinkConfigTableId = "2"; break;
                case "project": child["yzProjectId"] = "other"; break;
                case "admin": catalog.ConfigPages.Single(page => page.Id == "201").YzAdminTypeId = "other"; break;
                case "placement": catalog.ConfigPages[0].Extra["childPlace"] = JsonSerializer.SerializeToElement("Unknown"); break;
                case "readonlyExpression": catalog.ConfigPages[0].Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("locked || sideEffect()"); break;
                case "listEdit": catalog.ConfigPages.Single(page => page.Id == "201").Extra["isListEdit"] = JsonSerializer.SerializeToElement(1); break;
                case "unknownType": catalog.ConfigPages.Single(page => page.Id == "201").Extra["type"] = JsonSerializer.SerializeToElement(2); break;
                case "duplicate": catalog.ConfigChildren.Add(catalog.ConfigChildren[0]); break;
                case "cycle":
                    catalog.ConfigColumns.Add(new() { Id = "17", ConfigTableId = "1", ColumnName = "note_id", LogicalType = "id", LinkConfigTableId = "2" });
                    catalog.ConfigPages[0].WhereSql += " and note_id=@parent_id";
                    catalog.ConfigChildren.Add(JsonSerializer.SerializeToElement(new { id = "cycle", parentConfigPageId = "201", childConfigPageId = "101", yzProjectId = "100" })); break;
            }
            catalog.ConfigChildren[0] = JsonSerializer.SerializeToElement(child);
            Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(catalog, output, "Example.InvalidChildren")); Assert.False(Directory.Exists(output));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>覆盖原八条平台子定义，真实平台元数据必须能够完整渲染。</summary>
    [Fact]
    public async Task Platform_child_metadata_renders_all_existing_children()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-children"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "metadata/platform.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            new CSharpGenerator().Generate(catalog, output, "Example.Platform");
            foreach (var child in catalog.ConfigChildren)
            {
                var id = child.GetProperty("parentConfigPageId").GetString()!; var page = catalog.ConfigPages.Single(page => page.Id == id); var table = catalog.ConfigTables.Single(table => table.Id == page.ConfigTableId);
                var detail = File.ReadAllText(Path.Combine(output, $"frontend/pages/yzp/{table.TableName}-detail-{id}.vue"));
                Assert.Contains("ref=\"child_" + child.GetProperty("id").GetString() + "\"", detail);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>整型排序包含旧字符串数据；三个子页面复用同一已声明父外键。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog(); catalog.ConfigPages[0].IsCopy = 1;
        catalog.ConfigPages[0].Extra["childPlace"] = JsonSerializer.SerializeToElement("Tab"); catalog.ConfigPages[0].Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("locked");
        catalog.ConfigColumns.Add(new() { Id = "16", ConfigTableId = "1", ColumnName = "locked", LogicalType = "bool" });
        catalog.ConfigPageColumns.Add(new() { Id = "116", ConfigPageId = "101", ConfigColumnId = "16" });
        catalog.ConfigTables.Add(new() { Id = "2", TableName = "customer_note", Name = "客户备注" });
        foreach (var column in new[] {
            new ColumnMetadata { Id = "201", ColumnName = "id", LogicalType = "id" }, new() { Id = "202", ColumnName = "name", LogicalType = "string" },
            new() { Id = "203", ColumnName = "customer_id", LogicalType = "id", LinkConfigTableId = "1" }, new() { Id = "204", ColumnName = "yz_project_id", LogicalType = "id" },
            new() { Id = "205", ColumnName = "remark", LogicalType = "string", ControlKind = "RTF" } }) { column.ConfigTableId = "2"; catalog.ConfigColumns.Add(column); }
        foreach (var id in new[] { "201", "202", "203" })
        {
            catalog.ConfigPages.Add(new() { Id = id, ConfigTableId = "2", Name = "子页" + id, WhereSql = "customer_id=@parent_id and yz_project_id=@current_yz_project_id", IsCopy = 1, IsImport = 1 });
            foreach (var column in new[] { "202", "205" }) catalog.ConfigPageColumns.Add(new() { Id = id + column, ConfigPageId = id, ConfigColumnId = column, Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1), ["isSort"] = JsonSerializer.SerializeToElement(1) } });
        }
        catalog.ConfigChildren = [
            JsonSerializer.SerializeToElement(new { id = "cB", parentConfigPageId = "101", childConfigPageId = "201", sortNo = "10", childPageOpenType = 2, yzProjectId = "100" }),
            JsonSerializer.SerializeToElement(new { id = "cZ", parentConfigPageId = "101", childConfigPageId = "202", sortNo = 2, childPageOpenType = 1, yzProjectId = "100" }),
            JsonSerializer.SerializeToElement(new { id = "cA", parentConfigPageId = "101", childConfigPageId = "203", sortNo = "2", childPageOpenType = (int?)null, yzProjectId = "100" })
        ];
        return catalog;
    }

    /// <summary>定位仓库内模板和平台元数据，不依赖测试进程工作目录。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../.."));
    /// <summary>执行实际生成的 Vue 方法；HTTP 和设备环境为可观察测试边界。</summary>
    private static async Task RunProbe(string output, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "GeneratedChildrenBehaviorProbe.mjs")); start.ArgumentList.Add(output);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
