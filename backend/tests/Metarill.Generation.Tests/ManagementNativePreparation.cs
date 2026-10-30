using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
namespace Metarill.Generation.Tests;

/// <summary>完整管理公开包与编译输出共同冻结；不连接数据库，不创建或批准租约。</summary>
internal static class ManagementNativePreparation
{
    internal const string ProductCommit="04680ac53d59e8d3037a1e1f344416a768f76bd3",RootNamespace="Metarill.ManagementNative";
    internal const string Admin="2200000000000100001",ForeignAdmin="2300000000000000099";
    /// <summary>原普通业务样例增加第二个已发布后台，用同一包验证管理页面后台边界。</summary>
    internal static MetadataCatalog Catalog(string sourceRoot)
    {
        var app=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(sourceRoot,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        app.YzAdminTypes.Add(new(){Id=ForeignAdmin,Name="独占异后台",YzProjectId=app.YzProjectId});return app;
    }
    /// <summary>只调用公开完整管理入口，不换生产Program、身份、Service或工作台。</summary>
    internal static void Create(string sourceRoot,string package)=>new ApplicationPackager().CreateManagement(Catalog(sourceRoot),package,RootNamespace,sourceRoot,Admin);
    /// <summary>编译后冻结全部源码、依赖和bin/obj，未编译包不能取得正式包证明。</summary>
    internal static GlobalCachedPackageProof Capture(string package)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));var files=Files(root).ToDictionary(path=>Path.GetRelativePath(root,path).Replace('\\','/'),Hash,StringComparer.Ordinal);
        return new(root,Hash(CSharpGenerator.SafePath(root,".metarill-package.json")),Hash(CSharpGenerator.SafePath(root,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll")),Hash(CSharpGenerator.SafePath(root,"backend/metadata/project.json")),files);
    }
    /// <summary>完整集合、实际模式、后台、业务来源及当前固定工厂均满足才允许建立连接。</summary>
    internal static SecurityManagementModelSet Verify(GlobalCachedPackageProof proof,string sourceRoot,bool runtime=false)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.PackageRoot));if(!Path.IsPathFullyQualified(proof.PackageRoot)||root!=proof.PackageRoot)throw new InvalidOperationException("管理正式包根不规范。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";if(proof.Files.ContainsKey(journal))throw new InvalidOperationException("管理正式包不能已有结构运行日志。");
        var actual=Capture(root);var paths=actual.Files.Keys.ToHashSet(StringComparer.Ordinal);if(runtime)paths.Remove(journal);
        if(!paths.SetEquals(proof.Files.Keys)||proof.Files.Any(file=>!actual.Files.TryGetValue(file.Key,out var value)||value!=file.Value))throw new InvalidOperationException("管理正式包完整集合或字节漂移。");
        if(actual.ManifestSha256!=proof.ManifestSha256||actual.AssemblySha256!=proof.AssemblySha256||actual.MetadataSha256!=proof.MetadataSha256)throw new InvalidOperationException("管理正式包关键摘要漂移。");
        using var document=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(root,".metarill-package.json")));var header=document.RootElement;
        if(header.GetProperty("packageMode").GetString()!="FullManagementApplication"||header.GetProperty("authenticationProfile").GetString()!="web-database-v1"||header.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("管理正式验收不能替换为普通或准备包。");
        foreach(var file in header.GetProperty("files").EnumerateArray())if(!proof.Files.TryGetValue(file.GetProperty("path").GetString()!,out var digest)||digest!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("管理正式包受管归属不一致。");
        var context=PreparedSecurityManagementSource.Read(root);var source=SecurityManagementModuleFactory.RequireApplicationForPreparation(context.Module);
        if(context.RootNamespace!=RootNamespace||context.Module.Binding.ManagementAdminTypeId!=Admin||!CSharpGenerator.CanonicalMetadata(source).AsSpan().SequenceEqual(CSharpGenerator.CanonicalMetadata(GenerationMetadataPublisher.Publish(Catalog(sourceRoot)).Metadata)))throw new InvalidOperationException("管理正式包不是本轮原业务及两个已发布后台。");
        var identity=new LocalGenerationUpdater().InspectTarget(root);if(identity.RootNamespace!=RootNamespace||identity.ProjectId!=source.YzProjectId||identity.DatabaseProvider!="mysql")throw new InvalidOperationException("管理正式包身份不符。");
        return context.Module;
    }
    /// <summary>执行只用新目录的固定副本，准备包保持可再次核验但不得重复消费实库租约。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof,string sourceRoot,string destination)
    {
        Verify(proof,sourceRoot);var root=Path.GetFullPath(destination);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("管理正式执行目录已存在。");
        if(root==proof.PackageRoot||root.StartsWith(proof.PackageRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||proof.PackageRoot.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("管理正式副本与准备包不能互相包含。");
        _=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var output=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),output,false);}
        var copied=proof with{PackageRoot=root};Verify(copied,sourceRoot);return copied;
    }
    /// <summary>链接、非规范路径和未知文件均不能进入固定包清单。</summary>
    private static IReadOnlyList<string> Files(string root)
    {
        if(!Directory.Exists(root))throw new InvalidOperationException("管理正式包不存在。");_=CSharpGenerator.SafePath(root,".metarill-package.json");var result=new List<string>();var pending=new Stack<string>();pending.Push(root);
        while(pending.TryPop(out var directory))foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        {var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("管理正式包不能包含链接。");if((attributes&FileAttributes.Directory)!=0)pending.Push(entry);else result.Add(entry);}
        return result;
    }
    /// <summary>只返回固定文件的摘要，不回显配置或凭据。</summary>
    private static string Hash(string file)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
}
