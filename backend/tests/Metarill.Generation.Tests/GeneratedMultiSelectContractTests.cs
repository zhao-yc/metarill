using System.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>仅已证明的冗余字段启用原弹层；额外范围、未知父和非冗余不能变绿。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GeneratedMultiSelectContractTests
{
    [Fact]
    public async Task Redundant_field_keeps_json_storage_array_vo_readonly_names_and_original_popup_contract()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-contract"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(), output, "Example.MultiProbe");
            var entity = File.ReadAllText(Path.Combine(output, "Entities/RelationSource.g.cs")); var dto = File.ReadAllText(Path.Combine(output, "DTOs/RelationSourcePage201Requests.g.cs"));
            var vo = File.ReadAllText(Path.Combine(output, "VOs/RelationSourcePage201Vo.g.cs")); var service = File.ReadAllText(Path.Combine(output, "Services/RelationSourceService.g.cs"));
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("string? TargetIds", entity); Assert.Contains("string? TargetNames", entity); Assert.Contains("Optional<JsonElement?> TargetIds", dto); Assert.DoesNotContain("TargetNames {", dto);
            Assert.Contains("JsonElement? TargetNames", vo); Assert.Contains("ReadMultiValue201_25", service); Assert.Contains("relationAuthorization.RequirePagePermission(\"301\", \"list\")", service);
            Assert.Contains("row => new { row.Id, Name = row.Name }", service); Assert.Contains("selected.Distinct().Count() != selected.Length", service); Assert.Contains("if (refreshNames)", service);
            Assert.Contains("selected-name-field=\"name\"", detail); Assert.Contains(":selectedIds=\"entity.targetIds\"", detail); Assert.Contains("@onPopSelect=\"onPopSelect25\"", detail); Assert.Contains("createMultiSelectFieldMixin", detail);
            using var capabilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "generation-capabilities.json")));
            var subset = Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray()); Assert.Equal("redundant", subset.GetProperty("storageMode").GetString()); Assert.False(subset.GetProperty("nonRedundantSupported").GetBoolean()); Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    [Fact]
    public async Task Actual_generated_original_detail_vue_preserves_popup_rows_cancellation_and_whole_entity_protocol()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-vue"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(), output, "Example.MultiVue");
            await RunVueProbe(output);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>实际生成自选/互选模块，不用目标组件stub绕过ESM初始化依赖环。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Self_and_mutual_selection_modules_evaluate_without_unreachable_crud_detail_dependencies(bool mutual)
    {
        var catalog = Catalog(); catalog.ConfigPages.Single(page => page.Id == "201").Extra["type"] = JsonSerializer.SerializeToElement(2);
        if (!mutual)
        {
            var column = catalog.ConfigColumns.Single(column => column.Id == "25"); column.LinkConfigTableId = "2"; column.ControlDetail = "{\"pageId\":\"201\",\"nameField\":\"name\"}";
        }
        else
        {
            catalog.ConfigColumns.Add(new() { Id = "37", ConfigTableId = "3", ColumnName = "source_ids", LogicalType = "string", ColumnLength = "max", LinkConfigTableId = "2", ControlKind = "MultiSelectPage", ControlDetail = "{\"pageId\":\"201\",\"nameField\":\"name\"}" });
            catalog.ConfigColumns.Add(new() { Id = "38", ConfigTableId = "3", ColumnName = "source_names", LogicalType = "string", ColumnLength = "max" });
            catalog.ConfigPageColumns.Add(new() { Id = "3003", ConfigPageId = "301", ConfigColumnId = "37", IsShow = 1, IsEdit = 1 });
            catalog.ConfigPageColumns.Add(new() { Id = "3004", ConfigPageId = "301", ConfigColumnId = "38", IsShow = 1, IsEdit = 1 });
        }
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-module"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, output, "Example.MultiModules");
            foreach (var page in new[] { ("source", "201"), ("target", "301") })
            {
                var list = File.ReadAllText(Path.Combine(output, $"frontend/pages/yzp/relation_{page.Item1}-list-{page.Item2}.vue")); Assert.DoesNotContain("-detail-"+page.Item2+".vue",list); Assert.DoesNotContain("<relation_"+page.Item1+"_detail_"+page.Item2,list);
            }
            var ordinary = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_parent-list-101.vue")); Assert.Contains("import relation_parent_detail_101",ordinary);Assert.Contains("<relation_parent_detail_101",ordinary);
            await RunVueProbe(output, "GeneratedMultiSelectModuleProbe.mjs");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>Vue3真实编译生成模板并挂载，不能只手调getter或复制页面行为。</summary>
    private static async Task RunVueProbe(string output, string probe = "GeneratedMultiSelectVueProbe.mjs", [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
        if (probe == "GeneratedMultiSelectModuleProbe.mjs") start.ArgumentList.Add("--experimental-vm-modules");
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, probe)); start.ArgumentList.Add(output);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    [Fact]
    public async Task Standalone_backend_only_package_builds_the_real_authorized_multi_registration()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-package"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "application");
        try
        {
            var result = new ApplicationPackager().Create(Catalog(), output, "Example.MultiStandalone", SourceRoot(), ApplicationPackageMode.BackendOnly); Assert.False(result.FrontendIncluded);
            var program = File.ReadAllText(Path.Combine(output, "backend/Application.Host/Program.cs")); var registration = File.ReadAllText(Path.Combine(output, "backend/generated/GeneratedRegistration.g.cs"));
            Assert.Contains("AddScoped<IRuntimeAuthorization, DevelopmentAuthorization>()", program); Assert.Contains("provider.GetRequiredService<IRuntimeAuthorization>()", registration);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = output, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
            foreach (var argument in new[] { "build", "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet" }) start.ArgumentList.Add(argument);
            start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; } Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>同表页可以只投影名称；隐藏IDs时不得生成无定义的方法或放行隐含复制。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Hidden_ids_and_second_projection_pages_keep_internal_validation_without_exposing_ids(bool secondPage)
    {
        var catalog = Catalog(); var hiddenPage = "201";
        if (secondPage)
        {
            var page = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page => page.Id == "201")))!; page.Id = "202"; catalog.ConfigPages.Add(page); hiddenPage = "202";
            catalog.ConfigPageColumns.Add(new() { Id = "projection_names", ConfigPageId = hiddenPage, ConfigColumnId = "27", IsShow = 1, IsEdit = 0 });
        }
        else catalog.ConfigPageColumns.RemoveAll(field => field.ConfigPageId == hiddenPage && field.ConfigColumnId == "25");
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-projection"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.MultiProbe");
            var service = File.ReadAllText(Path.Combine(directory, "generated/Services/RelationSourceService.g.cs"));
            Assert.Contains("private async Task ValidateMulti" + hiddenPage + "_25Async", service);
            Assert.Contains("await ValidateMulti" + hiddenPage + "_25Async", service);
            var vo = File.ReadAllText(Path.Combine(directory, "generated/VOs/RelationSourcePage" + hiddenPage + "Vo.g.cs")); var dto = File.ReadAllText(Path.Combine(directory, "generated/DTOs/RelationSourcePage" + hiddenPage + "Requests.g.cs"));
            Assert.DoesNotContain("TargetIds {", vo); Assert.DoesNotContain("TargetIds {", dto); Assert.DoesNotContain("TargetNames {", dto);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>独立包引用本轮源码副本，不借用主仓库生成物。</summary>
    private static string SourceRoot([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../.."));

    [Theory]
    [InlineData("scope", "配置或范围无法安全编译", "配置或范围无法安全编译")]
    [InlineData("name", "名称列未开放读取", "名称字段不在目标页可读投影")]
    [InlineData("parent", "目标父没有唯一可读来源字段", "来源页未开放目标父字段")]
    [InlineData("names", "冗余名称辅助列尚未明确声明", "冗余名称辅助列尚未明确声明")]
    public async Task Incomplete_field_proofs_stay_explicitly_unsupported(string kind, string generatedReason, string capabilityReason)
    {
        var catalog = Catalog();
        if (kind == "scope") catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail = "{\"pageId\":\"301\",\"whereSql\":\"name='unproved'\"}";
        if (kind == "name") catalog.ConfigPageColumns.RemoveAll(field => field.ConfigPageId == "301" && field.ConfigColumnId == "32");
        if (kind == "parent") catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = "yz_project_id=@current_yz_project_id";
        if (kind == "names") catalog.ConfigColumns.RemoveAll(column => column.Id == "27");
        if (kind == "names") catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "27");
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-contract"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            // 原控件不能换只读占位；证据不足的批次在落盘前拒绝，Full包门禁同样拒绝。
            var generatedError=Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,output,"Example.MultiGap"));
            Assert.Contains(generatedReason, generatedError.Message);
            Assert.False(Directory.Exists(output));
            // 完整包先走统一能力审计，定位与原因必须一致，不要求不同入口的外层前缀相同。
            var model = GenerationModel.Create(catalog, "Example.MultiGap");
            var gap = Assert.Single(GenerationCapabilityAudit.Collect(model), gap => gap.PageId == "201" && gap.Column == "target_ids" && gap.Usage == "detail");
            Assert.StartsWith("MultiSelectPage尚未支持：", gap.Reason);
            Assert.Contains(capabilityReason, gap.Reason);
            var packageError=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.MultiGap",SourceRoot(),ApplicationPackageMode.FullApplication));
            Assert.StartsWith("原页面仍有未完成的后端能力：", packageError.Message);
            Assert.Contains($"页面{gap.PageId}/{gap.Column}/{gap.Usage}：{gap.Reason}", packageError.Message);
            Assert.False(Directory.Exists(output));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    [Fact]
    public void Non_redundant_multi_rejects_generation_instead_of_writing_name_entities()
    {
        var catalog = Catalog(); catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 0;
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(catalog, Path.Combine(Path.GetTempPath(), "metarill-rejected-" + Guid.NewGuid().ToString("N")), "Example.NonRedundant"));
    }

    /// <summary>已声明名称物理列，不根据后缀创建缺少元数据的字段。</summary>
    internal static MetadataCatalog Catalog(string prefix = "relation")
    {
        var catalog = RelationOptionsContractTests.Catalog(prefix); var source = catalog.ConfigColumns.Single(column => column.Id == "25");
        source.ColumnName = "target_ids"; source.LogicalType = "string"; source.ColumnLength = "max"; source.ControlKind = "MultiSelectPage"; source.ControlDetail = "{\"pageId\":\"301\",\"nameField\":\"name\",\"buttonText\":\"选择目标\",\"futureOption\":{\"keep\":true}}";
        catalog.ConfigPages.Single(page => page.Id == "301").Extra["type"] = JsonSerializer.SerializeToElement(2);
        catalog.ConfigColumns.Add(new() { Id = "27", ConfigTableId = "2", ColumnName = "target_names", LogicalType = "string", ColumnLength = "max" });
        catalog.ConfigPageColumns.Add(new() { Id = "2003", ConfigPageId = "201", ConfigColumnId = "27", IsShow = 1, IsEdit = 1 });
        return catalog;
    }
}
