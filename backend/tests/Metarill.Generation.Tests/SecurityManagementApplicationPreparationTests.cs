using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实同源准备包独立编译，明确保留初始化/发布缺口，不连接数据库或注入假运行时。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SecurityManagementApplicationPreparationTests
{
    private const string Namespace="Example.Managed";
    /// <summary>实际源码闭包、原工作台/Agent页面、三个真实partial、编译绑定及再生成保护共同验证。</summary>
    [Fact]
    public async Task Prepared_application_contains_independent_sources_and_original_management_without_publishing()
    {
        var source=NonRedundantApplicationFixture.Root();var application=Application(source);const string secret="不可下发的准备测试密钥-782935";application.ConfigTables[0].Extra["apiKey"]=JsonSerializer.SerializeToElement(secret);
        var module=SecurityManagementModuleFactory.Compose(application,application.YzAdminTypes[0].Id);var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-preparation");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(root,"prepared");
        try
        {
            var result=new SecurityManagementApplicationPreparer().Prepare(module,output,Namespace,source);
            var repeated=Path.Combine(root,"repeated");new SecurityManagementApplicationPreparer().Prepare(module,repeated,Namespace,source);Assert.Equal(Snapshot(output),Snapshot(repeated));
            Assert.Throws<NotSupportedException>(()=>((IList<string>)result.Files).Add("未知路径"));
            Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));Assert.Contains(result.RequiredBackendAdapters,adapter=>adapter.Code=="initialize-account");Assert.Empty(result.BackendGaps);
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,".metarill-management-preparation.json")));var declaration=document.RootElement;Assert.False(declaration.GetProperty("readyForPublication").GetBoolean());Assert.Equal(result.MetadataSha256,declaration.GetProperty("metadataSha256").GetString());
            foreach(var file in declaration.GetProperty("files").EnumerateArray())Assert.Equal(file.GetProperty("sha256").GetString(),Hash(File.ReadAllBytes(CSharpGenerator.SafePath(output,file.GetProperty("path").GetString()!))));
            Assert.Contains("尚未发布",File.ReadAllText(Path.Combine(output,"README.md")));Assert.NotEmpty(result.RedactedPaths);
            foreach(var file in Directory.GetFiles(output,"*",SearchOption.AllDirectories))Assert.DoesNotContain(secret,System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            foreach(var name in new[]{"YzManager","YzRole","YzAdminType"})Assert.Contains("SecurityRealmMutationGuard.RequirePrepared",File.ReadAllText(Path.Combine(output,"backend/generated/Services/"+name+"Service.cs")));
            foreach(var name in ApplicationPackager.FullFoundationProjects)Assert.True(File.Exists(Path.Combine(output,"src",name,name+".csproj")));
            foreach(var project in Directory.GetFiles(output,"*.csproj",SearchOption.AllDirectories))foreach(var reference in XDocument.Load(project).Descendants("ProjectReference"))Assert.False(Path.IsPathRooted(reference.Attribute("Include")!.Value));
            foreach(var relative in new[]{"pages/main/set_role_permission.vue","components/yzp-permission-workbench/yzp-permission-workbench.vue","components/yzp-permission-legacy/yzp-permission-legacy.vue","common/yzp_runtime/permissionWorkbench.js","pages/custom/agent_authorize.vue","pages/custom/agent_grants.vue"})
                Assert.Equal(Text(Path.Combine(source,"frontend/metarill_uni_admin",relative)),Text(Path.Combine(output,"frontend",relative)));
            using(var routes=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"frontend/pages.json"))))
                foreach(var path in new[]{"pages/main/set_role_permission","pages/custom/agent_authorize","pages/custom/agent_grants"})Assert.Single(routes.RootElement.GetProperty("pages").EnumerateArray(),route=>route.GetProperty("path").GetString()==path);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})build.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(build);
            // 只读观察器使用公开手写Host钩子，不替换真实认证、Controller、权限或数据库类型。
            var hostDirectory=Path.Combine(output,"backend/Application.Host");await File.WriteAllTextAsync(Path.Combine(hostDirectory,"PreparedObservation.cs"),Observation.Replace("{{NAMESPACE}}",Namespace,StringComparison.Ordinal));
            var observedBuild=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","--no-restore","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})observedBuild.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(observedBuild);
            var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
            host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="management_offline_probe";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=management_offline_probe;User ID=offline;Password=not-used-offline;Connection Timeout=1";
            var logs=Path.Combine(source,".local/management-preparation-http",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(logs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;
            await using(var app=await FreshHost.StartAsync(host,"/health"))
            {
                File.Copy(Path.Combine(hostDirectory,"prepared-observation.json"),Path.Combine(logs,"observation.json"));
                using var observation=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(hostDirectory,"prepared-observation.json")));var state=observation.RootElement;
                Assert.Equal(3,state.GetProperty("services").GetInt32());Assert.True(state.GetProperty("sharedAuthorization").GetBoolean());Assert.True(state.GetProperty("workbench").GetBoolean());Assert.True(state.GetProperty("mcp").GetBoolean());Assert.False(state.GetProperty("platformRealm").GetBoolean());
                foreach(var route in new[]{"/api/admin/auto/yz_manager/6031906822946816/page","/api/admin/auto/yz_role/1692532858529329154/page","/api/admin/auto/yz_admin_type/1696799309006348290/page","/api/admin/custom/index/init_admin"})
                    await FreshWebHttp.SendAsync(app.Client,System.Net.Http.HttpMethod.Post,route,new{},System.Net.HttpStatusCode.Unauthorized);
            }
            // 当前三个安全partial是手写区：再次生成不得覆盖用户扩展，公开Create仍不得消费这个带安全CRUD的目录。
            var hand=Path.Combine(output,"backend/generated/Services/YzRoleService.cs");await File.AppendAllTextAsync(hand,"\n// 本项目保留的手写扩展。\n");var bytes=await File.ReadAllBytesAsync(hand);
            var clean=GenerationMetadataPublisher.Publish(application).Metadata;var cleanModule=SecurityManagementModuleFactory.Compose(clean,module.Binding.ManagementAdminTypeId);new CSharpGenerator().Generate(cleanModule,Path.Combine(output,"backend/generated"),Namespace);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
            Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(SecurityManagementModuleFactory.RequirePublishedForGeneration(cleanModule),Path.Combine(root,"not-published"),Namespace,source));Assert.False(Directory.Exists(Path.Combine(root,"not-published")));
            Assert.Throws<InvalidOperationException>(()=>new SecurityManagementApplicationPreparer().Prepare(module,output,Namespace,source));Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>签发源被私有冻结；调用方后改原输入或取出的副本不能改变准备包基础目录。</summary>
    [Fact]
    public void Original_application_snapshot_is_readonly_by_copy_and_forged_modules_cannot_prepare()
    {
        var source=NonRedundantApplicationFixture.Root();var app=Application(source);var name=app.YzProjectName;var module=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id);app.YzProjectName="外部后改";
        var first=SecurityManagementModuleFactory.RequireApplicationForPreparation(module);Assert.Equal(name,first.YzProjectName);first.YzProjectName="副本后改";Assert.Equal(name,SecurityManagementModuleFactory.RequireApplicationForPreparation(module).YzProjectName);
        var forged=new SecurityManagementModelSet(module.Physical,module.Published,module.Binding,module.PendingAdapters);var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-forged");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new SecurityManagementApplicationPreparer().Prepare(forged,output,Namespace,source));Assert.False(Directory.Exists(output));
    }
    /// <summary>发布故障保留真实本轮stage与诊断，不能删掉内外嵌套的失败证据。</summary>
    [Fact]
    public async Task Publication_failure_preserves_owned_complete_preparation_and_diagnostic()
    {
        var source=NonRedundantApplicationFixture.Root();var app=Application(source);var module=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id);var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-publication");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(root,"prepared");
        try
        {
            var builder=new SecurityManagementApplicationPreparer((stage,destination)=>throw new PackagePublicationException(stage,destination,new IOException("准备发布故障")));
            var error=Assert.Throws<PackagePublicationException>(()=>builder.Prepare(module,output,Namespace,source));Assert.True(Directory.Exists(error.StageDirectory));Assert.NotNull(error.ContainingStageDirectory);Assert.True(File.Exists(error.DiagnosticPath));Assert.False(Directory.Exists(output));Assert.True(File.Exists(Path.Combine(error.StageDirectory,".metarill-management-preparation.json")));
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    private const string Observation="""
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Metarill.Runtime.Mcp;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Text.Json;
namespace {{NAMESPACE}}.Host;
public static partial class HostCustomization
{
    // 只观察实际生产Program已注册的对象，不改身份或业务行为。
    static partial void ConfigureServices(IServiceCollection services,IConfiguration configuration,IHostEnvironment environment)=>services.AddHostedService<PreparedManagementObservation>();
}
internal sealed class PreparedManagementObservation(IServiceProvider services,IHostEnvironment environment):IHostedService
{
    // 构造真正的三个生成Service检验协调器顺序，完整过程不调用SQL或认证接口。
    public Task StartAsync(CancellationToken token)
    {
        using var scope=services.CreateScope();var provider=scope.ServiceProvider;
        foreach(var name in new[]{"YzManager","YzRole","YzAdminType"})_=provider.GetRequiredService(typeof(PreparedManagementObservation).Assembly.GetType("{{NAMESPACE}}.Generated.Services."+name+"Service",true)!);
        var registry=provider.GetRequiredService<ISecurityResourceRegistry>();var catalog=provider.GetRequiredService<VerifiedPackageSecurityCatalog>().Clone();
        var value=new{services=3,sharedAuthorization=ReferenceEquals(provider.GetRequiredService<IRuntimeAuthorization>(),provider.GetRequiredService<ISecurityIdentityAccessor>()),mcp=provider.GetRequiredService<PublishedPageInvoker>() is not null,workbench=provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Any(action=>action.ControllerName=="RolePermissionWorkbench"),platformRealm=catalog.YzAdminTypes.Any(admin=>registry.IsPlatformRealm(catalog.YzProjectId,admin.Id))};
        File.WriteAllText(Path.Combine(environment.ContentRootPath,"prepared-observation.json"),JsonSerializer.Serialize(value));return Task.CompletedTask;
    }
    // 没有后台循环、连接或自有资源。
    public Task StopAsync(CancellationToken token)=>Task.CompletedTask;
}
""";
    /// <summary>原业务输入从本clone已提交示例读取，普通CI不携带任何数据库连接。</summary>
    private static MetadataCatalog Application(string source)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>重复准备比较全部相对路径与字节摘要，不忽略任何目录声明或宿主源码。</summary>
    private static string Snapshot(string root)=>string.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(root,path).Replace('\\','/')+":"+Hash(File.ReadAllBytes(path))).Order(StringComparer.Ordinal));
    /// <summary>核对受管初始内容摘要，不输出原配置。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    /// <summary>原源码只统一跨平台换行，不改DOM、脚本或样式。</summary>
    private static string Text(string file)=>File.ReadAllText(file).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
}
