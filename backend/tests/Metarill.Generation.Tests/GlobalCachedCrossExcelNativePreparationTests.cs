using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Testing;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>六模型在一个真实生产Web宿主中准备；离线通过不等于取得新库租约或真实登录通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedCrossExcelNativePreparationTests
{
    /// <summary>合并后仍保留原六个R0存储、全局缓存角色、共享/短控件和12个维护按钮。</summary>
    [Fact]
    public void Combined_catalog_retains_six_original_models_and_numeric_configuration_identity()
    {
        var catalog=GlobalCachedCrossExcelNativePreparation.Catalog();var before=JsonSerializer.Serialize(catalog);
        MetadataValidation.Validate(catalog);var model=GenerationModel.Create(catalog,GlobalCachedCrossExcelNativePreparation.RootNamespace);
        Assert.Equal(32,catalog.ConfigTables.Count);Assert.Equal(32,catalog.ConfigPages.Count);Assert.Equal(32,catalog.ConfigMenus.Count);Assert.Equal(12,catalog.ConfigButtons.Count);
        foreach(var p in GlobalCachedCrossExcelMySqlLease.Profiles)
        {
            var profile=GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter);var prefix="gce_"+profile;
            var table=model.Tables.Single(value=>value.Source.TableName==prefix+"_cross_consumer");Assert.Equal(0,table.Source.IsRongyu);
            var page=Assert.Single(table.Pages);Assert.Equal(1,page.Source.IsImport);Assert.Equal(1,page.Source.IsExport);
            var original=p.Shared?GlobalCrossCachedTargetFixture.CreateShared(0,p.Key,p.Parent,p.Shorter):GlobalCrossCachedTargetFixture.Create(0,p.Role,p.Key,p.Parent,p.AllGlobal);
            GlobalCachedCrossExcelMySqlTests.Prefix(original,prefix);var prior=GenerationModel.Create(original,GlobalCachedCrossExcelNativePreparation.RootNamespace).Tables.Single(value=>value.Source.TableName==table.Source.TableName).Pages.Single();
            Assert.Equal(prior.Readable.Select(column=>column.Source.ColumnName),page.Readable.Select(column=>column.Source.ColumnName));
            Assert.Equal(prior.Writable.Select(column=>column.Source.ColumnName),page.Writable.Select(column=>column.Source.ColumnName));
            Assert.Equal(p.Key,table.Columns.Single(column=>column.Source.ColumnName==(p.Shorter?"area_id":"place_id")).Source.LogicalType);
            Assert.Equal(p.Shared?2:1,CrossTableReadCompilation.ForPage(model,table,page).Count);
            var cached=model.Tables.Single(value=>value.Source.TableName==prefix+"_cached_global");Assert.True(cached.IsGlobal);
            var name=cached.Columns.Single(column=>column.Source.ColumnName=="lookup_name");Assert.Equal("lookup.name",name.Source.Extra["columnFullName"].GetString());
        }
        foreach(var group in new IEnumerable<MetadataRecord>[]{catalog.ConfigTables,catalog.ConfigColumns,catalog.ConfigPages,catalog.ConfigPageColumns,catalog.ConfigMenus})
        {var ids=group.Select(value=>value.Id).ToArray();Assert.Equal(ids.Length,ids.Distinct(StringComparer.Ordinal).Count());Assert.All(ids,id=>Assert.True(long.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out var number)&&number>0));}
        Assert.NotEmpty(CrossTableExcelApplicationPreparer.RequireCurrent(catalog,model.Namespace).Gaps);Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        Assert.Equal(before,JsonSerializer.Serialize(catalog));Assert.Equal(before,JsonSerializer.Serialize(GlobalCachedCrossExcelNativePreparation.Catalog()));
    }
    /// <summary>真实完整源码严格编译、两轮partial保护及文件漂移拒绝，冻结副本只接受明确运行审计。</summary>
    [Fact]
    public async Task Combined_prepared_host_builds_and_freezes_all_six_original_transaction_hooks()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cross-excel-native");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"application");
        try
        {
            GlobalCachedCrossExcelNativePreparation.Create(source,package);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--disable-build-servers","--verbosity","quiet"})build.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(build);
            var proof=GlobalCachedCrossExcelNativePreparation.Capture(package);var catalog=GlobalCachedCrossExcelNativePreparation.Verify(proof);Assert.Equal(32,catalog.ConfigTables.Count);
            foreach(var asset in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(source,"frontend/metarill_uni_admin",asset)),File.ReadAllBytes(Path.Combine(package,"frontend",asset)));
            var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);
            host.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"files");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");
            var logs=CSharpGenerator.SafePath(source,GlobalCachedCrossExcelNativePreparation.Root+"/offline-hosts/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(logs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;
            await using(var running=await FreshHost.StartAsync(host,"/health"))foreach(var table in catalog.ConfigTables.Where(value=>value.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)))
            {
                var page=catalog.ConfigPages.Single(value=>value.ConfigTableId==table.Id);var route="/api/admin/auto/"+table.TableName+"/"+page.Id+"/";
                foreach(var action in new[]{"page","importDatas"+page.Id,"initExport"})await FreshWebHttp.SendAsync(running.Client,HttpMethod.Post,route+action,new{},System.Net.HttpStatusCode.Unauthorized);
            }
            GlobalCachedCrossExcelNativePreparation.Verify(proof);
            Assert.False(File.Exists(Path.Combine(package,".metarill-package.json")));
            Assert.Equal(6,proof.Files.Keys.Count(path=>path.StartsWith("backend/generated/Services/",StringComparison.Ordinal)&&path.EndsWith("CrossConsumerService.cs",StringComparison.Ordinal)));
            foreach(var path in new[]{"src/Metarill.Core/Cascades/CrossTableExcelPath.cs","backend/metadata/project.json",CrossTableExcelApplicationPreparer.Marker,"backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {var file=CSharpGenerator.SafePath(package,path);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"本轮漂移");Assert.ThrowsAny<Exception>(()=>GlobalCachedCrossExcelNativePreparation.Verify(proof));await File.WriteAllBytesAsync(file,bytes);}
            var foreign=CSharpGenerator.SafePath(package,"backend/Application.Host/ForeignOverride.cs");await File.WriteAllTextAsync(foreign,"// 不允许通过重新冻结收编额外源文件。\n");Assert.Throws<InvalidOperationException>(()=>GlobalCachedCrossExcelNativePreparation.Verify(GlobalCachedCrossExcelNativePreparation.Capture(package)));File.Delete(foreign);
            var markerPath=CSharpGenerator.SafePath(package,CrossTableExcelApplicationPreparer.Marker);var markerBytes=await File.ReadAllBytesAsync(markerPath);var marker=JsonNode.Parse(markerBytes)!;marker["readyForPublication"]=true;await File.WriteAllTextAsync(markerPath,marker.ToJsonString());Assert.Throws<InvalidOperationException>(()=>GlobalCachedCrossExcelNativePreparation.Verify(GlobalCachedCrossExcelNativePreparation.Capture(package)));await File.WriteAllBytesAsync(markerPath,markerBytes);
            var copied=GlobalCachedCrossExcelNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"));var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>GlobalCachedCrossExcelNativePreparation.Verify(copied));GlobalCachedCrossExcelNativePreparation.Verify(copied,true);GlobalCachedCrossExcelNativePreparation.Verify(proof);
            var output=Environment.GetEnvironmentVariable("METARILL_GLOBAL_CROSS_EXCEL_PREPARE_OUTPUT");
            if(!string.IsNullOrWhiteSpace(output))
            {
                Assert.Equal(CSharpGenerator.SafePath(source,GlobalCachedCrossExcelNativePreparation.Root+"/prepared"),Path.GetFullPath(output));
                var exported=GlobalCachedCrossExcelNativePreparation.CopyForExecution(proof,output);await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(output)!,"prepared-receipt.json"),JsonSerializer.Serialize(exported,new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
