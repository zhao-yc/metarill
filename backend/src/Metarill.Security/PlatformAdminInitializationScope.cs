using System.Data.Common;
using Metarill.Data;
using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Security;

/// <summary>只由平台锁后协调器签发的初始化范围；口令和源记录不对宿主调用者公开。</summary>
public sealed class PlatformAdminInitializationScope
{
    private readonly SqlRowStore source;private readonly DbTransaction transaction;private readonly Func<CancellationToken,Task> revalidate;
    private bool consumed;
    internal SqlRowStore Source=>source;
    public bool TargetCommittedIndependently {get;private set;}
    internal IReadOnlyDictionary<string,object?> Admin {get;}
    internal IReadOnlyDictionary<string,object?> Project {get;}
    internal PhysicalDatabaseIdentity SourceIdentity {get;}
    public string AuthorityProjectId {get;}
    public string ProjectId {get;}
    public string AdminTypeId {get;}
    public bool HasConfiguredAccount {get;}
    internal PlatformAdminInitializationScope(SqlRowStore source,DbTransaction transaction,IReadOnlyDictionary<string,object?> admin,IReadOnlyDictionary<string,object?> project,PhysicalDatabaseIdentity identity,string authorityProjectId,Func<CancellationToken,Task> revalidate)
    {
        this.source=source;this.transaction=transaction;AuthorityProjectId=authorityProjectId;this.revalidate=revalidate;Admin=admin.ToDictionary(item=>item.Key,item=>item.Value,StringComparer.Ordinal);
        Project=project.ToDictionary(item=>item.Key,item=>item.Value,StringComparer.Ordinal);SourceIdentity=identity;
        ProjectId=SqlRowStore.Text(Admin,"yz_project_id");AdminTypeId=SqlRowStore.Text(Admin,"id");
        var name=SqlRowStore.Text(Admin,"user_name");var password=SqlRowStore.Text(Admin,"password");HasConfiguredAccount=!string.IsNullOrWhiteSpace(name)&&!string.IsNullOrWhiteSpace(password);
        if(HasConfiguredAccount&&(name.Length>50||password.Length>50))throw new ArgumentException("初始化用户名和密码不能超过50个字符。");
    }
    /// <summary>授权只能在原平台锁事务内消费，退出、换连接或丢失事务后不得复用。</summary>
    internal void RequireActive()
    {
        if(!ReferenceEquals(source.Session.CurrentTransaction,transaction)||transaction.Connection is null||transaction.Connection.State!=System.Data.ConnectionState.Open)throw new InvalidOperationException("平台初始化授权范围已经失效。");
    }
    /// <summary>同一范围只消费一次；失败重试必须重新走平台当前授权，不能重用旧结果标记。</summary>
    internal void Consume()
    {RequireActive();if(consumed)throw new InvalidOperationException("初始化授权范围已使用。");consumed=true;}
    /// <summary>目标写前与提交前重读原平台连接上的权限/配置，断线、到期或变化均阻止目标提交。</summary>
    internal async Task RevalidateAsync(CancellationToken token)
    {RequireActive();await revalidate(token);RequireActive();}
    /// <summary>仅独立目标事务正常返回后记录提交证明；共库写入仍由源事务决定成败。</summary>
    internal void MarkIndependentCommit()=>TargetCommittedIndependently=true;

}
