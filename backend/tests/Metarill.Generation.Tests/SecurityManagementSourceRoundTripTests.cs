using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>管理更新保留脱敏原业务来源，不能从包含固定基础表的发布目录反推原输入。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SecurityManagementSourceRoundTripTests
{
    private const string ApplicationPath="backend/metadata/application-source.json";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    /// <summary>真实准备结果应持久保存原业务结构及摘要，凭据既不写快照也不改原输入。</summary>
    [Fact]
    public async Task Prepared_application_retains_only_redacted_business_source_for_regeneration()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-source");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var app=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),Json)!;
            const string secret="禁止写入原业务快照-915372";app.ConfigTables[0].Extra["apiKey"]=JsonSerializer.SerializeToElement(secret);
            app.ConfigTables[0].Extra["caseSensitiveMap"]=JsonSerializer.SerializeToElement(new Dictionary<string,int>{{"A",1},{"a",2}});
            var expected=GenerationMetadataPublisher.Publish(app).Metadata;var module=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id);
            var output=Path.Combine(root,"prepared");var result=new SecurityManagementApplicationPreparer().Prepare(module,output,"Example.RoundTrip",source);
            var filename=Path.Combine(output,ApplicationPath);Assert.True(File.Exists(filename));Assert.Contains(ApplicationPath,result.Files);
            var bytes=File.ReadAllBytes(filename);var restored=JsonSerializer.Deserialize<MetadataCatalog>(bytes,Json)!;
            Assert.Equal(CSharpGenerator.CanonicalMetadata(expected),CSharpGenerator.CanonicalMetadata(restored));Assert.DoesNotContain(secret,File.ReadAllText(filename));
            Assert.Equal(secret,app.ConfigTables[0].Extra["apiKey"].GetString());Assert.Equal(app.ConfigTables.Count,restored.ConfigTables.Count);
            using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,".metarill-management-preparation.json")));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)),marker.RootElement.GetProperty("applicationMetadataSha256").GetString());
            Assert.False(marker.RootElement.GetProperty("readyForPublication").GetBoolean());Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));
            var recovered=PreparedSecurityManagementSource.Read(output);Assert.NotSame(module,recovered.Module);
            Assert.Equal(CSharpGenerator.CanonicalMetadata(expected),CSharpGenerator.CanonicalMetadata(SecurityManagementModuleFactory.RequireApplicationForPreparation(recovered.Module)));
            // 独立进程的签发表初始为空；只给磁盘包路径和新输出目录，实际再生成全部管理产物。
            var probe=Path.Combine(root,"probe");Directory.CreateDirectory(probe);
            var project=new XDocument(new XElement("Project",new XAttribute("Sdk","Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",new XElement("OutputType","Exe"),new XElement("TargetFramework","net10.0"),new XElement("ImplicitUsings","enable"),new XElement("Nullable","enable"),new XElement("AssemblyName","Metarill.Generation.Tests")),
                new XElement("ItemGroup",new XElement("ProjectReference",new XAttribute("Include",Path.Combine(source,"backend/src/Metarill.Generation/Metarill.Generation.csproj"))))));
            await File.WriteAllTextAsync(Path.Combine(probe,"Probe.csproj"),project.ToString());await File.WriteAllTextAsync(Path.Combine(probe,"Program.cs"),Probe);
            var evidence=Path.Combine(source,".local/management-source-process",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",probe);build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            foreach(var argument in new[]{"build","Probe.csproj","-c","Release","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})build.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(build);
            var roundtrip=Path.Combine(root,"roundtrip-generated");var receipt=Path.Combine(evidence,"receipt.json");
            var run=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",probe);run.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            foreach(var argument in new[]{Path.Combine(probe,"bin/Release/net10.0/Metarill.Generation.Tests.dll"),output,roundtrip,receipt})run.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(run);
            using(var actual=JsonDocument.Parse(await File.ReadAllTextAsync(receipt)))Assert.Equal(recovered.ApplicationMetadataSha256,actual.RootElement.GetProperty("applicationMetadataSha256").GetString());
            var original=Path.Combine(output,"backend/generated");Assert.Equal(RelativeFiles(original),RelativeFiles(roundtrip));
            foreach(var relative in RelativeFiles(original))Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(original,relative)),await File.ReadAllBytesAsync(Path.Combine(roundtrip,relative)));
            // 来源读取不把手写文件当不可变快照；既有partial仍由原生成器只首次创建规则保护。
            var hand=Path.Combine(original,"Services/YzRoleService.cs");await File.AppendAllTextAsync(hand,"\n// 保留本项目的手写扩展。\n");var handwritten=await File.ReadAllBytesAsync(hand);
            recovered=PreparedSecurityManagementSource.Read(output);new CSharpGenerator().Generate(recovered.Module,original,recovered.RootNamespace);Assert.Equal(handwritten,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>文件摘要可由操作者重算，重建仍必须受当前固定模块及原业务来源约束。</summary>
    [Theory]
    [InlineData("source-rehashed")][InlineData("reserved-source-rehashed")][InlineData("project-rehashed")]
    [InlineData("source-secret-rehashed")][InlineData("module")][InlineData("namespace")][InlineData("generation-rehashed")]
    [InlineData("released")][InlineData("missing-source")][InlineData("marker-duplicate")][InlineData("source-duplicate-rehashed")]
    [InlineData("unowned-source")][InlineData("duplicate-ownership")][InlineData("foundation-rehashed")][InlineData("source-path")]
    public async Task Rehashed_or_ambiguous_disk_declarations_do_not_replace_the_fixed_source_contract(string change)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-source");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var app=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),Json)!;
            var module=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id);var output=Path.Combine(root,"prepared");new SecurityManagementApplicationPreparer().Prepare(module,output,"Example.RoundTrip",source);
            var filename=Path.Combine(output,ApplicationPath);var markerPath=Path.Combine(output,".metarill-management-preparation.json");var header=JsonNode.Parse(await File.ReadAllTextAsync(markerPath))!;
            var application=JsonNode.Parse(await File.ReadAllTextAsync(filename))!;var projectPath=Path.Combine(output,"backend/metadata/project.json");
            if(change=="source-rehashed"||change=="reserved-source-rehashed"||change=="source-secret-rehashed")
            {
                application["configTables"]![0]![change=="source-rehashed"?"name":change=="reserved-source-rehashed"?"tableName":"apiKey"]=change=="reserved-source-rehashed"?"yz_manager":"不能成为恢复来源";
                await File.WriteAllTextAsync(filename,application.ToJsonString(Json));
            }
            else if(change=="project-rehashed")
            {
                var project=JsonNode.Parse(await File.ReadAllTextAsync(projectPath))!;project["configTables"]![0]!["name"]="与原业务不符";await File.WriteAllTextAsync(projectPath,project.ToJsonString(Json));
            }
            else if(change=="module")header["moduleSha256"]=new string('0',64);
            else if(change=="namespace")header["rootNamespace"]="Example.Other";
            else if(change=="generation-rehashed")
            {
                var file=Path.Combine(output,"backend/generated/.metarill-generation.json");var ownership=JsonNode.Parse(await File.ReadAllTextAsync(file))!;ownership["rootNamespace"]="Example.Other";await File.WriteAllTextAsync(file,ownership.ToJsonString(Json));
            }
            else if(change=="released")header["readyForPublication"]=true;
            else if(change=="missing-source")File.Delete(filename);
            else if(change=="source-duplicate-rehashed")await File.WriteAllTextAsync(filename,"{\"SchemaVersion\":1,"+(await File.ReadAllTextAsync(filename))[1..]);
            else if(change=="unowned-source")
            {var entry=header["files"]!.AsArray().Single(value=>value!["path"]!.GetValue<string>()==ApplicationPath);header["files"]!.AsArray().Remove(entry);}
            else if(change=="duplicate-ownership")header["files"]!.AsArray().Add(header["files"]!.AsArray().Single(value=>value!["path"]!.GetValue<string>()==ApplicationPath)!.DeepClone());
            else if(change=="foundation-rehashed")
            {
                var file=Path.Combine(output,"backend/metadata/files.json");var foundation=JsonNode.Parse(await File.ReadAllTextAsync(file))!;foundation["configColumns"]![0]!["logicalType"]="string";await File.WriteAllTextAsync(file,foundation.ToJsonString(Json));
            }
            else if(change=="source-path")header["files"]!.AsArray().Single(value=>value!["path"]!.GetValue<string>()==ApplicationPath)!["path"]="../outside.json";
            RehashKnownMetadata(output,header);
            var text=header.ToJsonString(Json);if(change=="marker-duplicate")text="{\"Profile\":\"management-preparation-v1\","+text[1..];
            await File.WriteAllTextAsync(markerPath,text);
            Assert.Throws<InvalidOperationException>(()=>PreparedSecurityManagementSource.Read(output));Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>测试只重算固定包内元数据，不读取输入manifest中的任意路径。</summary>
    private static void RehashKnownMetadata(string root,JsonNode header)
    {
        if(File.Exists(Path.Combine(root,ApplicationPath)))header["applicationMetadataSha256"]=Hash(File.ReadAllBytes(Path.Combine(root,ApplicationPath)));
        var project=File.ReadAllBytes(Path.Combine(root,"backend/metadata/project.json"));header["securityCatalogSha256"]=Hash(project);
        header["metadataSha256"]=Hash(CSharpGenerator.CanonicalMetadata(JsonSerializer.Deserialize<MetadataCatalog>(project,Json)!));
        var generationPath=Path.Combine(root,"backend/generated/.metarill-generation.json");var generation=JsonNode.Parse(File.ReadAllText(generationPath))!;generation["metadataSha256"]=header["metadataSha256"]!.GetValue<string>();File.WriteAllText(generationPath,generation.ToJsonString(Json));
        foreach(var relative in new[]{ApplicationPath,"backend/metadata/project.json","backend/generated/.metarill-generation.json","backend/metadata/files.json","backend/metadata/security.json"})
            if(File.Exists(Path.Combine(root,relative)))foreach(var entry in header["files"]!.AsArray().Where(value=>value!["path"]!.GetValue<string>()==relative))entry!["sha256"]=Hash(File.ReadAllBytes(Path.Combine(root,relative)));
    }
    /// <summary>只比较真实生成产物的全部相对路径，不能漏掉控制器、页面或声明。</summary>
    private static string[] RelativeFiles(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(file=>Path.GetRelativePath(root,file)).Order(StringComparer.Ordinal).ToArray();
    /// <summary>摘要仅用于测试归属，不回显原业务数据。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private const string Probe="""
using System.Text.Json;
using Metarill.Generation;
// 新进程的工厂签发表为空，只从固定包中重建并实际生成。
var context=PreparedSecurityManagementSource.Read(args[0]);
var result=new CSharpGenerator().Generate(context.Module,args[1],context.RootNamespace);
File.WriteAllText(args[2],JsonSerializer.Serialize(new{applicationMetadataSha256=context.ApplicationMetadataSha256,generatedFiles=result.GeneratedFiles.Count}));
""";
}
