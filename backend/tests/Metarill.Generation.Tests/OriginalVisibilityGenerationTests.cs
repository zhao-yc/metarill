using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>字段、分组及行按钮共用同一套原可见规则。</summary>
public sealed class OriginalVisibilityGenerationTests
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

        await Probe(catalog, "boolean");
    }

    /// <summary>核对字符串 ID、JSON 子字段及规则数值常量的精度。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Original_visibility_distinguishes_adjacent_ids_and_exact_numeric_rules(bool jsonMember, bool physicalName, bool javaName=false)
    {
        const long lower = 9007199254740992;
        const long expected = 9007199254740993;
        var catalog = GeneratorTests.Catalog();
        var page = catalog.ConfigPages.Single();
        catalog.ConfigColumns.Add(new() { Id = "16", ConfigTableId = "1", ColumnName = javaName ? "owner_ID" : "owner_id", LogicalType = "int64", Name = "归属 ID" });
        catalog.ConfigPageColumns.Add(new() { Id = "116", ConfigPageId = page.Id, ConfigColumnId = "16", IsShow = 1, IsEdit = 1 });
        var owner = physicalName ? "owner_id" : "ownerId";
        var payload = physicalName ? "approval_payload" : "approvalPayload";
        var columnRule = """{"conditions":[{"field":"ownerId","operator":"eq","value":9007199254740993}]}"""
            .Replace("ownerId", owner, StringComparison.Ordinal);
        if (jsonMember)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "17", ConfigTableId = "1", ColumnName = javaName ? "approval_PAYLOAD" : "approval_payload", LogicalType = "json", Name = "审批参数", ControlKind = "TextArea",
                ColumnLength = "max"
            });
            catalog.ConfigPageColumns.Add(new() { Id = "117", ConfigPageId = page.Id, ConfigColumnId = "17", IsShow = 0, IsEdit = 1 });
            columnRule = columnRule.Replace(owner, payload + ".review.owner", StringComparison.Ordinal);
        }
        var buttonRule = """{"groups":[{"logic":"AND","conditions":[{"field":"ownerId","operator":"in","valueList":[9007199254740993,9007199254740996]}]}]}"""
            .Replace("ownerId", jsonMember ? payload + ".review.owner" : owner, StringComparison.Ordinal);
        catalog.ConfigColumns.Single(column => column.ColumnName == "remark").Extra["visibleCondition"] =
            JsonSerializer.SerializeToElement(columnRule);
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13").Extra["detailGroupKey"] = JsonSerializer.SerializeToElement("review");
        page.Extra["detailLayout"] = JsonSerializer.SerializeToElement(
            """{"version":1,"mode":"tabs","groups":[{"key":"review","name":"审批备注"}]}""");
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new
        {
            id = "900", name = "审批动作", configPageId = page.Id, place = "EditAndGrid",
            visibleScheme = javaName ? "AlwaysVisible" : "Normal", visibleRuleJson = buttonRule, actionType = 1, saveOption = "NotSave"
        }));
        foreach (var text in new[] { columnRule, buttonRule })
        {
            var rule = ButtonVisibilityRule.Compile(text);
            Assert.False(rule.Matches(Record(lower)));
            Assert.True(rule.Matches(Record(expected)));
        }
        if (javaName)
            page.Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("ownerId == 9007199254740993");
        var model = GenerationModel.Create(catalog, "Example.RuleFieldNames");
        var table = model.Tables.Single();
        var plan = ReadProjectionCompilation.ForPage(model, table, table.Pages.Single());
        Assert.True(plan.List.CanCompile, string.Join("；", plan.List.Gaps.Select(gap => gap.Reason)));
        Assert.True(plan.Detail.CanCompile, string.Join("；", plan.Detail.Gaps.Select(gap => gap.Reason)));
        Assert.Contains(plan.List.Dependencies, dependency => dependency.ColumnId == (jsonMember ? "17" : "16"));
        if (jsonMember)
        {
            Assert.Contains(plan.List.OutputFields, field => field.JsonName == (javaName ? "approvalPAYLOAD" : "approvalPayload"));
            Assert.Contains(plan.Detail.Dependencies, dependency => dependency.ColumnId == "17");
        }
        await Probe(catalog, (javaName ? "java-" : "") + (jsonMember ? "json-numeric" : "numeric"));

        Dictionary<string, object?> Record(long id) => new()
        {
            [owner] = id, [payload] = JsonSerializer.SerializeToElement(new { review = new { owner = id } })
        };
    }

    /// <summary>原文字与空白条件须在服务端和生成页面得到一致结果。</summary>
    [Fact]
    public async Task Original_visibility_compares_text_and_blank_values_as_the_server()
    {
        var catalog = GeneratorTests.Catalog();
        var page = catalog.ConfigPages.Single();
        const string columnRule = """{"conditions":[{"field":"name","operator":"lt","value":"a"}]}""";
        const string buttonRule = """{"conditions":[{"field":"name","operator":"in","valueList":["A","é","\u00a0",null]}]}""";
        catalog.ConfigColumns.Single(column => column.ColumnName == "remark").Extra["visibleCondition"] =
            JsonSerializer.SerializeToElement(columnRule);
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13").Extra["detailGroupKey"] = JsonSerializer.SerializeToElement("review");
        page.Extra["detailLayout"] = JsonSerializer.SerializeToElement(
            """{"version":1,"mode":"tabs","groups":[{"key":"review","name":"审批备注"}]}""");
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new
        {
            id = "900", name = "审批动作", configPageId = page.Id, place = "EditAndGrid",
            visibleScheme = "Normal", visibleRuleJson = buttonRule, actionType = 1, saveOption = "NotSave"
        }));

        var column = ButtonVisibilityRule.Compile(columnRule);
        var button = ButtonVisibilityRule.Compile(buttonRule);
        foreach (var (name, showColumn, showButton) in new (string, bool, bool)[]
        {
            ("A", true, true), ("a", false, false), ("e\u0301", false, false), ("\u00e9", false, true),
            ("\u00a0", false, true), ("\u001c", true, true), ("", true, true), ("\u202f", false, false)
        })
        {
            var record = new Dictionary<string, object?> { ["name"] = name };
            Assert.Equal(showColumn, column.Matches(record));
            Assert.Equal(showButton, button.Matches(record));
        }
        await Probe(catalog, "text");
    }

    /// <summary>修改原编辑器的一行不得覆盖其它操作数、嵌套分组或数字字面量。</summary>
    [Theory]
    [InlineData("button")]
    [InlineData("column")]
    public Task Original_editor_preserves_unedited_rule_values(string editor) =>
        Probe(GeneratorTests.Catalog(), "editor-" + editor);

    /// <summary>实际挂载生成的列表及详情，以独立原运行时执行规则。</summary>
    private static async Task Probe(MetadataCatalog catalog, string scenario)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-visibility");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, directory, "Example.OriginalVisibility");
            var start = NonRedundantApplicationFixture.Start("node", directory);
            start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(), "OriginalVisibilityProbe.mjs"));
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(scenario);
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
