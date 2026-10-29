using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

public enum ApplicationPackageMode { FullApplication, BackendOnly }
public sealed record ApplicationPackageResult(IReadOnlyList<string> Files, string MetadataSha256, IReadOnlyList<string> RedactedPaths, IReadOnlyList<string> Warnings)
{
    public bool FrontendIncluded { get; init; }
    public ApplicationPackageMode Mode { get; init; }
}

/// <summary>将通用基础设施和生成源码装配为独立应用，不读取私有配置，也不连接数据库。</summary>
public sealed class ApplicationPackager
{
    private readonly Action<string, string> publish;
    /// <summary>内外两层使用同一发布边界，失败stage必须一直保留到调用者诊断。</summary>
    public ApplicationPackager() : this(PackagePublication.Publish) { }
    /// <summary>内部逐实例注入发布动作，故障测试不使用全局可变hook。</summary>
    internal ApplicationPackager(Action<string, string> publish) => this.publish = publish;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };
    private static readonly string[] FoundationProjects = ["Metarill.Core", "Metarill.Data", "Metarill.Exports", "Metarill.Files"];
    private static readonly string[] WebFoundationProjects = [..FoundationProjects,"Metarill.Security","Metarill.Agent","Metarill.Mcp"];
    private static readonly string[] FullHostAdditionalTemplates=["WebAuthentication.cs","SecurityInitialization.cs","PackageSecurityCatalog.cs","SessionPolicy.cs","AgentIntegration.cs","McpIntegration.cs","WebSecurityCoordination.cs","SystemMaintenanceController.cs","OriginalBusinessButtonController.cs"];
    /// <summary>管理宿主等同源验收复用生产模板清单，避免新增接线只进入普通包而遗漏管理宿主。</summary>
    internal static IReadOnlyList<string> FullHostSourceTemplates {get;}=Array.AsReadOnly(new[]{"Program.cs","Authentication.cs","PasswordStore.cs","Navigation.cs"}.Concat(FullHostAdditionalTemplates).ToArray());
    /// <summary>仅公开不可修改的工程名快照；既有管理宿主按同一闭包编译，不另维护过期依赖名单。</summary>
    internal static IReadOnlyList<string> FullFoundationProjects {get;}=Array.AsReadOnly(WebFoundationProjects);

    /// <summary>先在同级临时目录完成整个包，成功后一次移动；既有非空项目永不覆盖。</summary>
    public ApplicationPackageResult Create(MetadataCatalog catalog, string outputDirectory, string rootNamespace, string sourceRoot, ApplicationPackageMode mode = ApplicationPackageMode.FullApplication)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var fullApplication=mode==ApplicationPackageMode.FullApplication;
        var projects=fullApplication?WebFoundationProjects:FoundationProjects;
        var authenticationProfile=fullApplication?"web-database-v1":"development-only";
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        CSharpGenerator.SafePath(output, ".metarill-package.json");
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidOperationException("独立包输出目录必须不存在或为空。");
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        foreach (var project in projects)
            if (output.StartsWith(Path.Combine(source, "backend", "src", project) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || output.Equals(Path.Combine(source, "backend", "src", project), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("打包目录不能位于基础源码项目内部。");
        var stage = output.TrimEnd(Path.DirectorySeparatorChar) + ".staging-" + Guid.NewGuid().ToString("N");
        var published = GenerationMetadataPublisher.Publish(catalog);
        var redactions = published.RedactedPaths.ToList();
        var sanitized = published.Metadata;
        var json = JsonNode.Parse(JsonSerializer.Serialize(sanitized, JsonOptions))!;
        var projectText=json.ToJsonString(JsonOptions)+"\n";
        // Agent两表来自公共程序集的固定派生产物，原业务不能覆盖结构或开放基础表CRUD。
        var agentModel=fullApplication?AgentFoundationCatalog.ForApplication(sanitized):null;
        if(agentModel is not null)_=MetadataCatalogComposition.MergePhysical(sanitized,agentModel);
        var securityCatalogSha256=fullApplication?Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projectText))):null;
        var frontend = new FrontendPackager(publish);
        // 完整包先报告原页面的具体能力缺口；物理合成仍必须在任何stage写入前严格通过。
        if (fullApplication) frontend.Validate(sanitized);
        // 基础表仅来自已提交的派生模型；先验证物理组合，冲突不产生任何输出。
        var infrastructureJson = JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(source, "backend/metadata/files.json")))!;
        GenerationMetadataPublisher.Scrub(infrastructureJson, "$.files", redactions);
        var infrastructure = infrastructureJson.Deserialize<MetadataCatalog>(JsonOptions)!;
        FileUploadLifetime.ValidateMetadata(infrastructure);
        MetadataCatalogComposition.MergePhysical(sanitized, infrastructure);
        JsonNode? securityJson=null;
        if(fullApplication)
        {
            securityJson=JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(source,"backend/metadata/security.json")))!;
            GenerationMetadataPublisher.Scrub(securityJson,"$.security",redactions);
            var security=securityJson.Deserialize<MetadataCatalog>(JsonOptions)!;
            MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(sanitized,infrastructure),security);
        }
        var frontendIncluded = mode == ApplicationPackageMode.FullApplication;
        var preserveStage = false;
        try
        {
            Directory.CreateDirectory(stage);
            var generated = new CSharpGenerator().Generate(sanitized, Path.Combine(stage, "backend/generated"), rootNamespace);
            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projects)
            {
                var projectSource = CSharpGenerator.SafePath(source, "backend/src/" + project);
                var csproj = CSharpGenerator.SafePath(projectSource, project + ".csproj");
                var xml = XDocument.Load(csproj);
                if (xml.Descendants().Any(element => element.Name.LocalName is "Import" or "Target" or "UsingTask" || element.Name.LocalName == "Compile" && element.Attribute("Include") is not null)) throw new InvalidOperationException("基础项目存在外部编译或构建扩展，不能自动打包。");
                foreach (var reference in xml.Descendants("ProjectReference"))
                {
                    var include = reference.Attribute("Include")?.Value.Replace('\\', '/') ?? "";
                    if (!projects.Any(target => include == "../" + target + "/" + target + ".csproj"))
                        throw new InvalidOperationException("基础项目引用超出独立包边界。");
                }
                foreach (var reference in xml.Descendants("PackageReference")) packageIds.Add(reference.Attribute("Include")?.Value ?? throw new InvalidOperationException("NuGet 引用缺少名称。"));
                foreach (var file in EnumerateSources(projectSource))
                {
                    var relative = Path.GetRelativePath(projectSource, file).Replace('\\', '/');
                    Write(stage, "src/" + project + "/" + relative, File.ReadAllText(file));
                }
                if (!File.Exists(Path.Combine(stage, "src", project, project + ".csproj"))) throw new InvalidOperationException("缺少基础项目工程文件。");
            }
            var versions = XDocument.Load(CSharpGenerator.SafePath(source, "Directory.Packages.props")).Descendants("PackageVersion")
                .Where(element => packageIds.Contains(element.Attribute("Include")?.Value ?? "")).ToArray();
            if (versions.Length != packageIds.Count || versions.Any(element => string.IsNullOrWhiteSpace(element.Attribute("Version")?.Value))) throw new InvalidOperationException("基础依赖必须有明确的中央版本。");
            foreach (var version in versions)
            {
                var value = version.Attribute("Version")!.Value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$")) throw new InvalidOperationException("独立包依赖必须是具体版本，不能使用浮动范围。");
                version.SetAttributeValue("Version", "[" + value + "]");
            }
            var packages = new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("ManagePackageVersionsCentrally", "true")), new XElement("ItemGroup", versions.OrderBy(element => element.Attribute("Include")!.Value, StringComparer.Ordinal).Select(element => new XElement(element)))));
            Write(stage, "Directory.Packages.props", packages.ToString());
            Write(stage, "global.json", File.ReadAllText(CSharpGenerator.SafePath(source, "global.json")));
            Write(stage, "LICENSE", File.ReadAllText(CSharpGenerator.SafePath(source, "LICENSE")));
            Write(stage, "Directory.Build.props", "<Project>\n  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><LangVersion>latest</LangVersion><Deterministic>true</Deterministic><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>\n</Project>\n");
            Write(stage, "backend/metadata/project.json", projectText);
            Write(stage, "backend/metadata/files.json", infrastructureJson.ToJsonString(JsonOptions) + "\n");
            if(securityJson is not null)Write(stage,"backend/metadata/security.json",securityJson.ToJsonString(JsonOptions)+"\n");
            foreach (var (template, destination) in new[] { (fullApplication?"Program.cs.tpl":"DevelopmentProgram.cs.tpl", "backend/Application.Host/Program.cs"), ("Authentication.cs.tpl", "backend/Application.Host/DevelopmentAuthentication.cs"), ("PasswordStore.cs.tpl", "backend/Application.Host/DevelopmentPasswordStore.cs"), (fullApplication?"Navigation.cs.tpl":"DevelopmentNavigation.cs.tpl", "backend/Application.Host/DevelopmentNavigation.cs"), ("Host.csproj.tpl", "backend/Application.Host/Application.Host.csproj"), (fullApplication?"README.md.tpl":"DevelopmentREADME.md.tpl", "README.md"), (fullApplication?"appsettings.example.json.tpl":"DevelopmentSettings.json.tpl", "backend/Application.Host/appsettings.example.json") })
                Write(stage, destination, Template(template).Replace("{{NAMESPACE}}", rootNamespace)
                    .Replace("{{SECURITY_REFERENCE}}",fullApplication?"<ProjectReference Include=\"../../src/Metarill.Security/Metarill.Security.csproj\" /><ProjectReference Include=\"../../src/Metarill.Agent/Metarill.Agent.csproj\" /><ProjectReference Include=\"../../src/Metarill.Mcp/Metarill.Mcp.csproj\" />":"")
                    .Replace("{{SECURITY_CONTENT}}",fullApplication?"<ItemGroup><Content Include=\"../metadata/security.json\" Link=\"metadata/security.json\" CopyToOutputDirectory=\"PreserveNewest\" /></ItemGroup>":""));
            if(fullApplication)
                foreach(var name in FullHostAdditionalTemplates)Write(stage,"backend/Application.Host/"+name,Template(name+".tpl").Replace("{{NAMESPACE}}",rootNamespace));
            if(fullApplication)Write(stage,"backend/Application.Host/PackageSecurityCatalogBinding.g.cs",SecurityCatalogBindingSource(rootNamespace,securityCatalogSha256!));
            Write(stage, ".gitignore", "**/bin/\n**/obj/\n.local/\n**/appsettings.local.json\n**/appsettings.Development.json\n");
            var warnings = generated.Warnings.Concat(frontendIncluded ? Array.Empty<string>() : new[] { "本包显式选择BackendOnly，不含可运行前端宿主；平台配置前端需单独提供。backend/generated/frontend仅为生成片段。" }).ToArray();
            if (frontendIncluded) frontend.Create(sanitized, Path.Combine(stage, "backend/generated/frontend"), Path.Combine(stage, "frontend"), source, includeAgent: true);
            File.AppendAllText(Path.Combine(stage, "README.md"), "\n## 打包模式\n\n" + (frontendIncluded ? "FullApplication：包含frontend独立uni-app宿主与web-database-v1认证，默认Web失败即拒绝；显式开发认证仍仅Development使用。具体初始化、配置及HBuilderX要求见README与frontend/README.md。" : warnings.Last()) + "\n", new UTF8Encoding(false));
            var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Select(file => new { path = Path.GetRelativePath(stage, file).Replace('\\', '/'), sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))) }).OrderBy(file => file.path, StringComparer.Ordinal).ToArray();
            // 与更新器PackageManifest的固定属性顺序一致，原样包预览不能只因profile重排而产生清单写入。
            Write(stage, ".metarill-package.json", JsonSerializer.Serialize(new { schemaVersion = 1, rootNamespace, packageMode = mode.ToString(), frontendIncluded, warnings, metadataSha256 = generated.MetadataSha256, files, authenticationProfile, securityCatalogSha256 }, JsonOptions) + "\n");
            // 移动前再次检查输出目录，避免其他进程在打包期间写入后被覆盖。
            publish(stage, output);
            return new ApplicationPackageResult(files.Select(file => file.path).Append(".metarill-package.json").ToArray(), generated.MetadataSha256, redactions.Distinct().Order(StringComparer.Ordinal).ToArray(), warnings) { Mode = mode, FrontendIncluded = frontendIncluded };
        }
        catch (PackagePublicationException error)
        {
            preserveStage = true; error.PreserveContainingStage(stage); throw;
        }
        finally
        {
            // 发布异常保留明确定义的本次stage；其余失败仍按原边界清理。
            if (!preserveStage && Directory.Exists(stage) && Path.GetDirectoryName(stage) == Path.GetDirectoryName(output) && Path.GetFileName(stage).StartsWith(Path.GetFileName(output) + ".staging-", StringComparison.Ordinal)) Directory.Delete(stage, true);
        }
    }
    /// <summary>配置绑定只生成编译常量；更新器复用同一固定模板核验候选，不能信任manifest自报摘要。</summary>
    internal static string SecurityCatalogBindingSource(string rootNamespace,string sha256)=>Template("PackageSecurityCatalogBinding.g.cs.tpl").Replace("{{NAMESPACE}}",rootNamespace).Replace("{{PACKAGE_CATALOG_SHA256}}",sha256).Replace("\r\n","\n").Replace('\r','\n');

    /// <summary>仅枚举基础源码、工程和固定许可证，拒绝链接，跳过环境文件和构建目录。</summary>
    private static IEnumerable<string> EnumerateSources(string directory)
    {
        foreach (var item in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            CSharpGenerator.SafePath(directory, Path.GetFileName(item));
            if (Directory.Exists(item))
            {
                if (Path.GetFileName(item) is "bin" or "obj" || Path.GetFileName(item).StartsWith('.')) continue;
                foreach (var nested in EnumerateSources(item)) yield return nested;
            }
            else if (Path.GetExtension(item) is ".cs" or ".csproj" || (Path.GetFileName(directory) == "licenses" && (Path.GetFileName(item) is "LICENSE.txt" or "THIRD-PARTY-NOTICES.txt"))) yield return item;
        }
    }
    /// <summary>只从随程序集发布的固定模板读取宿主文件。</summary>
    private static string Template(string name)
    {
        var assembly = typeof(ApplicationPackager).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(value => value.EndsWith("." + name, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!); return reader.ReadToEnd();
    }
    /// <summary>以无 BOM、统一 LF 写入已验证的包内路径。</summary>
    private static void Write(string root, string relative, string content)
    {
        var path = CSharpGenerator.SafePath(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n").Replace('\r', '\n'), new UTF8Encoding(false));
    }
}
