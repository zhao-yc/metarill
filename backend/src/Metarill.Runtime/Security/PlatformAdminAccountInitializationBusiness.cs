using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Security;

/// <summary>异库时平台只持授权读锁、目标独立写入；共库复用原事务，不承诺跨库分布式事务。</summary>
public sealed class PlatformAdminAccountInitializationBusiness(SqlRowStore source,SecurityRealmMutationGuard authority,
    Func<string,SqlRowStore> targets,IIdGenerator ids,IBusinessButtonExecutionGuard execution,IAdminInitializationAgentPolicy agentPolicy,Action? completedSourceCleanupFailure=null):IAdminAccountInitializer
{
    /// <summary>源配置在原按钮当前权限下固定；目标提交证明取得后，源只读锁清理故障不谎报业务已回滚。</summary>
    public async Task<AdminAccountInitializationResult> InitializeAsync(string adminTypeId,CancellationToken token=default)
    {
        adminTypeId=AdminAccountInitializationBusiness.RequireTargetId(adminTypeId);
        if(source.Session.CurrentTransaction is not null)throw new InvalidOperationException("跨库初始化必须拥有独立平台授权事务。");
        AdminAccountInitializationResult? committed=null;
        try
        {
            return await source.Session.InTransactionAsync(async()=>
            {
                var scope=await authority.PreparePlatformAdminInitializationAsync(adminTypeId,token);
                if(!scope.HasConfiguredAccount)return new(false);
                return await execution.ExecuteAsync(scope.AuthorityProjectId,AdminAccountInitializationBusiness.ButtonId,async()=>
                {
                    var result=await PlatformAdminAccountTarget.InitializeAsync(targets(scope.ProjectId),ids,scope,agentPolicy,token);
                    // 只有目标事务方法正常返回才有提交证明，目标提交异常不能进入此分支。
                    if(result.Applied&&scope.TargetCommittedIndependently)committed=result;return result;
                },token);
            },token);
        }
        catch when(committed is{Applied:true})
        {
            // 本流程源端仅授权锁读；目标已提交时只记录固定清理提示，不回显配置或异常正文。
            try{completedSourceCleanupFailure?.Invoke();}catch(Exception){/* 日志故障不得覆盖已证明的业务结果。 */}
            return committed;
        }
    }
}
