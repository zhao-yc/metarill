using Metarill.Core.Contracts;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed record AdminAccountInitializationResult(bool Applied,string? AccountId=null);

/// <summary>原初始化按钮的同库事务实现；尚未发布路由，不能将准备能力当作完整管理包已上线。</summary>
public sealed class AdminAccountInitializationBusiness(SqlRowStore store,SecurityRealmMutationGuard coordinator,IIdGenerator ids)
{
    /// <summary>只接已保存后台ID；配置、权限、角色与账号全部在同一事务中重新取得，不接客户端密码或后台归属。</summary>
    public Task<AdminAccountInitializationResult> InitializeAsync(string adminTypeId,CancellationToken token=default)=>
        store.Session.InTransactionAsync<AdminAccountInitializationResult>(async()=>
        {
            var admin=await coordinator.PrepareAdminInitializationAsync(adminTypeId,token);
            var name=SqlRowStore.Text(admin,"user_name");var configuredPassword=SqlRowStore.Text(admin,"password");
            if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(configuredPassword))return new(false);
            if(name.Length>50||configuredPassword.Length>50)throw new ArgumentException("初始化用户名和密码不能超过50个字符。");
            // 先锁角色，再锁用户名唯一索引；墓碑也参与唯一约束，不复活或删除任何同名对象。
            var roles=await store.ReadForUpdateAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",long.Parse(adminTypeId,CultureInfo.InvariantCulture)}},token);
            var accounts=await store.ReadAccountNameForInitializationAsync(name,token);
            if(accounts.Count>1||accounts.Any(account=>account.GetValueOrDefault("delete_no") is not null||SqlRowStore.Text(account,"yz_admin_type_id")!=adminTypeId))
                throw new InvalidOperationException("初始化账号名已被其他后台或已删除记录占用，请修改已保存的初始化用户名。");
            var existing=accounts.SingleOrDefault();
            var sessions=existing is null?[]:await store.ReadForUpdateAsync("sys_session",new Dictionary<string,object?>{{"account_id",SqlRowStore.Id(existing)}},token);
            if(sessions.Any(session=>SqlRowStore.Text(session,"kind")!="web"))
                throw new InvalidOperationException("目标账号存在未接线的非Web会话，未执行账号初始化。");
            // 保留原非空、最长50字符配置语义；普通新账号的12字符规则不在此入口强加到原页面。
            var digest=PasswordHasher.HashInitializationConfiguration(configuredPassword);
            var superRole=roles.Where(role=>SqlRowStore.Text(role,"is_super")=="1").OrderByDescending(role=>SqlRowStore.Id(role)).FirstOrDefault();
            var now=BusinessCalendar.Now;var projectId=long.Parse(SqlRowStore.Text(admin,"yz_project_id"),CultureInfo.InvariantCulture);
            if(superRole is null)
            {
                var roleId=ids.NextId();
                await store.InsertAsync("yz_role",new Dictionary<string,object?>{{"id",roleId},{"yz_admin_type_id",long.Parse(adminTypeId,CultureInfo.InvariantCulture)},
                    {"yz_project_id",projectId},{"name","超级管理员"},{"is_super",1},{"is_built_in",1},{"sort_no",99},{"insert_time",now}},token);
                superRole=new(){{"id",roleId}};
            }
            var accountId=existing is null?ids.NextId():SqlRowStore.Id(existing);
            var values=new Dictionary<string,object?>{{"name",name},{"password",digest},{"salt",null},{"old_password",null},
                {"yz_admin_type_id",long.Parse(adminTypeId,CultureInfo.InvariantCulture)},{"yz_project_id",projectId},
                {"yz_role_ids",JsonSerializer.Serialize(new[]{SqlRowStore.Text(superRole,"id")})}};
            if(existing is null)
            {
                values["id"]=accountId;values["insert_time"]=now;values["is_disabled"]=0;values["real_name"]=name;
                await store.InsertAsync("yz_manager",values,token);
            }
            else
            {
                values["update_time"]=now;
                if(await store.UpdateAsync("yz_manager",accountId,values,token)!=1)throw new InvalidOperationException("YZP_CONFLICT：初始化账号已变化。");
            }
            foreach(var session in sessions)
                if(await store.UpdateAsync("sys_session",SqlRowStore.Id(session),new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}},token)!=1)
                    throw new InvalidOperationException("YZP_CONFLICT：初始化会话已变化。");
            return new(true,accountId.ToString(CultureInfo.InvariantCulture));
        },token);
}
