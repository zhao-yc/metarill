using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>离线冻结实际包和执行器范围，不读取连接或请求创建数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelNativePreparationTests
{
    /// <summary>八组合真实包严格编译，手写业务两轮保留，来源／执行副本及运行日志边界均固定。</summary>
    [Fact]
    public async Task Eight_model_application_is_frozen_with_real_transaction_hook_before_database_lease()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-native");
        var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(root,"prepared");
        try
        {
            CrossTableExcelNativePreparation.Create(source,package);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var item in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(item);
            await NonRedundantApplicationFixture.RunAsync(build);
            var proof=CrossTableExcelNativePreparation.Capture(package);var catalog=CrossTableExcelNativePreparation.Verify(proof);
            Assert.Equal(32,catalog.ConfigTables.Count);Assert.Equal(8,catalog.ConfigPages.Count(page=>page.IsImport==1));Assert.False(File.Exists(Path.Combine(package,".metarill-package.json")));
            foreach(var path in new[]{CrossTableExcelApplicationPreparer.Marker,"src/Metarill.Core/Cascades/CrossTableExcelPath.cs","backend/metadata/project.json","backend/generated/Services/CxR0SsSourceService.cs","backend/Application.Host/bin/Release/net10.0/Application.Host.dll"})
            {
                var file=CSharpGenerator.SafePath(package,path);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"漂移");
                Assert.ThrowsAny<Exception>(()=>CrossTableExcelNativePreparation.Verify(proof));await File.WriteAllBytesAsync(file,bytes);
            }
            // 重新冻结也不能把未知源文件或可发布标记变成合法准备包。
            var foreign=Path.Combine(package,"backend/Application.Host/ForeignOverride.cs");await File.WriteAllTextAsync(foreign,"// 未声明的额外源码。");
            Assert.Throws<InvalidOperationException>(()=>CrossTableExcelNativePreparation.Verify(CrossTableExcelNativePreparation.Capture(package)));File.Delete(foreign);
            var markerPath=Path.Combine(package,CrossTableExcelApplicationPreparer.Marker);var markerBytes=await File.ReadAllBytesAsync(markerPath);
            var marker=JsonNode.Parse(markerBytes)!;marker["readyForPublication"]=true;await File.WriteAllTextAsync(markerPath,marker.ToJsonString());
            Assert.Throws<InvalidOperationException>(()=>CrossTableExcelNativePreparation.Verify(CrossTableExcelNativePreparation.Capture(package)));await File.WriteAllBytesAsync(markerPath,markerBytes);
            var copied=CrossTableExcelNativePreparation.CopyForExecution(proof,Path.Combine(root,"execution/application"));
            var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");
            Assert.Throws<InvalidOperationException>(()=>CrossTableExcelNativePreparation.Verify(copied));CrossTableExcelNativePreparation.Verify(copied,true);
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/foreign.json");await File.WriteAllTextAsync(unknown,"{}");
            Assert.Throws<InvalidOperationException>(()=>CrossTableExcelNativePreparation.Verify(copied,true));File.Delete(unknown);CrossTableExcelNativePreparation.Verify(proof);
            Assert.Throws<InvalidOperationException>(()=>CrossTableExcelNativePreparation.CopyForExecution(proof,copied.PackageRoot));
            var exported=Environment.GetEnvironmentVariable("METARILL_CROSS_EXCEL_PREPARE_OUTPUT");
            if(!string.IsNullOrWhiteSpace(exported))
            {
                Assert.Equal(CSharpGenerator.SafePath(source,".local/cross-table-excel-native/prepared"),Path.GetFullPath(exported));
                var result=CrossTableExcelNativePreparation.CopyForExecution(proof,exported);
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(exported)!,"prepared-receipt.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>批准形状的错误状态／用途／产品／包根均在任何副作用之前拒绝，不把合成数据当授权。</summary>
    [Theory][InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("proof")]
    public void Native_lease_rejects_foreign_or_unapproved_configuration(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-gate",Guid.NewGuid().ToString("N"));
        var model=JsonSerializer.SerializeToNode(new{Purpose=CrossTableExcelNativeLease.Purpose,AuthorizationStatus="approved",LeaseStatus="running",ProductCommit=CrossTableExcelNativePreparation.ProductCommit,RunId="0123456789abcdef",CrossTableExcelPreparation=new{PackageRoot=root}})!;
        Assert.Equal("0123456789abcdef",CrossTableExcelNativeLease.Validate(JsonSerializer.SerializeToElement(model),root));
        if(invalid=="purpose")model["Purpose"]="foreign";if(invalid=="authorization")model["AuthorizationStatus"]="pending";if(invalid=="ended")model["LeaseStatus"]="ended";if(invalid=="product")model["ProductCommit"]=new string('0',40);if(invalid=="run")model["RunId"]="../old";if(invalid=="path")model["CrossTableExcelPreparation"]!["PackageRoot"]=Path.Combine(root,"foreign");if(invalid=="relative")model["CrossTableExcelPreparation"]!["PackageRoot"]="prepared";if(invalid=="proof")model.AsObject().Remove("CrossTableExcelPreparation");
        Assert.Throws<InvalidOperationException>(()=>CrossTableExcelNativeLease.Validate(JsonSerializer.SerializeToElement(model),root));Assert.False(Directory.Exists(root));
    }
    /// <summary>正式节点号在打开数据库前实际生成精确正整数。</summary>
    [Fact]
    public void Formal_worker_generates_positive_monotonic_ids()
    {
        var ids=new Metarill.Core.Security.SnowflakeIdGenerator(CrossTableExcelNativeMySqlTests.WorkerId);
        var first=ids.NextId();Assert.True(first>0);Assert.True(ids.NextId()>first);
    }
}
