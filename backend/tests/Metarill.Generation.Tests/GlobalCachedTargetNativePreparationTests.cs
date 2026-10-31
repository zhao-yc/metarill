using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Security;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>先独立严格构建并冻结全部四模型，缺批准租约不能打开数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedTargetNativePreparationTests
{
    /// <summary>使用正式完整包并验证篡改、错模型、额外文件与唯一运行日志边界；可导出供之后固定验收。</summary>
    [Theory][InlineData(0,"string","cross_ss")][InlineData(0,"int64","cross_sl")][InlineData(1,"string","cross_ls")][InlineData(1,"int64","cross_ll")]
    public async Task Four_original_models_are_frozen_before_database_lease(int mode,string type,string suffix)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cached-target-native");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"prepared");
        try
        {
            new ApplicationPackager().Create(GlobalCachedTargetNativePreparation.Catalog(mode,type),package,GlobalCachedTargetNativePreparation.RootNamespace,source);
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(start);
            var proof=GlobalCachedTargetNativePreparation.Capture(package);var catalog=GlobalCachedTargetNativePreparation.Verify(proof,mode,type);Assert.Equal(3,catalog.ConfigTables.Count);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,GlobalCachedTargetNativePreparation.RootNamespace)));
            // 正式用例的所有固定路由必须来自实际生成Controller，不能靠猜测在实库中试错。
            var controllers=string.Join("\n",Directory.GetFiles(Path.Combine(package,"backend/generated/Controllers"),"*.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
            foreach(var route in new[]{"findNameIdGlobalSource34/page","findNameIdGlobalSource34/selected","importDatas30","initExport"})Assert.Contains(route,controllers);
            foreach(var relative in new[]{"src/Metarill.Core/Metadata/GlobalPhysicalRelationValue.cs","backend/generated/Services/CachedConsumerService.g.cs","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {var path=CSharpGenerator.SafePath(package,relative);var bytes=await File.ReadAllBytesAsync(path);await File.AppendAllTextAsync(path,"漂移");Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativePreparation.Verify(proof,mode,type));await File.WriteAllBytesAsync(path,bytes);}
            Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativePreparation.Verify(proof,1-mode,type));Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativePreparation.Verify(proof,mode,type=="int64"?"string":"int64"));
            var copied=GlobalCachedTargetNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"),mode,type);var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativePreparation.Verify(copied,mode,type));GlobalCachedTargetNativePreparation.Verify(copied,mode,type,true);
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/unknown.json");await File.WriteAllTextAsync(unknown,"{}");Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativePreparation.Verify(copied,mode,type,true));File.Delete(unknown);Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativePreparation.CopyForExecution(proof,copied.PackageRoot,mode,type));GlobalCachedTargetNativePreparation.Verify(proof,mode,type);
            var output=Environment.GetEnvironmentVariable("METARILL_CACHED_TARGET_PREPARE_OUTPUT");if(!string.IsNullOrWhiteSpace(output))
            {Assert.Equal(CSharpGenerator.SafePath(source,".local/global-cached-target-native/prepared"),Path.GetFullPath(output));var result=GlobalCachedTargetNativePreparation.CopyForExecution(proof,Path.Combine(output,suffix),mode,type);await File.WriteAllTextAsync(Path.Combine(output,suffix+".json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>专属批准、单次状态及完整四模型路径缺一即拒绝；合成配置不代表真实批准。</summary>
    [Theory][InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("missing")][InlineData("extra")][InlineData("type")]
    public void Lease_rejects_unapproved_or_foreign_input_before_any_side_effect(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-cached-target-lease",Guid.NewGuid().ToString("N"));var models=new JsonObject();foreach(var suffix in new[]{"cross_ss","cross_sl","cross_ls","cross_ll"})models[suffix]=new JsonObject{{"PackageRoot",Path.Combine(root,suffix)}};
        var settings=new JsonObject{{"Purpose",GlobalCachedTargetNativeLease.Purpose},{"AuthorizationStatus","approved"},{"LeaseStatus","running"},{"ProductCommit",GlobalCachedTargetNativePreparation.ProductCommit},{"RunId","0123456789abcdef"},{"CachedTargetPreparation",models}};
        Assert.Equal("0123456789abcdef",GlobalCachedTargetNativeLease.Validate(JsonSerializer.SerializeToElement(settings),root));
        if(invalid=="purpose")settings["Purpose"]="global-cached-source";if(invalid=="authorization")settings["AuthorizationStatus"]="pending";if(invalid=="ended")settings["LeaseStatus"]="ended";if(invalid=="product")settings["ProductCommit"]="old";if(invalid=="run")settings["RunId"]="../old";if(invalid=="path")models["cross_ss"]!["PackageRoot"]=Path.Combine(root,"other");if(invalid=="relative")models["cross_ss"]!["PackageRoot"]="prepared";if(invalid=="missing")models.Remove("cross_ss");if(invalid=="extra")models["foreign"]=new JsonObject();if(invalid=="type")settings["CachedTargetPreparation"]="not-object";
        Assert.Throws<InvalidOperationException>(()=>GlobalCachedTargetNativeLease.Validate(JsonSerializer.SerializeToElement(settings),root));Assert.False(Directory.Exists(root));
    }
    /// <summary>正式节点设置先实际生成单调正整数，不留到初始化时才发现越界。</summary>
    [Fact]
    public void Formal_workers_generate_monotonic_ids_before_database_lease()
    {foreach(var worker in new[]{870,871,872,873}){var ids=new SnowflakeIdGenerator(worker);var first=ids.NextId();Assert.True(first>0);Assert.True(ids.NextId()>first);}}
}
