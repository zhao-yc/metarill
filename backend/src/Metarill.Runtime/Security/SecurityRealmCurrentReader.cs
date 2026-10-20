using System.Data.Common;
using System.Globalization;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>已经按项目、后台稳定预锁的真实记录；工作区项目仅是锁序兼容项，不是授权。</summary>
internal sealed record SecurityRealmLocks(DbTransaction Transaction,IReadOnlyDictionary<long,Dictionary<string,object?>> Projects,
    IReadOnlyDictionary<long,Dictionary<string,object?>> Admins);
internal sealed record SecurityCurrentPrincipal(AuthenticatedIdentity Identity,Dictionary<string,object?> Account,MetadataCatalog Catalog);

/// <summary>锁后身份复核完全使用当前读；不能在Repeatable Read下调用普通认证载入旧快照。</summary>
public sealed class SecurityRealmCurrentReader(SqlRowStore store,MetadataRegistry registry,MetadataRepository metadata)
{
    /// <summary>Agent授权先于会话，之后按操作者、角色、页面授权和固定资源目录的顺序读取。</summary>
    internal async Task<SecurityCurrentPrincipal> ReadAsync(AuthenticatedIdentity request,SecurityRealmLocks locks,CancellationToken token)
    {
        if(!ReferenceEquals(locks.Transaction,store.Session.CurrentTransaction)||locks.Transaction.Connection is null)
            throw new InvalidOperationException("安全当前读必须使用聚合预锁的同一连接及事务。");
        var actor=request.Actor;
        if(actor.AccountId<=0||request.SessionId<=0||!Positive(request.PrincipalAdminTypeId,out var adminId)||!Positive(request.PrincipalProjectId,out var projectId)||
            !Positive(actor.ProjectId,out var workspace)||!locks.Admins.ContainsKey(adminId)||!locks.Projects.ContainsKey(projectId)||!locks.Projects.ContainsKey(workspace))throw InvalidIdentity();
        // OAuth撤销/刷新原本先锁sys_agent_grant再锁sys_session，此处必须沿用，不能反序补锁。
        Dictionary<string,object?>? grant=null;
        if(request.GrantId is{} grantId)
        {
            if(grantId<=0)throw InvalidIdentity();
            grant=(await store.ReadForUpdateAsync("sys_agent_grant",new Dictionary<string,object?>{{"id",grantId}},token)).SingleOrDefault();
        }
        var session=(await store.ReadForUpdateAsync("sys_session",new Dictionary<string,object?>{{"id",request.SessionId}},token)).SingleOrDefault();
        if(session is null||SqlRowStore.Id(session,"account_id")!=actor.AccountId||!Future(session,"expires_at"))throw InvalidIdentity();
        var kind=SqlRowStore.Text(session,"kind");
        if(kind=="agent")
        {
            if(grant is null||request.GrantId!=OptionalId(session,"grant_id")||SqlRowStore.Text(session,"project_id")!=actor.ProjectId||
                SqlRowStore.Text(grant,"status")!="approved"||SqlRowStore.Text(grant,"client_id")!=Agent.AgentOAuthService.ClientId||
                SqlRowStore.Id(grant,"account_id")!=actor.AccountId||SqlRowStore.Text(grant,"project_id")!=actor.ProjectId||!Future(grant,"refresh_expires_at"))throw InvalidIdentity();
        }
        else if(kind!="web"||request.GrantId is not null||OptionalId(session,"grant_id") is not null)throw InvalidIdentity();
        var account=(await store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"id",actor.AccountId}},token)).SingleOrDefault();
        if(account is null||!AuthenticationService.AccountActive(account)||SqlRowStore.Text(account,"yz_admin_type_id")!=request.PrincipalAdminTypeId||actor.AdminTypeId!=request.PrincipalAdminTypeId)throw InvalidIdentity();
        var admin=locks.Admins[adminId];
        if(SqlRowStore.Text(admin,"yz_project_id")!=request.PrincipalProjectId)throw InvalidIdentity();
        var ids=PrincipalRolePolicy.Identifiers(SqlRowStore.Text(account,"yz_role_ids")).Select(value=>long.Parse(value,CultureInfo.InvariantCulture)).Order().ToArray();
        var roles=await store.ReadForUpdateByIdsAsync("yz_role","id",ids,new Dictionary<string,object?>{{"yz_admin_type_id",adminId}},token);
        var validIds=roles.Select(row=>SqlRowStore.Id(row)).Distinct().Order().ToArray();
        var grants=await store.ReadForUpdateByIdsAsync("yz_role_permission","yz_role_id",validIds,cancellationToken:token);
        var catalog=await metadata.LoadRealmForUpdateAsync(projectId,adminId,token);
        var realm=PrincipalRolePolicy.Evaluate(registry,account,admin,projectId,roles,grants,PlatformSecurityResourceProjection.Create(catalog));
        // 不能把认证时的超级标志继续交给文件/手写钩子；发生身份语义变化则刷新整个请求。
        if(realm.IsPlatformAdministrator!=actor.IsAdministrator||realm.IsSuper!=request.IsRealmSuper||kind=="agent"&&!realm.IsPlatformRealm)throw InvalidIdentity();
        var identity=request with{Permissions=realm.Permissions,MenuIds=realm.MenuIds,ButtonIds=realm.ButtonIds};
        return new(identity,account,catalog);
    }

    /// <summary>失效或损坏的时间直接拒绝，不返回连接、列值或内部转换异常。</summary>
    private static bool Future(IReadOnlyDictionary<string,object?> row,string name)
    {
        if(row.GetValueOrDefault(name) is not{} value)return false;
        try{return Convert.ToDateTime(value,CultureInfo.InvariantCulture)>DateTime.UtcNow;}
        catch(Exception error)when(error is FormatException or InvalidCastException or ArgumentException){return false;}
    }
    private static long? OptionalId(IReadOnlyDictionary<string,object?> row,string name)=>row.GetValueOrDefault(name) is null?null:SqlRowStore.Id(row,name);
    private static bool Positive(string value,out long id)=>long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0&&value==id.ToString(CultureInfo.InvariantCulture);
    private static UnauthorizedAccessException InvalidIdentity()=>new("当前会话、角色或身份后台已变化，请刷新或重新登录。");
}
