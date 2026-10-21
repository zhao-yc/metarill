using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Api.AgentControllers;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>真实认证/授权与HTTP管线共用固定离线SQL传输；不冒充MySQL或浏览器验收。</summary>
public sealed class PrincipalRoleRealmTests
{
    /// <summary>执行真实认证类，SQL为离线夹具；范围角色变化在下次认证刷新，旧身份对象不被原地修改。</summary>
    [Fact]
    public async Task Exact_scope_decision_is_preserved_and_refreshed_with_actual_authentication()
    {
        using var f=new PrincipalRealmFixture(business:true,super:false);f.Role["is_scope"]=1;
        f.Add("yz_role_permission",("id",87L),("yz_role_id",10L),("config_page_id",long.Parse(f.DemoPage)),("permission_values","[\"is_query\"]"));
        var original=await f.Identity();Assert.Contains(f.DemoPage+":is_query",original.ExactDataManagePermissions);
        f.Role["is_scope"]=0;var unrestricted=await f.Identity();Assert.Empty(unrestricted.ExactDataManagePermissions);
        Assert.Contains(f.DemoPage+":is_query",original.ExactDataManagePermissions);
        f.Role["is_scope"]=1;Assert.Contains(f.DemoPage+":is_query",(await f.Identity()).ExactDataManagePermissions);
        f.Role["is_super"]=1;Assert.Empty((await f.Identity()).ExactDataManagePermissions);
    }
    /// <summary>普通角色仅拥有新增权不能取消查询的范围，改为查询权才解除，不增加页面原动作。</summary>
    [Fact]
    public async Task Mixed_roles_relax_only_the_effective_action_in_real_authentication()
    {
        using var f=new PrincipalRealmFixture(business:true,super:false);f.Role["is_scope"]=1;f.Account["yz_role_ids"]="[\"10\",\"11\"]";
        f.Add("yz_role",("id",11L),("yz_admin_type_id",long.Parse(f.DemoAdmin)),("is_scope",0),("is_super",0));
        f.Add("yz_role_permission",("id",87L),("yz_role_id",10L),("config_page_id",long.Parse(f.DemoPage)),("permission_values","[\"is_query\"]"));
        f.Add("yz_role_permission",("id",88L),("yz_role_id",11L),("config_page_id",long.Parse(f.DemoPage)),("permission_values","[\"is_new\"]"));
        var exact=await f.Identity();Assert.Contains(f.DemoPage+":is_query",exact.ExactDataManagePermissions);Assert.Contains(f.DemoPage+":is_new",exact.Permissions);
        f.Row("yz_role_permission",88)["permission_values"]="[\"is_query\"]";var mixed=await f.Identity();Assert.Empty(mixed.ExactDataManagePermissions);Assert.DoesNotContain(f.DemoPage+":is_new",mixed.Permissions);
    }
    [Fact]
    public async Task Business_super_is_not_a_platform_administrator()
    {
        using var f=new PrincipalRealmFixture(business:true);var identity=await f.Identity();
        Assert.False(identity.Actor.IsAdministrator);Assert.Equal(f.DemoAdmin,identity.Actor.AdminTypeId);
        Assert.True(identity.IsRealmSuper);Assert.Equal(f.DemoProject,identity.PrincipalProjectId);
    }
    [Fact]
    public async Task Foreign_admin_role_never_contributes_super_or_permissions()
    {
        using var f=new PrincipalRealmFixture();f.Role["yz_admin_type_id"]=long.Parse(f.DemoAdmin);
        f.Add("yz_role_permission",("id",90L),("yz_role_id",10L),("config_page_id",long.Parse(f.Registry.Platform.ConfigPages[0].Id)),("permission_values","[\"is_all\"]"));
        var identity=await f.Identity();Assert.False(identity.Actor.IsAdministrator);Assert.Empty(identity.Permissions);
    }
    [Fact]
    public async Task Login_default_project_comes_from_real_admin_not_account_compatibility_column()
    {
        using var f=new PrincipalRealmFixture();f.Account["yz_project_id"]=long.Parse(f.DemoProject);f.Role["yz_project_id"]=long.Parse(f.DemoProject);
        var login=await f.Authentication.LoginAsync("realm-test","离线Realm-Password879");
        var identity=await f.Authentication.AuthenticateAsync(login.Token,null);
        Assert.NotNull(identity);Assert.Equal(f.PlatformProject,identity.Actor.ProjectId);Assert.True(identity.Actor.IsAdministrator);
        Assert.Equal(f.PlatformProject,f.Provider.Rows["sys_session"].Last()["project_id"]!.ToString());
    }
    [Theory]
    [InlineData("admin-delete")] [InlineData("admin-missing")] [InlineData("project-delete")] [InlineData("project-missing")] [InlineData("invalid-admin")]
    public async Task Invalid_real_admin_chain_invalidates_current_login(string mode)
    {
        using var f=new PrincipalRealmFixture();
        switch(mode)
        {
            case "admin-delete": f.Row("yz_admin_type",long.Parse(f.PlatformAdmin))["delete_no"]=DateTime.UtcNow;break;
            case "admin-missing": f.Provider.Rows["yz_admin_type"].Clear();break;
            case "project-delete": f.Row("yz_project",long.Parse(f.PlatformProject))["delete_no"]=DateTime.UtcNow;break;
            case "project-missing": f.Provider.Rows["yz_project"].Clear();break;
            default:f.Account["yz_admin_type_id"]=null;break;
        }
        Assert.Null(await f.Authentication.AuthenticateAsync(f.Token,null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Authentication.LoginAsync("realm-test","离线Realm-Password879"));
    }
    [Theory]
    [InlineData("[]")] [InlineData("null")] [InlineData("{}")] [InlineData("[true]")] [InlineData("[10.0]")] [InlineData("[1e1]")] [InlineData("[\"00010\"]")] [InlineData("[\"10\",{}]")] [InlineData("[\"9223372036854775808\"]")]
    public async Task Malformed_or_empty_role_ids_never_invent_super(string value)
    {
        using var f=new PrincipalRealmFixture();f.Account["yz_role_ids"]=value;var identity=await f.Identity();
        Assert.False(identity.Actor.IsAdministrator);Assert.False(identity.IsRealmSuper);Assert.Empty(identity.Permissions);
    }
    [Fact]
    public async Task Role_soft_delete_and_super_revocation_apply_on_next_authentication()
    {
        using var f=new PrincipalRealmFixture();Assert.True((await f.Identity()).Actor.IsAdministrator);
        f.Role["is_super"]=0;Assert.False((await f.Identity()).Actor.IsAdministrator);
        f.Role["is_super"]=1;f.Role["delete_no"]=DateTime.UtcNow;Assert.False((await f.Identity()).Actor.IsAdministrator);
    }
    [Fact]
    public async Task Same_realm_normal_grants_remain_valid_but_foreign_resources_are_filtered()
    {
        using var f=new PrincipalRealmFixture(business:true,super:false);
        f.Add("yz_role_permission",("id",30L),("yz_role_id",10L),("config_page_id",long.Parse(f.DemoPage)),("permission_values","[\"is_query\",\"is_new\",\"is_update\",\"invented\"]"));
        f.Add("yz_role_permission",("id",31L),("yz_role_id",10L),("config_page_id",long.Parse(f.Registry.Platform.ConfigPages[0].Id)),("permission_values","[\"is_all\"]"));
        f.Add("yz_role_permission",("id",32L),("yz_role_id",10L),("config_menu_id",long.Parse(f.Registry.Platform.ConfigMenus.Last().Id)),("config_button_id",long.Parse(f.Registry.Platform.ConfigButtons[0].GetProperty("id").ToString())));
        var identity=await f.Identity();Assert.Equal(3,identity.Permissions.Count);Assert.All(identity.Permissions,value=>Assert.StartsWith(f.DemoPage+":",value));
        Assert.Empty(identity.MenuIds);Assert.Empty(identity.ButtonIds);
        var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=identity;var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Registry);
        foreach(var operation in new[]{"list","detail","create","update"})auth.RequirePagePermission(f.DemoPage,operation);
        Assert.Throws<UnauthorizedAccessException>(()=>auth.RequirePagePermission(f.DemoPage,"delete"));
        Assert.Throws<UnauthorizedAccessException>(()=>auth.RequirePagePermission(f.Registry.Platform.ConfigPages[0].Id,"list"));
    }
    [Fact]
    public async Task Platform_super_keeps_platform_permission_when_selecting_business_workspace()
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity(f.DemoProject);
        Assert.True(identity.Actor.IsAdministrator);Assert.Equal(f.DemoProject,identity.Actor.ProjectId);
        Assert.Equal(f.PlatformProject,identity.PrincipalProjectId);Assert.Equal(f.PlatformAdmin,identity.PrincipalAdminTypeId);
        f.Role["is_super"]=0;Assert.Null(await f.Authentication.AuthenticateAsync(f.Token,f.DemoProject));
    }
    [Fact]
    public async Task Real_http_preserves_global_super_bool_and_separates_business_super_page_permissions()
    {
        using var f=new PrincipalRealmFixture(business:true);await using var host=await RealmHttpHost.Start(f);
        using var response=await host.Send(HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var body=await response.Content.ReadFromJsonAsync<JsonElement>();var permissions=body.GetProperty("data");
        Assert.False(permissions.GetProperty("isSuper").GetBoolean());
        Assert.Contains(permissions.GetProperty("pagePermissions").GetProperty(f.DemoPage).EnumerateArray(),value=>value.GetString()=="is_all");
        Assert.Empty(permissions.GetProperty("pagePermissions").GetProperty(f.Registry.Platform.ConfigPages[0].Id).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.DemoPage+"?operation=delete")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.Registry.Platform.ConfigPages[0].Id+"?operation=update")).StatusCode);
        var device=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Post,"/api/admin/sys/agent/approve",new{userCode=device["user_code"],approve=true})).StatusCode);
        Assert.Equal("pending",f.Provider.Rows["sys_agent_grant"][0]["status"]);
    }
    [Fact]
    public async Task Real_http_platform_super_can_switch_workspace_and_approve_using_real_realm()
    {
        using var f=new PrincipalRealmFixture();await using var host=await RealmHttpHost.Start(f);var device=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);
        Assert.Equal(HttpStatusCode.OK,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.Registry.Platform.ConfigPages[0].Id+"?operation=update",project:f.DemoProject)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await host.Send(HttpMethod.Post,"/api/admin/sys/agent/approve",new{userCode=device["user_code"],approve=true},f.DemoProject)).StatusCode);
        var grant=f.Provider.Rows["sys_agent_grant"][0];Assert.Equal("approved",grant["status"]);Assert.Equal(f.DemoProject,grant["project_id"]!.ToString());
        f.Role["is_super"]=0;
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.Registry.Platform.ConfigPages[0].Id+"?operation=update",project:f.DemoProject)).StatusCode);
    }
    [Fact]
    public async Task Real_http_normal_business_grants_keep_only_declared_actions_and_revoke_immediately()
    {
        using var f=new PrincipalRealmFixture(business:true,super:false);f.Add("yz_role_permission",("id",30L),("yz_role_id",10L),("config_page_id",long.Parse(f.DemoPage)),("permission_values","[\"is_query\",\"is_new\"]"));
        await using var host=await RealmHttpHost.Start(f);
        foreach(var operation in new[]{"list","create"})Assert.Equal(HttpStatusCode.OK,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.DemoPage+"?operation="+operation)).StatusCode);
        foreach(var operation in new[]{"update","delete","export"})Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.DemoPage+"?operation="+operation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.DemoPage+"?operation=list",project:f.PlatformProject)).StatusCode);
        f.Row("yz_role_permission",30)["delete_no"]=DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Send(HttpMethod.Get,"/__principal-realm/page/"+f.DemoPage+"?operation=list")).StatusCode);
    }
    [Fact]
    public async Task Agent_exchange_refresh_and_access_reject_platform_account_moved_into_business_realm()
    {
        using var f=new PrincipalRealmFixture();var device=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);
        await f.OAuth.ApproveAsync((string)device["user_code"]!,true,await f.Identity(f.DemoProject));
        var pending=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);
        await f.OAuth.ApproveAsync((string)pending["user_code"]!,true,await f.Identity(f.DemoProject));
        var token=await f.OAuth.TokenAsync(AgentOAuthService.ClientId,AgentOAuthService.DeviceGrant,(string)device["device_code"]!,null);
        var access=(string)token["access_token"]!;var refresh=(string)token["refresh_token"]!;
        Assert.NotNull(await f.Authentication.AuthenticateAsync(access,null));
        f.Account["yz_admin_type_id"]=long.Parse(f.DemoAdmin);f.Role["yz_admin_type_id"]=long.Parse(f.DemoAdmin);
        Assert.Null(await f.Authentication.AuthenticateAsync(access,null));
        Assert.Equal("invalid_grant",(await Assert.ThrowsAsync<AgentOAuthException>(()=>f.OAuth.TokenAsync(AgentOAuthService.ClientId,"refresh_token",null,refresh))).Error);
        Assert.Equal("access_denied",(await Assert.ThrowsAsync<AgentOAuthException>(()=>f.OAuth.TokenAsync(AgentOAuthService.ClientId,AgentOAuthService.DeviceGrant,(string)pending["device_code"]!,null))).Error);
    }
    [Theory]
    [InlineData("account")] [InlineData("project")] [InlineData("client")] [InlineData("expired")] [InlineData("revoked")] [InlineData("deleted")] [InlineData("missing-grant")]
    public async Task Direct_agent_authentication_rejects_invalid_grant_session_binding(string mode)
    {
        using var f=new PrincipalRealmFixture();var device=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);
        await f.OAuth.ApproveAsync((string)device["user_code"]!,true,await f.Identity(f.DemoProject));
        var tokens=await f.OAuth.TokenAsync(AgentOAuthService.ClientId,AgentOAuthService.DeviceGrant,(string)device["device_code"]!,null);
        var access=(string)tokens["access_token"]!;Assert.NotNull(await f.Authentication.AuthenticateAsync(access,null));
        var grant=f.Provider.Rows["sys_agent_grant"][0];
        switch(mode)
        {
            case "account":grant["account_id"]=999L;break;
            case "project":grant["project_id"]=long.Parse(f.PlatformProject);break;
            case "client":grant["client_id"]="other-client";break;
            case "expired":grant["refresh_expires_at"]=DateTime.UtcNow.AddSeconds(-1);break;
            case "revoked":grant["status"]="revoked";break;
            case "deleted":grant["delete_no"]=DateTime.UtcNow;break;
            default:f.Provider.Rows["sys_session"].Last()["grant_id"]=null;break;
        }
        Assert.Null(await f.Authentication.AuthenticateAsync(access,null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new AgentAuthenticationService(f.Store,f.Authentication).AuthenticateAsync(access,"agent"));
    }
    [Fact]
    public async Task Same_project_other_admin_role_cannot_grant_super_and_web_session_cannot_carry_grant()
    {
        using var f=new PrincipalRealmFixture(business:true);f.Add("yz_admin_type",("id",44L),("yz_project_id",long.Parse(f.DemoProject)));f.Role["yz_admin_type_id"]=44L;
        Assert.False((await f.Identity()).IsRealmSuper);
        f.WebSession["grant_id"]=123L;Assert.Null(await f.Authentication.AuthenticateAsync(f.Token,null));
    }
    [Fact]
    public async Task Unpublished_admin_never_turns_real_super_into_platform_administrator()
    {
        using var f=new PrincipalRealmFixture();f.Add("yz_admin_type",("id",44L),("yz_project_id",long.Parse(f.PlatformProject)));f.Account["yz_admin_type_id"]=44L;f.Role["yz_admin_type_id"]=44L;
        var identity=await f.Identity();Assert.True(identity.IsRealmSuper);Assert.False(identity.Actor.IsAdministrator);Assert.Empty(identity.Permissions);
    }
    [Fact]
    public async Task Agent_qualification_does_not_read_permission_table_per_resource()
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity();var device=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);f.Provider.Commands.Clear();
        await f.OAuth.ApproveAsync((string)device["user_code"]!,true,identity);
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.Contains("FROM "+(char)96+"yz_role_permission"+(char)96,StringComparison.Ordinal));
        Assert.Single(f.Provider.Commands,sql=>sql.Contains("FROM "+(char)96+"yz_role"+(char)96,StringComparison.Ordinal));
    }
    [Fact]
    public async Task Agent_approval_reloads_roles_even_if_caller_holds_old_identity_snapshot()
    {
        using var f=new PrincipalRealmFixture();var identity=await f.Identity();var device=await f.OAuth.StartDeviceAsync(AgentOAuthService.ClientId,null);
        f.Role["delete_no"]=DateTime.UtcNow;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.OAuth.ApproveAsync((string)device["user_code"]!,true,identity));
        Assert.Equal("pending",f.Provider.Rows["sys_agent_grant"][0]["status"]);
    }
}

