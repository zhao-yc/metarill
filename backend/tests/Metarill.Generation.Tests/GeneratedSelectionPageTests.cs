using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成type=2页面执行选择草稿，不以多选页UI冒充多值关系完整支持。</summary>
public sealed class GeneratedSelectionPageTests
{
    [Fact]
    public void Selected_rows_contract_bounds_ids_and_rejects_dynamic_targets()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var json in new[] { "{\"table\":\"secret\"}", "{\"sql\":\"1=1\"}", "{\"projectId\":\"2\"}", "{\"targetParentId\":\"3\"}" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SelectionRowsRequest>(json, options));
        Assert.Equal(new long[] { 9007199254740993, 2 }, new SelectionRowsRequest { SelectedIds = ["9007199254740993", "2", "2"] }.ParseIds());
        foreach (var ids in new[] { new List<string> { "0" }, ["-1"], ["1 OR 1=1"], ["9223372036854775808"], Enumerable.Repeat("1", 201).ToList() })
            Assert.Throws<ArgumentException>(() => new SelectionRowsRequest { SelectedIds = ids }.ParseIds());
    }

    [Fact]
    public async Task Generated_type_two_page_preserves_row_array_confirmation_and_cancellation_contracts()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-selection-ui"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = RelationOptionsContractTests.Catalog(); catalog.ConfigPages.Single(page => page.Id == "301").Extra["type"] = JsonSerializer.SerializeToElement(2);
            new CSharpGenerator().Generate(catalog, output, "Example.SelectionUi");
            var target = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_target-list-301.vue"));
            Assert.Contains("multiSelecting: true", target); Assert.Contains("createSelectionPageMixin(\"/admin/auto/relation_target/301\",true)", target);
            Assert.Contains("onPopSelect", File.ReadAllText(RuntimeFile("selectionPageMixin.js")));
            Assert.Contains("selection/selected", File.ReadAllText(Path.Combine(output, "Controllers/RelationTargetPage301Controller.g.cs")));
            Assert.DoesNotContain("selection/selected", File.ReadAllText(Path.Combine(output, "Controllers/RelationParentPage101Controller.g.cs")));
            await RunProbe(output);
            // 仍未完成的多值名称派生/传播不得因选择页可用而静默改为可写控件。
            catalog.ConfigColumns.Single(column => column.Id == "25").ControlKind = "MultiSelectPage";
            catalog.ConfigColumns.Single(column => column.Id == "25").LogicalType = "string";
            new CSharpGenerator().Generate(catalog, output, "Example.SelectionUi");
            using var gaps = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "generation-capabilities.json")));
            Assert.Contains(gaps.RootElement.GetProperty("unsupportedControls").EnumerateArray(), item => item.GetProperty("column").GetString() == "target_id");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>固定源码位置，避免测试复制一份实现自行变绿。</summary>
    private static string RuntimeFile(string name, [CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../../frontend/metarill_uni_admin/common/yzp_runtime", name));
    /// <summary>Node执行真实生成Vue脚本，仅替换网络和宿主事件。</summary>
    private static async Task RunProbe(string output, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "GeneratedSelectionPageProbe.mjs")); start.ArgumentList.Add(output);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
