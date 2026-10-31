using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
namespace Metarill.Generation.Tests;

/// <summary>四种普通来源引用同一全局缓存链；仅准备完整包，不连接数据库或创建对象。</summary>
internal static class GlobalCachedTargetNativePreparation
{
    internal const string RootNamespace="Metarill.GlobalCachedTargetNative",ProductCommit="9f798ebec4a0fda0ff36ff83f4aa39d8d14354ca";
    /// <summary>沿B105原模型生成，不为正式用例增加项目列、替换组件或简化维护关系。</summary>
    internal static MetadataCatalog Catalog(int mode,string type)=>GlobalCachedTargetGenerationTests.Catalog(mode,type);
    /// <summary>固定全部源码、程序集和手写文件，复用既有不跟随链接的清单捕获。</summary>
    internal static GlobalCachedPackageProof Capture(string package)=>GlobalCachedSourceNativePreparation.Capture(package);
    /// <summary>先证明包集合及整份原模型；运行副本只容许模板固定的结构审计日志。</summary>
    internal static MetadataCatalog Verify(GlobalCachedPackageProof proof,int mode,string type,bool runtime=false)
    {
        if(mode is not(0 or 1)||type is not("string" or "int64"))throw new InvalidOperationException("全局缓存目标模型不在固定范围。");
        if(!Path.IsPathFullyQualified(proof.PackageRoot)||Path.GetFullPath(proof.PackageRoot)!=proof.PackageRoot)throw new InvalidOperationException("固定包根必须规范。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";if(proof.Files.ContainsKey(journal))throw new InvalidOperationException("准备包不能含运行状态。");
        var actual=Capture(proof.PackageRoot);var names=actual.Files.Keys.ToHashSet(StringComparer.Ordinal);if(runtime)names.Remove(journal);
        if(!names.SetEquals(proof.Files.Keys)||proof.Files.Any(file=>!actual.Files.TryGetValue(file.Key,out var digest)||digest!=file.Value))throw new InvalidOperationException("完整包集合或摘要漂移。");
        if(actual.ManifestSha256!=proof.ManifestSha256||actual.AssemblySha256!=proof.AssemblySha256||actual.MetadataSha256!=proof.MetadataSha256)throw new InvalidOperationException("完整包关键摘要漂移。");
        using var manifest=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,".metarill-package.json")));var package=manifest.RootElement;
        if(package.GetProperty("packageMode").GetString()!="FullApplication"||package.GetProperty("authenticationProfile").GetString()!="web-database-v1"||package.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("必须使用原正式Web完整包。");
        foreach(var file in package.GetProperty("files").EnumerateArray())if(!proof.Files.TryGetValue(file.GetProperty("path").GetString()!,out var digest)||digest!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("受管文件与原归属不符。");
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog),JsonSerializer.SerializeToNode(Catalog(mode,type))))throw new InvalidOperationException("缓存链模型与原定义不符。");
        return catalog;
    }
    /// <summary>任何实际安装均在独占新副本运行，原冻结包不可写入审计或上传文件。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof,string destination,int mode,string type)
    {
        Verify(proof,mode,type);var root=Path.GetFullPath(destination);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("执行副本已经存在。");
        _=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var target=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),target);}
        var copied=proof with{PackageRoot=root};Verify(copied,mode,type);return copied;
    }
}
