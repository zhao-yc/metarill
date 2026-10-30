using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>当前生产Program+固定三管理页的离线真实进程；不连接Server-200，不解除管理发布或初始化门禁。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class TrustedManagementProgramTests(TrustedSecurityServiceFixture fixture):IClassFixture<TrustedSecurityServiceFixture>
{
    /// <summary>共享清单不可被夹具或调用者改变，新增基础接线必须同时进入生产包与管理宿主。</summary>
    [Fact]
    public void Production_template_and_project_catalogs_are_readonly_and_include_current_dependencies()
    {
        foreach(var name in new[]{"AgentIntegration.cs","McpIntegration.cs","SystemMaintenanceController.cs"})Assert.Contains(name,ApplicationPackager.FullHostSourceTemplates);
        foreach(var name in new[]{"Metarill.Agent","Metarill.Mcp"})Assert.Contains(name,ApplicationPackager.FullFoundationProjects);
        Assert.Throws<NotSupportedException>(()=>((IList<string>)ApplicationPackager.FullHostSourceTemplates).Add("Untrusted.cs"));Assert.Throws<NotSupportedException>(()=>((IList<string>)ApplicationPackager.FullFoundationProjects).Add("Metarill.Untrusted"));
    }
    /// <summary>编译实际Program并解析三张安全Service及工作台；不可达回环配置保证不以真实数据库支撑该证明。</summary>
    [Fact]
    public async Task Current_program_compiles_management_services_and_keeps_anonymous_requests_rejected()
    {
        var evidence=Path.Combine(fixture.DirectoryPath,"offline-program");Directory.CreateDirectory(evidence);
        var options=new DatabaseOptions{Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1;"};
        await using var program=await TrustedManagementProgram.StartAsync(fixture,options,evidence,observeServices:true);
        using var client=new HttpClient{BaseAddress=new Uri(program.Address),Timeout=TimeSpan.FromSeconds(5)};
        using(var health=await client.GetAsync("/health"))Assert.Equal(HttpStatusCode.OK,health.StatusCode);
        using(var rpc=await client.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=1,method="tools/list",@params=new{}}))Assert.Equal(HttpStatusCode.Unauthorized,rpc.StatusCode);
        foreach(var route in new[]{"/api/admin/sys/agent/grants","/api/admin/sys/yz_manager/getCurrentPermissions"})
        {using var denied=await client.GetAsync(route);Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
        using(var denied=await client.PostAsJsonAsync("/api/admin/auto/yz_manager-6031906822946816/page",new{}))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        foreach(var route in new[]{"/api/admin/custom/index/init_admin","/api/admin/sys/config_button/execute"})
        {using var denied=await client.PostAsJsonAsync(route,new{id="91"});Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
        var proof=JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(evidence,"program/services-observation.json")));
        Assert.Equal(3,proof.GetProperty("servicesResolved").GetInt32());Assert.True(proof.GetProperty("sharedAuthorization").GetBoolean());Assert.True(proof.GetProperty("mcpLinked").GetBoolean());Assert.True(proof.GetProperty("workbench").GetBoolean());Assert.False(proof.GetProperty("platformRealm").GetBoolean());Assert.Equal("AgentSecurityCurrentSessionPolicy",proof.GetProperty("agentCurrentPolicy").GetString());
        Assert.True(proof.GetProperty("initializerResolved").GetBoolean());Assert.Equal(1,proof.GetProperty("initializationRoutes").GetInt32());Assert.Equal(1,proof.GetProperty("centralRoutes").GetInt32());
        // 初始化规则已确认；同库HTTP/DI接线仍不代表完整管理发布、实库或跨业务库验收。
        var plan=new FrontendPackager().PrepareSecurityManagement(fixture.Models,Path.Combine(fixture.DirectoryPath,"generated/frontend"),Path.Combine(TrustedSecurityServiceFixture.SourceRoot,".."));Assert.Contains(plan.RequiredBackendAdapters,adapter=>adapter.Code=="initialize-account");Assert.Empty(plan.BackendGaps);
    }
}
