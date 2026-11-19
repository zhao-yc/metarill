using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class CheckboxFilterDefaultsGenerationTests
{
    /// <summary>实际完整包的 Vue 首次请求、原复选绑定和重置共享同一个源字段和数组合同。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkbox_defaults_keep_typed_options_and_the_source_key_in_first_request(bool nonRedundant)
    {
        var catalog = RedundantMultiNameCacheTests.Published(nonRedundant
            ? NonRedundantMultiSelectionTests.Catalog() : GeneratedMultiSelectContractTests.Catalog());
        var relation = catalog.ConfigColumns.Single(column => column.Id == "25");
        var relationField = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == "25");
        relation.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
        relationField.Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        relationField.Extra["queryDefaultValue"] = JsonSerializer.SerializeToElement("""["1","9007199254740993"]""");
        Add("51", "billing_month", "string", "month", "本月|2026-10,下月|2026-11", "today");
        Add("52", "event_time", "datetime", "date_time", "上午|2026-10-20 09:30:00,下午|2026-10-20 17:15:00,昨天|2026-10-19", "today");
        Add("53", "priority", "int32", "ConstList", "高|2,低|1", "2");
        Add("54", "reference_no", "int64", "ConstList", "甲|9007199254740993,乙|9007199254740994", "9007199254740993");
        Add("55", "is_active", "bool", "CheckBox", "", nonRedundant ? "true" : "0");

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-checkbox-filter-defaults");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, "Example.FilterDefaults",
                NonRedundantApplicationFixture.Root()).FrontendIncluded);
            await RunVue(output, nonRedundant);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }

        void Add(string id, string name, string type, string kind, string detail, string value)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = id, ConfigTableId = "2", YzProjectId = catalog.YzProjectId,
                ColumnName = name, Name = name, LogicalType = type, ControlKind = kind, ControlDetail = detail,
                Extra = new() { ["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox") }
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "10" + id, ConfigPageId = "201", ConfigColumnId = id, YzProjectId = catalog.YzProjectId, IsShow = 1,
                Extra = new()
                {
                    ["isSearch"] = JsonSerializer.SerializeToElement(1),
                    ["queryDefaultValue"] = JsonSerializer.SerializeToElement(value)
                }
            });
        }
    }

    /// <summary>挂载原模板生成的实际 Vue，网络调用由本地测试夹具记录。</summary>
    private static async Task RunVue(string output, bool nonRedundant, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "CheckboxFilterDefaultsProbe.mjs"));
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(nonRedundant ? "nonredundant" : "redundant");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (Exception error) { await SharedSourceBuildGate.StopOwnProcessAsync(process, stdout, stderr, error); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
