namespace Metarill.Data;

/// <summary>租约只拥有独立协调连接，不是业务事务；释放失败不得伪装成业务回滚。</summary>
public interface IDatabaseExecutionLease:IAsyncDisposable
{
    bool IsExpired {get;}
    bool CleanupFailed {get;}
}
