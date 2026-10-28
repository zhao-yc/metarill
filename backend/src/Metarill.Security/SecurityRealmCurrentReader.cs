using System.Data.Common;
using System.Globalization;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>已经按项目、后台稳定预锁的真实记录；工作区项目仅是锁序兼容项，不是授权。</summary>
internal sealed record SecurityRealmLocks(DbTransaction Transaction,IReadOnlyDictionary<long,Dictionary<string,object?>> Projects,
    IReadOnlyDictionary<long,Dictionary<string,object?>> Admins);
internal sealed record SecurityCurrentPrincipal(AuthenticatedIdentity Identity,Dictionary<string,object?> Account,SecurityResourceCatalog Catalog);

/// <summary>锁后身份复核完全使用当前读；不能在Repeatable Read下调用普通认证载入旧快照。</summary>
public sealed class SecurityRealmCurrentReader
{
    private readonly SqlRowStore store;
    private readonly ISecurityResourceRegistry registry;
    private readonly ISecurityCurrentResources resources;
    private readonly ISecurityCurrentSessionPolicy sessionPolicy;
    /// <summary>所有宿主适配均为必要依赖；缺失时立即失败，不能在首次当前读后回退发布目录。</summary>
    public SecurityRealmCurrentReader(SqlRowStore store,ISecurityResourceRegistry registry,ISecurityCurrentResources resources,ISecurityCurrentSessionPolicy sessionPolicy)
    {
        this.store=store??throw new ArgumentNullException(nameof(store));this.registry=registry??throw new ArgumentNullException(nameof(registry));
        this.resources=resources??throw new ArgumentNullException(nameof(resources));this.sessionPolicy=sessionPolicy??throw new ArgumentNullException(nameof(sessionPolicy));
    }

    /// <summary>后台引用检查复用同一适配，不给共享协调器增加config_*表访问或新的连接。</summary>
    internal Task<bool> HasAdminMenuReferencesForUpdateAsync(DbTransaction transaction,long adminTypeId,CancellationToken token)
    {
        if(!ReferenceEquals(transaction,store.Session.CurrentTransaction)||transaction.Connection is null)
            throw new InvalidOperationException("安全菜单引用必须使用同一当前连接及事务。");
        return resources.HasAdminMenuReferencesForUpdateAsync(store.Session,transaction,adminTypeId,token);
    }
    /// <summary>只读取已经按固定顺序预锁的来源后台目录，不在主体/记录锁后补取新的项目或后台。</summary>
    internal async Task<SecurityResourceCatalog> ReadDeclaredResourcesAsync(SecurityRealmLocks locks,long project,long admin,CancellationToken token)
    {
        if(!ReferenceEquals(locks.Transaction,store.Session.CurrentTransaction)||locks.Transaction.Connection is null
            ||!locks.Projects.ContainsKey(project)||!locks.Admins.TryGetValue(admin,out var row)||SqlRowStore.Text(row,"yz_project_id")!=project.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("按钮来源目录必须使用本事务已预锁的项目及后台。");
        return await resources.ReadForUpdateAsync(store.Session,locks.Transaction,project,admin,token)??throw new InvalidOperationException("按钮来源当前目录不可用，不能回退发布快照。");
    }
    /// <summary>Agent授权先于会话，之后按操作者、角色、页面授权和固定资源目录的顺序读取。</summary>
    internal async Task<SecurityCurrentPrincipal> ReadAsync(AuthenticatedIdentity request,SecurityRealmLocks locks,CancellationToken token)
    {
        if(!ReferenceEquals(locks.Transaction,store.Session.CurrentTransaction)||locks.Transaction.Connection is null)
            throw new InvalidOperationException("安全当前读必须使用聚合预锁的同一连接及事务。");
        var actor=request.Actor;
        if(actor.AccountId<=0||request.SessionId<=0||!Positive(request.PrincipalAdminTypeId,out var adminId)||!Positive(request.PrincipalProjectId,out var projectId)||
            !Positive(actor.ProjectId,out var workspace)||!locks.Admins.ContainsKey(adminId)||!locks.Projects.ContainsKey(projectId)||!locks.Projects.ContainsKey(workspace))throw InvalidIdentity();
        // OAuth撤销/刷新原本先锁sys_agent_grant再锁sys_session，此处必须沿用，不能反序补锁。
        ISecurityLockedAgentGrant? grant=null;
        if(request.GrantId is{} grantId)
        {
            if(grantId<=0)throw InvalidIdentity();
            grant=await sessionPolicy.ReadAgentGrantForUpdateAsync(store.Session,locks.Transaction,grantId,token);
        }
        var session=(await store.ReadForUpdateAsync("sys_session",new Dictionary<string,object?>{{"id",request.SessionId}},token)).SingleOrDefault();
        if(session is null||SqlRowStore.Id(session,"account_id")!=actor.AccountId||!Future(session,"expires_at"))throw InvalidIdentity();
        var kind=SqlRowStore.Text(session,"kind");
        if(kind=="agent")
        {
            if(grant is null||request.GrantId!=OptionalId(session,"grant_id")||SqlRowStore.Text(session,"project_id")!=actor.ProjectId||
                !grant.IsValidFor(new(request.GrantId!.Value,actor.AccountId,actor.ProjectId)))throw InvalidIdentity();
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
        var catalog=await resources.ReadForUpdateAsync(store.Session,locks.Transaction,projectId,adminId,token)
            ??throw new InvalidOperationException("当前安全资源适配未返回目录，不能回退发布快照。");
        var realm=PrincipalRolePolicy.Evaluate(registry,account,admin,projectId,roles,grants,catalog);
        // 不能把认证时的超级标志继续交给文件/手写钩子；发生身份语义变化则刷新整个请求。
        if(realm.IsPlatformAdministrator!=actor.IsAdministrator||realm.IsSuper!=request.IsRealmSuper||kind=="agent"&&!registry.IsAgentRealm(realm.ProjectId,realm.AdminTypeId))throw InvalidIdentity();
        var identity=request.WithPermissionContext(realm);
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
