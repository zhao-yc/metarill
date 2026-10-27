namespace Metarill.Runtime.Security;

public sealed partial class SecurityRealmMutationGuard
{
    /// <summary>元数据导入沿用平台管理员资格，但在项目/后台预锁后当前读会话与角色，防止旧RR身份或排队期间撤权。</summary>
    public async Task<AuthenticatedIdentity> RequireCurrentPlatformAdministratorAsync(CancellationToken token = default)
    {
        var transaction = store.Session.CurrentTransaction ?? throw new InvalidOperationException("当前管理员复核必须加入配置事务。");
        var request = RequireRequest(authorization.RequireIdentity().Actor, transaction);
        if (request.GrantId is not null) throw new UnauthorizedAccessException("导入配置要求真实网页登录身份。");
        var state = await PrepareLocksAsync(request, "yz_manager", null, new Dictionary<string,object?>(), token);
        var current = await reader.ReadAsync(request, state.Locks, token);
        if (!current.Identity.Actor.IsAdministrator) throw new UnauthorizedAccessException("当前身份已失去平台管理员资格。");
        return current.Identity;
    }
}
