using System.Globalization;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed record AuthenticatedIdentity(RuntimeActor Actor,IReadOnlySet<string> Permissions,long SessionId,long? GrantId)
{
    /// <summary>真实身份后台不随开发工作区切换；超级角色仅在自己的后台有效。</summary>
    public string PrincipalAdminTypeId {get;init;}="";
    public string PrincipalProjectId {get;init;}="";
    public bool IsRealmSuper {get;init;}
    public IReadOnlySet<string> MenuIds {get;init;}=new HashSet<string>();
    public IReadOnlySet<string> ButtonIds {get;init;}=new HashSet<string>();
}

public sealed class AuthenticationService(SqlRowStore store,IIdGenerator ids,MetadataRegistry registry)
{
    /// <summary>网页登录校验账号状态，令牌仅保存摘要；原始密码与令牌不写日志。</summary>
    public async Task<(Dictionary<string,object?> User,string Token)> LoginAsync(string username,string password,CancellationToken cancellationToken=default)
    {
        var account=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken)).SingleOrDefault();
        if(account is null||!AccountActive(account)||!PasswordHasher.Verify(password,SqlRowStore.Text(account,"password")))
            throw new UnauthorizedAccessException("用户名或密码不正确。");
        var realm=await PrincipalRolePolicy.LoadAsync(store,registry,account,cancellationToken,includePermissions:false) ?? throw new UnauthorizedAccessException("用户名或密码不正确。");
        var token=PasswordHasher.Token();
        await store.InsertAsync("sys_session",new Dictionary<string,object?>
        {{"id",ids.NextId()},{"account_id",SqlRowStore.Id(account)},{"token_hash",PasswordHasher.Digest(token)},{"expires_at",DateTime.UtcNow.AddHours(8)},{"kind","web"},{"project_id",long.Parse(realm.ProjectId,CultureInfo.InvariantCulture)},{"insert_time",DateTime.UtcNow}},cancellationToken);
        return (PublicAccount(account),token);
    }
    public async Task<AuthenticatedIdentity?> AuthenticateAsync(string token,string? selectedProject,CancellationToken cancellationToken=default)
    {
        if(token.Length is < 32 or > 200)return null;
        var session=(await store.ReadAsync("sys_session",new Dictionary<string,object?>{{"token_hash",PasswordHasher.Digest(token)}},cancellationToken)).SingleOrDefault();
        if(session is null||SqlRowStore.Text(session,"kind") is not ("web" or "agent")||Convert.ToDateTime(session["expires_at"],CultureInfo.InvariantCulture)<=DateTime.UtcNow)return null;
        var account=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",SqlRowStore.Id(session,"account_id")}},cancellationToken)).SingleOrDefault();
        if(account is null||!AccountActive(account))return null;
        var realm=await PrincipalRolePolicy.LoadAsync(store,registry,account,cancellationToken);if(realm is null)return null;
        var project=string.IsNullOrWhiteSpace(selectedProject)?SqlRowStore.Text(session,"kind")=="agent"?SqlRowStore.Text(session,"project_id"):realm.ProjectId:selectedProject;
        if(!long.TryParse(project,NumberStyles.None,CultureInfo.InvariantCulture,out var projectId)||projectId<=0)return null;
        var registered=(await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",projectId}},cancellationToken)).SingleOrDefault();
        if(registered is null)return null;
        if(!realm.IsPlatformAdministrator&&project!=realm.ProjectId)return null;
        if(SqlRowStore.Text(session,"kind")=="agent"&&(!realm.IsPlatformRealm||project!=SqlRowStore.Text(session,"project_id")))return null;
        long? grantId=session.GetValueOrDefault("grant_id") is null?null:SqlRowStore.Id(session,"grant_id");
        if(SqlRowStore.Text(session,"kind")=="agent")
        {
            if(grantId is not {} grant)return null;
            var record=(await store.ReadAsync("sys_agent_grant",new Dictionary<string,object?>{{"id",grant}},cancellationToken)).SingleOrDefault();
            // 直接认证入口也核对grant与会话绑定，不依赖外层transport包装才能获得同等保护。
            if(record is null||SqlRowStore.Text(record,"status")!="approved"||SqlRowStore.Text(record,"client_id")!=Metarill.Runtime.Agent.AgentOAuthService.ClientId||
                SqlRowStore.Id(record,"account_id")!=SqlRowStore.Id(account)||SqlRowStore.Text(record,"project_id")!=SqlRowStore.Text(session,"project_id")||
                record.GetValueOrDefault("refresh_expires_at") is not {} expiry||Convert.ToDateTime(expiry,CultureInfo.InvariantCulture)<=DateTime.UtcNow)return null;
        }
        else if(grantId is not null)return null;
        var actor=new RuntimeActor(SqlRowStore.Id(account),SqlRowStore.Text(account,"name"),realm.IsPlatformAdministrator,project,realm.AdminTypeId);
        return new(actor,realm.Permissions,SqlRowStore.Id(session),grantId){PrincipalAdminTypeId=realm.AdminTypeId,PrincipalProjectId=realm.ProjectId,IsRealmSuper=realm.IsSuper,MenuIds=realm.MenuIds,ButtonIds=realm.ButtonIds};
    }
    /// <summary>有效期与登录起止时间在每次请求重新检查，停用立即生效。</summary>
    public static bool AccountActive(IReadOnlyDictionary<string,object?> account)
    {
        var now=DateTime.UtcNow;if(SqlRowStore.Text(account,"is_disabled")=="1")return false;
        foreach(var field in new[]{"validity_time","login_stop_time"})if(account.GetValueOrDefault(field) is{} end&&Convert.ToDateTime(end,CultureInfo.InvariantCulture)<=now)return false;
        return account.GetValueOrDefault("login_rise_time") is not{} start||Convert.ToDateTime(start,CultureInfo.InvariantCulture)<=now;
    }
    public Task LogoutAsync(long sessionId,CancellationToken cancellationToken=default)=>store.UpdateAsync("sys_session",sessionId,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}},cancellationToken);
    public static Dictionary<string,object?> PublicAccount(IReadOnlyDictionary<string,object?> row)=>new()
    {["id"]=SqlRowStore.Id(row),["name"]=SqlRowStore.Text(row,"name"),["realName"]=SqlRowStore.Text(row,"real_name"),["avatar"]=SqlRowStore.Text(row,"avatar"),["yzAdminTypeId"]=SqlRowStore.Text(row,"yz_admin_type_id")};
}
