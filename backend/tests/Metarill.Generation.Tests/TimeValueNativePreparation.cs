using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
namespace Metarill.Generation.Tests;

/// <summary>原三表样板与显式时间精度的固定完整包；本工具不读取连接或创建数据库对象。</summary>
internal static class TimeValueNativePreparation
{
    internal const string RootNamespace="Metarill.TimeValueNative",ProductCommit="cfe33a8015a1ee9bd1b89c9fa9337bbafbc5e4b8";
    /// <summary>保留原三表50列、6索引及原页面，只增加隔离TIME精度列并开启原导入导出，原页面模板不改。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog=GlobalCachedSourceGenerationTests.Catalog();
        foreach(var column in catalog.ConfigColumns.Where(column=>column.LogicalType=="datetime"))
        {column.ColumnType="datetime(0)";if(column.ColumnName is "insert_time" or "update_time")column.DefaultValue="now()";}
        var table=catalog.ConfigTables.Single(table=>table.TableName=="sample_contract");
        var page=catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id);
        page.IsImport=1;page.IsExport=1;page.IsCopy=1;page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("list");
        var index=3800000000000000100L;
        foreach(var field in new[]{("clock_zero","零位时刻","time(0)","08:09:00","time"),("clock_tenth","一位时刻","time(1)","08:09:00.1","time"),("clock_micro","微秒时刻","time(6)","08:09:00.123456","time"),("duration","带符号时长","time(6)","-30:01:02.123456","")})
        {
            var id=(index++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId=table.Id,ColumnName=field.Item1,Name=field.Item2,LogicalType="time",ColumnType=field.Item3,DefaultValue=field.Item4,ControlKind=field.Item5,YzProjectId=catalog.YzProjectId});
            catalog.ConfigPageColumns.Add(new(){Id=(index++).ToString(System.Globalization.CultureInfo.InvariantCulture),ConfigPageId=page.Id,ConfigColumnId=id,IsEdit=1,IsShow=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)},YzProjectId=catalog.YzProjectId});
        }
        return catalog;
    }
    /// <summary>必须是新目录；两轮正规生成同时检查原手写partial保护。</summary>
    internal static void Create(string sourceRoot,string package)
    {
        if(Directory.Exists(package)||File.Exists(package))throw new InvalidOperationException("固定TIME字段包已存在，拒绝覆盖。");
        new ApplicationPackager().Create(Catalog(),package,RootNamespace,sourceRoot);
        var generated=CSharpGenerator.SafePath(package,"backend/generated");var handwritten=Directory.GetFiles(generated,"*.cs",SearchOption.AllDirectories).Where(file=>!file.EndsWith(".g.cs",StringComparison.Ordinal)).ToDictionary(file=>Path.GetRelativePath(generated,file),File.ReadAllBytes,StringComparer.Ordinal);
        for(var round=0;round<2;round++)
        {new CSharpGenerator().Generate(Catalog(),generated,RootNamespace);if(handwritten.Any(file=>!File.ReadAllBytes(CSharpGenerator.SafePath(generated,file.Key)).SequenceEqual(file.Value)))throw new InvalidOperationException("TIME字段包手写partial被生成覆盖。");}
    }
    /// <summary>复用已有不跟随链接的逐文件冻结工具，覆盖源码、bin、obj和真实程序集。</summary>
    internal static GlobalCachedPackageProof Capture(string package)=>GlobalCachedSourceNativePreparation.Capture(package);
    /// <summary>连接数据库前核对完整集合、每份字节、正式Web身份及整份原元数据。</summary>
    internal static MetadataCatalog Verify(GlobalCachedPackageProof proof,bool runtime=false)
    {
        if(Path.GetFullPath(proof.PackageRoot)!=proof.PackageRoot)throw new InvalidOperationException("TIME字段包根必须是规范绝对路径。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";if(proof.Files.ContainsKey(journal))throw new InvalidOperationException("准备包不能含运行日志。");
        var actual=Capture(proof.PackageRoot);var names=actual.Files.Keys.ToHashSet(StringComparer.Ordinal);if(runtime)names.Remove(journal);
        if(!names.SetEquals(proof.Files.Keys)||proof.Files.Any(file=>!actual.Files.TryGetValue(file.Key,out var digest)||digest!=file.Value))throw new InvalidOperationException("TIME字段完整包集合或摘要漂移。");
        if(actual.ManifestSha256!=proof.ManifestSha256||actual.AssemblySha256!=proof.AssemblySha256||actual.MetadataSha256!=proof.MetadataSha256)throw new InvalidOperationException("TIME字段包关键摘要漂移。");
        using var manifest=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,".metarill-package.json")));var value=manifest.RootElement;
        if(value.GetProperty("packageMode").GetString()!="FullApplication"||value.GetProperty("authenticationProfile").GetString()!="web-database-v1"||value.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("必须使用固定正式Web完整包。");
        foreach(var file in value.GetProperty("files").EnumerateArray())if(!proof.Files.TryGetValue(file.GetProperty("path").GetString()!,out var digest)||digest!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("TIME字段包受管文件与归属不符。");
        var files=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;FileUploadLifetime.ValidateMetadata(files);
        using(var dependencies=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/Application.Host/bin/Release/net10.0/Application.Host.deps.json"))))
            if(!dependencies.RootElement.GetProperty("libraries").TryGetProperty("MySqlConnector/2.6.1",out _))throw new InvalidOperationException("完整包未解析固定MySQL驱动。");
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog),JsonSerializer.SerializeToNode(Catalog())))throw new InvalidOperationException("TIME字段正式模型与原三表样板不符。");return catalog;
    }
    /// <summary>正式执行只启动新副本；结构日志不得污染冻结来源，混合分隔符先规范化。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof,string destination)
    {
        Verify(proof);var root=Path.GetFullPath(destination);if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("TIME字段执行目录已存在。");_=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var target=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),target);}
        var copied=proof with{PackageRoot=root};Verify(copied);return copied;
    }
}
