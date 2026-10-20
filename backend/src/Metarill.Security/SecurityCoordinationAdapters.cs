using System.Data.Common;
using Metarill.Data;

namespace Metarill.Runtime.Security;

/// <summary>身份由宿主认证管线建立；安全核心不依赖HTTP上下文，也不能从请求字段构造身份。</summary>
public interface ISecurityIdentityAccessor
{
    /// <summary>只返回当前请求已经真实认证的完整身份，缺失时必须拒绝。</summary>
    AuthenticatedIdentity RequireIdentity();
}

/// <summary>当前目录与菜单引用必须从指定真实连接及事务读取；不能以已发布快照或缺表充当空结果。</summary>
public interface ISecurityCurrentResources
{
    /// <summary>在项目/后台已经预锁后读取当前资源；适配失败或空返回不得回退发布目录。</summary>
    Task<SecurityResourceCatalog> ReadForUpdateAsync(DatabaseSession session,DbTransaction transaction,long projectId,long adminTypeId,CancellationToken cancellationToken);
    /// <summary>沿既有后台引用锁序读取真实菜单引用，未知存储或缺少适配必须明确失败。</summary>
    Task<bool> HasAdminMenuReferencesForUpdateAsync(DatabaseSession session,DbTransaction transaction,long adminTypeId,CancellationToken cancellationToken);
}

/// <summary>宿主仅提供grant的当前锁读取；共享核心随后读取session，之后才求值此证明。</summary>
public interface ISecurityCurrentSessionPolicy
{
    /// <summary>只在指定实际Session/DbTransaction中取得grant锁，不能提前锁session或完成绑定求值。</summary>
    Task<ISecurityLockedAgentGrant?> ReadAgentGrantForUpdateAsync(DatabaseSession session,DbTransaction transaction,long grantId,CancellationToken cancellationToken);
}

/// <summary>只暴露锁后匹配行为，不公开grant、令牌或设备码的原始字典；不得包含新的数据库读取。</summary>
public interface ISecurityLockedAgentGrant
{
    /// <summary>调用位置固定在session锁后，账号/项目绑定及到期检查不能提前缓存求值。</summary>
    bool IsValidFor(SecuritySessionBinding binding);
}
