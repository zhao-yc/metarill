using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Metarill.Api.Controllers;

[ApiController,Route("api/admin/sys/yz_manager")]
public sealed class IdentityController(AuthenticationService authentication,HttpRuntimeAuthorization authorization,MenuWorkbenchBusiness menus,SqlRowStore store,MetadataRegistry registry):ControllerBase
{
    /// <summary>兼容既有表单协议；不返回密码或会话摘要。</summary>
    [HttpPost("login"),EnableRateLimiting("login")]
    public async Task<R<Dictionary<string,object?>>> Login([FromForm]string username,[FromForm]string password,CancellationToken cancellationToken)
    {
        var result=await authentication.LoginAsync(username,password,cancellationToken);
        var response=R<Dictionary<string,object?>>.Success(result.User);response.Map["loginStateId"]=result.Token;return response;
    }
    [HttpGet("cap-config")]public R<object> CapConfig()=>R<object>.Success(new{enabled=false,apiEndpoint=""});
    [HttpPost("exit_login")]public async Task<R<object>> Logout(CancellationToken cancellationToken){await authentication.LogoutAsync(authorization.RequireIdentity().SessionId,cancellationToken);return R<object>.Success();}
    [HttpGet("getMenu")]public async Task<object> GetMenu(CancellationToken cancellationToken)
    {
        var actor=authorization.RequireActor();return new{code=200,menu=await menus.NavigationAsync(actor.ProjectId,cancellationToken),permissions=Permissions()};
    }
    [HttpGet("getCurrentPermissions")]public R<object> CurrentPermissions()=>R<object>.Success(Permissions());
    private object Permissions()
    {
        var identity=authorization.RequireIdentity();
        var pages=registry.Platform.ConfigPages.Concat(registry.Demo.ConfigPages).ToDictionary(p=>p.Id,p=>(object)(identity.Actor.IsAdministrator?new[]{"is_query","is_new","is_update","is_delete","is_all","is_export"}:identity.Permissions.Where(v=>v.StartsWith(p.Id+":",StringComparison.Ordinal)).Select(v=>v[(p.Id.Length+1)..]).ToArray()));
        return new{isSuper=identity.Actor.IsAdministrator,pagePermissions=pages,buttonPermissions=identity.Actor.IsAdministrator?registry.Platform.ConfigButtons.Select(b=>b.TryGetProperty("id",out var id)?id.ToString():"").ToArray():identity.ButtonIds.ToArray()};
    }
    [HttpGet("edit_password")]public async Task<R<object>> ChangePassword([FromQuery]string oldPwd,[FromQuery]string pwd,CancellationToken cancellationToken)
    {
        var actor=authorization.RequireActor();var row=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",actor.AccountId}},cancellationToken)).Single();
        if(!PasswordHasher.Verify(oldPwd,SqlRowStore.Text(row,"password")))throw new UnauthorizedAccessException("旧密码不正确。");
        await store.UpdateAsync("yz_manager",actor.AccountId,new Dictionary<string,object?>{{"password",PasswordHasher.Hash(pwd)}},cancellationToken);
        foreach(var session in await store.ReadAsync("sys_session",new Dictionary<string,object?>{{"account_id",actor.AccountId}},cancellationToken))await authentication.LogoutAsync(SqlRowStore.Id(session),cancellationToken);
        return R<object>.Success();
    }
}
[ApiController,Route("api/admin/sys/index")]
public sealed class WorkspaceController(SqlRowStore store,HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpGet("findNameIdYzManagerDevices")]public async Task<R<object>> Devices(CancellationToken cancellationToken)
    {
        var actor=authorization.RequireActor();var rows=await store.ReadAsync("yz_manager_devices",new Dictionary<string,object?>{{"yz_manager_id",actor.AccountId}},cancellationToken);
        return R<object>.Success(rows.Select(r=>new{value=SqlRowStore.Text(r,"id"),text=SqlRowStore.Text(r,"name")}));
    }
    [HttpGet("findNameIdYzProject")]public async Task<R<object>> Projects([FromQuery]string? yzManagerDevicesId,CancellationToken cancellationToken)
    {
        var actor=authorization.RequireActor();
        if(!string.IsNullOrEmpty(yzManagerDevicesId)&&(await store.ReadAsync("yz_manager_devices",new Dictionary<string,object?>{{"id",long.Parse(yzManagerDevicesId)},{"yz_manager_id",actor.AccountId}},cancellationToken)).Count==0)throw new UnauthorizedAccessException("设备不属于当前账号。");
        var projects=await store.ReadAsync("yz_project",cancellationToken:cancellationToken);
        return R<object>.Success(projects.Where(p=>actor.IsAdministrator||SqlRowStore.Text(p,"id")==actor.ProjectId).Select(r=>new{value=SqlRowStore.Text(r,"id"),text=SqlRowStore.Text(r,"name")}));
    }
}
