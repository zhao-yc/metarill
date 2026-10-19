using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定平台组件的真实源码消费者、用途隔离与失败边界；不开放完整包或修改写入合同。</summary>
public sealed class FixedCustomControlReadTests
{
    /// <summary>实际组件读取和修改窄实体与完整实体行为一致；透传拷贝不能扩大读计划。</summary>
    [Theory]
    [InlineData("config_button")] [InlineData("config_table")] [InlineData("config_column")] [InlineData("config_page_column")]
    public async Task Real_components_consume_only_fixed_current_page_dependencies(string tableName)
    {
        var catalog = Catalog(tableName); var before = JsonSerializer.Serialize(catalog);
        var (model, table, page, source) = Model(catalog); var result = FixedCustomControlReadCompilation.Resolve(model, table, page, source);
        var contract = Assert.IsType<FixedCustomControlReadContract>(result.Contract);
        Assert.Null(result.Reason); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Detail.CanCompile, string.Join(";", plan.Detail.Gaps.Select(gap => gap.Reason)));
        Assert.True(plan.List.CanCompile); Assert.DoesNotContain(plan.Detail.OutputFields, field => field.JsonName == "unrelatedSecret");
        Assert.Contains(plan.List.OutputFields, field => field.JsonName == "unrelatedSecret");
        foreach (var dependency in contract.Columns)
            Assert.Contains(plan.Detail.PhysicalColumns, column => column.Source.Id == dependency.Source.Id);
        Assert.Contains(plan.Detail.Dependencies, dependency => dependency.ColumnId == source.Source.Id && dependency.Reasons.Any(reason => reason.Contains(contract.Tag, StringComparison.Ordinal)));
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
        var detail = OriginalUniappDetailRenderer.Render(model, table, page, []);
        Assert.Contains("<" + contract.Tag, detail); Assert.Contains(":entity=\"entity\"", detail);
        Assert.DoesNotContain("item." + source.JsonName, OriginalUniappListRenderer.Render(model, table, page));
        // 固定读取证明不是组件发布许可；现有Full门禁必须仍然拒绝同一真实元数据。
        Assert.Contains("自定义控件", Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog)).Message);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-custom-read"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated"); new CSharpGenerator().Generate(catalog, output, "Example.CustomRead");
            await File.WriteAllTextAsync(Path.Combine(directory, "contract.json"), JsonSerializer.Serialize(new { table = tableName, contract.Tag, fields = plan.Detail.OutputFields.Select(field => field.JsonName) }));
            await Probe(directory);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>真实平台元数据的五处原控件都能证明读取，而数据库复选与join仍是原独立Gap。</summary>
    [Fact]
    public void Actual_platform_custom_contracts_do_not_clear_unrelated_gaps()
    {
        var metadata = PlatformPath();
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(metadata), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var model = GenerationModel.Create(catalog, "Example.PlatformCustomRead");
        var controls = model.Tables.SelectMany(table => table.Pages.SelectMany(page => page.DetailColumns.Where(column => column.Source.ControlKind == "CustomControl").Select(column => (table, page, column)))).ToArray();
        Assert.Equal(5, controls.Length);
        foreach (var (table, page, column) in controls)
        {
            var proof = FixedCustomControlReadCompilation.Resolve(model, table, page, column);
            Assert.NotNull(proof.Contract); Assert.Null(proof.Reason);
            Assert.DoesNotContain(ReadProjectionCompilation.ForPage(model, table, page).Detail.Gaps, gap => gap.ColumnId == column.Source.Id);
        }
        var gaps = model.Tables.SelectMany(table => table.Pages.SelectMany(page => ReadProjectionCompilation.ForPage(model, table, page).Detail.Gaps)).ToArray();
        Assert.Contains(gaps, gap => gap.Reason.Contains("MultiCheckBoxDatabase", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Reason.Contains("联接", StringComparison.Ordinal));
    }

    /// <summary>原mode只接受0/1；另外两旗标由Number读取，真实bool值与整数矩阵都能保持原行为。</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Boolean_flags_follow_actual_number_consumers_without_allowing_boolean_mode(bool isView, bool isFunction)
    {
        var catalog = Catalog("config_table");
        foreach (var column in catalog.ConfigColumns.Where(column => column.ColumnName is "is_view" or "is_function_table")) column.LogicalType = "bool";
        var (model, table, page, source) = Model(catalog); var result = FixedCustomControlReadCompilation.Resolve(model, table, page, source);
        Assert.NotNull(result.Contract); var plan = ReadProjectionCompilation.ForPage(model, table, page); Assert.True(plan.Detail.CanCompile);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-custom-flags"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "contract.json"), JsonSerializer.Serialize(new { table = "config_table", Tag = result.Contract.Tag, fields = plan.Detail.OutputFields.Select(field => field.JsonName), isView, isFunction, booleanFlags = true }));
            await Probe(directory);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
        catalog.ConfigColumns.Single(column => column.ColumnName == "is_rongyu").LogicalType = "bool";
        (model, table, page, source) = Model(catalog);
        Assert.Null(FixedCustomControlReadCompilation.Resolve(model, table, page, source).Contract);
        Assert.Contains(ReadProjectionCompilation.ForPage(model, table, page).Detail.Gaps, gap => gap.Reason.Contains("固定依赖类型", StringComparison.Ordinal));
    }

    /// <summary>当前页未开放的必需依赖不能向另一页借权限或降级为空值。</summary>
    [Theory]
    [InlineData("config_button", "visible_rule_json")] [InlineData("config_table", "is_view")]
    [InlineData("config_column", "control_kind")] [InlineData("config_page_column", "config_column_id")]
    public void Missing_or_other_page_dependency_remains_a_gap(string tableName, string dependency)
    {
        var catalog = Catalog(tableName); var id = catalog.ConfigColumns.Single(column => column.ColumnName == dependency).Id;
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigColumnId == id)) { field.IsShow = 0; field.IsEdit = 0; }
        catalog.ConfigPages.Add(new() { Id = "102", ConfigTableId = "1" });
        catalog.ConfigPageColumns.Add(new() { Id = "other", ConfigPageId = "102", ConfigColumnId = id, IsShow = 1 });
        var (model, table, page, source) = Model(catalog);
        var result = FixedCustomControlReadCompilation.Resolve(model, table, page, source);
        Assert.Null(result.Contract); Assert.Contains("可读依赖", result.Reason);
        var plan = ReadProjectionCompilation.ForPage(model, table, page);
        if (dependency == source.Source.ColumnName) Assert.DoesNotContain(plan.Detail.PhysicalColumns, column => column.Source.Id == source.Source.Id);
        else Assert.Contains(plan.Detail.Gaps, gap => gap.ColumnId == source.Source.Id && gap.Reason.Contains("可读依赖", StringComparison.Ordinal));
    }

    /// <summary>物理依赖的类型和分支不能被隐藏联接、虚拟列或新增动态属性替换。</summary>
    [Theory]
    [InlineData("join")] [InlineData("virtual")] [InlineData("array")] [InlineData("branch")] [InlineData("type")]
    public void Unknown_dependency_storage_or_component_branch_stays_a_gap(string kind)
    {
        var catalog = Catalog("config_column");
        var required = catalog.ConfigColumns.Single(column => column.ColumnName == "control_kind");
        if (kind == "join") required.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        if (kind == "virtual") required.IsVirtualColumn = 1;
        if (kind == "array") { required.ControlKind = "FileLibrary"; required.LogicalType = "string"; }
        if (kind == "type") required.LogicalType = "int64";
        if (kind == "branch") Add(catalog, "98", "config_column_id", "int64");
        var (model, table, page, source) = Model(catalog);
        var result = FixedCustomControlReadCompilation.Resolve(model, table, page, source);
        Assert.Null(result.Contract); Assert.NotNull(result.Reason);
        Assert.False(ReadProjectionCompilation.ForPage(model, table, page).Detail.CanCompile);
    }

    /// <summary>精确标签不能夹带props、脚本或换用另一宿主字段，页覆盖按原明细规则生效。</summary>
    [Theory]
    [InlineData("unknown")] [InlineData("props")] [InlineData("host")] [InlineData("source")] [InlineData("whitespace")]
    public void Unknown_tag_attribute_or_host_remains_explicit(string kind)
    {
        var catalog = Catalog("config_button");
        var source = catalog.ConfigColumns.Single(column => column.Id == "13");
        var field = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13");
        if (kind == "unknown") field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("yzp-unproven-editor");
        if (kind == "props") field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("yzp-button-visible-rule-editor :field=\"secret\"");
        if (kind == "whitespace") field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(" ");
        if (kind == "host") catalog.ConfigTables.Single().TableName = "other";
        if (kind == "source") source.ColumnName = "other_json";
        var (model, table, page, column) = Model(catalog); var result = FixedCustomControlReadCompilation.Resolve(model, table, page, column);
        Assert.Null(result.Contract); Assert.Contains("标签、属性或宿主", result.Reason);
        Assert.False(ReadProjectionCompilation.ForPage(model, table, page).Detail.CanCompile);
    }

    /// <summary>页级明细可选精确已知标签，controlKind覆盖不能把原List/Detail语义混为一谈。</summary>
    [Fact]
    public void Original_page_overlays_preserve_purpose_specific_custom_branch()
    {
        var catalog = Catalog("config_button");
        var source = catalog.ConfigColumns.Single(column => column.Id == "13"); source.ControlDetail = "yzp-unproven-editor";
        var field = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13");
        field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("yzp-button-visible-rule-editor");
        field.Extra["controlKind"] = JsonSerializer.SerializeToElement("RTF");
        field.IsShow = 1;
        var (model, table, page, _) = Model(catalog);
        var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Detail.CanCompile); Assert.True(plan.List.CanCompile);
        Assert.Contains("<yzp-button-visible-rule-editor", OriginalUniappDetailRenderer.Render(model, table, page, []));
        field.Extra["controlKind"] = JsonSerializer.SerializeToElement("CustomControl");
        (model, table, page, _) = Model(catalog); plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Detail.CanCompile); Assert.False(plan.List.CanCompile);
        source.ControlKind = ""; field.IsShow = 0;
        (model, table, page, _) = Model(catalog); plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Detail.CanCompile);
        Assert.DoesNotContain(plan.Detail.Dependencies, dependency => dependency.Reasons.Any(reason => reason.Contains("固定控件", StringComparison.Ordinal)));
        Assert.DoesNotContain("<yzp-button-visible-rule-editor", OriginalUniappDetailRenderer.Render(model, table, page, []));
    }

    /// <summary>空格是原有效覆盖而不是空值：列表不回落常量，详情仍按表kind，明细字节不回落。</summary>
    [Fact]
    public void Whitespace_kind_and_detail_follow_actual_original_branches()
    {
        var catalog = GeneratorTests.Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == "12");
        source.ControlKind = "ConstList"; source.ControlDetail = "原值|old";
        var field = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "12"); field.Extra["controlKind"] = JsonSerializer.SerializeToElement(" ");
        var model = GenerationModel.Create(catalog, "Example.CustomRead"); var table = model.Tables.Single(); var page = table.Pages.Single();
        var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.DoesNotContain(plan.List.OutputFields, output => output.JsonName == "nameShow");
        Assert.DoesNotContain("item.nameShow", OriginalUniappListRenderer.Render(model, table, page));
        Assert.Contains("entity.nameShow", OriginalUniappDetailRenderer.Render(model, table, page, []));
        Assert.Contains(plan.Detail.OutputFields, output => output.JsonName == "nameShow");
        field.Extra.Remove("controlKind"); field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(" ");
        model = GenerationModel.Create(catalog, "Example.CustomRead"); table = model.Tables.Single(); page = table.Pages.Single(); plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.Equal(" ", Assert.Single(plan.List.OutputFields, output => output.JsonName == "nameShow").ControlDetail);
        Assert.Equal(" ", Assert.Single(plan.Detail.OutputFields, output => output.JsonName == "nameShow").ControlDetail);
        Assert.DoesNotContain("原值", OriginalUniappListRenderer.Render(model, table, page));
        Assert.DoesNotContain("原值", OriginalUniappDetailRenderer.Render(model, table, page, []));
    }

    /// <summary>原数据库复选和联接缺口不因新增平台Custom合同而放行。</summary>
    [Fact]
    public void Unrelated_join_and_database_checkbox_gaps_are_unchanged()
    {
        var catalog = Catalog("config_table"); Add(catalog, "97", "unproved", "string");
        var extra = catalog.ConfigColumns.Single(column => column.Id == "97"); extra.ControlKind = "MultiCheckBoxDatabase";
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "97").IsEdit = 1;
        var (model, table, page, _) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.Contains(plan.Detail.Gaps, gap => gap.ColumnId == "97" && gap.Reason.Contains("MultiCheckBoxDatabase", StringComparison.Ordinal));
        extra.ControlKind = ""; extra.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        (model, table, page, _) = Model(catalog);
        Assert.Contains(ReadProjectionCompilation.ForPage(model, table, page).Detail.Gaps, gap => gap.ColumnId == "97" && gap.Reason.Contains("联接", StringComparison.Ordinal));
    }

    /// <summary>按实际固定宿主建表级元数据，必需字段仅网格可读，秘密列不进入Detail基础集合。</summary>
    private static MetadataCatalog Catalog(string tableName)
    {
        var catalog = GeneratorTests.Catalog(); catalog.ConfigTables.Single().TableName = tableName;
        var source = catalog.ConfigColumns.Single(column => column.Id == "13");
        source.ControlKind = "CustomControl";
        (source.ColumnName, source.ControlDetail, source.LogicalType) = tableName switch
        {
            "config_button" => ("visible_rule_json", "yzp-button-visible-rule-editor", "string"),
            "config_table" => ("is_rongyu", "yzp-table-storage-mode", "int32"),
            _ => ("control_detail", "yzp-control-detail-editor", "string")
        };
        foreach (var field in catalog.ConfigPageColumns) { field.IsShow = field.ConfigColumnId == "13" ? 0 : 1; field.IsEdit = field.ConfigColumnId == "13" ? 1 : 0; }
        Add(catalog, "16", "unrelated_secret", "string");
        if (tableName == "config_table") { Add(catalog, "17", "is_view", "int32"); Add(catalog, "18", "is_function_table", "int32"); }
        if (tableName == "config_column") Add(catalog, "17", "control_kind", "string");
        if (tableName == "config_page_column") Add(catalog, "17", "config_column_id", "int64");
        return catalog;
    }

    /// <summary>新增声明列只开放网格，供真实依赖扩展反例使用。</summary>
    private static void Add(MetadataCatalog catalog, string id, string name, string type)
    {
        catalog.ConfigColumns.Add(new() { Id = id, ConfigTableId = "1", ColumnName = name, LogicalType = type });
        catalog.ConfigPageColumns.Add(new() { Id = "f" + id, ConfigPageId = "101", ConfigColumnId = id, IsShow = 1, IsEdit = 0 });
    }

    /// <summary>使用真实生成模型与稳定来源列，不手造Readable集合。</summary>
    private static (GenerationModel Model, TableModel Table, PageModel Page, ColumnModel Source) Model(MetadataCatalog catalog)
    {
        var model = GenerationModel.Create(catalog, "Example.CustomRead"); var table = model.Tables.Single();
        return (model, table, table.Pages.Single(page => page.Source.Id == "101"), table.Columns.Single(column => column.Source.Id == "13"));
    }

    /// <summary>定位仓库受管元数据，测试不依赖开发机旧YZP目录。</summary>
    private static string PlatformPath([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../metadata/platform.json"));

    /// <summary>实际Vue组件消费者仅模拟外部网络与宿主节点，不复制控件方法。</summary>
    private static async Task Probe(string directory, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "FixedCustomControlReadProbe.mjs")); start.ArgumentList.Add(directory);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
