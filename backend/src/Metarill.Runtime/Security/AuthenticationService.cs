using System.Globalization;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed record AuthenticatedIdentity(RuntimeActor Actor,IReadOnlySet<string> Permissions,long SessionId,long? GrantId)
{
    public IReadOnlySet<string> MenuIds {get;init;}=new HashSet<string>();
    public IReadOnlySet<string> ButtonIds {get;init;}=new HashSet<string>();
}

public sealed class AuthenticationService(SqlRowStore store,MetadataRegistry registry,IIdGenerator ids)
{
    /// <summary>网页登录校验账号状态，令牌仅保存摘要；原始密码与令牌不写日志。</summary>
    public async Task<(Dictionary<string,object?> User,string Token)> LoginAsync(string username,string password,CancellationToken cancellationToken=default)
    {
        var account=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken)).SingleOrDefault();
        if(account is null||!AccountActive(account)||!PasswordHasher.Verify(password,SqlRowStore.Text(account,"password")))
            throw new UnauthorizedAccessException("用户名或密码不正确。");
        var token=PasswordHasher.Token();
        await store.InsertAsync("sys_session",new Dictionary<string,object?>
        {{"id",ids.NextId()},{"account_id",SqlRowStore.Id(account)},{"token_hash",PasswordHasher.Digest(token)},{"expires_at",DateTime.UtcNow.AddHours(8)},{"kind","web"},{"project_id",long.Parse(registry.Platform.YzProjectId)},{"insert_time",DateTime.UtcNow}},cancellationToken);
        return (PublicAccount(account),token);
    }
    public async Task<AuthenticatedIdentity?> AuthenticateAsync(string token,string? selectedProject,CancellationToken cancellationToken=default)
    {
        if(token.Length is < 32 or > 200)return null;
        var session=(await store.ReadAsync("sys_session",new Dictionary<string,object?>{{"token_hash",PasswordHasher.Digest(token)}},cancellationToken)).SingleOrDefault();
        if(session is null||SqlRowStore.Text(session,"kind") is not ("web" or "agent")||Convert.ToDateTime(session["expires_at"],CultureInfo.InvariantCulture)<=DateTime.UtcNow)return null;
        var account=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",SqlRowStore.Id(session,"account_id")}},cancellationToken)).SingleOrDefault();
        if(account is null||SqlRowStore.Text(account,"is_disabled")=="1")return null;
        var roles=await store.ReadAsync("yz_role",cancellationToken:cancellationToken);
        var roleIds=ParseIds(SqlRowStore.Text(account,"yz_role_ids"));
        var owned=roles.Where(r=>roleIds.Contains(SqlRowStore.Text(r,"id"))).ToArray();
        var administrator=owned.Any(r=>SqlRowStore.Text(r,"is_super")=="1");
        var permissions=new HashSet<string>(StringComparer.Ordinal);var menuIds=new HashSet<string>(StringComparer.Ordinal);var buttonIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in await store.ReadAsync("yz_role_permission",cancellationToken:cancellationToken))
        {
            if(!roleIds.Contains(SqlRowStore.Text(row,"yz_role_id")))continue;
            var menuId=SqlRowStore.Text(row,"config_menu_id");if(menuId.Length>0)menuIds.Add(menuId);
            var buttonId=SqlRowStore.Text(row,"config_button_id");if(buttonId.Length>0)buttonIds.Add(buttonId);
            var pageId=SqlRowStore.Text(row,"config_page_id");
            if(string.IsNullOrEmpty(pageId)) continue; // 菜单显示授权不等价于页面操作授权。
            foreach(var operation in ParseIds(SqlRowStore.Text(row,"permission_values")))permissions.Add(pageId+":"+operation);
        }
        var project=string.IsNullOrWhiteSpace(selectedProject)?registry.Platform.YzProjectId:selectedProject;
        if(!long.TryParse(project,NumberStyles.None,CultureInfo.InvariantCulture,out var projectId)||projectId<=0)return null;
        var registered=(await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",projectId}},cancellationToken)).SingleOrDefault();
        if(registered is null)return null;
        if(!administrator&&project!=SqlRowStore.Text(account,"yz_project_id"))return null;
        if(SqlRowStore.Text(session,"kind")=="agent"&&project!=SqlRowStore.Text(session,"project_id"))return null;
        long? grantId=session.GetValueOrDefault("grant_id") is null?null:SqlRowStore.Id(session,"grant_id");
        if(grantId is { } grant)
        {
            var record=(await store.ReadAsync("sys_agent_grant",new Dictionary<string,object?>{{"id",grant}},cancellationToken)).SingleOrDefault();
            if(record is null||SqlRowStore.Text(record,"status")!="approved")return null;
        }
        var actor=new RuntimeActor(SqlRowStore.Id(account),SqlRowStore.Text(account,"name"),administrator,project,SqlRowStore.Text(account,"yz_admin_type_id"));
        return new(actor,permissions,SqlRowStore.Id(session),grantId){MenuIds=menuIds,ButtonIds=buttonIds};
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
    private static HashSet<string> ParseIds(string value)
    {
        try{return JsonSerializer.Deserialize<List<JsonElement>>(value)?.Select(x=>x.ToString()).ToHashSet()??[];}catch(JsonException){return [];}
    }
}
