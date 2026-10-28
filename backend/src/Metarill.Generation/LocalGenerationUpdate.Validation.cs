using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

public sealed partial class LocalGenerationUpdater
{
    private sealed record UpdateEntry(string Path, string Sha256);
    private sealed record PackageManifest(int SchemaVersion, string RootNamespace, string PackageMode, bool FrontendIncluded, string[] Warnings, string MetadataSha256, UpdateEntry[] Files, string? AuthenticationProfile=null,string? SecurityCatalogSha256=null);
    private sealed record GeneratedManifest(int Version, string ProjectId, string RootNamespace, string MetadataSha256, UpdateEntry[] Files);
    private sealed record FrontendManifest(int SchemaVersion, string ProjectId, string Authentication, UpdateEntry[] Files);
    private sealed record PackageSnapshot(string Root, PackageManifest Package, GeneratedManifest Generated, FrontendManifest Frontend, MetadataCatalog Metadata, Dictionary<string, UpdateEntry> Entries)
    { public string ProjectId => Generated.ProjectId; }
    private static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    { "README.md", ".gitignore", "backend/Application.Host/appsettings.example.json", "frontend/README.md", "frontend/.gitignore", "frontend/manifest.json", "frontend/common/application.config.js", "frontend/common/project.js", "frontend/static/logo.png", "frontend/App.vue", "frontend/main.js", "frontend/pages/main/index.vue", "frontend/pages/main/main.vue", "frontend/pages/main/left.vue", "frontend/pages/main/top.vue", "frontend/pages/main/menu.vue", "frontend/pages/main/my.vue", "frontend/pages/main/change_password.vue" };
    // 固定原壳骨架只能在完全缺失时首次创建；已有字节始终属于开发者，不包含AppID/API配置或私有凭据。
    private static readonly HashSet<string> FirstCreateFrontend = new(StringComparer.Ordinal)
    { "frontend/App.vue", "frontend/main.js", "frontend/pages/main/index.vue", "frontend/pages/main/main.vue", "frontend/pages/main/left.vue", "frontend/pages/main/top.vue", "frontend/pages/main/menu.vue", "frontend/pages/main/my.vue", "frontend/pages/main/change_password.vue", "frontend/common/project.js", "frontend/static/logo.png" };
    private const string SecurityCatalogBindingPath="backend/Application.Host/PackageSecurityCatalogBinding.g.cs";
    private const string SecurityCatalogRuntimePath="backend/Application.Host/PackageSecurityCatalog.cs";
    private static readonly HashSet<string> Configuration = new(StringComparer.Ordinal)
    { "backend/metadata/project.json", SecurityCatalogBindingPath, "frontend/common/application.generated.json", "frontend/pages.json", "frontend/pages.generated.json", "backend/generated/frontend/pages.generated.json" };
    private static readonly HashSet<string> RuntimeFiles = new(StringComparer.Ordinal)
    {
      // 跨表读取partial只登记这一路径，未知多点源码继续拒绝。
      "src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.CrossTable.cs",
      "src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.SharedCrossTable.cs",
      // 派生多选名称partial只登记此固定路径，未知相邻源码仍拒绝。
      "src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.MultiNames.cs",
      // 新文件浏览partial只登记此固定路径，不扩成任意多点源码覆盖。
      "src/Metarill.Files/FileLibraryService.Browsing.cs", SecurityCatalogRuntimePath, "backend/Application.Host/SessionPolicy.cs", "backend/Application.Host/AgentIntegration.cs", "backend/Application.Host/McpIntegration.cs", "src/Metarill.Agent/AgentFoundationCatalog.g.cs", "frontend/pages/custom/agent_authorize.vue", "frontend/pages/custom/agent_grants.vue", "backend/Application.Host/WebSecurityCoordination.cs", "backend/Application.Host/SystemMaintenanceController.cs", "backend/Application.Host/OriginalBusinessButtonController.cs", "Directory.Build.props", "Directory.Packages.props", "global.json", "LICENSE", "backend/metadata/files.json", "backend/metadata/security.json", "backend/Application.Host/WebAuthentication.cs", "backend/Application.Host/SecurityInitialization.cs", "src/Metarill.Security/PrincipalRolePolicy.Evaluation.cs", "src/Metarill.Security/PrincipalRolePolicy.Inheritance.cs", "src/Metarill.Security/SecurityRealmMutationGuard.Validation.cs", "src/Metarill.Security/SecurityRealmMutationGuard.Initialization.cs", "src/Metarill.Security/SecurityRealmMutationGuard.BusinessButtons.cs", "backend/Application.Host/Program.cs", "backend/Application.Host/DevelopmentAuthentication.cs", "backend/Application.Host/DevelopmentNavigation.cs", "backend/Application.Host/DevelopmentPasswordStore.cs", "backend/Application.Host/Application.Host.csproj",
      "frontend/App.vue", "frontend/main.js", "frontend/uni.scss", "frontend/index.html", "frontend/common/environment.js", "frontend/common/yzp.js", "frontend/common/utils.js", "frontend/common/yzp.scss", "frontend/pages/login.vue", "frontend/pages/main/index.vue", "frontend/pages/main/menu.vue", "frontend/pages/main/left.vue", "frontend/pages/main/top.vue", "frontend/pages/main/my.vue", "frontend/pages/main/rtf/rtf.vue", "frontend/scripts/verify-frontend.mjs", "frontend/package.json", "frontend/package-lock.json", "frontend/THIRD-PARTY-NOTICES.md", "frontend/uni_modules/LICENSE.uni-ui", "frontend/licenses/sheetjs.LICENSE", "frontend/licenses/sheetjs-dist.LICENSE", "frontend/licenses/tinymce-vue.LICENSE.txt", "frontend/vendor/xlsx-0.20.3.tgz", "frontend/vendor/sheetjs-source.json", "frontend/common/yzp_runtime/vendor/cropperjs/cropper.esm.js", "frontend/common/yzp_runtime/vendor/cropperjs/cropper.css", "frontend/common/yzp_runtime/vendor/cropperjs/LICENSE" };
    private static readonly Lazy<HashSet<string>> FrontendAssets = new(() =>
    {
        var assembly = typeof(CSharpGenerator).Assembly; var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".frontend-assets.allowlist.json.tpl", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return JsonSerializer.Deserialize<string[]>(reader.ReadToEnd())!.Select(path => "frontend/" + path).ToHashSet(StringComparer.Ordinal);
    });

