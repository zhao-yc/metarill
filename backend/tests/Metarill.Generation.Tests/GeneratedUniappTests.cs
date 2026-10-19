using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>从真实元数据生成 Vue，再执行其实际方法，验证请求和原控件协议而不使用人工模板替身。</summary>
public sealed class GeneratedUniappTests
{
    /// <summary>查询、排序、显示、RTF回传与部分更新在真实生成脚本中共同运行。</summary>
    [Fact]
    public async Task Generated_vue_executes_query_display_and_partial_write_contracts()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-uniapp");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var result = new CSharpGenerator().Generate(Catalog(), output, "Example.Ui");
            var list = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/customer-list-101.vue"));
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/customer-detail-101.vue"));
            Assert.Contains("draftFilters.name", list); Assert.Contains("draftFilters.enabled", list); Assert.Contains("type=\"daterange\"", list);
            Assert.Contains("sortable @sort-change=\"changeSort('name', $event)\"", list);
            Assert.Contains("openRtf('remark', 'edit')", detail); Assert.DoesNotContain("此控件待移植，当前只读</text></uni-forms-item>", detail);
            Assert.Contains("access-mode=\"PUBLIC\" accept=\"image,video\" :max-count=\"2\" :max-size-mb=\"3\"", detail);
            Assert.Contains(":allow-library=\"false\" :crop-images=\"true\"", detail);
            Assert.Contains("access-mode=\"PROTECTED\"", detail);
            Assert.Contains("&quot;9007199254740993&quot;", detail);
            var vo = File.ReadAllText(Path.Combine(output, "VOs/CustomerPage101Vo.g.cs"));
            Assert.Contains("JsonPropertyName(\"statusShow\")", vo); Assert.Contains("JsonPropertyName(\"groupName\")", vo); Assert.DoesNotContain("JsonPropertyName(\"ownerName\")", vo);
            using var capabilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "generation-capabilities.json")));
            Assert.Contains(capabilities.RootElement.GetProperty("unsupportedControls").EnumerateArray(), item => item.GetProperty("column").GetString() == "attachments" && item.GetProperty("usage").GetString() == "filter");
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("RTF", StringComparison.Ordinal));
            await RunProbe(output);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>不支持的复杂只读控件仍列为缺口，不能因不可编辑就宣称已经完成。</summary>
    [Fact]
    public async Task Unsupported_readonly_control_is_explicit_in_capabilities()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-uniapp"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = GeneratorTests.Catalog(); catalog.ConfigColumns.Single(column => column.ColumnName == "remark").ControlKind = "CascadeTree";
            catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13").IsEdit = 0;
            new CSharpGenerator().Generate(catalog, output, "Example.Unsupported");
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "generation-capabilities.json")));
            Assert.Contains(document.RootElement.GetProperty("unsupportedControls").EnumerateArray(), item => item.GetProperty("column").GetString() == "remark" && item.GetProperty("reason").GetString()!.Contains("CascadeTree", StringComparison.Ordinal));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>只读RTF和文件仍使用真实预览组件，空配置沿用后端默认而不变为公开。</summary>
    [Fact]
    public async Task Readonly_media_keeps_preview_and_protected_defaults_without_write_events()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-uniapp"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = Catalog();
            foreach (var id in new[] { "13", "23" }) catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == id).Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
            catalog.ConfigColumns.Single(column => column.ColumnName == "attachments").ControlDetail = "{\"accessMode\":null,\"maxCount\":null,\"maxSizeMb\":null}";
            new CSharpGenerator().Generate(catalog, output, "Example.Readonly");
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/customer-detail-101.vue"));
            Assert.DoesNotContain("openRtf('remark', 'edit')", detail); Assert.Contains("openRtf('remark', 'review')", detail);
            Assert.DoesNotContain("@update:modelValue=\"change('attachments', $event)\"", detail);
            Assert.Contains("access-mode=\"PROTECTED\" accept=\"image,file,video\" :max-count=\"9\" :max-size-mb=\"50\"", detail);
            Assert.Contains(":readonly=\"true\"", detail);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>未知访问级别、错类型或越界配置必须在整个生成批次落盘前失败。</summary>
    [Theory]
    [InlineData("{\"accessMode\":\"PRIVATE\"}")]
    [InlineData("{\"maxCount\":0}")]
    [InlineData("{\"maxSizeMb\":10241}")]
    [InlineData("{\"accept\":\"image,unknown\"}")]
    [InlineData("{\"extensions\":\".png\"}")]
    [InlineData("{\"allowLibrary\":\"false\"}")]
    [InlineData("{\"unknownFlag\":true}")]
    public async Task Invalid_file_control_configuration_never_writes_a_partial_batch(string detail)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-uniapp"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = Catalog(); catalog.ConfigColumns.Single(column => column.ColumnName == "attachments").ControlDetail = detail;
            Assert.ThrowsAny<Exception>(() => new CSharpGenerator().Generate(catalog, output, "Example.Invalid"));
            Assert.False(Directory.Exists(output));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>使用声明类型与真实关系端点构造查询场景，展示别名只来自实际可读字段。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog(); catalog.ConfigPages[0].IsExport = 1;
        catalog.ConfigColumns.Single(column => column.ColumnName == "remark").ControlKind = "RTF";
        void Add(string id, string name, string type, string kind = "", string control = "", bool search = false, bool sort = false, bool readOnly = false)
        {
            catalog.ConfigColumns.Add(new() { Id = id, ConfigTableId = "1", ColumnName = name, LogicalType = type, Name = name, ControlKind = kind, ControlDetail = control });
            var flags = new Dictionary<string, JsonElement>(); if (search) flags["isSearch"] = JsonSerializer.SerializeToElement(1); if (sort) flags["isSort"] = JsonSerializer.SerializeToElement(1); if (readOnly) flags["isReadOnly"] = JsonSerializer.SerializeToElement(1);
            catalog.ConfigPageColumns.Add(new() { Id = "100" + id, ConfigPageId = "101", ConfigColumnId = id, Extra = flags });
        }
        Add("16", "status", "int32", "ConstList", "启用|1,停用|0", search: true);
        Add("17", "enabled", "bool", "CheckBox", search: true);
        Add("18", "amount", "decimal", search: true, sort: true);
        Add("19", "signed_at", "datetime", "date", search: true);
        Add("20", "group_id", "int64", "List", "{\"tableId\":\"2\",\"nameField\":\"name\"}", search: true);
        Add("21", "group_name", "string", readOnly: true);
        Add("22", "owner_id", "int64", "List", "{\"tableId\":\"2\",\"nameField\":\"name\"}");
        Add("23", "attachments", "string", "FileLibrary", "{\"accessMode\":\"PUBLIC\",\"accept\":\"image,video\",\"maxCount\":2,\"maxSizeMb\":3,\"extensions\":[\"PNG\",\".webp\"],\"allowLibrary\":false,\"cropImages\":true}", search: true);
        Add("24", "protected_files", "string", "FileLibrary");
        Add("25", "large_enum", "int64", "ConstList", "大整数|9007199254740993");
        catalog.ConfigTables.Add(new() { Id = "2", TableName = "group_item", Name = "分组" });
        catalog.ConfigColumns.Add(new() { Id = "201", ConfigTableId = "2", ColumnName = "id", LogicalType = "id" });
        catalog.ConfigColumns.Add(new() { Id = "202", ConfigTableId = "2", ColumnName = "name", LogicalType = "string" });
        catalog.ConfigColumns.Add(new() { Id = "203", ConfigTableId = "2", ColumnName = "yz_project_id", LogicalType = "id" });
        catalog.ConfigPages.Add(new() { Id = "201", ConfigTableId = "2", Name = "分组", WhereSql = "yz_project_id=@current_yz_project_id" });
        catalog.ConfigPageColumns.Add(new() { Id = "1202", ConfigPageId = "201", ConfigColumnId = "202" });
        return catalog;
    }

    /// <summary>由 Node 执行已生成的页面，失败输出断言信息，不连接浏览器或真实业务库。</summary>
    private static async Task RunProbe(string output, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "GeneratedVueBehaviorProbe.mjs")); start.ArgumentList.Add(output);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
