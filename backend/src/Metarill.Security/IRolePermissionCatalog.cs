using System.Data.Common;
using Metarill.Core.Metadata;
using Metarill.Data;

namespace Metarill.Runtime.Security;

/// <summary>角色工作台的完整目录来源；与窄安全投影分开，保留原名称、扩展JSON及revision合同。</summary>
public interface IRolePermissionCatalog
{
    /// <summary>使用调用者的实际Session读取身份项目完整目录；平台当前存储或显式编译绑定的不可变包权威均不得回退发布列表。</summary>
    Task<MetadataCatalog> ReadAsync(DatabaseSession session,string projectId,CancellationToken cancellationToken);
    /// <summary>仅取回同一实际事务已经加载的原完整目录；等值投影、另实例或另事务必须拒绝。</summary>
    MetadataCatalog RequireLoaded(DatabaseSession session,DbTransaction transaction,SecurityResourceCatalog resources);
}
