using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>保留平台原注册类型，实际Agent绑定证明复用共享运行时。</summary>
public sealed class PlatformSecuritySessionPolicy(SqlRowStore store) : ISecuritySessionPolicy
{
    private readonly AgentSecuritySessionPolicy policy = new(store);
    /// <summary>查询顺序、项目/账号/client及实时有效期规则保持原样。</summary>
    public Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding, CancellationToken cancellationToken) =>
        policy.ValidateAgentSessionAsync(binding, cancellationToken);
}
