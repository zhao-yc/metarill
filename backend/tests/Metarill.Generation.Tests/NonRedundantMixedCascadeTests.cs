using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>同一R0表的跨表、自树与多选由原生成链共同处理，不能以放宽单控件门禁冒充完整支持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantMixedCascadeTests
{
    /// <summary>四种ID与目标父键均证明独立读写计划，隐藏同伴也必须保留。</summary>
    [Theory]
    [InlineData("string", "string")]
    [InlineData("string", "int64")]
    [InlineData("int64", "string")]
    [InlineData("int64", "int64")]
    public void Mixed_cascade_plans_share_one_nonredundant_source(string type, string parentType)
    {
        var catalog = Catalog("nr_cascade_mix", type, parentType);
        var saved = JsonSerializer.Serialize(catalog);
        var model = GenerationModel.Create(catalog, "Example.MixedNonRedundant");
        var table = model.Tables.Single(table => table.Source.Id == "2");
        foreach (var page in table.Pages)
        {
            _ = NonRedundantReadCompilation.ForPage(model, table, page);
            Assert.Single(CrossTableWriteCompilation.ForService(model, table, page));
            Assert.Single(CascadeTreeWriteCompilation.ForService(model, table, page));
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        Assert.Equal(saved, JsonSerializer.Serialize(catalog));
        Assert.DoesNotContain(MetadataStoragePolicy.ForTable(catalog, catalog.ConfigTables.Single(table => table.Id == "2")).PhysicalColumns,
            column => column.ColumnName.EndsWith("_name", StringComparison.Ordinal) || column.ColumnName == "choice_names");
    }

    /// <summary>原详情和两个级联组件完整装配，包内实际构建并验证更新不覆盖手写partial。</summary>
    [Theory]
    [InlineData("string", "int64")]
    [InlineData("int64", "string")]
    public async Task Full_mixed_application_builds_and_preserves_original_controls(string type, string parentType)
    {
        var catalog = Catalog("nr_cascade_mix", type, parentType);
        var root = NonRedundantApplicationFixture.Root();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-nr-cascade-mix-full");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "application");
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, "Example.MixedCascadeApplication", root).FrontendIncluded);
            foreach (var operation in new[] { "restore", "build" }) await NonRedundantApplicationFixture.BuildAsync(output, operation);
            var detail = await File.ReadAllTextAsync(Path.Combine(output, "frontend/pages/yzp/nr_cascade_mix_source-detail-201.vue"));
            Assert.Contains("treeId", detail); Assert.Contains("placeId", detail);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root, "frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),
                File.ReadAllBytes(Path.Combine(output, "frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            var probe = NonRedundantApplicationFixture.Start("node", output);
            probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(), "NonRedundantMixedCascadeVueProbe.mjs"));
            probe.ArgumentList.Add(Path.Combine(output, "frontend"));
            await NonRedundantApplicationFixture.RunAsync(probe);
            using var profile = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/generation-capabilities.json")));
            var cascades = profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item => item.GetProperty("controlKind").GetString() == "CascadeTree").ToArray();
            Assert.Equal(10, cascades.Length);
            Assert.All(cascades, item => Assert.True(item.GetProperty("mixedCascadeModesSupported").GetBoolean()));
            Assert.All(cascades.Where(item => item.GetProperty("sourceMode").GetString() == "selfTree"), item => Assert.False(item.GetProperty("multipleSelfTreesSupported").GetBoolean()));
            Assert.All(cascades.Where(item => item.GetProperty("sourceMode").GetString() == "crossTable"), item => Assert.False(item.GetProperty("multipleCrossTablesSupported").GetBoolean()));
            var hand = Path.Combine(output, "backend/generated/Services/NrCascadeMixSourceService.cs");
            await File.AppendAllTextAsync(hand, "\n// 混合级联的手写业务必须在再生成后保留。\n");
            var bytes = await File.ReadAllBytesAsync(hand);
            var candidate = Path.Combine(directory, "candidate");
            new ApplicationPackager().Create(catalog, candidate, "Example.MixedCascadeApplication", root);
            var updater = new LocalGenerationUpdater();
            var plan = updater.Preview(output, candidate, ["generate-project-config", "sync-runtime", "generate-all-page"]);
            Assert.True(plan.CanApply); updater.Apply(plan, plan.PlanSha256);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(hand));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>隐藏有效同伴依然参与写入与复制，但不能因此把有效页面拒绝为未完成。</summary>
    [Theory]
    [InlineData("25")]
    [InlineData("81")]
    public void Hidden_valid_companion_retains_both_internal_plans(string hidden)
    {
        var catalog = Catalog("nr_cascade_mix", "string", "int64");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == hidden);
        var model = GenerationModel.Create(catalog, "Example.HiddenMixedCascade");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var table = model.Tables.Single(table => table.Source.Id == "2");
        foreach (var page in table.Pages)
        {
            Assert.Single(CrossTableWriteCompilation.ForService(model, table, page));
            Assert.Single(CascadeTreeWriteCompilation.ForService(model, table, page));
        }
    }

    /// <summary>混合包不能借隐藏另一类控件绕过坏链、来源限制或未发布目标。</summary>
    [Theory]
    [InlineData("hiddenSelf")]
    [InlineData("hiddenCross")]
    [InlineData("selfRealm")]
    [InlineData("crossRealm")]
    [InlineData("globalSelf")]
    [InlineData("crossImport")]
    public void Invalid_mixed_companion_is_rejected_before_staging(string kind)
    {
        var catalog = Catalog("nr_cascade_mix", "string", "int64");
        if (kind == "hiddenSelf") { catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "81"); catalog.ConfigColumns.Single(column => column.Id == "81").ControlDetail = "{\"sourceMode\":\"selfTree\",\"selfTree\":{}}"; }
        if (kind == "hiddenCross") { catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "25"); catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail = "{\"sourceMode\":\"crossTable\",\"levels\":[]}"; }
        if (kind == "selfRealm") catalog.ConfigTables.Single(table => table.Id == "6").YzProjectId = "";
        if (kind == "crossRealm") catalog.ConfigTables.Single(table => table.Id == "3").YzProjectId = "";
        if (kind == "globalSelf") catalog.ConfigTables.Single(table => table.Id == "6").Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        if (kind == "crossImport") catalog.ConfigPages.Single(page => page.Id == "201").IsImport = 1;
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-nr-cascade-mix-reject");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        Assert.ThrowsAny<Exception>(() => new ApplicationPackager().Create(catalog, output, "Example.RejectedMixedCascade", NonRedundantApplicationFixture.Root()));
        Assert.False(Directory.Exists(output));
    }

    /// <summary>测试模型从既有混合元数据扩展；只改变本轮存储模式，路径Excel未配置，不修改实际项目按钮。</summary>
    internal static MetadataCatalog Catalog(string prefix, string type, string parentType)
    {
        var catalog = CascadeMixedRequiredTests.Catalog(prefix, type, parentType);
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 0;
        catalog.ConfigColumns.Single(column => column.Id == "84").Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Single(column => column.Id == "84").Extra["columnFullName"] = JsonSerializer.SerializeToElement("choice.name");
        foreach (var page in catalog.ConfigPages.Where(page => page.ConfigTableId == "2")) { page.IsImport = 0; page.IsExport = 0; }
        RedundantMultiNameCacheTests.Published(catalog);
        return catalog;
    }
}
