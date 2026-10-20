using System.Globalization;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>平台OAuth附加证明留在平台程序集；共享认证不能把未核grant当作有效Agent会话。</summary>
public sealed class PlatformSecuritySessionPolicy(SqlRowStore store):ISecuritySessionPolicy
{
    /// <summary>沿用直接认证原查询、顺序和账号/项目/client/有效期绑定，不引入新缓存或传输规则。</summary>
    public async Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding,CancellationToken cancellationToken)
    {
        var record=(await store.ReadAsync("sys_agent_grant",new Dictionary<string,object?>{{"id",binding.GrantId}},cancellationToken)).SingleOrDefault();
        return record is not null&&SqlRowStore.Text(record,"status")=="approved"&&SqlRowStore.Text(record,"client_id")==AgentOAuthService.ClientId&&
            SqlRowStore.Id(record,"account_id")==binding.AccountId&&SqlRowStore.Text(record,"project_id")==binding.ProjectId&&
            record.GetValueOrDefault("refresh_expires_at") is{} expiry&&Convert.ToDateTime(expiry,CultureInfo.InvariantCulture)>DateTime.UtcNow;
    }
}
