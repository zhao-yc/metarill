using System.Data.Common;
using System.Runtime.CompilerServices;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>平台当前目录适配与工作台原revision共用一个请求实例；共享核心只得到窄投影。</summary>
public sealed class PlatformSecurityCurrentResources(SqlRowStore store,MetadataRepository metadata):ISecurityCurrentResources,IRolePermissionCatalog
{
    private sealed record LoadedCatalog(DatabaseSession Session,DbTransaction Transaction,MetadataCatalog Catalog);
    // 弱引用表按对象身份比较；批量CRUD临时投影不因请求缓存而保留全部原目录。
    private readonly ConditionalWeakTable<SecurityResourceCatalog,LoadedCatalog> loaded=new();

    /// <summary>工作台普通读取仍走原完整项目目录，首先证明仓储与调用者使用同一Session。</summary>
    public Task<MetadataCatalog> ReadAsync(DatabaseSession session,string projectId,CancellationToken cancellationToken)
    {
        if(!ReferenceEquals(store.Session,session))throw new InvalidOperationException("角色工作台目录必须使用同一Session。");
        metadata.RequireSecuritySession(session);
        return metadata.LoadProjectAsync(projectId,cancellationToken);
    }

    /// <summary>跨程序集只开放必要目录合同，原CWT引用身份及实际事务证明保持不变。</summary>
    MetadataCatalog IRolePermissionCatalog.RequireLoaded(DatabaseSession session,DbTransaction transaction,SecurityResourceCatalog resources)=>RequireLoaded(session,transaction,resources);

    /// <summary>只缓存本次真实FOR UPDATE目录，引用键不能被等值投影或另一个事务替换。</summary>
    public async Task<SecurityResourceCatalog> ReadForUpdateAsync(DatabaseSession session,DbTransaction transaction,long projectId,long adminTypeId,CancellationToken cancellationToken)
    {
        RequireCurrent(session,transaction);
        var catalog=await metadata.LoadRealmForUpdateAsync(projectId,adminTypeId,cancellationToken);
        var resources=PlatformSecurityResourceProjection.Create(catalog);
        loaded.Add(resources,new(session,transaction,catalog));return resources;
    }

    /// <summary>菜单锁仍位于角色锁之后、账号锁之前；缺少config_menu定义不会被解释为无引用。</summary>
    public async Task<bool> HasAdminMenuReferencesForUpdateAsync(DatabaseSession session,DbTransaction transaction,long adminTypeId,CancellationToken cancellationToken)
    {
        RequireCurrent(session,transaction);
        return (await store.ReadForUpdateAsync("config_menu",new Dictionary<string,object?>{{"yz_admin_type_id",adminTypeId}},cancellationToken)).Count>0;
    }

    /// <summary>工作台沿同一实际事务取回原完整JSON目录；不重查、不拼瘦DTO、不回退发布快照。</summary>
    internal MetadataCatalog RequireLoaded(DatabaseSession session,DbTransaction transaction,SecurityResourceCatalog resources)
    {
        RequireCurrent(session,transaction);
        if(!loaded.TryGetValue(resources,out var original)||!ReferenceEquals(original.Session,session)||!ReferenceEquals(original.Transaction,transaction))
            throw new InvalidOperationException("角色工作台缺少本事务原始当前目录，请重新读取。");
        return original.Catalog;
    }

    /// <summary>适配器、元数据仓储和调用者必须绑定同一个当前Session/DbTransaction，禁止跨事务复用缓存。</summary>
    private void RequireCurrent(DatabaseSession session,DbTransaction transaction)
    {
        if(!ReferenceEquals(store.Session,session)||!ReferenceEquals(session.CurrentTransaction,transaction)||transaction.Connection is null)
            throw new InvalidOperationException("平台安全目录必须使用同一当前连接及事务。");
        metadata.RequireSecurityTransaction(session,transaction);
    }
}
