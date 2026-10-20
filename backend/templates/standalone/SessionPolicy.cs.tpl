using System.Data.Common;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>独立Full只允许真实Web会话；Web当前行由共享Reader锁读，本适配不创建或伪造Agent证明。</summary>
internal sealed class ApplicationSecurityCurrentSessionPolicy(SqlRowStore store):ISecurityCurrentSessionPolicy
{
    private readonly DatabaseSession source=(store??throw new ArgumentNullException(nameof(store))).Session;
    /// <summary>仅验证必要实际事务上下文后拒绝Agent；不能提前锁session、调用普通认证或读取OAuth控制面。</summary>
    public Task<ISecurityLockedAgentGrant?> ReadAgentGrantForUpdateAsync(DatabaseSession session,DbTransaction transaction,long grantId,CancellationToken cancellationToken)
    {
        if(session is null||transaction is null||!ReferenceEquals(source,session)||!ReferenceEquals(source.CurrentTransaction,transaction)||transaction.Connection is null)
            throw new InvalidOperationException("独立应用当前会话适配必须使用同一Session和实际事务。");
        cancellationToken.ThrowIfCancellationRequested();
        throw new UnauthorizedAccessException("独立应用未启用Agent会话。");
    }
}
