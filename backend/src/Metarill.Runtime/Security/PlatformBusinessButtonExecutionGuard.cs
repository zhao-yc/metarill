using System.Data.Common;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>平台原 Web 与 Agent 按钮共用租约，在分库流程中持有平台权限锁直至业务库结束。</summary>
public sealed class PlatformBusinessButtonExecutionGuard(
    SqlRowStore source,
    ISecurityResourceRegistry registry,
    ISecurityIdentityAccessor identities,
    SecurityRealmMutationGuard authority,
    Func<string,DatabaseSession> targets,
    Action? leaseCleanupFailure = null,
    Action? authorityCleanupFailure = null):IBusinessButtonExecutionGuard
{
    private readonly DatabaseButtonExecutionGuard leases = new(targets, cleanupFailure: leaseCleanupFailure);
    private readonly AsyncLocal<SplitScope?> scope = new();

    private sealed record SplitScope(string PageId, string ButtonId, RuntimeActor Actor, DbTransaction SourceTransaction, DatabaseSession Target)
    {
        private int active = 1;
        public bool IsActive => Volatile.Read(ref active) != 0;
        public DbTransaction? ProvenTargetTransaction { get; set; }
        /// <summary>同一请求的异步子上下文也立即失去锁证明。</summary>
        public void Close() => Volatile.Write(ref active, 0);
    }

    internal bool HasSplitScope => scope.Value is not null;

    /// <summary>同会话保持原执行方式，分库在实际租约内取得平台当前权限。</summary>
    public Task<T> ExecuteAsync<T>(string projectId, string buttonId, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return leases.ExecuteAsync(projectId, buttonId, () => WithAuthorityAsync(projectId, buttonId, action, cancellationToken), cancellationToken);
    }

    /// <summary>用固定发布来源解析实际业务会话，在调用业务前锁定真实当前身份和按钮。</summary>
    private async Task<T> WithAuthorityAsync<T>(string projectId, string buttonId, Func<Task<T>> action, CancellationToken token)
    {
        var target = targets(projectId);
        if (scope.Value is not null) throw new InvalidOperationException("不能在分库业务按钮内再次启动按钮执行。");
        if (ReferenceEquals(target, source.Session)) return await action();
        if (target.CurrentTransaction is not null)
            throw new InvalidOperationException("分库业务按钮必须先取得平台授权锁，再开启业务库事务。");

        var request = identities.RequireIdentity();
        var resource = registry.PublishedCatalogs.Where(catalog => catalog.ProjectId == projectId)
            .SelectMany(catalog => catalog.Buttons).SingleOrDefault(button => button.Id == buttonId)
            ?? throw new UnauthorizedAccessException("分库业务按钮未发布。");
        if (request.Actor.ProjectId != projectId)
            throw new UnauthorizedAccessException("分库业务按钮与当前真实项目不一致。");

        T result = default!;
        var completed = false;
        try
        {
            return await source.Session.InAuthorizationLockScopeAsync(async transaction =>
            {
                await authority.RequireBusinessButtonAsync(resource.PageId, buttonId, request.Actor, transaction, token);
                var current = new SplitScope(resource.PageId, buttonId, request.Actor, transaction, target);
                scope.Value = current;
                try
                {
                    result = await action();
                    completed = true;
                    return result;
                }
                finally
                {
                    // 子任务可能仍继承本对象。退出时同时作废对象，防止它借旧上下文取得证明。
                    current.Close();
                    scope.Value = null;
                }
            }, token);
        }
        catch when (completed)
        {
            // 业务侧已经返回，平台只释放授权读锁。清理异常不得把确认结果改成操作失败。
            try { authorityCleanupFailure?.Invoke(); } catch (Exception) { /* 日志故障不得覆盖原结果。 */ }
            return result;
        }
    }

    /// <summary>锁记录和实际更新前，从仍持锁的平台事务取得证明，并绑定同一个业务写事务。</summary>
    internal async Task RequireCurrentAsync(string pageId, string buttonId, RuntimeActor actor, DbTransaction transaction, CancellationToken token)
    {
        var current = RequireScope(pageId, buttonId, actor, transaction);
        await authority.RequireBusinessButtonAsync(pageId, buttonId, actor, current.SourceTransaction, token);
        current.ProvenTargetTransaction = transaction;
    }

    /// <summary>文件来源必须先经过同一目标事务的按钮证明，文件引用仍完全留在目标库。</summary>
    internal Task<AuthenticatedIdentity> ReadFileIdentityAsync(string pageId, string buttonId, RuntimeActor actor, DbTransaction transaction, CancellationToken token)
    {
        var current = RequireScope(pageId, buttonId, actor, transaction);
        if (!ReferenceEquals(current.ProvenTargetTransaction, transaction))
            throw new InvalidOperationException("分库文件来源当前读必须复用已证明的业务写事务。");
        return authority.ReadBusinessButtonFileIdentityAsync(pageId, buttonId, actor, current.SourceTransaction, token);
    }

    /// <summary>仅接原请求中的固定项目、页面、按钮及两个仍然活动的事务。</summary>
    private SplitScope RequireScope(string pageId, string buttonId, RuntimeActor actor, DbTransaction transaction)
    {
        var current = scope.Value;
        if (current is not { IsActive:true } || current.PageId != pageId || current.ButtonId != buttonId || current.Actor != actor
            || !current.Target.HasWriteTransaction || !ReferenceEquals(current.Target.CurrentTransaction, transaction)
            || transaction.Connection is null || current.SourceTransaction.Connection is null
            || !ReferenceEquals(source.Session.CurrentTransaction, current.SourceTransaction)
            || current.ProvenTargetTransaction is not null && !ReferenceEquals(current.ProvenTargetTransaction, transaction))
            throw new InvalidOperationException("分库按钮当前权限必须复用原授权锁和同一个目标业务写事务。");
        return current;
    }
}
