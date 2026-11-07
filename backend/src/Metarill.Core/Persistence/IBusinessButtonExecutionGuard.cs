namespace Metarill.Core.Persistence;

/// <summary>业务入口先完成真实授权，再以固定发布项目/按钮取得执行租约；不能退回进程内互斥。</summary>
public interface IBusinessButtonExecutionGuard
{
    /// <summary>保持原按钮粒度和30分钟有效期；忙时不调用业务，同按钮不同记录也互斥。</summary>
    Task<T> ExecuteAsync<T>(string projectId,string buttonId,Func<Task<T>> action,CancellationToken cancellationToken=default);
}
