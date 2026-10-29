using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
namespace Metarill.Generation.Tests;

/// <summary>C25实际共享跨表与六类父页面的固定完整包；本工具不读取连接或创建数据库对象。</summary>
internal static class RuleRecordNativePreparation
{
    internal const string RootNamespace="Metarill.RuleRecordNative",ProductCommit="b34f8c667eedbb797e99fa2a0be28857fedaa15f",Prefix="rule_native";
    /// <summary>沿用真实共享R0及派生目标元数据，仅明确启用只读规则和已支持的标准写操作。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog=SharedCrossParentGenerationTests.Catalog(Prefix,"string","int64",false,true);
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))
        {page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("countryId > 0 && name == '禁止'");page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);page.IsDelete=1;page.IsCopy=1;}
        return catalog;
    }
    /// <summary>必须是新目录；两轮正规生成同时检查原手写partial保护。</summary>
    internal static void Create(string sourceRoot,string package)
    {
        if(Directory.Exists(package)||File.Exists(package))throw new InvalidOperationException("固定规则包已存在，拒绝覆盖。");
        new ApplicationPackager().Create(Catalog(),package,RootNamespace,sourceRoot);
        var generated=CSharpGenerator.SafePath(package,"backend/generated");var handwritten=Directory.GetFiles(generated,"*.cs",SearchOption.AllDirectories).Where(file=>!file.EndsWith(".g.cs",StringComparison.Ordinal)).ToDictionary(file=>Path.GetRelativePath(generated,file),File.ReadAllBytes,StringComparer.Ordinal);
        for(var round=0;round<2;round++)
        {new CSharpGenerator().Generate(Catalog(),generated,RootNamespace);if(handwritten.Any(file=>!File.ReadAllBytes(CSharpGenerator.SafePath(generated,file.Key)).SequenceEqual(file.Value)))throw new InvalidOperationException("规则包手写partial被生成覆盖。");}
    }
    /// <summary>复用已有不跟随链接的逐文件冻结工具，覆盖源码、bin、obj和真实程序集。</summary>
    internal static GlobalCachedPackageProof Capture(string package)=>GlobalCachedSourceNativePreparation.Capture(package);
    /// <summary>连接数据库前核对完整集合、每份字节、正式Web身份及整份原元数据。</summary>
    internal static MetadataCatalog Verify(GlobalCachedPackageProof proof,bool runtime=false)
    {
        if(Path.GetFullPath(proof.PackageRoot)!=proof.PackageRoot)throw new InvalidOperationException("规则包根必须是规范绝对路径。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";if(proof.Files.ContainsKey(journal))throw new InvalidOperationException("准备包不能含运行日志。");
        var actual=Capture(proof.PackageRoot);var names=actual.Files.Keys.ToHashSet(StringComparer.Ordinal);if(runtime)names.Remove(journal);
        if(!names.SetEquals(proof.Files.Keys)||proof.Files.Any(file=>!actual.Files.TryGetValue(file.Key,out var digest)||digest!=file.Value))throw new InvalidOperationException("规则完整包集合或摘要漂移。");
        if(actual.ManifestSha256!=proof.ManifestSha256||actual.AssemblySha256!=proof.AssemblySha256||actual.MetadataSha256!=proof.MetadataSha256)throw new InvalidOperationException("规则包关键摘要漂移。");
        using var manifest=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,".metarill-package.json")));var value=manifest.RootElement;
        if(value.GetProperty("packageMode").GetString()!="FullApplication"||value.GetProperty("authenticationProfile").GetString()!="web-database-v1"||value.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("必须使用固定正式Web完整包。");
        foreach(var file in value.GetProperty("files").EnumerateArray())if(!proof.Files.TryGetValue(file.GetProperty("path").GetString()!,out var digest)||digest!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("规则包受管文件与归属不符。");
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog),JsonSerializer.SerializeToNode(Catalog())))throw new InvalidOperationException("规则正式模型与原六类页面不符。");return catalog;
    }
    /// <summary>正式执行只启动新副本；结构日志不得污染冻结来源，混合分隔符先规范化。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof,string destination)
    {
        Verify(proof);var root=Path.GetFullPath(destination);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("规则执行目录已存在。");_=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var target=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),target);}
        var copied=proof with{PackageRoot=root};Verify(copied);return copied;
    }
}
