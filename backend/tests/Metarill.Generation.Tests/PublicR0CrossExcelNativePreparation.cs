using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation.Tests;

/// <summary>六模型走正规公开Full生成；历史内部包及其授权不能被当作公开包正式验收。</summary>
internal static class PublicR0CrossExcelNativePreparation
{
    internal const string Root=".local/public-r0-cross-excel-native";
    internal const string OverwriteRoot=".local/public-r0-cross-overwrite-native";
    internal const string OverwriteProductCommit="cc56aae8cffedd1e5bac53eb4c7bb6202ce02a7c";
    internal const string RootNamespace=GlobalCachedCrossExcelNativePreparation.RootNamespace;
    internal const string ProductCommit="d90932ed5808f761591b8c84ca56a72e70f9155b";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};

    /// <summary>两种模式复用六个原模型，覆盖模式只启用已验收的原首列策略。</summary>
    internal static MetadataCatalog Catalog(bool overwrite=false)=>overwrite
        ?NonRedundantCrossOverwriteApplicationTests.Catalog()
        :GlobalCachedCrossExcelNativePreparation.Catalog();

    /// <summary>只在生成器首次创建的六份手写壳内加入原事务观察，重复生成不得覆盖。</summary>
    internal static void Create(string sourceRoot,string output,bool overwrite=false)
    {
        var catalog=Catalog(overwrite);var result=new ApplicationPackager().Create(catalog,output,RootNamespace,sourceRoot);
        if(result.Mode!=ApplicationPackageMode.FullApplication||!result.FrontendIncluded)throw new InvalidOperationException("公开路径验收必须是真正Full应用。");
        var generated=CSharpGenerator.SafePath(output,"backend/generated");var hands=Hands();
        foreach(var hand in hands)File.WriteAllText(CSharpGenerator.SafePath(output,hand.Key),hand.Value);
        for(var round=0;round<2;round++)
        {
            new CSharpGenerator().Generate(catalog,generated,RootNamespace);
            if(hands.Any(hand=>File.ReadAllText(CSharpGenerator.SafePath(output,hand.Key))!=hand.Value))throw new InvalidOperationException("公开路径手写规则被再生成覆盖。");
        }
    }

    /// <summary>逐文件捕获公开标记、正式输出及原模型，不跟随链接；快照本身不授予新库权限。</summary>
    internal static CrossTableExcelPackageProof Capture(string package)
    {
        var proof=GlobalCachedSourceNativePreparation.Capture(package);
        return new(proof.PackageRoot,proof.ManifestSha256,proof.AssemblySha256,proof.MetadataSha256,proof.Files);
    }

    /// <summary>关键身份、完整文件集合、元数据和受管来源同时核对，不能重新捕获收编额外代码。</summary>
    internal static MetadataCatalog Verify(CrossTableExcelPackageProof proof, bool runtime = false, bool overwrite = false)
    {
        if (!Path.IsPathFullyQualified(proof.PackageRoot) || Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.PackageRoot)) != proof.PackageRoot) throw new InvalidOperationException("公开R0路径固定包根不规范。");
        if(File.Exists(CSharpGenerator.SafePath(proof.PackageRoot,CrossTableExcelApplicationPreparer.Marker)))throw new InvalidOperationException("公开验收不能使用内部准备包。");
        const string journal = "backend/Application.Host/.local/schema-history.jsonl";
        if (proof.Files.ContainsKey(journal)) throw new InvalidOperationException("公开R0路径准备包不能含运行状态。");
        var actual = Capture(proof.PackageRoot); var paths = actual.Files.Keys.ToHashSet(StringComparer.Ordinal); if (runtime) paths.Remove(journal);
        if (!paths.SetEquals(proof.Files.Keys) || proof.Files.Any(pair => !actual.Files.TryGetValue(pair.Key, out var hash) || hash != pair.Value)) throw new InvalidOperationException("公开R0路径固定包文件漂移。");
        if (actual.ManifestSha256 != proof.ManifestSha256 || actual.AssemblySha256 != proof.AssemblySha256 || actual.MetadataSha256 != proof.MetadataSha256) throw new InvalidOperationException("公开R0路径固定包摘要漂移。");
        using var document = JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot, ".metarill-package.json")));
        var marker = document.RootElement;
        if (marker.GetProperty("packageMode").GetString() != "FullApplication" || marker.GetProperty("authenticationProfile").GetString() != "web-database-v1" || marker.GetProperty("rootNamespace").GetString() != RootNamespace) throw new InvalidOperationException("公开R0路径验收不能替换为开发或准备宿主。");
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot, "backend/metadata/project.json")), Json)!;
        if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog), JsonSerializer.SerializeToNode(Catalog(overwrite)))) throw new InvalidOperationException("公开R0路径固定模型漂移。");
        if (marker.GetProperty("metadataSha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(CSharpGenerator.CanonicalMetadata(catalog))) || marker.GetProperty("securityCatalogSha256").GetString() != proof.MetadataSha256) throw new InvalidOperationException("公开R0路径模型与安全绑定不符。");
        var hands = Hands();
        var declared = marker.GetProperty("files").EnumerateArray().ToDictionary(file => file.GetProperty("path").GetString()!, file => file.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        var projects = declared.Keys.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal)).Select(path => path[..path.LastIndexOf('/')]).ToArray();
        foreach (var path in proof.Files.Keys)
            if (!declared.ContainsKey(path) && path != ".metarill-package.json" && !projects.Any(project => path.StartsWith(project + "/bin/", StringComparison.Ordinal) || path.StartsWith(project + "/obj/", StringComparison.Ordinal) || path == project + "/packages.lock.json")) throw new InvalidOperationException("公开R0路径固定包混入额外来源。");
        foreach (var pair in declared)
            if (!proof.Files.TryGetValue(pair.Key, out var hash) || !hands.ContainsKey(pair.Key) && hash != pair.Value) throw new InvalidOperationException("公开R0路径受管文件与清单不符。");
        // 原六份手写规则的原始行尾来自历史源文件；只作两侧一致的文本核对，逐文件摘要仍固定真实字节。
        foreach (var pair in hands)
            if (File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot, pair.Key)).Replace("\r\n", "\n", StringComparison.Ordinal) != pair.Value.Replace("\r\n", "\n", StringComparison.Ordinal)) throw new InvalidOperationException("公开R0路径手写规则漂移。");
        return catalog;
    }

    /// <summary>固定包不启动，执行副本只允许新增原宿主结构审计。</summary>
    internal static CrossTableExcelPackageProof CopyForExecution(CrossTableExcelPackageProof proof, string destination, bool overwrite=false)
    {
        Verify(proof,overwrite:overwrite); var root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("公开R0路径执行副本已存在。");
        _ = CSharpGenerator.SafePath(root, ".metarill-execution-boundary"); Directory.CreateDirectory(root);
        foreach (var pair in proof.Files) { var target = CSharpGenerator.SafePath(root, pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(CSharpGenerator.SafePath(proof.PackageRoot, pair.Key), target); }
        var copied = proof with { PackageRoot = root }; Verify(copied,overwrite:overwrite); return copied;
    }

    /// <summary>只接受固定六表的原事务钩子；其它手写文件仍须匹配公开包原始清单。</summary>
    private static IReadOnlyDictionary<string,string> Hands()=>Catalog().ConfigTables
        .Where(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal))
        .ToDictionary(table=>"backend/generated/Services/"+GenerationModel.Pascal(table.TableName)+"Service.cs",table=>GlobalCachedCrossExcelNativePreparation.Handwritten(table.TableName),StringComparer.Ordinal);
}
