using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Agent;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真正完整包与原页面、原更新器及独立进程验证；不连接真实数据库，不代替正式OAuth/CLI验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class StandaloneAgentPackageTests
{
    /// <summary>两张固定表及索引保持副本隔离；业务不能借保留身份覆盖结构或开放页面。</summary>
    [Fact]
    public void Fixed_agent_model_composes_without_mutating_application_or_allowing_business_overrides()
    {
        var app=GeneratorTests.Catalog();var before=JsonSerializer.Serialize(app);var agent=AgentFoundationCatalog.ForApplication(app);
        Assert.Equal(new[]{"sys_agent_grant","sys_agent_task"},agent.ConfigTables.Select(table=>table.TableName));
        Assert.Equal(44,agent.ConfigColumns.Count);Assert.Equal(3,agent.ConfigIndexes.Count);Assert.Empty(agent.ConfigPages);
        var combined=MetadataCatalogComposition.MergePhysical(app,agent);Assert.Equal(3,combined.ConfigTables.Count);Assert.Equal(before,JsonSerializer.Serialize(app));
        agent.ConfigColumns[0].ColumnName="caller_changed";Assert.DoesNotContain(AgentFoundationCatalog.ForApplication(app).ConfigColumns,column=>column.ColumnName=="caller_changed");
        app.ConfigTables.Add(new(){Id="999",TableName="sys_agent_grant"});
        Assert.Throws<InvalidOperationException>(()=>MetadataCatalogComposition.MergePhysical(app,AgentFoundationCatalog.ForApplication(GeneratorTests.Catalog())));
    }

    /// <summary>真实源码包独立编译、原两页逐字复用、更新保护，以及缺配置/开发模式真实HTTP拒绝。</summary>
    [Fact]
    public async Task Full_package_has_real_agent_module_original_pages_and_safe_host_pipeline()
    {
        var source=NonRedundantApplicationFixture.Root();var metadata=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-agent-full-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try
        {
            new ApplicationPackager().Create(metadata,output,"Example.AgentFull",source);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Agent/Metarill.Agent.csproj")));
            foreach(var name in new[]{"agent_authorize","agent_grants"})Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(source,"frontend/metarill_uni_admin/pages/custom/"+name+".vue")),await File.ReadAllBytesAsync(Path.Combine(output,"frontend/pages/custom/"+name+".vue")));
            var app=JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(output,"frontend/common/application.generated.json")));Assert.True(app.GetProperty("capabilities").GetProperty("agentGrants").GetBoolean());
            var route=JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages.json")));foreach(var name in new[]{"agent_authorize","agent_grants"})Assert.Contains(route.GetProperty("pages").EnumerateArray(),page=>page.GetProperty("path").GetString()=="pages/custom/"+name);
            var hand=Directory.GetFiles(Path.Combine(output,"backend/generated/Services"),"*.cs").First(path=>!path.EndsWith(".g.cs",StringComparison.Ordinal));await File.AppendAllTextAsync(hand,"\n// Agent更新保护手写规则。\n");var handBytes=await File.ReadAllBytesAsync(hand);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(metadata,candidate,"Example.AgentFull",source);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(handBytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            // 仅测试包追加宿主扩展：实际服务注册、真实MVC目录检查，不覆盖任何产品认证或业务实现。
            await File.WriteAllTextAsync(Path.Combine(output,"backend/Application.Host/AgentProof.cs"),Proof);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var host=Path.Combine(output,"backend/Application.Host");
            foreach(var mode in new[]{"missing","development","configured"})
            {
                await using var server=await Server.Start(host,mode);
                foreach(var path in new[]{"/api/admin/sys/agent/grants","/api/admin/sys/agent/capabilities","/api/admin/sys/yz_manager/getCurrentPermissions"})
                {using var response=await server.Client.GetAsync(path);Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);}
                using var oauth=await server.Client.PostAsync("/api/agent/oauth/device",new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id","invalid"}}));
                Assert.Equal(mode=="configured"?HttpStatusCode.BadRequest:HttpStatusCode.Unauthorized,oauth.StatusCode);
                if(mode=="configured")
                {
                    var receipt=JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(host,"agent-proof.json")));
                    Assert.Equal("Metarill.Runtime.Security.AgentSecurityCurrentSessionPolicy",receipt.GetProperty("currentPolicy").GetString());
                    Assert.Equal(metadata.YzProjectId,receipt.GetProperty("project").GetString());Assert.True(receipt.GetProperty("entries").GetInt32()>0);
                    Assert.Equal("Metarill.Runtime.Agent.AgentAuthenticationService",receipt.GetProperty("authentication").GetString());
                }
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    private const string Proof="""
using System.Reflection;
using System.Text.Json;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
namespace Example.AgentFull.Host;
public static partial class HostCustomization
{
    // 仅隔离测试扩展；不注册替身认证、数据库或控制器。
    static partial void ConfigureServices(IServiceCollection services,IConfiguration configuration,IHostEnvironment environment)
    { if(configuration["AGENT_TEST_PROOF"]=="true")services.AddHostedService<AgentProof>(); }
}
internal sealed class AgentProof(IServiceProvider services,IHostEnvironment environment):IHostedService
{
    // 只记录实际DI类型和真实MVC能力数，不建立连接或替换产品实现。
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope=services.CreateScope();var provider=scope.ServiceProvider;
        var resources=provider.GetRequiredService<IAgentHostResources>();var index=provider.GetRequiredService<GeneratedCapabilityIndex>();
        var entries=((System.Collections.IEnumerable)typeof(GeneratedCapabilityIndex).GetMethod("Entries",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(index,null)!).Cast<object>().Count();
        var receipt=new{project=resources.AgentCatalog.PendingProjectId,entries,currentPolicy=provider.GetRequiredService<ISecurityCurrentSessionPolicy>().GetType().FullName,authentication=provider.GetRequiredService<AgentAuthenticationService>().GetType().FullName};
        File.WriteAllText(Path.Combine(environment.ContentRootPath,"agent-proof.json"),JsonSerializer.Serialize(receipt));return Task.CompletedTask;
    }
    // 探针没有后台循环或外部资源。
    public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
}
""";

    /// <summary>仅管理自身隔离宿主，所有进程与两条管道在finally停止和消费，不重试业务写入。</summary>
    private sealed class Server(Process process,HttpClient client,Task<string> stdout,Task<string> stderr):IAsyncDisposable
    {
        internal HttpClient Client=>client;
        /// <summary>仅使用回环临时端口；配置模式的不可达DB仅验证构造，从不执行业务查询。</summary>
        internal static async Task<Server> Start(string host,string mode)
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",host);info.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));info.ArgumentList.Add("--urls");info.ArgumentList.Add("http://127.0.0.1:"+port);
            info.Environment["ASPNETCORE_ENVIRONMENT"]=mode=="missing"?"Production":"Development";info.Environment["DOTNET_ENVIRONMENT"]=info.Environment["ASPNETCORE_ENVIRONMENT"];
            info.Environment["Authentication__Mode"]=mode=="development"?"Development":"Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]=mode=="development"?"true":"false";
            info.Environment["Authentication__DevelopmentBootstrap__Username"]="agent-package-test";info.Environment["Authentication__DevelopmentBootstrap__Password"]=Guid.NewGuid().ToString("N");
            info.Environment["Database__ExpectedDatabase"]=mode=="configured"?"offline":"";info.Environment["Database__ConnectionString"]=mode=="configured"?"Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1":"";
            info.Environment["AGENT_TEST_PROOF"]=mode=="configured"?"true":"false";info.Environment["Logging__LogLevel__Default"]="Warning";
            var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var result=new Server(process,new HttpClient{BaseAddress=new("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(5)},stdout,stderr);
            try{await StandaloneCorsHttpProbe.EnsureReadyAsync(result.Client,process,stdout,stderr,TimeSpan.FromSeconds(30));return result;}catch{await result.DisposeAsync();throw;}
        }
        /// <summary>只终止此实例拥有的子进程并等待两条日志管道结束。</summary>
        public async ValueTask DisposeAsync(){client.Dispose();if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await stdout;await stderr;process.Dispose();}
    }
}
