using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class TrustedSecurityRuntimeMySqlTests
{
    /// <summary>真实认证读出旧RR身份后撤权，生成Service必须拒写而不能沿用请求入口的旧权限。</summary>
    [StandaloneExportMySqlFact]
    public Task Generated_management_http_rechecks_revocation_after_old_rr_snapshot()=>RunManagementAsync("revocation");
    /// <summary>实际生成账号接口迁移目标后台后，旧快照请求冲突且不能逆序补锁新后台。</summary>
    [StandaloneExportMySqlFact]
    public Task Generated_management_http_rejects_changed_target_realm_without_late_lock()=>RunManagementAsync("target-realm");
    /// <summary>无关后台账号/角色/授权物理锁持续存在时，原工作台保存和生成角色删除都应完成。</summary>
    [StandaloneExportMySqlFact]
    public Task Generated_management_http_does_not_wait_for_unrelated_realm_rows()=>RunManagementAsync("unrelated");

    /// <summary>只增加本轮夹具屏障；实际身份解析、当前读、生成页权和事务实现均保持产品路径。</summary>
    private sealed class ManagementRaceProbe(string nonce)
    {
        internal const string Header="X-Metarill-Acceptance-Race";
        internal readonly TaskCompletionSource Resolved=new(TaskCreationOptions.RunContinuationsAsynchronously), Resume=new(TaskCreationOptions.RunContinuationsAsynchronously), Finished=new(TaskCreationOptions.RunContinuationsAsynchronously), Finish=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ExpectOldGrant,SnapshotStillOld,RollbackOnlyObserved,ConflictObserved,RepeatableReadObserved;
        internal long? ExpectOldAdmin;
        internal int ResponseStatus;
        internal string Stage="idle",IsolationName="unobserved",ServerIsolationName="unobserved",FaultType="";
        internal object Diagnostics=>new{Stage,IsolationName,ServerIsolationName,FaultType,ResponseStatus,RepeatableReadObserved,SnapshotStillOld,RollbackOnlyObserved,ConflictObserved};
        internal string Nonce=>nonce;
        private bool Matches(HttpContext context)=>context.Request.Headers[Header]==nonce;
        /// <summary>缓冲测试响应直到外层事务回滚结束，避免失败请求留下事务或提前发出HTTP完成。</summary>
        internal async Task InTransactionAsync(HttpContext context,RequestDelegate next,DatabaseSession session)
        {
            if(!Matches(context)){await next(context);return;}
            Stage="matched";
            var original=context.Response.Body;await using var buffer=new MemoryStream();context.Response.Body=buffer;
            try
            {
                try
                {
                    await session.InTransactionAsync(async()=>
                    {
                        Stage="transaction";IsolationName=session.CurrentTransaction?.IsolationLevel.ToString()??"missing";
                        // 驱动在沿用服务端默认隔离级别时返回Unspecified，实际隔离级别必须从同一事务连接核实。
                        var current=session.CurrentTransaction??throw new InvalidOperationException("缺少真实事务。");
                        await using(var command=current.Connection!.CreateCommand())
                        {command.Transaction=current;command.CommandText="SELECT @@transaction_isolation";ServerIsolationName=Convert.ToString(await command.ExecuteScalarAsync(context.RequestAborted))??"missing";}
                        RepeatableReadObserved=ServerIsolationName=="REPEATABLE-READ";
                        if(!RepeatableReadObserved)throw new InvalidOperationException("本专项要求实际RR事务。");
                        await next(context);ResponseStatus=context.Response.StatusCode;Finished.TrySetResult();
                        await Finish.Task.WaitAsync(TimeSpan.FromSeconds(15),context.RequestAborted);return true;
                    },context.RequestAborted);
                }
                catch(InvalidOperationException error)when(error.Message=="内层操作失败，事务已被标记为仅回滚。"&&ResponseStatus>=400){RollbackOnlyObserved=true;}
            }
            catch(Exception error){FaultType=error.GetType().Name;throw;}
            finally{context.Response.Body=original;}
            buffer.Position=0;await buffer.CopyToAsync(original,context.RequestAborted);
        }
        /// <summary>认证确已执行后才放行第二连接；恢复后用同一RR普通读取证明旧视图仍在。</summary>
        internal async Task AfterAuthenticationAsync(HttpContext context,DatabaseSession session,SqlRowStore store,long editorRole,long? target)
        {
            if(!Matches(context))return;
            if(session.CurrentTransaction is null)throw new InvalidOperationException("屏障请求不在真实事务中。");
            Stage="authenticated";Resolved.TrySetResult();await Resume.Task.WaitAsync(TimeSpan.FromSeconds(15),context.RequestAborted);
            var grants=await store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",editorRole}},context.RequestAborted);
            var account=Assert.Single(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",target}},context.RequestAborted));
            SnapshotStillOld=(!ExpectOldGrant||grants.Any(row=>SqlRowStore.Text(row,"config_page_id")==ManagerPage))&&(!ExpectOldAdmin.HasValue||SqlRowStore.Id(account,"yz_admin_type_id")==ExpectOldAdmin);
            if(!SnapshotStillOld)throw new InvalidOperationException("旧RR视图未成立，不计本专项通过。");
        }
        /// <summary>只记录产品冲突分类，不把账号、口令或内部异常正文写入回执。</summary>
        internal void RecordFailure(HttpContext context,Exception error){if(Matches(context))ConflictObserved=error.Message.StartsWith("YZP_CONFLICT",StringComparison.Ordinal);}
        /// <summary>失败收尾也解除自有请求屏障，随后正常停止并排空Kestrel。</summary>
        internal void ReleaseAll(){Resume.TrySetResult();Finish.TrySetResult();}
    }

    /// <summary>每轮只使用既有固定隔离库中的自有项目；普通编辑者的T2也走真实生成接口和工作台。</summary>
    private static async Task<object> RunConcurrencyAsync(string kind,ManagementRaceProbe race,HttpClient client,SqlRowStore store,DatabaseSession session,DatabaseOptions options,Metarill.Core.Security.IIdGenerator ids,long project,long admin,long foreignAdmin,long editorRole,long foreignRole,long target,long role,string name)
    {
        const string workbench="/api/admin/sys/yzp_permission";
        async Task<JsonElement> Post(string url,object body)
        {using var response=await client.PostAsJsonAsync(url,body);Assert.True(response.IsSuccessStatusCode,"并发准备的真实生成请求拒绝，未输出实体。状态："+(int)response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(200,json.RootElement.GetProperty("code").GetInt32());return json.RootElement.Clone();}
        async Task<Dictionary<string,object?>> Target()=>Assert.Single(await store.ReadIncludingDeletedAsync("yz_manager",new Dictionary<string,object?>{{"id",target}}));
        async Task<bool> ProbeLock(string table,long id,bool expectedLocked)
        {
            if(table is not("yz_admin_type" or "yz_manager"))throw new InvalidOperationException("锁探针表不符。");
            await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();
            await using(var setting=connection.CreateCommand()){setting.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setting.ExecuteNonQueryAsync();}
            await using var transaction=await connection.BeginTransactionAsync();
            // 先证实精确项目主键存在，避免零行UPDATE被误记成“未持锁”。
            await using(var identity=connection.CreateCommand())
            {identity.Transaction=transaction;identity.CommandText="SELECT id FROM `"+table+"` WHERE id=@id AND yz_project_id=@project";identity.Parameters.AddWithValue("id",id);identity.Parameters.AddWithValue("project",project);Assert.Equal(id,Convert.ToInt64(await identity.ExecuteScalarAsync()));}
            try
            {
                await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandTimeout=4;command.CommandText="UPDATE `"+table+"` SET name=name WHERE id=@id AND yz_project_id=@project";command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("project",project);await command.ExecuteNonQueryAsync();
                Assert.False(expectedLocked,"应被自有事务阻塞的精确行实际可写。");return false;
            }
            catch(MySqlException error)when(error.Number==1205){Assert.True(expectedLocked,"无关精确行出现锁等待。");return true;}
            finally{await transaction.RollbackAsync();}
        }
        if(kind=="unrelated")
        {
            var foreignAccount=ids.NextId();var foreignGrant=ids.NextId();
            await session.InTransactionAsync(async()=>
            {
                await store.InsertAsync("yz_manager",new Dictionary<string,object?>{{"id",foreignAccount},{"name",name+"_unrelated"},{"yz_project_id",project},{"yz_admin_type_id",foreignAdmin},{"yz_role_ids",JsonSerializer.Serialize(new[]{foreignRole.ToString()})},{"password",PasswordHasher.Hash("Metarill-"+Guid.NewGuid().ToString("N"))},{"is_disabled",0}});
                await store.InsertAsync("yz_role_permission",new Dictionary<string,object?>{{"id",foreignGrant},{"yz_project_id",project},{"yz_role_id",foreignRole},{"config_page_id",long.Parse(RolePage)},{"permission_values","[\"is_query\"]"}});return true;
            });
            await using var holder=new MySqlConnection(options.ConnectionString);await holder.OpenAsync();await using var held=await holder.BeginTransactionAsync();
            foreach(var (table,id) in new[]{("yz_manager",foreignAccount),("yz_role",foreignRole),("yz_role_permission",foreignGrant)})
            {await using var command=holder.CreateCommand();command.Transaction=held;command.CommandText="SELECT id FROM `"+table+"` WHERE id=@id AND yz_project_id=@project FOR UPDATE";command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("project",project);Assert.Equal(id,Convert.ToInt64(await command.ExecuteScalarAsync()));}
            var lockedBefore=await ProbeLock("yz_manager",foreignAccount,true);
            var snapshot=(await Post(workbench+"/snapshot",new{roleId=editorRole.ToString()})).GetProperty("data");
            await Post(workbench+"/save",new RolePermissionSave(editorRole.ToString(),snapshot.GetProperty("revision").GetString()!,new([],new(){{ManagerPage,["is_all"]},{RolePage,["is_all"]},{AdminPage,["is_all"]}},[])));
            using(var request=new HttpRequestMessage(HttpMethod.Delete,Route("yz_role",RolePage)){Content=JsonContent.Create(new{id=role.ToString()})})
            using(var response=await client.SendAsync(request))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var deleted=Assert.Single(await store.ReadIncludingDeletedAsync("yz_role",new Dictionary<string,object?>{{"id",role}}));Assert.NotNull(deleted["delete_no"]);
            var lockedAfter=await ProbeLock("yz_manager",foreignAccount,true);await held.RollbackAsync();
            return new{caseName=kind,actualPersistedWebIdentity=true,generatedRoleDeletion=true,originalWorkbenchSave=true,unrelatedAccountRoleGrantHeld=true,lockedBefore,lockedAfter,observationGrantsAttempted=false};
        }
        var before=Fingerprint(await Target());race.ExpectOldGrant=kind=="revocation";race.ExpectOldAdmin=kind=="target-realm"?admin:null;
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pendingRequest=new HttpRequestMessage(HttpMethod.Post,Route("yz_manager",ManagerPage)+"/update"){Content=JsonContent.Create(new{id=target.ToString(),realName="旧快照不得写入"})};pendingRequest.Headers.Add(ManagementRaceProbe.Header,race.Nonce);
        var pending=client.SendAsync(pendingRequest,cancellation.Token);
        try
        {
            await Task.WhenAny(race.Resolved.Task,pending).WaitAsync(TimeSpan.FromSeconds(15));
            if(!race.Resolved.Task.IsCompleted){using var early=await pending;throw new InvalidOperationException("屏障前请求已结束，HTTP="+(int)early.StatusCode+"，阶段="+race.Stage+"，隔离="+race.IsolationName+"，异常类型="+race.FaultType);}
            if(kind=="revocation")
            {
                var snapshot=(await Post(workbench+"/snapshot",new{roleId=editorRole.ToString()})).GetProperty("data");
                await Post(workbench+"/save",new RolePermissionSave(editorRole.ToString(),snapshot.GetProperty("revision").GetString()!,new([],new(){{RolePage,["is_all"]},{AdminPage,["is_all"]}},[])));
            }
            else if(kind=="target-realm")
            {
                await Post(Route("yz_manager",ManagerPage)+"/update",new{id=target.ToString(),yzAdminTypeId=foreignAdmin.ToString(),yzRoleIds=new[]{foreignRole.ToString()},realName="另一连接合法迁移"});
                before=Fingerprint(await Target());
            }
            else throw new InvalidOperationException("未知并发场景。");
            race.Resume.TrySetResult();await race.Finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(race.RepeatableReadObserved&&race.SnapshotStillOld,"未证明真实旧RR读取，不能计入当前读验收。");
            Assert.Equal(kind=="revocation"?401:400,race.ResponseStatus);
            var ownAdminLocked=await ProbeLock("yz_admin_type",admin,true);var addedAdminUnlocked=!await ProbeLock("yz_admin_type",foreignAdmin,false);
            if(kind=="target-realm")Assert.True(race.ConflictObserved,"迁移后必须明确当前锁集冲突。");
            race.Finish.TrySetResult();using var response=await pending;Assert.Equal(race.ResponseStatus,(int)response.StatusCode);Assert.True(race.RollbackOnlyObserved);
            Assert.Equal(before,Fingerprint(await Target()));
            var ownAdminReleased=!await ProbeLock("yz_admin_type",admin,false);
            return new{caseName=kind,actualPersistedWebIdentity=true,ownAdminReleased,race.ServerIsolationName,race.RepeatableReadObserved,race.SnapshotStillOld,race.RollbackOnlyObserved,race.ConflictObserved,ownAdminLocked,addedAdminUnlocked,targetWholeRowUnchanged=true,observationGrantsAttempted=false};
        }
        finally
        {
            race.ReleaseAll();if(!pending.IsCompleted)cancellation.Cancel();
            try{using var response=await pending.WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception error)when(error is OperationCanceledException or HttpRequestException or TimeoutException){}
        }
    }
}