/// <summary>只在测试程序集加载的真实ASP.NET管线，认证、原权限响应与Agent审批使用产品实现。</summary>
internal sealed class RealmHttpHost(WebApplication application,HttpClient client,PrincipalRealmFixture fixture):IAsyncDisposable
{
    internal static async Task<RealmHttpHost> Start(PrincipalRealmFixture fixture)
    {
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();builder.Logging.ClearProviders();
        builder.Services.AddSingleton(fixture.Store);builder.Services.AddSingleton(fixture.Registry);builder.Services.AddSingleton(fixture.Authentication);builder.Services.AddSingleton(fixture.OAuth);builder.Services.AddSingleton(fixture.Ids);
        builder.Services.AddSingleton<AgentAuthenticationService>();builder.Services.AddSingleton<AgentPublicRateLimiter>();builder.Services.AddSingleton<GeneratedCapabilityIndex>();builder.Services.AddScoped<AgentCapabilityCatalog>();
        builder.Services.AddSingleton(new AgentTaskStore(fixture.Store,fixture.Ids,new()));builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<HttpRuntimeAuthorization>();builder.Services.AddScoped<IRuntimeAuthorization>(sp=>sp.GetRequiredService<HttpRuntimeAuthorization>());
        builder.Services.AddScoped<MetadataRepository>();builder.Services.AddScoped<MenuWorkbenchBusiness>();
        builder.Services.AddControllers().AddApplicationPart(typeof(AgentManagementController).Assembly).AddApplicationPart(typeof(PrincipalRealmProbeController).Assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));
        var app=builder.Build();
        // 只在测试宿主用401观测真实拒绝，不改变产品R.code/HTTP习惯。
        app.Use(async(context,next)=>{try{await next();}catch(UnauthorizedAccessException){context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(R<object>.Error("无权执行。"));}});
        app.UseRouting();app.UseMiddleware<AgentAuthenticationMiddleware>();app.MapControllers();await app.StartAsync();return new(app,app.GetTestClient(),fixture);
    }
    internal async Task<HttpResponseMessage> Send(HttpMethod method,string path,object? body=null,string? project=null)
    {
        using var request=new HttpRequestMessage(method,path);request.Headers.Add("loginStateId",fixture.Token);
        if(project is not null)request.Headers.Add("sysParams",Uri.EscapeDataString(JsonSerializer.Serialize(new{yzProjectId=project})));
        if(body is not null)request.Content=JsonContent.Create(body);return await client.SendAsync(request);
    }
    public async ValueTask DisposeAsync(){client.Dispose();await application.DisposeAsync();}
}

/// <summary>仅为测试触发产品页权限决策，不属于产品路由，不伪造Actor。</summary>
[ApiController,Route("__principal-realm")]
public sealed class PrincipalRealmProbeController(HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpGet("page/{page}")]
    public R<object> Page(string page,[FromQuery]string operation){authorization.RequirePagePermission(page,operation);return R<object>.Success();}
}
