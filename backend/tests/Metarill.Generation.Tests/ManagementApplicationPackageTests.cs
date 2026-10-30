using System.Text.Json;
using System.Security.Cryptography;
using System.Net;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>固定管理模块必须按实际适配器编译，不以普通元数据标志解除能力门禁。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class ManagementApplicationPackageTests
{
    /// <summary>当前可信签发已选择实际Service/工作台/初始化模板，不能仍报告普通未接线协调器。</summary>
    [Fact]
    public void Issued_management_compilation_uses_real_runtime_adapters_without_loosening_ordinary_models()
    {
        var source=NonRedundantApplicationFixture.Root();var application=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var module=SecurityManagementModuleFactory.Compose(application,application.YzAdminTypes[0].Id);
        var catalog=SecurityManagementModuleFactory.RequirePublishedForGeneration(module);var model=GenerationModel.Create(catalog,"Example.ManagedPackage");
        Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Usage=="runtime"&&gap.Column=="yz_role_ids");
        var bound=TrustedSecurityServiceCompilation.Bind(model,module);Assert.Empty(GenerationCapabilityAudit.Collect(bound));
    }

    /// <summary>实际完整源码、清单、原工作台及生产宿主组成一包；匿名访问真实管理API保持拒绝。</summary>
    [Fact]
    public async Task Full_management_package_builds_from_its_own_sources_and_keeps_original_anonymous_boundaries()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-full");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var app=Catalog(source);const string secret="完整管理不能泄露-732985";app.ConfigTables[0].Extra["apiKey"]=JsonSerializer.SerializeToElement(secret);
            var output=Path.Combine(root,"full");var result=new ApplicationPackager().CreateManagement(app,output,"Example.FullManagement",source,app.YzAdminTypes[0].Id);
            Assert.Equal(ApplicationPackageMode.FullManagementApplication,result.Mode);Assert.True(result.FrontendIncluded);Assert.NotEmpty(result.Warnings);
            Assert.False(File.Exists(Path.Combine(output,".metarill-management-preparation.json")));Assert.True(File.Exists(Path.Combine(output,PreparedSecurityManagementSource.SourceMarker)));
            var context=PreparedSecurityManagementSource.Read(output);Assert.Equal(app.YzProjectId,context.Module.Binding.ProjectId);Assert.Equal(app.YzAdminTypes[0].Id,context.Module.Binding.ManagementAdminTypeId);
            // 完整管理模式也须真正下发原List分页依赖；源码编译不能代替前端导入闭包。
            const string remoteRelative="frontend/common/yzp_runtime/originalRemoteOptionsMixin.js";
            Assert.Contains(remoteRelative,result.Files);var remotePath=Path.Combine(output,remoteRelative);
            Assert.Equal(File.ReadAllText(Path.Combine(source,"frontend/metarill_uni_admin/common/yzp_runtime/originalRemoteOptionsMixin.js")).Replace("\r\n","\n"),File.ReadAllText(remotePath).Replace("\r\n","\n"));
            Assert.Contains("createOriginalRemoteOptionsMixin",File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/yz_role-detail-1692532858529329154.vue")));
            var verified=await FrontendPackagerTests.VerifyFrontendAsync(Path.Combine(output,"frontend"));Assert.True(verified.Code==0,verified.Error+verified.Output);
            // 缺失模块必须由随包真实Node校验拒绝，不能只检查文件清单中曾经存在。
            var held=Path.Combine(root,"held-original-remote.js");File.Move(remotePath,held);
            try
            {
                var missing=await FrontendPackagerTests.VerifyFrontendAsync(Path.Combine(output,"frontend"));
                Assert.NotEqual(0,missing.Code);Assert.Contains("originalRemoteOptionsMixin",missing.Error+missing.Output);
            }
            finally{File.Move(held,remotePath);}
            // 同时存在准备/通用来源声明时不能按其中一个猜测，拒绝后保留原完整包。
            var ambiguous=Path.Combine(output,".metarill-management-preparation.json");await File.WriteAllTextAsync(ambiguous,await File.ReadAllTextAsync(Path.Combine(output,PreparedSecurityManagementSource.SourceMarker)));
            Assert.Throws<InvalidOperationException>(()=>PreparedSecurityManagementSource.Read(output));File.Delete(ambiguous);
            using(var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,".metarill-package.json"))))
            {
                Assert.Equal("FullManagementApplication",manifest.RootElement.GetProperty("packageMode").GetString());Assert.Equal("web-database-v1",manifest.RootElement.GetProperty("authenticationProfile").GetString());
                Assert.Equal(context.ApplicationMetadataSha256,manifest.RootElement.GetProperty("managementModule").GetProperty("applicationMetadataSha256").GetString());
                foreach(var file in manifest.RootElement.GetProperty("files").EnumerateArray())
                {
                    var bytes=await File.ReadAllBytesAsync(Path.Combine(output,file.GetProperty("path").GetString()!));Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)),file.GetProperty("sha256").GetString());
                    Assert.DoesNotContain(secret,System.Text.Encoding.UTF8.GetString(bytes));
                }
            }
            // 管理模式按同一固定来源与后台绑定识别，不能仅凭模式字符串接受。
            Assert.Equal(app.YzProjectId,new LocalGenerationUpdater().InspectTarget(output).ProjectId);
            foreach(var relative in new[]{"pages/main/set_role_permission.vue","components/yzp-permission-workbench/yzp-permission-workbench.vue","components/yzp-permission-legacy/yzp-permission-legacy.vue"})
                Assert.Equal(File.ReadAllText(Path.Combine(source,"frontend/metarill_uni_admin",relative)).Replace("\r\n","\n"),File.ReadAllText(Path.Combine(output,"frontend",relative)).Replace("\r\n","\n"));
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})build.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(build);
            var hostDirectory=Path.Combine(output,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));
            host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
            host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="full_management_offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=full_management_offline;User ID=unused;Connection Timeout=1";
            var evidence=Path.Combine(source,".local/management-full-package",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            await using var running=await FreshHost.StartAsync(host,"/health");
            foreach(var route in new[]{"/api/admin/auto/yz_manager/6031906822946816/page","/api/admin/auto/yz_role/1692532858529329154/page","/api/admin/auto/yz_admin_type/1696799309006348290/page","/api/admin/custom/index/init_admin"})
                await FreshWebHttp.SendAsync(running.Client,System.Net.Http.HttpMethod.Post,route,new{},HttpStatusCode.Unauthorized);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>未知后台、伪业务安全表和未选择管理后台的模式调用均不能产生输出，非空工程不接管。</summary>
    [Fact]
    public async Task Full_management_mode_is_explicit_and_never_overwrites_existing_projects()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-full");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var app=Catalog(source);var output=Path.Combine(root,"output");var builder=new ApplicationPackager();
            Assert.Throws<ArgumentOutOfRangeException>(()=>builder.Create(app,output,"Example.FullManagement",source,ApplicationPackageMode.FullManagementApplication));Assert.False(Directory.Exists(output));
            Assert.Throws<InvalidOperationException>(()=>builder.CreateManagement(app,output,"Example.FullManagement",source,"7"));Assert.False(Directory.Exists(output));
            var fake=Catalog(source);fake.ConfigTables[0].TableName="yz_manager";Assert.Throws<InvalidOperationException>(()=>builder.CreateManagement(fake,output,"Example.FullManagement",source,fake.YzAdminTypes[0].Id));Assert.False(Directory.Exists(output));
            Directory.CreateDirectory(output);var keep=Path.Combine(output,"keep.txt");await File.WriteAllTextAsync(keep,"保留已有工程");
            Assert.Throws<InvalidOperationException>(()=>builder.CreateManagement(app,output,"Example.FullManagement",source,app.YzAdminTypes[0].Id));Assert.Equal("保留已有工程",await File.ReadAllTextAsync(keep));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>完整包发布失败保留已核验源码和外层诊断，不删除失败现场或留下半发布目标。</summary>
    [Fact]
    public async Task Full_management_publication_failure_preserves_the_owned_complete_candidate()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-full");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var app=Catalog(source);var output=Path.Combine(root,"failed");var builder=new ApplicationPackager((stage,destination)=>throw new PackagePublicationException(stage,destination,new IOException("完整管理发布故障")));
            var error=Assert.Throws<PackagePublicationException>(()=>builder.CreateManagement(app,output,"Example.FullManagement",source,app.YzAdminTypes[0].Id));
            Assert.False(Directory.Exists(output));Assert.NotNull(error.ContainingStageDirectory);Assert.True(File.Exists(error.DiagnosticPath));Assert.True(File.Exists(Path.Combine(error.StageDirectory,".metarill-package.json")));
            Assert.NotNull(PreparedSecurityManagementSource.Read(error.StageDirectory));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>从已提交普通业务目录开始，不把富管理图当作原业务来源。</summary>
    private static MetadataCatalog Catalog(string source)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
