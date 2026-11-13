using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Security;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>先独立严格构建并冻结全部八模型，缺批准租约不能打开数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalArrayTargetNativePreparationTests
{
    /// <summary>使用正式完整包并验证篡改、错模型、额外文件与唯一运行日志边界；可导出供之后固定验收。</summary>
    [Theory][InlineData(0,0,"int64")][InlineData(0,0,"string")][InlineData(0,1,"int64")][InlineData(0,1,"string")]
    [InlineData(1,0,"int64")][InlineData(1,0,"string")][InlineData(1,1,"int64")][InlineData(1,1,"string")]
    public async Task Eight_original_array_models_are_frozen_before_database_lease(int targetMode,int sourceMode,string inner)
    {
        var model=new GlobalArrayTargetNativeModel(targetMode,sourceMode,inner);var suffix=model.Suffix;
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-array-target-native");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"prepared");
        try
        {
            new ApplicationPackager().Create(model.Catalog(),package,GlobalArrayTargetNativePreparation.RootNamespace,source);
            var logs=CSharpGenerator.SafePath(source,".local/global-array-native-preparation-logs/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(logs);
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(arg);start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;await NonRedundantApplicationFixture.RunAsync(start);
            var node=NonRedundantApplicationFixture.Start("node",package);node.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalMultipleCachedTargetVueProbe.mjs"));node.ArgumentList.Add(package);node.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;await NonRedundantApplicationFixture.RunAsync(node);
            var proof=GlobalArrayTargetNativePreparation.Capture(package);var catalog=GlobalArrayTargetNativePreparation.Verify(proof,model);Assert.Equal(3,catalog.ConfigTables.Count);Assert.Equal(targetMode,catalog.ConfigTables.Single(t=>t.Id=="1").IsRongyu);Assert.Equal(targetMode==1,MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(t=>t.Id=="1")).IsPhysical(catalog.ConfigColumns.Single(c=>c.Id=="14")));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,GlobalArrayTargetNativePreparation.RootNamespace)));
            // 正式用例的所有固定路由必须来自实际生成Controller，不能靠猜测在实库中试错。
            var controllers=string.Join("\n",Directory.GetFiles(Path.Combine(package,"backend/generated/Controllers"),"*.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
            foreach(var route in new[]{"selection/selected","findNameIdSelectedGlobalSource38","importDatas30","initExport"})Assert.Contains(route,controllers);
            foreach(var relative in new[]{"src/Metarill.Core/Metadata/GlobalDerivedRelationSource.cs","backend/generated/Services/CachedConsumerService.g.cs","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {var path=CSharpGenerator.SafePath(package,relative);var bytes=await File.ReadAllBytesAsync(path);await File.AppendAllTextAsync(path,"漂移");Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.Verify(proof,model));await File.WriteAllBytesAsync(path,bytes);}
            Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.Verify(proof,model with{TargetMode=1-targetMode}));Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.Verify(proof,model with{SourceMode=1-sourceMode}));Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.Verify(proof,model with{InnerType=inner=="int64"?"string":"int64"}));
            var copied=GlobalArrayTargetNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"),model);var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.Verify(copied,model));GlobalArrayTargetNativePreparation.Verify(copied,model,true);
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/unknown.json");await File.WriteAllTextAsync(unknown,"{}");Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.Verify(copied,model,true));File.Delete(unknown);Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativePreparation.CopyForExecution(proof,copied.PackageRoot,model));GlobalArrayTargetNativePreparation.Verify(proof,model);
            var output=Environment.GetEnvironmentVariable("METARILL_GLOBAL_ARRAY_PREPARE_OUTPUT");if(!string.IsNullOrWhiteSpace(output))
            {Assert.Equal(CSharpGenerator.SafePath(source,".local/global-array-target-native/prepared"),Path.GetFullPath(output));var result=GlobalArrayTargetNativePreparation.CopyForExecution(proof,Path.Combine(output,suffix),model);await File.WriteAllTextAsync(Path.Combine(output,suffix+".json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>专属批准、单次状态及完整八模型路径缺一即拒绝；合成配置不代表真实批准。</summary>
    [Theory][InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("missing")][InlineData("extra")][InlineData("type")][InlineData("duplicate")]
    public void Lease_rejects_unapproved_or_foreign_input_before_any_side_effect(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-cached-target-lease",Guid.NewGuid().ToString("N"));var models=new JsonObject();foreach(var suffix in GlobalArrayTargetNativeModel.All.Select(model=>model.Suffix))models[suffix]=new JsonObject{{"PackageRoot",Path.Combine(root,suffix)}};
        var settings=new JsonObject{{"Purpose",GlobalArrayTargetNativeLease.Purpose},{"AuthorizationStatus","approved"},{"LeaseStatus","running"},{"ProductCommit",GlobalArrayTargetNativePreparation.ProductCommit},{"RunId","0123456789abcdef"},{"ArrayTargetPreparation",models}};
        Assert.Equal("0123456789abcdef",GlobalArrayTargetNativeLease.Validate(JsonSerializer.SerializeToElement(settings),root));
        if(invalid=="purpose")settings["Purpose"]="global-cached-source";if(invalid=="authorization")settings["AuthorizationStatus"]="pending";if(invalid=="ended")settings["LeaseStatus"]="ended";if(invalid=="product")settings["ProductCommit"]="old";if(invalid=="run")settings["RunId"]="../old";if(invalid=="path")models["array_t0_s0_long"]!["PackageRoot"]=Path.Combine(root,"other");if(invalid=="relative")models["array_t0_s0_long"]!["PackageRoot"]="prepared";if(invalid=="missing")models.Remove("array_t0_s0_long");if(invalid=="extra")models["foreign"]=new JsonObject();if(invalid=="type")settings["ArrayTargetPreparation"]="not-object";
        var input=JsonSerializer.SerializeToElement(settings);
        if(invalid=="duplicate")input=JsonDocument.Parse(settings.ToJsonString().Replace("\"ArrayTargetPreparation\":{","\"ArrayTargetPreparation\":{\"array_t0_s0_long\":{\"PackageRoot\":"+JsonSerializer.Serialize(Path.Combine(root,"array_t0_s0_long"))+"},",StringComparison.Ordinal)).RootElement.Clone();
        Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativeLease.Validate(input,root));Assert.False(Directory.Exists(root));
    }
    /// <summary>正式节点设置先实际生成单调正整数，不留到初始化时才发现越界。</summary>
    [Fact]
    public void Formal_workers_generate_monotonic_ids_before_database_lease()
    {foreach(var worker in GlobalArrayTargetNativeModel.All.Select(model=>model.Worker)){var ids=new SnowflakeIdGenerator(worker);var first=ids.NextId();Assert.True(first>0);Assert.True(ids.NextId()>first);}}
}
