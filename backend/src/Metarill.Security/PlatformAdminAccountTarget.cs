using System.Globalization;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Security;

/// <summary>仅消费仍在原平台事务内的真实授权范围；目标账号/角色/会话和必要归属种子在唯一业务事务内。</summary>
public static class PlatformAdminAccountTarget
{
    /// <summary>先识别实际物理库；共库复用已授权原事务，异库只有独立目标事务正常返回后才产生提交证明。</summary>
    public static async Task<AdminAccountInitializationResult> InitializeAsync(SqlRowStore target,IIdGenerator ids,PlatformAdminInitializationScope scope,IAdminInitializationAgentPolicy agentPolicy,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(scope);scope.Consume();
        if(!scope.HasConfiguredAccount)return new(false);
        await scope.RevalidateAsync(token);
        if(ReferenceEquals(target.Session,scope.Source.Session))return await ApplySharedAsync(scope,ids,agentPolicy,token);
        if(target.Session.CurrentTransaction is not null)throw new InvalidOperationException("跨库初始化不能加入调用方未知目标事务。");
        var outcome=await target.Session.InTransactionAsync(async()=>
        {
            scope.RequireActive();var transaction=target.Session.CurrentTransaction!;
            var identity=await target.Session.Provider.ReadPhysicalDatabaseIdentityAsync(target.Session.Options,transaction,token);
            // 同物理库探测事务只有身份SELECT；先结束该事务，再在原授权事务中写，避免两连接争抢原锁。
            if(identity==scope.SourceIdentity)return (Shared:true,Result:new AdminAccountInitializationResult(false));
            await EnsureRealmAsync(target,scope,token);
            var result=await AdminAccountInitializationBusiness.ApplySavedAdminAsync(target,ids,scope.Admin,agentPolicy,token);
            await scope.RevalidateAsync(token);return (Shared:false,Result:result);
        },token);
        if(outcome.Shared)return await ApplySharedAsync(scope,ids,agentPolicy,token);
        if(outcome.Result.Applied)scope.MarkIndependentCommit();return outcome.Result;
    }
    /// <summary>共库只写已绑定业务后台；源提交失败时账号/角色/会话一并回滚，不记录独立提交。</summary>
    private static async Task<AdminAccountInitializationResult> ApplySharedAsync(PlatformAdminInitializationScope scope,IIdGenerator ids,IAdminInitializationAgentPolicy agentPolicy,CancellationToken token)
    {
        var result=await AdminAccountInitializationBusiness.ApplySavedAdminAsync(scope.Source,ids,scope.Admin,agentPolicy,token);
        await scope.RevalidateAsync(token);return result;
    }
    /// <summary>先项目后后台取锁；缺失仅补本范围归属行，既有记录和应用配置不覆盖，不自动建表。</summary>
    private static async Task EnsureRealmAsync(SqlRowStore target,PlatformAdminInitializationScope scope,CancellationToken token)
    {
        var projectId=long.Parse(scope.ProjectId,CultureInfo.InvariantCulture);var adminId=long.Parse(scope.AdminTypeId,CultureInfo.InvariantCulture);
        var projects=await target.ReadInitializationScopeForUpdateAsync("yz_project",projectId,token);
        if(projects.Count>1||projects.Any(project=>project.GetValueOrDefault("delete_no") is not null))throw new InvalidOperationException("目标项目归属不唯一。");
        if(projects.Count==0)await target.InsertAsync("yz_project",new Dictionary<string,object?>{{"id",projectId},{"name",SqlRowStore.Text(scope.Project,"name")},{"insert_time",BusinessCalendar.Now}},token);
        var admins=await target.ReadInitializationScopeForUpdateAsync("yz_admin_type",adminId,token);
        if(admins.Count>1||admins.Any(admin=>admin.GetValueOrDefault("delete_no") is not null||SqlRowStore.Text(admin,"yz_project_id")!=scope.ProjectId))throw new InvalidOperationException("目标后台已属于其他项目，未执行初始化。");
        if(admins.Count==0)await target.InsertAsync("yz_admin_type",new Dictionary<string,object?>{{"id",adminId},{"yz_project_id",projectId},{"name",SqlRowStore.Text(scope.Admin,"name")},{"insert_time",BusinessCalendar.Now}},token);
    }
}
