using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Testing;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>B121真实六模型的公开打包接线；实际Full构建与原资源核对不替代同包实库验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelPublicationTests
{
    /// <summary>独立配置编号和表名前缀也必须可发布，不能靠测试模型名称白名单放行。</summary>
    [Fact]
    public void Proven_R0_six_model_catalog_has_no_cross_excel_publication_gap()
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        var before = JsonSerializer.Serialize(catalog);
        var model = GenerationModel.Create(catalog, "Example.PublishedR0Excel");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        VerifyPathCapabilities(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content, true, true);
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>R1的物理名称缓存仍需独立完整证据，不能借R0的正式通过解除其Excel门槛。</summary>
    [Fact]
    public void R1_cross_excel_keeps_its_independent_publication_gap()
    {
        var catalog = GlobalCrossCachedTargetFixture.Create(1, 1, "string", "int64", false);
        var gaps = GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, "Example.UnprovenR1Excel"));
        Assert.Contains(gaps, gap => gap.Reason == CrossTableExcelRenderer.ImportAcceptancePending || gap.Reason == CrossTableExcelRenderer.ExportAcceptancePending);
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
    }

    /// <summary>混合自树和来源重复角色的原读写仍可编译，Excel公开用途须保留独立验收门槛。</summary>
    [Theory]
    [InlineData("mixed-self")]
    [InlineData("repeated-source")]
    [InlineData("overwrite")]
    public void Unproven_R0_compositions_keep_their_excel_gap(string kind)
    {
        var catalog = kind switch
        {
            "mixed-self" => NonRedundantMixedCascadeTests.Catalog("unproven_mix", "string", "int64"),
            "repeated-source" => RepeatedCrossSourceTests.Catalog("unproven_repeat", 3, "string", "int64", false),
            _ => NonRedundantCrossTableGenerationTests.Catalog("unproven_overwrite", "string", "int64")
        };
        // 混合／重复来源夹具本来已发布；该辅助方法会新增后台，只补尚未发布的覆盖导入夹具。
        if (kind == "overwrite") RedundantMultiNameCacheTests.Published(catalog);
        var importPage = catalog.ConfigPages.Single(page => page.Id == "201"); importPage.IsImport = 1;
        if (kind == "overwrite") importPage.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        var model = GenerationModel.Create(catalog, "Example.UnprovenR0Excel");
        var table = model.Tables.Single(table => table.Source.Id == "2");
        var page = table.Pages.Single(page => page.Source.Id == "201");
        Assert.NotEmpty(CrossTableWriteCompilation.ForService(model, table, page));
        Assert.Equal(CrossTableExcelRenderer.ImportAcceptancePending, NonRedundantCrossTableSubset.Reason(model, table, page));
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
    }

    /// <summary>公开R0可用不应扩大内部准备通道；没有Excel用途仍必须拒绝。</summary>
    [Fact]
    public void Internal_preparation_still_rejects_catalog_without_excel_usage()
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        foreach (var page in catalog.ConfigPages) { page.IsImport = 0; page.IsExport = 0; page.Extra.Remove("importMethod"); page.Extra.Remove("exportMethod"); }
        var model = GenerationModel.Create(catalog, "Example.NoExcel");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        VerifyPathCapabilities(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content, false, false);
        Assert.Throws<InvalidOperationException>(() => CrossTableExcelApplicationPreparer.RequireCurrent(catalog, "Example.NoExcel"));
    }

    /// <summary>原导入／导出是独立开关；只开放一种用途不能把另一种也声明为支持。</summary>
    [Theory]
    [InlineData(1, 0, "")]
    [InlineData(0, 1, "")]
    [InlineData(1, 1, "OnlyNew")]
    public void Original_import_export_switches_are_reported_independently(int imports, int exports, string mode)
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        foreach (var page in catalog.ConfigPages)
        {
            page.IsImport = imports; page.IsExport = exports;
            if (imports == 1 && mode.Length > 0) page.Extra["importMethod"] = JsonSerializer.SerializeToElement(mode);
            if (imports == 0) page.Extra.Remove("importMethod");
            if (exports == 0) page.Extra.Remove("exportMethod");
        }
        var model = GenerationModel.Create(catalog, "Example.IndependentExcelFlags");
        new FrontendPackager().Validate(catalog);
        VerifyPathCapabilities(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content, imports == 1, exports == 1);
    }

    /// <summary>六模型包含八个实际跨表控件，能力清单必须与公开包／关闭用途的页面分别一致。</summary>
    private static void VerifyPathCapabilities(string text, bool imports, bool exports)
    {
        using var profile = JsonDocument.Parse(text);
        Assert.Empty(profile.RootElement.GetProperty("unsupportedControls").EnumerateArray());
        var paths = profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray()
            .Where(row => row.TryGetProperty("sourceMode", out var mode) && mode.GetString() == "crossTable" && row.GetProperty("storageMode").GetString() == "nonRedundant").ToArray();
        Assert.Equal(8, paths.Length);
        Assert.All(paths, row => { Assert.Equal(imports, row.GetProperty("pathCellImportSupported").GetBoolean()); Assert.Equal(exports, row.GetProperty("pathCellExportSupported").GetBoolean()); });
    }

    /// <summary>正规公开打包必须保留Web身份、完整元数据及原组件；包使用自身源码严格构建。</summary>
    [Fact]
    public async Task Public_six_model_Full_builds_with_original_pages_and_web_identity()
    {
        var source = NonRedundantApplicationFixture.Root();
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-r0-excel-publication");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog, directory, "Example.PublishedR0Excel", source);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, ".metarill-package.json")));
            Assert.Equal("FullApplication", manifest.RootElement.GetProperty("packageMode").GetString());
            Assert.Equal("web-database-v1", manifest.RootElement.GetProperty("authenticationProfile").GetString());
            Assert.False(File.Exists(Path.Combine(directory, CrossTableExcelApplicationPreparer.Marker)));
            VerifyPathCapabilities(await File.ReadAllTextAsync(Path.Combine(directory, "backend/generated/generation-capabilities.json")), true, true);
            var actual = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(directory, "backend/metadata/project.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(JsonSerializer.Serialize(catalog), JsonSerializer.Serialize(actual));
            foreach (var asset in new[] { "components/yzp-cascade-tree/yzp-cascade-tree.vue", "common/yzp_runtime/detailLayoutRuntime.js" })
                Assert.Equal(File.ReadAllBytes(Path.Combine(source, "frontend/metarill_uni_admin", asset)), File.ReadAllBytes(Path.Combine(directory, "frontend", asset)));
            foreach (var table in catalog.ConfigTables)
            {
                var hand = Path.Combine(directory, "backend/generated/Services", GenerationModel.Pascal(table.TableName) + "Service.cs");
                await File.AppendAllTextAsync(hand, "\n// 公开R0跨表Excel重复生成必须保留手写业务。\n");
            }
            var partials = Directory.GetFiles(Path.Combine(directory, "backend/generated/Services"), "*.cs")
                .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal)).ToDictionary(path => path, File.ReadAllBytes);
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "backend/generated"), "Example.PublishedR0Excel");
            Assert.Equal(32, partials.Count);
            foreach (var partial in partials) Assert.Equal(partial.Value, await File.ReadAllBytesAsync(partial.Key));
            var build = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", directory);
            foreach (var argument in new[] { "build", "backend/Application.Host/Application.Host.csproj", "-c", "Release", "-warnaserror", "--disable-build-servers", "--verbosity", "quiet" }) build.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(build);
            var hostDirectory = Path.Combine(directory, "backend/Application.Host");
            var host = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory);
            host.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Release/net10.0/Application.Host.dll"));
            host.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; host.Environment["DOTNET_ENVIRONMENT"] = "Production";
            host.Environment["Authentication__Mode"] = "Web"; host.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
            host.Environment["Database__Provider"] = "mysql"; host.Environment["Database__ExpectedDatabase"] = "offline";
            host.Environment["Database__ConnectionString"] = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";
            host.Environment["Files__RootDirectory"] = Path.Combine(directory, "files"); host.Environment["Exports__RootDirectory"] = Path.Combine(directory, "exports");
            await using var running = await FreshHost.StartAsync(host, "/health");
            foreach (var table in catalog.ConfigTables.Where(table => table.TableName.EndsWith("_cross_consumer", StringComparison.Ordinal)))
            {
                var page = catalog.ConfigPages.Single(page => page.ConfigTableId == table.Id);
                var route = "/api/admin/auto/" + table.TableName + "/" + page.Id + "/";
                foreach (var action in new[] { "page", "importDatas" + page.Id, "initExport" })
                    await FreshWebHttp.SendAsync(running.Client, System.Net.Http.HttpMethod.Post, route + action, new { }, System.Net.HttpStatusCode.Unauthorized);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }
}
