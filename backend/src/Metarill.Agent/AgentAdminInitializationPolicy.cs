using System.Globalization;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
namespace Metarill.Runtime.Agent;

/// <summary>授权重置规则待用户确认；明确拒绝仍可刷新授权，避免仅撤Web会话后旧CLI继续签发令牌。</summary>
public sealed class AgentAdminInitializationPolicy:IAdminInitializationAgentPolicy
{
    /// <summary>复用账号→grant→session锁序；只检查当前目标事务，不修改授权或会话。</summary>
    public async Task PrepareAsync(SqlRowStore target,long accountId,CancellationToken token)
    {
        if(target.Session.CurrentTransaction is null)throw new InvalidOperationException("Agent初始化检查必须加入目标事务。");
        var grants=await target.ReadForUpdateAsync("sys_agent_grant",new Dictionary<string,object?>{{"account_id",accountId}},token);
        if(grants.Any(grant=>SqlRowStore.Text(grant,"status")=="approved"&&(grant.GetValueOrDefault("refresh_expires_at") is null
            ||Convert.ToDateTime(grant["refresh_expires_at"],CultureInfo.InvariantCulture)>DateTime.UtcNow)))
            throw new NotSupportedException("该账号的Agent授权重置规则尚待确认，本次未执行初始化。");
    }
}
