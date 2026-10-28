using System.Data.Common;
using System.Globalization;
using Metarill.Data;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>Agent当前锁适配供宿主复用；OAuth表及原始grant不进入共享安全程序集。</summary>
public sealed class AgentSecurityCurrentSessionPolicy(SqlRowStore store):ISecurityCurrentSessionPolicy
{
    /// <summary>只锁原grant行并提取必要字段；匹配与时间求值延迟到共享Reader锁session之后。</summary>
    public async Task<ISecurityLockedAgentGrant?> ReadAgentGrantForUpdateAsync(DatabaseSession session,DbTransaction transaction,long grantId,CancellationToken cancellationToken)
    {
        if(!ReferenceEquals(store.Session,session)||!ReferenceEquals(session.CurrentTransaction,transaction)||transaction.Connection is null)
            throw new InvalidOperationException("Agent当前证明必须使用同一当前连接及事务。");
        var grant=(await store.ReadForUpdateAsync("sys_agent_grant",new Dictionary<string,object?>{{"id",grantId}},cancellationToken)).SingleOrDefault();
        return grant is null?null:new LockedGrant(grantId,SqlRowStore.Text(grant,"status"),SqlRowStore.Text(grant,"client_id"),
            grant.GetValueOrDefault("account_id"),SqlRowStore.Text(grant,"project_id"),grant.GetValueOrDefault("refresh_expires_at"));
    }
    /// <summary>私有闭包只保留实际匹配字段；不把token/device/refresh摘要或原始grant字典跨程序集传递。</summary>
    private sealed class LockedGrant(long grantId,string status,string clientId,object? accountId,string projectId,object? refreshExpiresAt):ISecurityLockedAgentGrant
    {
        /// <summary>条件与转换顺序沿用原Reader；损坏时间仍按失效，禁止提前缓存当前时间。</summary>
        public bool IsValidFor(SecuritySessionBinding binding)=>binding.GrantId==grantId&&status=="approved"&&clientId==AgentOAuthService.ClientId&&
            Convert.ToInt64(accountId,CultureInfo.InvariantCulture)==binding.AccountId&&projectId==binding.ProjectId&&Future(refreshExpiresAt);
        private static bool Future(object? value)
        {
            if(value is null)return false;
            try{return Convert.ToDateTime(value,CultureInfo.InvariantCulture)>DateTime.UtcNow;}
            catch(Exception error)when(error is FormatException or InvalidCastException or ArgumentException){return false;}
        }
    }
}
