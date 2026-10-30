using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>离线只证明完整包与租约边界；不把合成批准文档冒充真实用户授权。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class ManagementNativePreparationTests
{
    /// <summary>实际公开管理包严格编译后冻结与复制；源码、编译输出、未知文件及运行审计例外分别校验。</summary>
    [Fact]
    public async Task Public_management_package_builds_and_immutable_execution_copy_rejects_drift()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-native-management-prepare");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var package=Path.Combine(root,"prepared");ManagementNativePreparation.Create(source,package);Assert.ThrowsAny<IOException>(()=>ManagementNativePreparation.Capture(package));
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var proof=ManagementNativePreparation.Capture(package);var models=ManagementNativePreparation.Verify(proof,source);Assert.Equal(2,models.Published.YzAdminTypes.Count);Assert.Equal(ManagementNativePreparation.Admin,models.Binding.ManagementAdminTypeId);
            var copied=ManagementNativePreparation.CopyForExecution(proof,source,Path.Combine(root,"execution"));Assert.Equal(proof.Files.Count,copied.Files.Count);Assert.Equal(proof.ManifestSha256,copied.ManifestSha256);Assert.Throws<InvalidOperationException>(()=>ManagementNativePreparation.CopyForExecution(proof,source,Path.Combine(root,"execution")));
            foreach(var path in new[]{"backend/generated/Services/YzRoleService.cs","backend/Application.Host/bin/Release/net10.0/Application.Host.dll","backend/metadata/application-source.json"})
            {var file=Path.Combine(copied.PackageRoot,path);var prior=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"漂移");Assert.Throws<InvalidOperationException>(()=>ManagementNativePreparation.Verify(copied,source));await File.WriteAllBytesAsync(file,prior);}
            var extra=Path.Combine(copied.PackageRoot,"unknown.txt");await File.WriteAllTextAsync(extra,"不能自行扩展固定包");Assert.Throws<InvalidOperationException>(()=>ManagementNativePreparation.Verify(copied,source));File.Delete(extra);
            var journal=Path.Combine(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>ManagementNativePreparation.Verify(copied,source));ManagementNativePreparation.Verify(copied,source,runtime:true);Assert.Throws<InvalidOperationException>(()=>ManagementNativePreparation.Verify(ManagementNativePreparation.Capture(copied.PackageRoot),source,runtime:true));
            ManagementNativePreparation.Verify(proof,source);Assert.False(Directory.Exists(Path.Combine(package,"backend/Application.Host/.local")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>在独立进程运行真实正式入口；配置故意没有数据库字段，必须先因未授权而拒绝且不建副本。</summary>
    [Fact]
    public async Task Actual_native_entry_rejects_pending_authorization_before_reading_database_configuration()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-native-entry");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"uncreated-source");var prepared=Path.Combine(source,".local/management-full-native/prepared");var config=Path.Combine(root,"pending.json");await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{Purpose=ManagementNativeLease.Purpose,AuthorizationStatus="pending",LeaseStatus="not-started",ProductCommit=ManagementNativePreparation.ProductCommit,RunId="0123456789abcdef",ManagementPreparation=new{PackageRoot=prepared}}));
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",NonRedundantApplicationFixture.Root());foreach(var argument in new[]{"vstest",typeof(ManagementNativeMySqlTests).Assembly.Location,"/TestCaseFilter:FullyQualifiedName=Metarill.Generation.Tests.ManagementNativeMySqlTests.Fixed_public_management_package_uses_real_identity_database_cli_mcp_and_initialization","/Logger:trx;LogFileName=unapproved.trx","/ResultsDirectory:"+Path.Combine(root,"results")})start.ArgumentList.Add(argument);
            start.Environment["METARILL_MANAGEMENT_NATIVE_SETTINGS"]=config;start.Environment["METARILL_FRESH_INSTALL_SETTINGS"]=config;start.Environment["METARILL_MANAGEMENT_NATIVE_SOURCE_ROOT"]=source;
            using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
            _=await stdout;_=await stderr;Assert.NotEqual(0,process.ExitCode);var trx=System.Xml.Linq.XDocument.Load(Path.Combine(root,"results/unapproved.trx"));var ns=trx.Root!.Name.Namespace;var result=Assert.Single(trx.Descendants(ns+"UnitTestResult"));Assert.Equal("Failed",result.Attribute("outcome")!.Value);Assert.Contains("完整管理正式验收未取得本轮专属运行租约。",Assert.Single(result.Descendants(ns+"Message")).Value);Assert.False(Directory.Exists(source));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>未经批准、旧用途、结束租约及换包根都在任何副作用之前拒绝。</summary>
    [Theory]
    [InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("proof")]
    public void Full_management_lease_rejects_unapproved_or_foreign_input_without_side_effects(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-management-native-gate",Guid.NewGuid().ToString("N"));var node=JsonSerializer.SerializeToNode(new{Purpose=ManagementNativeLease.Purpose,AuthorizationStatus="approved",LeaseStatus="running",ProductCommit=ManagementNativePreparation.ProductCommit,RunId="0123456789abcdef",ManagementPreparation=new{PackageRoot=root}})!;
        Assert.Equal("0123456789abcdef",ManagementNativeLease.Validate(JsonSerializer.SerializeToElement(node),root));
        if(invalid=="purpose")node["Purpose"]="standalone-agent-v1";if(invalid=="authorization")node["AuthorizationStatus"]="pending";if(invalid=="ended")node["LeaseStatus"]="ended";if(invalid=="product")node["ProductCommit"]=new string('0',40);if(invalid=="run")node["RunId"]="../old";if(invalid=="path")node["ManagementPreparation"]!["PackageRoot"]=Path.Combine(root,"foreign");if(invalid=="relative")node["ManagementPreparation"]!["PackageRoot"]="prepared";if(invalid=="proof")node.AsObject().Remove("ManagementPreparation");
        Assert.Throws<InvalidOperationException>(()=>ManagementNativeLease.Validate(JsonSerializer.SerializeToElement(node),root));Assert.False(Directory.Exists(root));
    }
}