    /// <summary>识别既有v1三层归属；候选必须仍与当前CLI的真实生成输出匹配，不能猜测未来版本协议。</summary>
    private static PackageSnapshot ReadPackage(string root, bool candidate)
    {
        var package = Decode<PackageManifest>(ReadBytes(root, PackageMarker)); var generated = Decode<GeneratedManifest>(ReadBytes(root, GenerationMarker)); var frontend = Decode<FrontendManifest>(ReadBytes(root, FrontendMarker));
        if (package.SchemaVersion != 1 || generated.Version != 1 || frontend.SchemaVersion != 1 || frontend.Authentication is not("development-only" or "web-database-v1")) throw new InvalidOperationException("未知包、生成或前端清单版本，当前更新器不能证明兼容。");
        if(package.AuthenticationProfile is not null && package.AuthenticationProfile!=frontend.Authentication || candidate && frontend.Authentication!="web-database-v1")throw new InvalidOperationException("包认证profile与当前候选宿主不一致。旧开发包不能伪称数据库Web。 ");
        if (package.PackageMode != "FullApplication" || !package.FrontendIncluded) throw new InvalidOperationException("首期已有项目更新只支持FullApplication，BackendOnly需独立适配。");
        if (!Regex.IsMatch(package.RootNamespace ?? "", @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") || generated.RootNamespace != package.RootNamespace || !ValidHash(package.MetadataSha256) || package.MetadataSha256 != generated.MetadataSha256) throw new InvalidOperationException("namespace或元数据摘要不一致。");
        if (!long.TryParse(generated.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var project) || project <= 0 || project.ToString(CultureInfo.InvariantCulture) != generated.ProjectId || generated.ProjectId != frontend.ProjectId) throw new InvalidOperationException("项目标识不一致或无效。");
        var entries = Entries(package.Files); var generatedEntries = Entries(generated.Files); var frontendEntries = Entries(frontend.Files);
        // 首次旧目标允许尚无proof；当前候选必须同时拥有编译绑定与固定实现，不能由manifest自报代替。
        if(frontend.Authentication=="web-database-v1"&&(candidate||package.SecurityCatalogSha256 is not null))
        {
            var actual=Hash(ReadBytes(root,"backend/metadata/project.json"));
            if(package.SecurityCatalogSha256!=actual||!entries.ContainsKey(SecurityCatalogBindingPath)||!entries.ContainsKey(SecurityCatalogRuntimePath))throw new InvalidOperationException("固定目录编译摘要或完整归属缺失、不一致。");
            if(candidate&&Hash(ReadBytes(root,SecurityCatalogBindingPath))!=Hash(System.Text.Encoding.UTF8.GetBytes(ApplicationPackager.SecurityCatalogBindingSource(package.RootNamespace,actual))))throw new InvalidOperationException("固定目录编译绑定源码不匹配当前候选。");
        }
        var metadata = Decode<MetadataCatalog>(ReadBytes(root, "backend/metadata/project.json"));
        var infrastructure = Decode<MetadataCatalog>(ReadBytes(root, "backend/metadata/files.json"));
        MetadataCatalogComposition.MergePhysical(metadata, infrastructure);
        if(frontend.Authentication=="web-database-v1")MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(metadata,infrastructure),Decode<MetadataCatalog>(ReadBytes(root,"backend/metadata/security.json")));
        MetadataValidation.Validate(metadata);
        if (metadata.YzProjectId != generated.ProjectId || CanonicalHash(metadata) != generated.MetadataSha256) throw new InvalidOperationException("应用元数据与生成清单摘要不一致。");
        if (package.Warnings is null || package.Warnings.Any(warning => warning is null)) throw new InvalidOperationException("包提示列表无效。");
        var snapshot = new PackageSnapshot(root, package, generated, frontend, metadata, entries);
        // FullApplication的启动入口不能通过同时删文件和删归属来伪装成可选运行时删除。
        var bootstrap = new[] { GenerationMarker, FrontendMarker, "Directory.Build.props", "Directory.Packages.props", "global.json", "LICENSE",
            "backend/metadata/project.json", "backend/metadata/files.json", "backend/Application.Host/Program.cs", "backend/Application.Host/Application.Host.csproj", "backend/Application.Host/DevelopmentAuthentication.cs", "backend/Application.Host/DevelopmentNavigation.cs",
            "frontend/App.vue", "frontend/main.js", "frontend/uni.scss", "frontend/index.html", "frontend/manifest.json", "frontend/package.json", "frontend/package-lock.json",
            "frontend/common/application.config.js", "frontend/common/application.generated.json", "frontend/common/environment.js", "frontend/common/yzp.js", "frontend/common/yzp_runtime/yzp-runtime.js", "frontend/pages.json", "frontend/pages.generated.json" }
            .Concat(new[] { "Core", "Data", "Exports", "Files" }.Select(name => "src/Metarill." + name + "/Metarill." + name + ".csproj"));
        if(frontend.Authentication=="web-database-v1")bootstrap=bootstrap.Concat(new[]{"backend/metadata/security.json","backend/Application.Host/WebAuthentication.cs","backend/Application.Host/SecurityInitialization.cs","src/Metarill.Security/Metarill.Security.csproj"});
        // 旧Full仍可预览显式运行时升级；当前候选不能同时删源码和归属来伪造完整协调闭包。
        if(candidate&&frontend.Authentication=="web-database-v1")bootstrap=bootstrap.Concat(new[]{"backend/Application.Host/SessionPolicy.cs", "backend/Application.Host/AgentIntegration.cs", "backend/Application.Host/McpIntegration.cs", "src/Metarill.Agent/AgentFoundationCatalog.g.cs", "frontend/pages/custom/agent_authorize.vue", "frontend/pages/custom/agent_grants.vue","backend/Application.Host/WebSecurityCoordination.cs","backend/Application.Host/SystemMaintenanceController.cs", "backend/Application.Host/OriginalBusinessButtonController.cs", "src/Metarill.Core/Metadata/ButtonVisibilityRule.cs", "src/Metarill.Agent/AgentButtonVisibility.cs", "src/Metarill.Core/Persistence/IBusinessButtonExecutionGuard.cs", "src/Metarill.Data/DatabaseExecutionLeases.cs", "src/Metarill.Data/MySqlExecutionLocks.cs", "src/Metarill.Data/Coordination/DatabaseButtonExecutionGuard.cs"});
        foreach (var required in bootstrap)
            if (!entries.ContainsKey(required)) throw new InvalidOperationException("完整包缺少必要启动归属：" + required);
        foreach (var entry in generatedEntries.Values)
        {
            if (!entries.TryGetValue("backend/generated/" + entry.Path, out var outer) || outer.Sha256 != entry.Sha256) throw new InvalidOperationException("生成清单与包清单不一致。");
            if (entry.Path.StartsWith("frontend/", StringComparison.Ordinal) && (!entries.TryGetValue(entry.Path, out var mirror) || mirror.Sha256 != entry.Sha256)) throw new InvalidOperationException("生成前端片段与可运行前端镜像不一致。");
        }
        foreach (var entry in frontendEntries.Values)
            if (!entries.TryGetValue("frontend/" + entry.Path, out var outer) || outer.Sha256 != entry.Sha256) throw new InvalidOperationException("前端清单与包清单不一致。");
        foreach (var entry in entries.Values)
        {
            if (entry.Path.StartsWith("frontend/", StringComparison.Ordinal) && entry.Path != FrontendMarker
                && (!frontendEntries.TryGetValue(entry.Path["frontend/".Length..], out var registered) || registered.Path != entry.Path["frontend/".Length..] || registered.Sha256 != entry.Sha256)) throw new InvalidOperationException("包内前端文件漏登内层归属清单。");
            var role = Role(entry.Path, snapshot); var actual = ExistingHash(root, entry.Path);
            if (!candidate && role is "handwritten" or "preserve") continue;
            if (actual != entry.Sha256) throw new InvalidOperationException("受管文件缺失或被修改：" + entry.Path);
        }
        // 可编辑AppID和API地址不按旧占位摘要拒绝，但仍须是本包必需的真实文件。
        foreach (var path in new[] { "frontend/manifest.json", "frontend/common/application.config.js" }) if (!File.Exists(Safe(root, path))) throw new InvalidOperationException("缺少开发者宿主配置：" + path);
        using (var manifest = JsonDocument.Parse(ReadBytes(root, "frontend/manifest.json")))
            if (!manifest.RootElement.TryGetProperty("vueVersion", out var vue) || vue.GetString() != "3" || !manifest.RootElement.TryGetProperty("appid", out var appid) || appid.ValueKind != JsonValueKind.String) throw new InvalidOperationException("已有前端必须保持Vue3及字符串AppID配置。");
        if (candidate)
        {
            // 候选必须满足完整前端能力合同；旧目标只读归属验证不反向强加新能力门禁。
            new FrontendPackager().Validate(metadata);
            var model = GenerationModel.Create(metadata, package.RootNamespace);
            var rendered = BackendRenderer.Render(model).Concat(UniappRenderer.Render(model)).Concat(WriteSchemaRenderer.Render(model)).ToArray();
            var expected = rendered.Where(file => !file.Handwritten).Select(file => new UpdateEntry(file.Path, Hash(System.Text.Encoding.UTF8.GetBytes(file.Content.Replace("\r\n", "\n").Replace("\r", "\n"))))).ToArray();
            if (!SameEntries(expected, generated.Files)) throw new InvalidOperationException("候选生成器输出与当前CLI不兼容，请使用对应版本重新导出。");
            foreach (var shell in rendered.Where(file => file.Handwritten))
                if (!entries.TryGetValue("backend/generated/" + shell.Path, out var entry) || entry.Sha256 != Hash(System.Text.Encoding.UTF8.GetBytes(shell.Content.Replace("\r\n", "\n").Replace("\r", "\n")))) throw new InvalidOperationException("候选手写壳不是当前生成器的首次创建模板。");
            ValidateFrontend(snapshot);
        }
        return snapshot;
    }

    /// <summary>路径策略由本地工具固定，不能只相信输入manifest把任意配置登记为可覆盖文件。</summary>
    private static string Role(string path, PackageSnapshot package)
    {
        ValidateRelative(path);
        if (path is PackageMarker or GenerationMarker or FrontendMarker) return "manifest";
        if (Protected.Contains(path)) return "preserve";
        if (Configuration.Contains(path)) return Config;
        if (path.StartsWith("backend/generated/", StringComparison.Ordinal))
        {
            var relative = path["backend/generated/".Length..];
            if (package.Generated.Files.Any(entry => entry.Path == relative) && Regex.IsMatch(relative, @"^(?:Entities/[A-Za-z0-9_]+\.g\.cs|Mappers/[A-Za-z0-9_]+Mapper\.g\.cs|Services/[A-Za-z0-9_]+Service\.g\.cs|DTOs/[A-Za-z0-9_]+Requests\.g\.cs|VOs/[A-Za-z0-9_]+\.g\.cs|Controllers/[A-Za-z0-9_]+Controller\.g\.cs|GeneratedRegistration\.g\.cs|GeneratedWriteSchema\.g\.cs|generation-capabilities\.json|frontend/pages/yzp/[A-Za-z0-9_-]+\.vue|frontend/common/yzp_runtime/page-write-schema\.generated\.json)$")) return Pages;
            if (Regex.IsMatch(relative, @"^Services/[A-Za-z0-9_]+Service\.cs$") && package.Generated.Files.Any(entry => entry.Path == relative.Replace("Service.cs", "Service.g.cs", StringComparison.Ordinal))) return "handwritten";
            throw new InvalidOperationException("未知生成文件归属：" + path);
        }
        if (path.StartsWith("frontend/", StringComparison.Ordinal) && package.Generated.Files.Any(entry => entry.Path == path)) return Pages;
        if (RuntimeFiles.Contains(path) || FrontendAssets.Value.Contains(path) || Regex.IsMatch(path, @"^frontend/common/yzp_runtime/[A-Za-z0-9_-]+\.js$")
            || Regex.IsMatch(path, @"^src/Metarill\.(?:Core|Data|Exports|Files|Security|Agent|Mcp)/(?:[A-Za-z0-9_/-]+\.cs|[A-Za-z0-9_.-]+\.csproj|licenses/(?:LICENSE|THIRD-PARTY-NOTICES)\.txt)$")) return Runtime;
        throw new InvalidOperationException("包包含未声明更新策略的路径：" + path);
    }

    /// <summary>候选的H5静态导入、生成路由和必要组件必须有真实归属文件，不以空壳冒充依赖闭包。</summary>
    private static void ValidateFrontend(PackageSnapshot package)
    {
        var known = package.Frontend.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        using var dependencies = JsonDocument.Parse(ReadBytes(package.Root, "frontend/package.json"));
        var externals = dependencies.RootElement.GetProperty("dependencies").EnumerateObject().Select(property => property.Name).Concat(["vue", "@dcloudio/uni-i18n"]).ToHashSet(StringComparer.Ordinal);
        foreach (var path in known.Where(path => Regex.IsMatch(path, @"\.(?:js|mjs|vue|scss|css)$")))
        {
            var text = H5(System.Text.Encoding.UTF8.GetString(ReadBytes(package.Root, "frontend/" + path)));
            foreach (Match match in Regex.Matches(text, @"(?:\bfrom\s*|\bimport\s*(?:\(\s*)?|\brequire\s*\(\s*)['""]([^'""]+)['""]"))
            {
                var name = match.Groups[1].Value;
                if (!name.StartsWith('.') && !name.StartsWith("@/", StringComparison.Ordinal))
                { if (externals.Contains(name) || path.StartsWith("scripts/", StringComparison.Ordinal) && name.StartsWith("node:", StringComparison.Ordinal)) continue; throw new InvalidOperationException("候选包含未声明外部模块：" + name); }
                var resolved = name.StartsWith("@/", StringComparison.Ordinal) ? name[2..] : Path.GetRelativePath("/", Path.GetFullPath(Path.Combine("/", Path.GetDirectoryName(path)!.Replace('\\', '/'), name))).Replace('\\', '/');
                ValidateRelative(resolved);
                if (!new[] { resolved, resolved + ".js", resolved + ".mjs", resolved + ".vue", resolved + ".json", resolved + ".scss", resolved + ".css", resolved + "/index.js" }.Any(known.Contains)) throw new InvalidOperationException("候选缺少前端依赖：" + path + " -> " + name);
            }
        }
        using var routes = JsonDocument.Parse(ReadBytes(package.Root, "frontend/pages.json")); var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in routes.RootElement.GetProperty("pages").EnumerateArray())
        { var path = route.GetProperty("path").GetString()!; ValidateRelative(path); if (!registered.Add(path) || !known.Contains(path + ".vue")) throw new InvalidOperationException("候选路由重复或缺少页面：" + path); }
        using var generated = JsonDocument.Parse(ReadBytes(package.Root, "frontend/pages.generated.json"));
        foreach (var route in generated.RootElement.GetProperty("pages").EnumerateArray()) if (!registered.Contains(route.GetProperty("path").GetString()!)) throw new InvalidOperationException("候选漏注册生成页面。");
    }

