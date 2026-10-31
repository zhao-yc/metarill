using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
namespace Metarill.Generation.Tests;

/// <summary>冻结实际生产宿主准备包的全部来源、编译输出和手写规则；此记录不是正式Full发布许可。</summary>
internal sealed record CrossTableExcelPackageProof(string PackageRoot,string ManifestSha256,string AssemblySha256,string MetadataSha256,IReadOnlyDictionary<string,string> Files);
internal static partial class CrossTableExcelNativePreparation
{
    /// <summary>手写规则只用于独占样本：第二行失败前必须在同一真实事务读到首行及文件引用。</summary>
    internal static string Handwritten(string tableName)
    {
        var name=GenerationModel.Pascal(tableName);
        return $$"""
using FreeSql;
using Metarill.Core.Security;
namespace {{RootNamespace}}.Generated.Services;
public partial class {{name}}Service
{
    /// <summary>当前事务已完成首行DML及文件引用才触发故障；不替换生成CRUD或基础设施观察者。</summary>
    partial void BeforeSave({{RootNamespace}}.Generated.Entities.{{name}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(entity.Name!="后置拒绝")return;
        var prior=mapper.Query().Where(row=>row.Name=="回滚首行").ToList();
        if(prior.Count!=1)throw new InvalidOperationException("未观察到批次首行的真实事务内DML。");
        var transaction=session.CurrentTransaction??throw new InvalidOperationException("手写故障未加入真实事务。");
        using var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;
        command.CommandText="SELECT COUNT(*) FROM yz_file_ref WHERE record_id=@id AND delete_no IS NULL";
        var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=prior[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture);command.Parameters.Add(parameter);
        var references=Convert.ToInt64(command.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture);
        if(references!=1)throw new InvalidOperationException("未观察到批次首行的真实文件引用。");
        var path=Environment.GetEnvironmentVariable("METARILL_CROSS_EXCEL_HOOK_RECEIPT")??throw new InvalidOperationException("缺少本轮手写规则观察路径。");
        File.AppendAllText(path,System.Text.Json.JsonSerializer.Serialize(new{table={{JsonSerializer.Serialize(tableName)}},recordId=prior[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture),references,insideTransaction=true})+"\n");
        throw new InvalidOperationException("本轮合成后置拒绝。");
    }
}
"""+"\n";
    }
    /// <summary>不跟随链接，摘要覆盖所有源码及bin/obj；不存在编译输出时拒绝冻结。</summary>
    internal static CrossTableExcelPackageProof Capture(string package)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));
        var files=Enumerate(root).ToDictionary(path=>Path.GetRelativePath(root,path).Replace('\\','/'),Hash,StringComparer.Ordinal);
        return new(root,Hash(CSharpGenerator.SafePath(root,CrossTableExcelApplicationPreparer.Marker)),Hash(CSharpGenerator.SafePath(root,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll")),Hash(CSharpGenerator.SafePath(root,"backend/metadata/project.json")),files);
    }
    /// <summary>逐字冻结不代替语义证明：准备类型、实际模型、手写钩子和生产来源同时满足才允许连接实库。</summary>
    internal static MetadataCatalog Verify(CrossTableExcelPackageProof proof,bool runtime=false)
    {
        if(Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.PackageRoot))!=proof.PackageRoot)throw new InvalidOperationException("Excel固定包根必须规范。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";
        if(proof.Files.ContainsKey(journal)||File.Exists(CSharpGenerator.SafePath(proof.PackageRoot,".metarill-package.json")))throw new InvalidOperationException("Excel准备包不能含运行审计或冒充普通Full。");
        var actual=Capture(proof.PackageRoot);var paths=actual.Files.Keys.ToHashSet(StringComparer.Ordinal);if(runtime)paths.Remove(journal);
        if(!paths.SetEquals(proof.Files.Keys)||proof.Files.Any(file=>!actual.Files.TryGetValue(file.Key,out var digest)||digest!=file.Value))throw new InvalidOperationException("Excel固定包文件集合或字节漂移。");
        if(actual.ManifestSha256!=proof.ManifestSha256||actual.AssemblySha256!=proof.AssemblySha256||actual.MetadataSha256!=proof.MetadataSha256)throw new InvalidOperationException("Excel准备标记、宿主或元数据摘要漂移。");
        using var marker=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,CrossTableExcelApplicationPreparer.Marker)));var value=marker.RootElement;
        if(value.GetProperty("schemaVersion").GetInt32()!=1||value.GetProperty("profile").GetString()!="cross-table-excel-preparation-v1"||value.GetProperty("readyForPublication").GetBoolean()||value.GetProperty("authenticationProfile").GetString()!="web-database-v1"||value.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("必须使用不可发布的原生产Web准备包。");
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/metadata/project.json")),Json)!;
        if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog),JsonSerializer.SerializeToNode(Catalog())))throw new InvalidOperationException("Excel八组合实际模型漂移。");
        if(value.GetProperty("metadataSha256").GetString()!=Convert.ToHexStringLower(SHA256.HashData(CSharpGenerator.CanonicalMetadata(catalog)))||value.GetProperty("securityCatalogSha256").GetString()!=proof.MetadataSha256)throw new InvalidOperationException("Excel实际元数据安全绑定不符。");
        var hands=catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_source",StringComparison.Ordinal)).ToDictionary(table=>"backend/generated/Services/"+GenerationModel.Pascal(table.TableName)+"Service.cs",table=>Handwritten(table.TableName),StringComparer.Ordinal);
        var declared=value.GetProperty("files").EnumerateArray().Select(file=>file.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
        var buildRoots=declared.Where(path=>path.EndsWith(".csproj",StringComparison.Ordinal)).Select(path=>path[..path.LastIndexOf('/')]).ToArray();
        // 还原会在每个明确项目根生成精确锁文件；不放行其它根文件或额外源码。
        foreach(var path in proof.Files.Keys)
            if(!declared.Contains(path)&&path!=CrossTableExcelApplicationPreparer.Marker&&!buildRoots.Any(root=>path.StartsWith(root+"/bin/",StringComparison.Ordinal)||path.StartsWith(root+"/obj/",StringComparison.Ordinal)||path==root+"/packages.lock.json"))throw new InvalidOperationException("Excel准备包混入未声明的来源文件："+path);
        foreach(var file in value.GetProperty("files").EnumerateArray())
        {
            var path=file.GetProperty("path").GetString()!;
            if(!proof.Files.TryGetValue(path,out var hash)||!hands.ContainsKey(path)&&hash!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("Excel受管来源与准备标记不符。");
        }
        foreach(var file in hands)if(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,file.Key)).Replace("\r\n","\n",StringComparison.Ordinal)!=file.Value)throw new InvalidOperationException("Excel手写事务观察规则漂移。");
        return catalog;
    }
    /// <summary>执行放到新规范副本，保留全部固定文件；仅结构日志允许运行时新增。</summary>
    internal static CrossTableExcelPackageProof CopyForExecution(CrossTableExcelPackageProof proof,string destination)
    {
        Verify(proof);var root=Path.GetFullPath(destination);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("Excel执行副本必须是新目录。");
        _=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var target=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),target);}
        var copied=proof with{PackageRoot=root};Verify(copied);return copied;
    }
    /// <summary>逐层拒绝重解析文件和目录，不把链接目标带入固定清单。</summary>
    private static IReadOnlyList<string> Enumerate(string root)
    {
        if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Excel准备根不存在或为链接。");
        var directories=new Stack<string>();directories.Push(root);var files=new List<string>();
        while(directories.TryPop(out var directory))foreach(var path in Directory.EnumerateFileSystemEntries(directory))
        {var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Excel准备包不能含链接。");if((attributes&FileAttributes.Directory)!=0)directories.Push(path);else files.Add(path);}
        return files;
    }
    /// <summary>仅计算内容摘要，不输出正文或连接。</summary>
    private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
