namespace Metarill.Runtime.Security;

/// <summary>非Web会话的附加证明由宿主提供；共享认证不引用OAuth实现或授权表。</summary>
public interface ISecuritySessionPolicy
{
    /// <summary>共享层已验证会话、账号和身份后台；宿主仍须完整核验grant与会话的真实绑定。</summary>
    Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding,CancellationToken cancellationToken);
}

/// <summary>扩展只接收已验证会话的真实绑定，不传密码摘要、令牌摘要或完整账号/会话字典。</summary>
public sealed record SecuritySessionBinding(long GrantId,long AccountId,string ProjectId);
