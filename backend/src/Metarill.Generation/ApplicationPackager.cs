using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

public sealed record ApplicationPackageResult(IReadOnlyList<string> Files, string MetadataSha256, IReadOnlyList<string> RedactedPaths, IReadOnlyList<string> Warnings);

/// <summary>将通用基础设施和生成源码装配为独立应用，不读取私有配置，也不连接数据库。</summary>
public sealed class ApplicationPackager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };
    private static readonly string[] FoundationProjects = ["Metarill.Core", "Metarill.Data", "Metarill.Exports", "Metarill.Files"];
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    { "connstring", "connectionstring", "password", "pwd", "dbpassword", "accesskey", "accesskeyid", "accesskeysecret", "secret", "secretkey", "privatekey", "clientsecret", "apitoken", "accesstoken", "refreshtoken", "token", "apikey", "credentials" };

    /// <summary>先在同级临时目录完成整个包，成功后一次移动；既有非空项目永不覆盖。</summary>
    public ApplicationPackageResult Create(MetadataCatalog catalog, string outputDirectory, string rootNamespace, string sourceRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        CSharpGenerator.SafePath(output, ".metarill-package.json");
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidOperationException("独立包输出目录必须不存在或为空。");
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        foreach (var project in FoundationProjects)
            if (output.StartsWith(Path.Combine(source, "backend", "src", project) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || output.Equals(Path.Combine(source, "backend", "src", project), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("打包目录不能位于基础源码项目内部。");
        var stage = output.TrimEnd(Path.DirectorySeparatorChar) + ".staging-" + Guid.NewGuid().ToString("N");
        var redactions = new List<string>();
        var json = JsonNode.Parse(JsonSerializer.Serialize(catalog, JsonOptions))!;
        Scrub(json, "$", redactions);
        // 密码列保留定义，只清除默认凭据，不能把 columnName/password 误当敏感值移除。
        var sensitiveColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in json["configColumns"]?.AsArray() ?? [])
        {
            if (column is not JsonObject item || !SensitiveKeys.Contains(Normalize(item["columnName"]?.ToString() ?? ""))) continue;
            sensitiveColumns.Add(item["id"]?.ToString() ?? "");
            foreach (var key in new[] { "defaultValue", "initValue" }) if (item[key] is not null) { item[key] = ""; redactions.Add("$.configColumns[" + item["id"] + "]." + key); }
        }
        foreach (var field in json["configPageColumns"]?.AsArray() ?? [])
            if (field is JsonObject item && sensitiveColumns.Contains(item["configColumnId"]?.ToString() ?? ""))
                foreach (var key in new[] { "defaultValue", "initValue" }) if (item[key] is not null) { item[key] = ""; redactions.Add("$.configPageColumns[" + item["id"] + "]." + key); }
        var sanitized = json.Deserialize<MetadataCatalog>(JsonOptions)!;
        // 基础表仅来自已提交的派生模型；先验证物理组合，冲突不产生任何输出。
        var infrastructureJson = JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(source, "backend/metadata/files.json")))!;
        Scrub(infrastructureJson, "$.files", redactions);
        var infrastructure = infrastructureJson.Deserialize<MetadataCatalog>(JsonOptions)!;
        MetadataCatalogComposition.MergePhysical(sanitized, infrastructure);
        try
        {
            Directory.CreateDirectory(stage);
            var generated = new CSharpGenerator().Generate(sanitized, Path.Combine(stage, "backend/generated"), rootNamespace);
            var projects = FoundationProjects;
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
                    if (!FoundationProjects.Any(target => include == "../" + target + "/" + target + ".csproj"))
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
            Write(stage, "backend/metadata/project.json", json.ToJsonString(JsonOptions) + "\n");
            Write(stage, "backend/metadata/files.json", infrastructureJson.ToJsonString(JsonOptions) + "\n");
            foreach (var (template, destination) in new[] { ("Program.cs.tpl", "backend/Application.Host/Program.cs"), ("Authentication.cs.tpl", "backend/Application.Host/DevelopmentAuthentication.cs"), ("Host.csproj.tpl", "backend/Application.Host/Application.Host.csproj"), ("README.md.tpl", "README.md"), ("appsettings.example.json.tpl", "backend/Application.Host/appsettings.example.json") })
                Write(stage, destination, Template(template).Replace("{{NAMESPACE}}", rootNamespace));
            Write(stage, ".gitignore", "**/bin/\n**/obj/\n.local/\n**/appsettings.local.json\n**/appsettings.Development.json\n");
            var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Select(file => new { path = Path.GetRelativePath(stage, file).Replace('\\', '/'), sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))) }).OrderBy(file => file.path, StringComparer.Ordinal).ToArray();
            Write(stage, ".metarill-package.json", JsonSerializer.Serialize(new { schemaVersion = 1, rootNamespace, metadataSha256 = generated.MetadataSha256, files }, JsonOptions) + "\n");
            // 移动前再次检查输出目录，避免其他进程在打包期间写入后被覆盖。
            if (Directory.Exists(output)) Directory.Delete(output, false);
            Directory.Move(stage, output);
            return new ApplicationPackageResult(files.Select(file => file.path).Append(".metarill-package.json").ToArray(), generated.MetadataSha256, redactions.Distinct().Order(StringComparer.Ordinal).ToArray(), generated.Warnings);
        }
        finally
        {
            // 只清理本次创建的、已验证位于输出同级的临时目录。
            if (Directory.Exists(stage) && Path.GetDirectoryName(stage) == Path.GetDirectoryName(output) && Path.GetFileName(stage).StartsWith(Path.GetFileName(output) + ".staging-", StringComparison.Ordinal)) Directory.Delete(stage, true);
        }
    }
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
    /// <summary>递归删除配置值；普通字段定义与注释不会按内容猜测删除。</summary>
    private static void Scrub(JsonNode node, string path, List<string> redactions)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                var childPath = path + "." + property.Key;
                if (SensitiveKeys.Contains(Normalize(property.Key))) { obj.Remove(property.Key); redactions.Add(childPath); }
                else if (property.Value is JsonObject or JsonArray) Scrub(property.Value, childPath, redactions);
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var content) && content.TrimStart() is [ '{' or '[', .. ])
                {
                    try { var nested = JsonNode.Parse(content); if (nested is JsonObject or JsonArray) { Scrub(nested, childPath, redactions); obj[property.Key] = nested.ToJsonString(JsonOptions); } }
                    catch (JsonException) { /* 非 JSON 配置说明保持原文，不执行或猜测其内容。 */ }
                }
            }
        else if (node is JsonArray array) for (var i = 0; i < array.Count; i++) if (array[i] is { } item) Scrub(item, path + "[" + i + "]", redactions);
    }
    /// <summary>匹配敏感键时忽略常见分隔符，不检查普通配置说明的文本内容。</summary>
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
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
