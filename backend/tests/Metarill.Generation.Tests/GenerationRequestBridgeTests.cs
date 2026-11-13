using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>真实请求只准备配置预览，目标自有命名空间、partial与AppID继续由旧归属保护。</summary>
    [Fact]
    public void Offline_configuration_request_prepares_without_applying_and_reuses_target_namespace()
    {
        Directory.CreateDirectory(root); var target = Path.Combine(root, "target"); new ApplicationPackager().Create(Catalog(), target, "Developer.CustomNamespace", SourceRoot());
        var next = Catalog(); next.YzProjectName = "离线新名称";
        var partial = Path.Combine(target, "backend/generated/Services/DemoCustomerService.cs"); File.AppendAllText(partial, "\n// 开发者扩展\n");
        var appid = Path.Combine(target, "frontend/manifest.json"); var customized = JsonNode.Parse(File.ReadAllText(appid))!; customized["appid"] = "__UNI__OWNED_TEST"; File.WriteAllText(appid, customized.ToJsonString(Json));
        var bytes = File.ReadAllBytes(partial); var appidBytes = File.ReadAllBytes(appid); var before = SourceSnapshot(target); var planPath = Path.Combine(root, "plan.json"); var updater = new LocalGenerationUpdater();
        var result = updater.PrepareRequest(RequestFile(next, "generate-project-config"), target, Path.Combine(root, "prepared"), SourceRoot(), planPath);
        Assert.Equal("preview-ready", result.Status); Assert.Equal(before, SourceSnapshot(target)); Assert.True(result.Plan.CanApply); Assert.Equal("Developer.CustomNamespace", result.Plan.RootNamespace); Assert.Equal(["generate-project-config"], result.Plan.Actions);
        Assert.Equal(result.Plan.PlanSha256, LocalGenerationUpdater.ReadPlan(planPath).PlanSha256); Assert.False(Directory.Exists(Path.Combine(target, ".local/metarill-updates")));
        Assert.All(result.Plan.Files.Where(file => file.Action != "preserve"), file => Assert.Contains(file.Scope, new[] { "manifest", "generate-project-config" }));
        Assert.Equal("committed", updater.Apply(result.Plan, result.Plan.PlanSha256).Status); Assert.Equal(bytes, File.ReadAllBytes(partial)); Assert.Equal(appidBytes, File.ReadAllBytes(appid)); Assert.Equal("离线新名称", JsonNode.Parse(File.ReadAllText(Path.Combine(target, "backend/metadata/project.json")))!["yzProjectName"]!.GetValue<string>());
    }

    /// <summary>显式可信源的运行时变更不写目标；源仅由公开包重建，不读取私有环境。</summary>
    [Fact]
    public void Offline_runtime_request_only_previews_runtime_scope()
    {
        var (target, _) = Packages(Catalog(), Catalog()); var source = PublicSource(target); File.AppendAllText(Path.Combine(source, "frontend/metarill_uni_admin/common/utils.js"), "\n// 可信运行时新版本\n");
        var before = SourceSnapshot(target); var result = new LocalGenerationUpdater().PrepareRequest(RequestFile(Catalog(), "sync-runtime"), target, Path.Combine(root, "prepared"), source, Path.Combine(root, "plan.json"));
        Assert.Equal(before, SourceSnapshot(target)); Assert.Equal("preview-ready", result.Status); Assert.Contains(result.Plan.Files, file => file.Path == "frontend/common/utils.js" && file.Action == "replace");
        Assert.All(result.Plan.Files.Where(file => file.Action != "preserve"), file => Assert.Contains(file.Scope, new[] { "manifest", "sync-runtime" }));
    }

    /// <summary>页面变化需要配置范围时返回依赖，只有开发者另行显式预览联合动作后才能应用。</summary>
    [Fact]
    public void Offline_page_request_returns_dependencies_without_expanding_actions()
    {
        var (target, _) = Packages(Catalog(), Catalog()); var next = Catalog(); next.ConfigColumns.Single(column => column.ColumnName == "name").Name = "离线字段标签"; var before = SourceSnapshot(target); var candidate = Path.Combine(root, "prepared"); var updater = new LocalGenerationUpdater();
        var result = updater.PrepareRequest(RequestFile(next, "generate-all-page"), target, candidate, SourceRoot(), Path.Combine(root, "plan.json"));
        Assert.Equal("dependencies-required", result.Status); Assert.False(result.Plan.CanApply); Assert.Equal(["generate-all-page"], result.Plan.Actions); Assert.Contains("generate-project-config", result.Plan.RequiredActions); Assert.Equal(before, SourceSnapshot(target)); Assert.Throws<InvalidOperationException>(() => updater.Apply(result.Plan, result.Plan.PlanSha256));
        var explicitPlan = updater.Preview(target, candidate, ["generate-all-page", "generate-project-config"]); Assert.True(explicitPlan.CanApply); Assert.Equal("committed", updater.Apply(explicitPlan, explicitPlan.PlanSha256).Status);
    }

    /// <summary>嵌套配置与敏感默认值共用包发布脱敏，原元数据保持不变。</summary>
    [Fact]
    public void Offline_request_scrubs_secrets_without_mutating_source()
    {
        var catalog = Catalog(); var column = catalog.ConfigColumns.Single(field => field.ColumnName == "name"); column.Extra["connectionString"] = JsonSerializer.SerializeToElement("仅测试私有连接"); column.ControlDetail = "{\"apiKey\":\"仅测试私有Key\",\"placeholder\":\"保留说明\"}";
        var password = new ColumnMetadata { Id = "8300000099900000001", YzProjectId = catalog.YzProjectId, ConfigTableId = column.ConfigTableId, Name = "密码", ColumnName = "password", LogicalType = "string", ColumnLength = "100", DefaultValue = "仅测试私有默认" }; catalog.ConfigColumns.Add(password);
        var request = LocalGenerationUpdater.CreateRequest(catalog, "generate-project-config"); var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(Json) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.DoesNotContain("仅测试私有默认", json); Assert.Equal("", request.Metadata.ConfigColumns.Single(field => field.Id == password.Id).DefaultValue); Assert.Equal("仅测试私有默认", password.DefaultValue); Assert.DoesNotContain("仅测试私有连接", json); Assert.DoesNotContain("仅测试私有Key", json); Assert.Equal("保留说明", JsonNode.Parse(request.Metadata.ConfigColumns.Single(field => field.Id == column.Id).ControlDetail)!["placeholder"]!.GetValue<string>()); Assert.Equal("仅测试私有连接", column.Extra["connectionString"].GetString()); Assert.Contains("apiKey", column.ControlDetail);
        Assert.Equal(request.RequestSha256, LocalGenerationUpdater.CreateRequest(catalog, "generate-project-config").RequestSha256);
    }

    /// <summary>拒绝未知、重复字段及摘要漂移；封套不能携带本机路径、namespace或scope覆盖。</summary>
    [Theory]
    [InlineData("unknown")] [InlineData("namespace")] [InlineData("duplicate")] [InlineData("version")] [InlineData("action")] [InlineData("metadata")] [InlineData("hash")] [InlineData("size")]
    public void Offline_request_rejects_unsafe_or_tampered_documents(string scenario)
    {
        var path = RequestFile(Catalog(), "sync-runtime"); var original = File.ReadAllText(path); var node = JsonNode.Parse(original)!;
        switch (scenario)
        {
            case "unknown": node["targetDirectory"] = "C:/arbitrary"; break;
            case "namespace": node["rootNamespace"] = "Attacker.Namespace"; break;
            case "version": node["schemaVersion"] = 2; break;
            case "action": node["action"] = "all"; break;
            case "metadata": node["metadata"]!["yzProjectName"] = "被篡改"; break;
            case "hash": node["requestSha256"] = new string('0', 64); break;
        }
        File.WriteAllText(path, scenario == "duplicate" ? original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1,\"schemaVersion\": 1") : scenario == "size" ? new string(' ', 4 * 1024 * 1024 + 1) : node.ToJsonString(Json)); Assert.ThrowsAny<Exception>(() => LocalGenerationUpdater.ReadRequest(path));
    }

    /// <summary>项目/归属冲突及目录重叠在候选写入前拒绝，既有计划和目标字节不受影响。</summary>
    [Theory]
    [InlineData("project")] [InlineData("provider")] [InlineData("backendOnly")] [InlineData("managed")] [InlineData("candidateInTarget")] [InlineData("candidateInSource")] [InlineData("sourceInTarget")] [InlineData("planInTarget")] [InlineData("planInSource")] [InlineData("requestInTarget")] [InlineData("planExists")]
    public void Offline_prepare_rejects_mismatch_and_overlaps_without_changing_target(string scenario)
    {
        Directory.CreateDirectory(root); var target = Path.Combine(root, "target"); new ApplicationPackager().Create(Catalog(), target, "Example.LocalUpdate", SourceRoot(), scenario == "backendOnly" ? ApplicationPackageMode.BackendOnly : ApplicationPackageMode.FullApplication);
        var catalog = Catalog(); if (scenario == "project") catalog = JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog, Json).Replace(catalog.YzProjectId, "2200000000000999000"), Json)!;
        if (scenario == "provider") catalog.DatabaseProvider = "postgresql";
        var request = RequestFile(catalog, "sync-runtime"); var candidate = Path.Combine(root, "prepared"); var source = SourceRoot(); var plan = Path.Combine(root, "plan.json");
        if (scenario == "managed") File.AppendAllText(Path.Combine(target, "frontend/common/utils.js"), "// 非归属手改\n");
        if (scenario == "candidateInTarget") candidate = Path.Combine(target, "candidate");
        if (scenario == "candidateInSource") candidate = Path.Combine(source, ".local/bridge-rejected-candidate");
        if (scenario == "sourceInTarget") source = target;
        if (scenario == "planInTarget") plan = Path.Combine(target, "plan.json");
        if (scenario == "planInSource") plan = Path.Combine(source, ".local/bridge-rejected-plan.json");
        if (scenario == "requestInTarget") { var copy = Path.Combine(target, "request.json"); File.Copy(request, copy); request = copy; }
        if (scenario == "planExists") File.WriteAllText(plan, "保留既有计划");
        var before = SourceSnapshot(target); Assert.ThrowsAny<Exception>(() => new LocalGenerationUpdater().PrepareRequest(request, target, candidate, source, plan)); Assert.Equal(before, SourceSnapshot(target)); Assert.False(Directory.Exists(candidate));
    }

    /// <summary>真实CLI返回依赖退出码3且无目标写入，不能通过额外--actions重写请求范围。</summary>
    [Fact]
    public async Task Cli_offline_prepare_uses_real_process_and_rejects_scope_override()
    {
        var (target, _) = Packages(Catalog(), Catalog()); var next = Catalog(); next.ConfigColumns.Single(column => column.ColumnName == "name").Name = "CLI离线标签"; var request = RequestFile(next, "generate-all-page"); var binary = Path.Combine(root, "cli");
        var build = await Cli(SourceRoot(), "build", "backend/src/Metarill.Cli/Metarill.Cli.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet", "--output", binary); Assert.True(build.Code == 0, build.Output); var cli = Path.Combine(binary, "Metarill.Cli.dll"); var candidate = Path.Combine(root, "prepared"); var plan = Path.Combine(root, "cli-plan.json"); var before = SourceSnapshot(target);
        var invalid = await Cli(SourceRoot(), cli, "update", "prepare", "--request", request, "--target", target, "--candidate", candidate, "--source-root", SourceRoot(), "--plan", plan, "--actions", "sync-runtime"); Assert.Equal(1, invalid.Code); Assert.False(Directory.Exists(candidate));
        var result = await Cli(SourceRoot(), cli, "update", "prepare", "--request", request, "--target", target, "--candidate", candidate, "--source-root", SourceRoot(), "--plan", plan); Assert.True(result.Code == 3, result.Output); using var json = JsonDocument.Parse(result.Output); Assert.Equal("dependencies-required", json.RootElement.GetProperty("status").GetString()); Assert.False(json.RootElement.GetProperty("plan").GetProperty("canApply").GetBoolean()); Assert.Equal(before, SourceSnapshot(target));
    }

    /// <summary>离线文件只落在测试GUID目录，使用产品封套且不混入R或登录配置。</summary>
    private string RequestFile(MetadataCatalog catalog, string action)
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "request-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(path, JsonSerializer.Serialize(LocalGenerationUpdater.CreateRequest(catalog, action), Json) + "\n"); return path;
    }
    /// <summary>从公开独立包重建可信源所需内容，不复制开发者环境或git目录。</summary>
    private string PublicSource(string package)
    {
        var source = Path.Combine(root, "trusted-source");
        foreach (var folder in new[] { "src", "frontend" }) foreach (var file in Directory.EnumerateFiles(Path.Combine(package, folder), "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(source, folder == "src" ? "backend/src" : "frontend/metarill_uni_admin", Path.GetRelativePath(Path.Combine(package, folder), file)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
        }
        // 已装配包菜单已注入宿主能力门禁；可信源码保留已核对的原公共组件，不能把适配产物反向当作模板。
        const string menu = "frontend/metarill_uni_admin/components/yzp-menu/yzp-menu.vue";
        File.Copy(Path.Combine(SourceRoot(), menu), Path.Combine(source, menu), true);
        foreach (var path in new[] { "global.json", "LICENSE", "Directory.Packages.props" }) File.Copy(Path.Combine(SourceRoot(), path), Path.Combine(source, path));
        var metadata = Path.Combine(source, "backend/metadata"); Directory.CreateDirectory(metadata); foreach(var name in new[]{"files","security"})File.Copy(Path.Combine(package,"backend/metadata/"+name+".json"),Path.Combine(metadata,name+".json")); return source;
    }
}
