using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>临时真实FullApplication目录中的源码更新、保护、清单与恢复验收；不连接数据库。</summary>
public sealed partial class LocalGenerationUpdateTests : IAsyncLifetime
{
    private PackagePublicationException? publicationFailure;
    private readonly string root = Path.Combine(Path.GetTempPath(), "metarill-local-update", Guid.NewGuid().ToString("N"));
    private static readonly string[] All = ["generate-project-config", "sync-runtime", "generate-all-page"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };
    /// <summary>生成两个真实独立包，正常手写扩展及自有AppID不会被误判为受管冲突。</summary>
    [Fact]
    public void Config_update_preserves_partial_appid_private_configuration_and_is_repeatable()
    {
        var old = Catalog(); var next = Catalog(); next.YzProjectName = "更新项目名";
        var (target, candidate) = Packages(old, next); var service = "backend/generated/Services/DemoCustomerService.cs";
        File.AppendAllText(Path.Combine(target, service), "\n// 本地手写partial扩展必须逐字保留。\n");
        var appid = Path.Combine(target, "frontend/manifest.json"); var manifest = JsonNode.Parse(File.ReadAllText(appid))!; manifest["appid"] = "__UNI__LOCAL_UPDATE_TEST"; File.WriteAllText(appid, manifest.ToJsonString(Json));
        var configuration = Path.Combine(target, "frontend/common/application.config.js"); File.WriteAllText(configuration, "export default { apiBaseUrl: 'https://local.example/api' }\n");
        var local = Path.Combine(target, "backend/Application.Host/appsettings.local.json"); File.WriteAllText(local, "{\"Database\":{\"ExpectedDatabase\":\"开发者自有配置\"}}\n");
        var custom = Path.Combine(target, "backend/Application.Host/HostCustomization.cs"); File.WriteAllText(custom, "// 独立手写partial，不在导出清单中。\n");
        var preserved = new[] { service, "frontend/manifest.json", "frontend/common/application.config.js", "backend/Application.Host/appsettings.local.json", "backend/Application.Host/HostCustomization.cs" }.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(target, path)));
        var before = SourceSnapshot(target); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, ["generate-project-config"]);
        Assert.True(plan.CanApply); Assert.Empty(plan.RequiredActions); Assert.Equal(before, SourceSnapshot(target));
        Assert.All(plan.Files.Where(file => file.Action != "preserve"), file => Assert.Contains(file.Scope, new[] { "generate-project-config", "manifest" }));
        var receipt = updater.Apply(plan, plan.PlanSha256); Assert.Equal("committed", receipt.Status);
        var committed = SourceSnapshot(target); File.WriteAllText(Path.Combine(target, ".local/metarill-updates/active.json"), JsonSerializer.Serialize(new { schemaVersion = 1, transactionId = receipt.TransactionId, planSha256 = receipt.PlanSha256 }, Json));
        Assert.Equal("committed", updater.Recover(target, receipt.TransactionId, receipt.PlanSha256).Status); Assert.Equal(committed, SourceSnapshot(target));
        foreach (var (path, bytes) in preserved) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(target, path)));
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "backend/metadata/project.json")))!; Assert.Equal("更新项目名", result["yzProjectName"]!.GetValue<string>());
        var repeated = updater.Preview(target, candidate, ["generate-project-config"]); Assert.True(repeated.CanApply); Assert.DoesNotContain(repeated.Files, file => file.Action != "preserve"); Assert.Equal("unchanged", updater.Apply(repeated, repeated.PlanSha256).Status);
    }

    /// <summary>metadata影响代码与路由时必须显式选择依赖动作，不把单范围静默扩成完整包。</summary>
    [Fact]
    public void Metadata_changes_require_explicit_scope_dependency_and_update_both_frontend_copies()
    {
        var next = Catalog(); next.ConfigColumns.Single(column => column.ColumnName == "name").Name = "新名称标签";
        var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater();
        var preview = updater.Preview(target, candidate, ["generate-all-page"]); Assert.False(preview.CanApply); Assert.Contains("generate-project-config", preview.RequiredActions); Assert.Throws<InvalidOperationException>(() => updater.Apply(preview, preview.PlanSha256));
        var ready = updater.Preview(target, candidate, ["generate-project-config", "generate-all-page"]); Assert.True(ready.CanApply); updater.Apply(ready, ready.PlanSha256);
        var live = File.ReadAllBytes(Path.Combine(target, "frontend/pages/yzp/demo_customer-detail-2200000000000100020.vue"));
        Assert.Equal(live, File.ReadAllBytes(Path.Combine(target, "backend/generated/frontend/pages/yzp/demo_customer-detail-2200000000000100020.vue")));
        Assert.Contains("新名称标签", System.Text.Encoding.UTF8.GetString(live)); Assert.True(updater.Preview(target, candidate, All).CanApply);
    }

    /// <summary>运行时只更新已登记公共代码；候选摘要也必须同步，文件漂移不允许冒充框架版本。</summary>
    [Fact]
    public void Runtime_update_only_changes_runtime_and_manifest_files()
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); const string runtime = "frontend/common/utils.js";
        File.AppendAllText(Path.Combine(candidate, runtime), "\n// 新运行时修订。\n"); Rehash(candidate, runtime);
        var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, ["sync-runtime"]); Assert.True(plan.CanApply);
        Assert.All(plan.Files.Where(file => file.Action != "preserve"), file => Assert.Contains(file.Scope, new[] { "sync-runtime", "manifest" }));
        var metadata = File.ReadAllBytes(Path.Combine(target, "backend/metadata/project.json")); updater.Apply(plan, plan.PlanSha256);
        Assert.Equal(metadata, File.ReadAllBytes(Path.Combine(target, "backend/metadata/project.json"))); Assert.EndsWith("// 新运行时修订。\n", File.ReadAllText(Path.Combine(target, runtime)));
    }

    /// <summary>受管改动、新路径碰撞、预览后手写或候选漂移都不能被强制覆盖。</summary>
    [Theory]
    [InlineData("managed")] [InlineData("targetDrift")] [InlineData("candidateDrift")] [InlineData("confirm")]
    public void Conflicts_and_preview_drift_never_start_source_writes(string scenario)
    {
        var next = Catalog(); next.YzProjectName = "候选"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater();
        if (scenario == "managed") { File.AppendAllText(Path.Combine(target, "frontend/common/utils.js"), "\n// 手工改动\n"); var before = SourceSnapshot(target); Assert.Throws<InvalidOperationException>(() => updater.Preview(target, candidate, All)); Assert.Equal(before, SourceSnapshot(target)); return; }
        var plan = updater.Preview(target, candidate, All);
        if (scenario == "targetDrift") File.AppendAllText(Path.Combine(target, "backend/generated/Services/DemoCustomerService.cs"), "\n// 预览后编辑\n");
        if (scenario == "candidateDrift") File.AppendAllText(Path.Combine(candidate, "frontend/common/utils.js"), "\n// 候选漂移\n");
        var snapshot = SourceSnapshot(target); Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, scenario == "confirm" ? new string('0', 64) : plan.PlanSha256)); Assert.Equal(snapshot, SourceSnapshot(target));
    }

    /// <summary>失败发生在真实文件替换后仍恢复所有源码，恢复日志和备份留在被忽略目录。</summary>
    [Fact]
    public void Failed_apply_rolls_back_real_files_and_persists_diagnostic_journal()
    {
        var next = Catalog(); next.YzProjectName = "触发失败"; var (target, candidate) = Packages(Catalog(), next); var before = SourceSnapshot(target); var count = 0;
        var updater = new LocalGenerationUpdater { AfterWrite = _ => { if (++count == 2) throw new IOException("测试替换失败"); } }; var plan = updater.Preview(target, candidate, All);
        var error = Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256)); Assert.Contains("已恢复", error.Message); Assert.Equal(before, SourceSnapshot(target));
        Assert.False(File.Exists(Path.Combine(target, ".local/metarill-updates/active.json"))); var journal = Assert.Single(Directory.GetFiles(Path.Combine(target, ".local/metarill-updates/transactions"), "journal.json", SearchOption.AllDirectories)); Assert.Contains("rolled-back", File.ReadAllText(journal)); Assert.True(new LocalGenerationUpdater().Preview(target, candidate, All).CanApply);
    }

    /// <summary>预览计划只允许在项目外首次创建，序列化后仍可验证；目标内路径不是写入捷径。</summary>
    [Fact]
    public void Plan_file_roundtrip_is_strict_and_never_written_inside_a_project()
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All);
        Assert.Throws<InvalidOperationException>(() => LocalGenerationUpdater.WritePlan(plan, Path.Combine(target, "plan.json")));
        var path = Path.Combine(root, "plan.json"); LocalGenerationUpdater.WritePlan(plan, path); Assert.Equal(plan.PlanSha256, LocalGenerationUpdater.ReadPlan(path).PlanSha256); Assert.Throws<IOException>(() => LocalGenerationUpdater.WritePlan(plan, path));
        var json = JsonNode.Parse(File.ReadAllText(path))!; json["force"] = true; File.WriteAllText(path, json.ToJsonString()); Assert.Throws<JsonException>(() => LocalGenerationUpdater.ReadPlan(path));
    }

    /// <summary>真实已提交demo模型用来创建临时目录，更新器测试不访问任何私有数据库或服务。</summary>
    private static MetadataCatalog Catalog() => JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "backend/metadata/demo.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>在同一测试GUID目录内创建两个独立真实导出包。</summary>
    private (string Target, string Candidate) Packages(MetadataCatalog old, MetadataCatalog next)
    {
        Directory.CreateDirectory(root); var target = Path.Combine(root, "target"); var candidate = Path.Combine(root, "candidate");
        try { new ApplicationPackager().Create(old, target, "Example.LocalUpdate", SourceRoot()); new ApplicationPackager().Create(next, candidate, "Example.LocalUpdate", SourceRoot()); return (target, candidate); }
        catch (PackagePublicationException error) when (error.StageDirectory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { publicationFailure = error; PublicationHandleProbe.Capture(error); throw; }
    }
    /// <summary>测试候选的新框架文件同步维护两层hash，与真实再打包的输出契约一致。</summary>
    private static void Rehash(string directory, string path)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, path))));
        var frontendPath = Path.Combine(directory, "frontend/.metarill-frontend.json"); var frontend = JsonNode.Parse(File.ReadAllText(frontendPath))!;
        frontend["files"]!.AsArray().Single(entry => entry!["path"]!.GetValue<string>() == path["frontend/".Length..])!["sha256"] = hash; File.WriteAllText(frontendPath, frontend.ToJsonString(Json) + "\n");
        var packagePath = Path.Combine(directory, ".metarill-package.json"); var package = JsonNode.Parse(File.ReadAllText(packagePath))!;
        foreach (var file in new[] { path, "frontend/.metarill-frontend.json" }) package["files"]!.AsArray().Single(entry => entry!["path"]!.GetValue<string>() == file)!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, file))));
        File.WriteAllText(packagePath, package.ToJsonString(Json) + "\n");
    }
    /// <summary>源码快照排除事务控制目录，用实际字节校验应用或恢复结果。</summary>
    private static string SourceSnapshot(string directory) => string.Join("\n", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(path => !Path.GetRelativePath(directory, path).StartsWith(".local" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));
    /// <summary>测试源码位置允许显式覆盖，不把开发者绝对目录写入产品。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    /// <summary>由xUnit异步生命周期管理每个临时GUID目录。</summary>
    public Task InitializeAsync() => Task.CompletedTask;
    /// <summary>只清理本测试在固定临时根下建立的目录。</summary>
    // Packages已验证实际发布异常属于本GUID；权限错误可能令Exists返回false，不能据此销毁外层证据。
    public Task DisposeAsync() => publicationFailure is not null
        ? Task.CompletedTask : TestDirectoryCleanup.DeleteAsync(root, Path.Combine(Path.GetTempPath(), "metarill-local-update"));
}
