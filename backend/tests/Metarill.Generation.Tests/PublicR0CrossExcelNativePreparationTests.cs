using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Testing;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真正公开Full的来源与拒绝门槛；离线构建／匿名HTTP不替代正式登录和实库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class PublicR0CrossExcelNativePreparationTests
{
    /// <summary>原六模型、菜单和维护按钮不变，公开审计不得通过删除Excel字段取得通过。</summary>
    [Fact]
    public void Public_catalog_preserves_original_six_models_and_actions()
    {
        var catalog=PublicR0CrossExcelNativePreparation.Catalog();var before=JsonSerializer.Serialize(catalog);
        Assert.Equal(JsonSerializer.Serialize(GlobalCachedCrossExcelNativePreparation.Catalog()),before);
        MetadataValidation.Validate(catalog);Assert.Equal(32,catalog.ConfigTables.Count);Assert.Equal(32,catalog.ConfigPages.Count);Assert.Equal(32,catalog.ConfigMenus.Count);Assert.Equal(12,catalog.ConfigButtons.Count);
        Assert.Equal(43,FreshInstallConfiguration.ExpectedFullTableCount(catalog));
        var model=GenerationModel.Create(catalog,PublicR0CrossExcelNativePreparation.RootNamespace);Assert.Empty(GenerationCapabilityAudit.Collect(model));new FrontendPackager().Validate(catalog);
        var sourcePages=model.Tables.Where(table=>table.Source.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)).Select(table=>Assert.Single(table.Pages)).ToArray();Assert.Equal(6,sourcePages.Length);Assert.All(sourcePages,page=>{Assert.Equal(1,page.Source.IsImport);Assert.Equal(1,page.Source.IsExport);Assert.True(ImportRenderer.Mode(page) is "" or "OnlyNew");});
        Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }

    /// <summary>公开包自身严格构建、六partial保护、文件漂移与重新捕获拒绝，并核对18个匿名原入口。</summary>
    [Fact]
    public async Task Public_full_builds_freezes_and_rejects_drift_without_a_database()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-public-r0-cross-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"application");
        try
        {
            PublicR0CrossExcelNativePreparation.Create(source,package);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--disable-build-servers","--verbosity","quiet"})build.ArgumentList.Add(argument);
            var logs=CSharpGenerator.SafePath(source,PublicR0CrossExcelNativePreparation.Root+"/offline-builds/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(logs);build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;await NonRedundantApplicationFixture.RunAsync(build);
            // 先逐份证明文件就是原钩子的原始字节，避免用行尾规范化掩盖手写内容漂移。
            foreach(var table in PublicR0CrossExcelNativePreparation.Catalog().ConfigTables.Where(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)))
                Assert.Equal(System.Text.Encoding.UTF8.GetBytes(GlobalCachedCrossExcelNativePreparation.Handwritten(table.TableName)),await File.ReadAllBytesAsync(Path.Combine(package,"backend/generated/Services",GenerationModel.Pascal(table.TableName)+"Service.cs")));
            var proof=PublicR0CrossExcelNativePreparation.Capture(package);var catalog=PublicR0CrossExcelNativePreparation.Verify(proof);
            Assert.True(File.Exists(Path.Combine(package,".metarill-package.json")));Assert.False(File.Exists(Path.Combine(package,CrossTableExcelApplicationPreparer.Marker)));
            Assert.Equal(6,proof.Files.Keys.Count(path=>path.StartsWith("backend/generated/Services/",StringComparison.Ordinal)&&path.EndsWith("CrossConsumerService.cs",StringComparison.Ordinal)));
            foreach(var asset in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js","common/yzp_runtime/yzp-runtime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(source,"frontend/metarill_uni_admin",asset)),File.ReadAllBytes(Path.Combine(package,"frontend",asset)));
            var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));
            host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"files");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;
            await using(var running=await FreshHost.StartAsync(host,"/health"))foreach(var table in catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)))
            {
                var page=catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id);var route="/api/admin/auto/"+table.TableName+"/"+page.Id+"/";
                foreach(var action in new[]{"page","importDatas"+page.Id,"initExport"})await FreshWebHttp.SendAsync(running.Client,System.Net.Http.HttpMethod.Post,route+action,new{},System.Net.HttpStatusCode.Unauthorized);
            }
            foreach(var path in new[]{"src/Metarill.Core/Cascades/CrossTableExcelPath.cs","backend/metadata/project.json","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {
                var file=CSharpGenerator.SafePath(package,path);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"本轮来源漂移");Assert.ThrowsAny<Exception>(()=>PublicR0CrossExcelNativePreparation.Verify(proof));
                // 托管源码必须符合原生成清单，重新捕获也不能收编；编译输出由原冻结快照和外层授权范围绑定。
                if(!path.Contains("/bin/",StringComparison.Ordinal))Assert.ThrowsAny<Exception>(()=>PublicR0CrossExcelNativePreparation.Verify(PublicR0CrossExcelNativePreparation.Capture(package)));
                await File.WriteAllBytesAsync(file,bytes);
            }
            var foreign=CSharpGenerator.SafePath(package,"src/Metarill.Core/Foreign.cs");await File.WriteAllTextAsync(foreign,"// 重新捕获不能收编额外源码。\n");Assert.Throws<InvalidOperationException>(()=>PublicR0CrossExcelNativePreparation.Verify(PublicR0CrossExcelNativePreparation.Capture(package)));File.Delete(foreign);
            var marker=CSharpGenerator.SafePath(package,".metarill-package.json");var markerBytes=await File.ReadAllBytesAsync(marker);var node=JsonNode.Parse(markerBytes)!;node["authenticationProfile"]="development-only";await File.WriteAllTextAsync(marker,node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>PublicR0CrossExcelNativePreparation.Verify(PublicR0CrossExcelNativePreparation.Capture(package)));await File.WriteAllBytesAsync(marker,markerBytes);
            var copied=PublicR0CrossExcelNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"));var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>PublicR0CrossExcelNativePreparation.Verify(copied));PublicR0CrossExcelNativePreparation.Verify(copied,true);PublicR0CrossExcelNativePreparation.Verify(proof);
            var output=Environment.GetEnvironmentVariable("METARILL_PUBLIC_R0_CROSS_EXCEL_PREPARE_OUTPUT");
            if(!string.IsNullOrWhiteSpace(output))
            {
                Assert.Equal(CSharpGenerator.SafePath(source,PublicR0CrossExcelNativePreparation.Root+"/prepared"),Path.GetFullPath(output));
                var exported=PublicR0CrossExcelNativePreparation.CopyForExecution(proof,output);await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(output)!,"prepared-receipt.json"),JsonSerializer.Serialize(exported,new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
