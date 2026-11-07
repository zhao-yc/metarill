using System.Data.Common;
using Metarill.Data;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>保留平台原当前读注册类型，授权行锁及闭包证明复用共享Agent运行时。</summary>
public sealed class PlatformSecurityCurrentSessionPolicy(SqlRowStore store) : ISecurityCurrentSessionPolicy
{
    private readonly AgentSecurityCurrentSessionPolicy policy = new(store);
    /// <summary>仍要求同一Session和实际事务，锁后只返回必要绑定字段的私有证明。</summary>
    public Task<ISecurityLockedAgentGrant?> ReadAgentGrantForUpdateAsync(DatabaseSession session, DbTransaction transaction,
        long grantId, CancellationToken cancellationToken) => policy.ReadAgentGrantForUpdateAsync(session, transaction, grantId, cancellationToken);
}
