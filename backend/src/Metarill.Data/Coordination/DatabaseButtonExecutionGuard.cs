using System.Globalization;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;

namespace Metarill.Data;

/// <summary>平台按发布项目选择真实业务库；独立应用绑定自己的库，方言仅由提供程序负责。</summary>
public sealed class DatabaseButtonExecutionGuard(Func<string,DatabaseSession> sessions,TimeProvider? timeProvider=null,Action? cleanupFailure=null):IBusinessButtonExecutionGuard
{
    /// <summary>只有明确取得租约才调用业务，释放不使用已取消请求令牌，也不冒充业务事务回滚。</summary>
    public async Task<T> ExecuteAsync<T>(string projectId,string buttonId,Func<Task<T>> action,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(action);RequireId(projectId);RequireId(buttonId);cancellationToken.ThrowIfCancellationRequested();
        var session=sessions(projectId);var lease=await session.Provider.TryAcquireExecutionLeaseAsync(session.Options,"original-button:"+projectId+":"+buttonId,TimeSpan.FromMinutes(30),timeProvider??TimeProvider.System,cancellationToken);
        if(lease is null)throw new BusinessRuleException("按钮正在执行中，请稍后再试");
        try{cancellationToken.ThrowIfCancellationRequested();if(lease.IsExpired)throw new BusinessRuleException("按钮执行租约已过期，请重试");return await action();}
        finally
        {
            var failed=false;try{await lease.DisposeAsync();}catch(Exception){failed=true;}
            if(failed||lease.CleanupFailed){try{cleanupFailure?.Invoke();}catch(Exception){/* 清理提示不能覆盖原业务响应。 */}}
        }
    }
    /// <summary>范围只接受已发布正整数标识，不把工作区、账号、记录或外部键混入租约。</summary>
    private static void RequireId(string value)
    {if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||value!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException("执行租约的发布项目或按钮无效。");}
}
