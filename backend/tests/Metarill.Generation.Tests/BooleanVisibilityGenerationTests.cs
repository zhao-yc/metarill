using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>字段、分组及行按钮应按相同布尔值规则渲染原界面。</summary>
public sealed class BooleanVisibilityGenerationTests
{
    [Theory]
    [InlineData("CheckBox")]
    [InlineData("ConstList")]
    [InlineData("ConstDownList")]
    public async Task Actual_original_fields_tabs_and_buttons_use_declared_boolean_types(string control)
    {
        var catalog = GeneratorTests.Catalog();
        var page = catalog.ConfigPages.Single();
        catalog.ConfigColumns.Add(new()
        {
            Id = "16", ConfigTableId = "1", ColumnName = "approval_flag", Name = "审批标志",
            LogicalType = "bool", ControlKind = control,
            ControlDetail = control == "CheckBox" ? "" : "同意|1,否决|0"
        });
        catalog.ConfigPageColumns.Add(new() { Id = "116", ConfigPageId = page.Id, ConfigColumnId = "16", IsShow = 1, IsEdit = 1 });
        var remark = catalog.ConfigColumns.Single(column => column.ColumnName == "remark");
        const string columnRule = """{"conditions":[{"field":"approval_flag","operator":"eq","value":true}]}""";
        const string buttonRule = """{"conditions":[{"field":"approvalFlag","operator":"in","valueList":[true]}]}""";
        remark.Extra["visibleCondition"] = JsonSerializer.SerializeToElement(columnRule);
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == remark.Id).Extra["detailGroupKey"] = JsonSerializer.SerializeToElement("review");
        page.Extra["detailLayout"] = JsonSerializer.SerializeToElement(
            """{"version":1,"mode":"tabs","groups":[{"key":"review","name":"审批备注"}]}""");
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new
        {
            id = "900", name = "审批动作", configPageId = page.Id, place = "EditAndGrid",
            visibleScheme = "Normal", visibleRuleJson = buttonRule, actionType = 1, saveOption = "NotSave"
        }));

        // 服务端记录值为 bool。对照两个原规则的真实执行结果。
        var column = ButtonVisibilityRule.Compile(columnRule);
        var button = ButtonVisibilityRule.Compile(buttonRule);
        Assert.True(column.Matches(new Dictionary<string, object?> { ["approval_flag"] = true }));
        Assert.False(button.Matches(new Dictionary<string, object?> { ["approvalFlag"] = false }));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-boolean-visibility");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, directory, "Example.BooleanVisibility");
            var start = NonRedundantApplicationFixture.Start("node", directory);
            start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(), "BooleanVisibilityProbe.mjs"));
            start.ArgumentList.Add(directory);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (Exception error) { await SharedSourceBuildGate.StopOwnProcessAsync(process, stdout, stderr, error); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }
}
