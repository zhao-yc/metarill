using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Security;

/// <summary>Agent模块负责初始化时的授权检查；必须加入账号已锁定的目标事务，且先于会话锁执行。</summary>
public interface IAdminInitializationAgentPolicy
{
    /// <summary>按账号读取并处理当前授权；不能自行提交事务，也不能读取其他账号的授权。</summary>
    Task PrepareAsync(SqlRowStore target,long accountId,CancellationToken token);
}
