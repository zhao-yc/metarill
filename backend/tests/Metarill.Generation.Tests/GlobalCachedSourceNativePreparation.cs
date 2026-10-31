using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式包的固定清单包含手写覆盖和编译输出；不把初始生成清单当成修改后手写文件的摘要。</summary>
internal sealed record GlobalCachedPackageProof(string PackageRoot,string ManifestSha256,string AssemblySha256,string MetadataSha256,IReadOnlyDictionary<string,string> Files);
/// <summary>原样板与额外子表文件样本共用准备规则；无数据库、账号或容器操作。</summary>
internal static class GlobalCachedSourceNativePreparation
{
    internal const string RootNamespace="Metarill.GlobalSample";
    internal const string ProductCommit="95bdfdc1d6c17607ad07fa9157488feab48b0324";
    /// <summary>原模型保持不动；文件父验收只在独立变体新增一列，不回写原夹具。</summary>
    internal static MetadataCatalog Catalog(bool parentFiles)
    {
        var c=GlobalCachedSourceGenerationTests.Catalog();if(!parentFiles)return c;
        c.ConfigColumns.Add(new(){Id="82012",ConfigTableId="810000000000000013",YzProjectId=c.YzProjectId,ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"accept\":\"*\",\"maxCount\":5}"});
        c.ConfigPageColumns.Add(new(){Id="83112",ConfigPageId="81003",ConfigColumnId="82012",YzProjectId=c.YzProjectId,IsShow=1,IsEdit=1});return c;
    }
    /// <summary>手写业务沿原错误文本与保留标记，仅替换命名空间。</summary>
    internal static string Handwritten()=>GlobalCachedSourceGenerationTests.HandwrittenRule().Replace("Example.GlobalSource",RootNamespace,StringComparison.Ordinal)+"\n";
    /// <summary>完整包准备后冻结每个文件，包括手写与bin；运行期间的上传文件必须放到包外。</summary>
    internal static GlobalCachedPackageProof Capture(string package)
    {
        var root=Path.GetFullPath(package);var files=Files(root).ToDictionary(path=>Path.GetRelativePath(root,path).Replace('\\','/'),Hash,StringComparer.Ordinal);
        return new(root,Hash(CSharpGenerator.SafePath(root,".metarill-package.json")),Hash(CSharpGenerator.SafePath(root,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll")),Hash(CSharpGenerator.SafePath(root,"backend/metadata/project.json")),files);
    }
    /// <summary>所有路径、原完整包归属、宿主、原模型和手写覆盖都先核验，再允许测试打开数据库。</summary>
    internal static MetadataCatalog Verify(GlobalCachedPackageProof proof,bool parentFiles)=>VerifyCore(proof,parentFiles,false);
    /// <summary>执行副本只可新增模板固定的结构审计日志；源码、程序集和手写字节仍逐项固定。</summary>
    internal static MetadataCatalog VerifyRuntime(GlobalCachedPackageProof proof,bool parentFiles)=>VerifyCore(proof,parentFiles,true);
    /// <summary>不启动固定原包；显式安装会写结构审计，所有执行状态放到本轮独占副本。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof,string output,bool parentFiles)
    {
        Verify(proof,parentFiles);
        // Path.Combine不会规范内嵌的另一种分隔符；先固定绝对根，再交给原越界/链接守卫。
        var root=Path.GetFullPath(output);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("执行副本必须是新目录。");
        _=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var item in proof.Files){var target=CSharpGenerator.SafePath(root,item.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,item.Key),target);}
        var copied=Capture(root);Verify(copied,parentFiles);return copied;
    }
    /// <summary>运行白名单不来自配置或请求，且初始固定包不能已经含有运行日志。</summary>
    private static MetadataCatalog VerifyCore(GlobalCachedPackageProof proof,bool parentFiles,bool runtime)
    {
        var root=Path.GetFullPath(proof.PackageRoot);if(root!=proof.PackageRoot)throw new InvalidOperationException("固定包路径必须规范。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";if(proof.Files.ContainsKey(journal))throw new InvalidOperationException("固定包已有运行审计状态。");
        var actual=Files(root).Select(path=>Path.GetRelativePath(root,path).Replace('\\','/')).ToHashSet(StringComparer.Ordinal);if(runtime)actual.Remove(journal);if(!actual.SetEquals(proof.Files.Keys))throw new InvalidOperationException("固定包文件集合改变。");
        foreach(var file in proof.Files)if(Hash(CSharpGenerator.SafePath(root,file.Key))!=file.Value)throw new InvalidOperationException("固定包文件摘要改变。");
        var manifest=CSharpGenerator.SafePath(root,".metarill-package.json");if(Hash(manifest)!=proof.ManifestSha256||Hash(CSharpGenerator.SafePath(root,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll"))!=proof.AssemblySha256||Hash(CSharpGenerator.SafePath(root,"backend/metadata/project.json"))!=proof.MetadataSha256)throw new InvalidOperationException("正式宿主或模型摘要改变。");
        using var json=JsonDocument.Parse(File.ReadAllText(manifest));var package=json.RootElement;if(package.GetProperty("packageMode").GetString()!="FullApplication"||package.GetProperty("authenticationProfile").GetString()!="web-database-v1"||package.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("验收必须使用固定正式Web完整包。");
        const string hand="backend/generated/Services/SampleContractService.cs";foreach(var file in package.GetProperty("files").EnumerateArray()){var path=file.GetProperty("path").GetString()!;if(!proof.Files.ContainsKey(path))throw new InvalidOperationException("完整包清单缺失。");if(path!=hand&&Hash(CSharpGenerator.SafePath(root,path))!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("完整包受管文件漂移。");}
        // 手写业务比较仅统一跨平台换行；上面的逐文件摘要仍固定实际字节。
        if(File.ReadAllText(CSharpGenerator.SafePath(root,hand)).Replace("\r\n","\n",StringComparison.Ordinal)!=Handwritten().Replace("\r\n","\n",StringComparison.Ordinal))throw new InvalidOperationException("原手写金额规则漂移。");
        var metadata=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(root,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        // 比较完整语义，不能删控件、按钮、父关系或添加假项目列让测试通过。
        if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(metadata)),System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Catalog(parentFiles)))))throw new InvalidOperationException("正式样板与固定模型不符。");return metadata;
    }
    /// <summary>不跟随重解析目录或文件，避免固定清单越出自身完整包。</summary>
    private static IReadOnlyList<string> Files(string root)
    {
        if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("固定包不存在或为链接。");var result=new List<string>();var directories=new Stack<string>();directories.Push(root);
        while(directories.TryPop(out var directory))foreach(var item in Directory.EnumerateFileSystemEntries(directory)){var attrs=File.GetAttributes(item);if((attrs&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("固定包不能含链接。");if((attrs&FileAttributes.Directory)!=0)directories.Push(item);else result.Add(item);}return result;
    }
    /// <summary>摘要不输出文件正文或配置。</summary>
    internal static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