    /// <summary>与独立前端校验器相同，仅读取H5/Vue3条件块，不执行候选代码。</summary>
    private static string H5(string text)
    {
        var stack = new Stack<bool>(); stack.Push(true); var lines = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var directive = Regex.Match(line, @"^\s*(?://|<!--|/\*)\s*#(ifdef|ifndef|endif)\s*(.*?)(?:\s*-->|\s*\*/)?\s*$");
            if (!directive.Success) { if (stack.Peek()) lines.Add(line); continue; }
            if (directive.Groups[1].Value == "endif") { if (stack.Count == 1) throw new InvalidOperationException("条件编译指令不配对。"); stack.Pop(); }
            else { var enabled = directive.Groups[2].Value.Split("||").Any(flag => flag.Trim() is "H5" or "VUE3"); stack.Push(stack.Peek() && (directive.Groups[1].Value == "ifdef" ? enabled : !enabled)); }
        }
        if (stack.Count != 1) throw new InvalidOperationException("条件编译指令不配对。");
        return string.Join('\n', lines);
    }

    /// <summary>元数据摘要复用生成器的确定性规范：对象键及带id数组排序，不使用原始文件字节摘要代替。</summary>
    private static string CanonicalHash(MetadataCatalog catalog)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) Canonical(document.RootElement, writer); return Hash(stream.ToArray());
    }
    /// <summary>递归规范JSON对象及带ID数组的顺序，标量值保持原类型。</summary>
    private static void Canonical(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object) { writer.WriteStartObject(); foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Canonical(property.Value, writer); } writer.WriteEndObject(); }
        else if (value.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); var items = value.EnumerateArray().ToArray(); if (items.Length > 0 && items.All(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _))) items = items.OrderBy(item => item.GetProperty("id").ToString(), StringComparer.Ordinal).ToArray(); foreach (var item in items) Canonical(item, writer); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
    /// <summary>所有协议记录严格拒绝未知和重复键，不能用JSON覆盖语义绕过清单验证。</summary>
    private static T Decode<T>(byte[] bytes)
    {
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidOperationException("协议清单超过4MiB限制。");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 }); CheckKeys(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidOperationException("协议清单为空。");
    }
    /// <summary>逐层拒绝重复JSON键，避免不同解析器采用不同覆盖结果。</summary>
    private static void CheckKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) { var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var property in value.EnumerateObject()) { if (!seen.Add(property.Name)) throw new InvalidOperationException("JSON包含重复字段。"); CheckKeys(property.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckKeys(item);
    }
    /// <summary>验证归属项数量、相对路径和摘要，同时拒绝大小写重名。</summary>
    private static Dictionary<string, UpdateEntry> Entries(UpdateEntry[] files)
    {
        if (files is null || files.Length is 0 or > 20000) throw new InvalidOperationException("归属清单为空或超过20000项。");
        var result = new Dictionary<string, UpdateEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in files) { if (entry is null) throw new InvalidOperationException("归属记录不能为空。"); ValidateRelative(entry.Path); if (!ValidHash(entry.Sha256) || !result.TryAdd(entry.Path, entry)) throw new InvalidOperationException("归属摘要无效或路径重复。"); }
        return result;
    }
    /// <summary>协议只接受完整的小写十六进制SHA-256。</summary>
    private static bool ValidHash(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{64}$");
    /// <summary>路径保持跨平台单一拼写，额外拒绝Windows设备名、ADS、尾点和控制字符。</summary>
    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.Contains('\\') || Path.IsPathRooted(path)) throw new InvalidOperationException("包内相对路径无效。");
        foreach (var segment in path.Split('/'))
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)) || Regex.IsMatch(segment, @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new InvalidOperationException("包内路径包含保留名或不安全分段。");
    }
    /// <summary>组合固定目录前验证跨平台路径，再沿祖先拒绝链接。</summary>
    private static string Safe(string root, string path) { ValidateRelative(path); return CSharpGenerator.SafePath(root, path); }
    /// <summary>只接受实际存在的独立目录，磁盘根目录与祖先链接均拒绝。</summary>
    private static string Root(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(root) || root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("目标和候选必须是明确存在的项目目录，不能是磁盘根目录。");
        CSharpGenerator.SafePath(root, PackageMarker); return root;
    }
    /// <summary>使用目录分隔符边界判断包含关系，避免相似前缀误判。</summary>
    private static bool IsWithin(string root, string path) => root.Equals(path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    /// <summary>有界读取已验证路径，不让未知文件大小耗尽内存。</summary>
    private static byte[] ReadBytes(string root, string path)
    {
        var target = Safe(root, path); var info = new FileInfo(target);
        if (!info.Exists || info.Length > 32 * 1024 * 1024) throw new InvalidOperationException("文件缺失或超过32MiB限制：" + path);
        return File.ReadAllBytes(target);
    }
}
