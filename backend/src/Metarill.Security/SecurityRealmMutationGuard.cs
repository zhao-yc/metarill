using System.Data.Common;
using System.Globalization;
using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>安全表聚合先锁项目、后台，再读取当前身份；平台已接线，实库及并发验收前保留Runtime Gap。</summary>
public sealed partial class SecurityRealmMutationGuard(SqlRowStore store,ISecurityResourceRegistry registry,ISecurityIdentityAccessor authorization,SecurityRealmCurrentReader reader):IRecordMutationGuard
{
    private static readonly IReadOnlyDictionary<string,string> SafetyTables=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["yz_manager"]="6031906809708544",["yz_role"]="1692532858499969026",["yz_admin_type"]="1696799308939239425",["yz_role_permission"]="2063264600555577345"
    };
    private readonly Dictionary<DbTransaction,CoordinationState> _states=new(ReferenceEqualityComparer.Instance);
    private sealed class CoordinationState(AuthenticatedIdentity request,SecurityRealmLocks locks)
    {
        internal AuthenticatedIdentity Request {get;}=request;
        internal SecurityRealmLocks Locks {get;set;}=locks;
        internal List<PreparedWrite> Pending {get;}=[];
        internal IReadOnlySet<long>? RoleIds {get;set;}
        internal HashSet<(string Page,string Button)> Buttons {get;}=[];
    }
    private sealed record PreparedWrite(RecordMutationPreparation Preparation,Dictionary<string,object?>? Original);
    internal sealed record RolePermissionWriteScope(AuthenticatedIdentity Identity,Dictionary<string,object?> Role,SecurityResourceCatalog Catalog);

    /// <summary>只接真实已发布固定安全表；原普通角色授权四写入口关闭，不能从Service旁路工作台。</summary>
    public async Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)
    {
        if(!SafetyTables.ContainsKey(preparation.TableName))return;
        if(preparation.TableName=="yz_role_permission")throw new InvalidOperationException("角色授权必须通过授权工作台写入，普通表级写入口未开放。");
        var page=PublishedPage(preparation.PageId,preparation.TableName);
        var request=RequireRequest(preparation.Actor,preparation.Transaction);
        EnsureEnabled(page,preparation.Operation);
        var state=await PrepareLocksAsync(request,preparation.TableName,preparation.TargetId,preparation.ScopeValues,cancellationToken);
        var current=await ReadCurrentAsync(request,state,cancellationToken);
        RequireSourceAction(current,page,preparation.Operation);
        var original=await LockedTarget(preparation.TableName,preparation.TargetId,state,cancellationToken);
        state.Pending.Add(new(preparation,original));
    }

    /// <summary>生成器已完成合并、对象规则与关系规则后，仍在同一事务中复核最终后台与角色。</summary>
    public async Task BeforeWriteAsync(RecordMutationValidation validation,CancellationToken cancellationToken=default)
    {
        if(!SafetyTables.ContainsKey(validation.TableName))return;
        if(validation.TableName=="yz_role_permission")throw new InvalidOperationException("角色授权必须通过授权工作台写入，普通表级写入口未开放。");
        var request=RequireRequest(validation.Actor,validation.Transaction);
        if(!_states.TryGetValue(validation.Transaction,out var state))throw Changed();
        var pending=state.Pending.LastOrDefault(item=>item.Preparation.PageId==validation.PageId&&item.Preparation.Operation==validation.Operation&&
            (validation.Operation is RecordMutationOperation.Create or RecordMutationOperation.Copy||item.Preparation.TargetId==validation.RecordId));
        if(pending is null||pending.Preparation.BusinessButtonId!=validation.BusinessButtonId)throw Changed();
        RequireSameRequest(state.Request,request);
        await RefreshLocks(state,cancellationToken);
        var current=await ReadCurrentAsync(request,state,cancellationToken);
        RequireSourceAction(current,PublishedPage(validation.PageId,validation.TableName),validation.Operation);
        ValidateOriginal(validation,pending.Original);
        await ValidateFinalAsync(validation,pending.Original,state,current,cancellationToken);
        state.Pending.Remove(pending);
    }

    /// <summary>工作台与CRUD使用同一项目/后台锁协议，目标角色及授权动作在锁后当前读中证明。</summary>
    internal async Task<RolePermissionWriteScope> PrepareRolePermissionsAsync(string roleId,CancellationToken token)
    {
        var transaction=store.Session.CurrentTransaction??throw new InvalidOperationException("角色授权保存必须在事务内。");
        var request=RequireRequest(authorization.RequireIdentity().Actor,transaction);
        var id=Id(roleId);
        var state=await PrepareLocksAsync(request,"yz_role",id,new Dictionary<string,object?>(),token);
        var current=await ReadCurrentAsync(request,state,token);
        var role=await LockedTarget("yz_role",id,state,token)??throw Changed();
        RequireRoleAction(current,Id(SqlRowStore.Text(role,"yz_admin_type_id")),"is_update");
        return new(current.Identity,role,current.Catalog);
    }

    /// <summary>首次只用未锁读取发现锁集合，不能据此授权；集合跑出预锁范围时只冲突，不逆序补锁。</summary>
    private async Task<CoordinationState> PrepareLocksAsync(AuthenticatedIdentity request,string table,long? targetId,IReadOnlyDictionary<string,object?> hints,CancellationToken token,IReadOnlySet<long>? declaredAdmins=null)
    {
        var transaction=store.Session.CurrentTransaction??throw new InvalidOperationException("安全预锁必须在事务内。");
        var adminIds=new SortedSet<long>{Id(request.PrincipalAdminTypeId)};if(declaredAdmins is not null)adminIds.UnionWith(declaredAdmins);
        var projects=new SortedSet<long>{Id(request.PrincipalProjectId),Id(request.Actor.ProjectId)};
        Dictionary<string,object?>? discovery=null;
        if(targetId is{} target)
        {
            if(target<=0)throw new ArgumentException("安全记录标识无效。");
            discovery=(await store.ReadAsync(table,new Dictionary<string,object?>{{"id",target}},token)).SingleOrDefault()??throw new InvalidOperationException("安全记录不存在或已删除。");
            if(table=="yz_admin_type"){adminIds.Add(target);projects.Add(Id(SqlRowStore.Text(discovery,"yz_project_id")));}
            else adminIds.Add(Id(SqlRowStore.Text(discovery,"yz_admin_type_id")));
        }
        if(table is "yz_manager" or "yz_role" && hints.TryGetValue("yz_admin_type_id",out var adminHint)&&adminHint is not null)adminIds.Add(Id(adminHint));
        if(table=="yz_admin_type"&&hints.TryGetValue("yz_project_id",out var projectHint)&&projectHint is not null)projects.Add(Id(projectHint));
        foreach(var admin in adminIds)
        {
            var row=(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin}},token)).SingleOrDefault()??throw new ArgumentException("后台不存在或已删除。");
            projects.Add(Id(SqlRowStore.Text(row,"yz_project_id")));
        }
        if(_states.TryGetValue(transaction,out var existing))
        {
            RequireSameRequest(existing.Request,request);
            if(adminIds.Any(id=>!existing.Locks.Admins.ContainsKey(id))||projects.Any(id=>!existing.Locks.Projects.ContainsKey(id)))throw Changed();
            await RefreshLocks(existing,token);return existing;
        }
        var lockedProjects=new Dictionary<long,Dictionary<string,object?>>();var lockedAdmins=new Dictionary<long,Dictionary<string,object?>>();
        // 工作区只为兼容配置聚合锁；真正身份来自锁后账号→后台→项目，不能用所选项目授新权。
        foreach(var id in projects)lockedProjects[id]=(await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",id}},token)).SingleOrDefault()??throw Changed();
        foreach(var id in adminIds)lockedAdmins[id]=(await store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",id}},token)).SingleOrDefault()??throw Changed();
        if(lockedAdmins.Values.Any(row=>!lockedProjects.ContainsKey(Id(SqlRowStore.Text(row,"yz_project_id")))))throw Changed();
        var state=new CoordinationState(request,new(transaction,lockedProjects,lockedAdmins));_states.Add(transaction,state);return state;
    }

    /// <summary>首次当前读固定角色预锁集合，后续检查只允许复用或缩小，防止记录锁后补锁新角色。</summary>
    private async Task<SecurityCurrentPrincipal> ReadCurrentAsync(AuthenticatedIdentity request,CoordinationState state,CancellationToken token)
    {
        var current=await reader.ReadPreparedAsync(request,state.Locks,token,state.RoleIds);
        state.RoleIds??=PrincipalRolePolicy.Identifiers(SqlRowStore.Text(current.Account,"yz_role_ids")).Select(value=>long.Parse(value,CultureInfo.InvariantCulture)).ToHashSet();
        return current;
    }

    /// <summary>同事务前一笔可能修改后台，复核只重读已经持有的聚合锁，绝不补未准备的新项目/后台。</summary>
    private async Task RefreshLocks(CoordinationState state,CancellationToken token)
    {
        var projects=new Dictionary<long,Dictionary<string,object?>>();var admins=new Dictionary<long,Dictionary<string,object?>>();
        foreach(var id in state.Locks.Projects.Keys.Order())projects[id]=(await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",id}},token)).SingleOrDefault()??throw Changed();
        foreach(var id in state.Locks.Admins.Keys.Order())admins[id]=(await store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",id}},token)).SingleOrDefault()??throw Changed();
        if(admins.Values.Any(row=>!projects.ContainsKey(Id(SqlRowStore.Text(row,"yz_project_id")))))throw Changed();
        state.Locks=new(state.Locks.Transaction,projects,admins);
    }

    /// <summary>目标行当前归属必须仍在准备集合；后续生成Mapper仍按原发布范围定位，不扩大workspace可见性。</summary>
    private async Task<Dictionary<string,object?>?> LockedTarget(string table,long? targetId,CoordinationState state,CancellationToken token)
    {
        if(targetId is null)return null;
        var row=(await store.ReadForUpdateAsync(table,new Dictionary<string,object?>{{"id",targetId}},token)).SingleOrDefault()??throw Changed();
        var admin=table=="yz_admin_type"?targetId.Value:Id(SqlRowStore.Text(row,"yz_admin_type_id"));
        if(!state.Locks.Admins.ContainsKey(admin))throw Changed();
        if(table=="yz_admin_type"&&!state.Locks.Projects.ContainsKey(Id(SqlRowStore.Text(row,"yz_project_id"))))throw Changed();
        return row;
    }

    /// <summary>显式暴露给本类型静态防漏接检查，仍要求写入使用同一Session当前事务。</summary>
    private Metarill.Data.DatabaseSession Session => store.Session;

    /// <summary>三个手写平台partial只能沿实际准备状态执行，误构造空guards不能绕过授权和锁协议。</summary>
    public static void RequirePrepared(IEnumerable<IRecordMutationGuard> guards,DbTransaction? transaction,string table,long recordId,
        Metarill.Core.Security.RuntimeActor actor,bool creating,bool deleting)
    {
        var coordinator=guards.OfType<SecurityRealmMutationGuard>().SingleOrDefault()??throw new InvalidOperationException("安全Service必须注册统一写入协调器。");
        if(transaction is null||!ReferenceEquals(transaction,coordinator.Session.CurrentTransaction)||!coordinator._states.TryGetValue(transaction,out var state)||recordId<=0)throw Changed();
        coordinator.RequireRequest(actor,transaction);
        var pending=state.Pending.LastOrDefault(item=>item.Preparation.TableName==table&&item.Preparation.Actor==actor&&ReferenceEquals(item.Preparation.Transaction,transaction)&&
            (deleting?item.Preparation.Operation==RecordMutationOperation.Delete&&item.Preparation.TargetId==recordId:
                creating?item.Preparation.Operation is RecordMutationOperation.Create or RecordMutationOperation.Copy:
                item.Preparation.Operation==RecordMutationOperation.Update&&item.Preparation.TargetId==recordId));
        if(pending is null)throw Changed();
    }

    /// <summary>旧配置守卫处理后台时必须复用本次同一准备对象，若DI反序则在任何旧项目锁前明确失败。</summary>
    internal void RequirePrepared(RecordMutationPreparation preparation)
    {
        RequireRequest(preparation.Actor,preparation.Transaction);
        if(!_states.TryGetValue(preparation.Transaction,out var state)||!state.Pending.Any(item=>ReferenceEquals(item.Preparation,preparation)))throw Changed();
    }

    /// <summary>只给原三个固定安全表页面恢复原页权，普通配置、全局目录和原始授权表不借此放行。</summary>
    internal static bool IsStandardSecurityTable(string tableId,string tableName)=>tableName!="yz_role_permission"&&SafetyTables.TryGetValue(tableName,out var expected)&&tableId==expected;

    private AuthenticatedIdentity RequireRequest(Metarill.Core.Security.RuntimeActor actor,DbTransaction transaction)
    {
        if(transaction.Connection is null||!ReferenceEquals(transaction,store.Session.CurrentTransaction))throw new InvalidOperationException("安全协调必须使用写入的同一连接及事务。");
        var request=authorization.RequireIdentity();if(request.Actor!=actor)throw new UnauthorizedAccessException("安全写入身份与当前认证不一致。");return request;
    }
    private SecurityPageResource PublishedPage(string pageId,string table)
    {
        var page=registry.PublishedCatalogs.SelectMany(catalog=>catalog.Pages).SingleOrDefault(page=>page.Id==pageId)??throw new UnauthorizedAccessException("安全写入页面未发布。");
        if(!SafetyTables.TryGetValue(table,out var tableId)||page.TableId!=tableId||!registry.PublishedCatalogs.SelectMany(catalog=>catalog.Tables).Any(item=>item.Id==tableId&&item.TableName==table))throw new UnauthorizedAccessException("安全写入页面与固定表不一致。");
        return page;
    }
    private static void RequireSameRequest(AuthenticatedIdentity old,AuthenticatedIdentity current)
    {
        if(old.Actor!=current.Actor||old.SessionId!=current.SessionId||old.GrantId!=current.GrantId||old.PrincipalAdminTypeId!=current.PrincipalAdminTypeId||old.PrincipalProjectId!=current.PrincipalProjectId)throw Changed();
    }
    private static long Id(object value)
    {
        var text=Convert.ToString(value,CultureInfo.InvariantCulture);
        if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException("安全归属标识必须为规范正整数。");return id;
    }
    private static InvalidOperationException Changed()=>new("YZP_CONFLICT：安全归属或锁集合已变化，请刷新后重试。");
}
