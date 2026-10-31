using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation.Tests;

/// <summary>原六份手写业务、完整包源码与编译产物共同冻结；不访问数据库或创建租约。</summary>
internal static class SampleActionsNativePreparation
{
    internal const string ProductCommit="e0512975301c2ef3afc51aea6c2f66fc428930a3";
    internal const string RootNamespace="Metarill.GlobalSample";
    private static readonly string[] HandNames=["SampleHostCustomization.cs","SampleContractService.cs","SampleContractDigest.cs","SampleContractButtonHandler.cs","SampleContractAgentController.cs","SampleContractAgentBusiness.cs"];
    /// <summary>复用同一原三表/三按钮目录，不另造测试专用业务模型。</summary>
    internal static MetadataCatalog Catalog()=>GlobalCachedSourceGenerationTests.Catalog();
    /// <summary>只安装明确列出的公开样板；不修改生成器或已有冻结B98包。</summary>
    internal static IReadOnlyDictionary<string,byte[]> Handwritten(string sourceRoot)=>HandNames.ToDictionary(name=>"backend/"+(name=="SampleContractService.cs"?"generated/Services/":"Application.Host/")+name,name=>File.ReadAllBytes(CSharpGenerator.SafePath(sourceRoot,"tools/acceptance/sample-host/"+name)),StringComparer.Ordinal);
    /// <summary>准备新的独占完整包，两轮生成只证明手写内容保护，编译由调用者明确执行。</summary>
    internal static void Create(string sourceRoot,string package)
    {
        if(Directory.Exists(package))throw new InvalidOperationException("正式准备目录已存在，拒绝覆盖。");
        new ApplicationPackager().Create(Catalog(),package,RootNamespace,sourceRoot);
        var hand=Handwritten(sourceRoot);foreach(var file in hand)File.WriteAllBytes(CSharpGenerator.SafePath(package,file.Key),file.Value);
        for(var round=0;round<2;round++)
        {
            new CSharpGenerator().Generate(Catalog(),CSharpGenerator.SafePath(package,"backend/generated"),RootNamespace);
            if(hand.Any(file=>!file.Value.SequenceEqual(File.ReadAllBytes(CSharpGenerator.SafePath(package,file.Key)))))throw new InvalidOperationException("原手写业务被再生成修改。");
        }
    }
    /// <summary>已核唯一根包含源码及bin/obj，不能把初次生成清单冒充当前手写内容摘要。</summary>
    internal static GlobalCachedPackageProof Capture(string package)
    {
        var root=Path.GetFullPath(package);var files=Files(root).ToDictionary(file=>Path.GetRelativePath(root,file).Replace('\\','/'),Hash,StringComparer.Ordinal);
        return new(root,Hash(CSharpGenerator.SafePath(root,".metarill-package.json")),Hash(CSharpGenerator.SafePath(root,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll")),Hash(CSharpGenerator.SafePath(root,"backend/metadata/project.json")),files);
    }
    /// <summary>打开数据库前验证包集合、正式身份、原完整模型及六份公开手写源。</summary>
    internal static MetadataCatalog Verify(GlobalCachedPackageProof proof,string sourceRoot,bool runtime=false)
    {
        var root=Path.GetFullPath(proof.PackageRoot);if(root!=proof.PackageRoot)throw new InvalidOperationException("固定包根必须规范化。");
        if(proof.Files.ContainsKey("backend/Application.Host/.local/schema-history.jsonl"))throw new InvalidOperationException("固定准备包不能已经包含运行审计状态。");
        var actual=Files(root).Select(file=>Path.GetRelativePath(root,file).Replace('\\','/')).ToHashSet(StringComparer.Ordinal);
        if(runtime)actual.ExceptWith(new[]{"backend/Application.Host/.local/schema-history.jsonl"});
        if(!actual.SetEquals(proof.Files.Keys))throw new InvalidOperationException("正式包文件集合变化。");
        foreach(var file in proof.Files)if(Hash(CSharpGenerator.SafePath(root,file.Key))!=file.Value)throw new InvalidOperationException("正式包文件摘要变化。");
        if(Hash(CSharpGenerator.SafePath(root,".metarill-package.json"))!=proof.ManifestSha256||Hash(CSharpGenerator.SafePath(root,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll"))!=proof.AssemblySha256||Hash(CSharpGenerator.SafePath(root,"backend/metadata/project.json"))!=proof.MetadataSha256)throw new InvalidOperationException("正式包关键摘要变化。");
        using var document=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(root,".metarill-package.json")));var manifest=document.RootElement;
        if(manifest.GetProperty("packageMode").GetString()!="FullApplication"||manifest.GetProperty("authenticationProfile").GetString()!="web-database-v1"||manifest.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("正式业务必须使用原命名空间及真实Web完整宿主。");
        var hand=Handwritten(sourceRoot);
        foreach(var file in manifest.GetProperty("files").EnumerateArray())
        {
            var path=file.GetProperty("path").GetString()!;if(!proof.Files.ContainsKey(path))throw new InvalidOperationException("完整包归属缺少固定文件。");
            if(!hand.ContainsKey(path)&&Hash(CSharpGenerator.SafePath(root,path))!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("受管文件与原清单不一致。");
        }
        foreach(var file in hand)if(!file.Value.SequenceEqual(File.ReadAllBytes(CSharpGenerator.SafePath(root,file.Key))))throw new InvalidOperationException("原六份手写业务漂移。");
        var model=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(root,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(Catalog()),JsonSerializer.SerializeToNode(model)))throw new InvalidOperationException("正式样板模型变化。");
        return model;
    }
    /// <summary>精确复制到新独占运行目录；源包不产生结构日志，后续只允许固定审计落点。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof,string sourceRoot,string destination)
    {
        Verify(proof,sourceRoot);var root=Path.GetFullPath(destination);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("运行目录已存在，拒绝覆盖。");_=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var output=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),output,false);}
        var copied=proof with{PackageRoot=root};Verify(copied,sourceRoot);return copied;
    }
    /// <summary>不跟随文件或目录链接；未知运行文件也不能借审计例外进入固定包。</summary>
    private static IReadOnlyList<string> Files(string root)
    {
        if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("正式包缺失或为链接。");var result=new List<string>();var queue=new Stack<string>();queue.Push(root);
        while(queue.TryPop(out var directory))foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        {var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("正式包不能包含链接。");if((attributes&FileAttributes.Directory)!=0)queue.Push(entry);else result.Add(entry);}
        return result;
    }
    /// <summary>只输出散列，不输出源码或私有文件正文。</summary>
    internal static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
