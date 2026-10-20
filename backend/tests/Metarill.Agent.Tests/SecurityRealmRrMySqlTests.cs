using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Data;
using Metarill.Platform.Generated.DTOs;
using Metarill.Platform.Generated.Services;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

[CollectionDefinition(Name,DisableParallelization=true)]
public sealed class SecurityRealmRrCollection{public const string Name="安全realm真实RR独占集合";}

/// <summary>只允许独占Server200隔离库执行；编译、离线SQL或任务状态均不算本组真实并发验收。</summary>
[Collection(SecurityRealmRrCollection.Name)]
public sealed class SecurityRealmRrMySqlTests
{
    /// <summary>旧RR快照仍看到编辑权，但等待聚合锁后的实际Service必须拒绝已经被工作台撤销的权限。</summary>
    [AgentIntegrationFact]
    public Task Old_repeatable_read_snapshot_cannot_keep_revoked_role_edit_permission()=>Run("rr-revocation",async f=>
    {
        var editor=await f.Actor(false);var administrator=await f.Actor(true);await editor.GrantAsync(editor.RolePage,"is_update");
        var request=await editor.IdentityAsync();var admin=await administrator.IdentityAsync();var target=await f.ExtraRole(administrator.Store);
        await f.ActualPlans([f.Admin],[editor.RoleId,administrator.RoleId,target]);
        var before=await RowHash(administrator.Session,"yz_role",target,f.Token);var source=f.Scope(editor,request).GetRequiredService<YzRoleService>();
        var business=f.Scope(administrator,admin).GetRequiredService<RolePermissionBusiness>();var revision=Revision(await business.SnapshotAsync(editor.RoleId.ToString(CultureInfo.InvariantCulture),f.Token));
        var snapshot=Signal<ulong>();var locked=Signal<ulong>();
        var t1=f.Track(Task.Run(()=>editor.Session.InTransactionAsync<bool>(async()=>
        {
            var connection=await f.Probe.RequireRepeatableReadAsync(editor.Session.CurrentTransaction!,f.Token);
            var old=RowsHash(await editor.Store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",editor.RoleId}},f.Token));snapshot.SetResult(connection);
            await locked.Task.WaitAsync(f.Token);
            try{await source.Update1692532858529329154Async(new(){Id=target,Name=new("不应提交的旧快照写入")},null,request.Actor,f.Token);throw new InvalidOperationException("撤权后Service意外接受写入，本测试外层事务强制回滚。");}
            catch(UnauthorizedAccessException)
            {
                var stale=RowsHash(await editor.Store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",editor.RoleId}},f.Token));
                Assert.Equal(old,stale);f.Evidence["ordinarySnapshotStillOld"]=true;throw;
            }
        },f.Token)));
        var t2=f.Track(Task.Run(()=>administrator.Session.InTransactionAsync(async()=>
        {
            var other=await snapshot.Task.WaitAsync(f.Token);var connection=await f.Probe.RequireRepeatableReadAsync(administrator.Session.CurrentTransaction!,f.Token);Assert.NotEqual(other,connection);
            await AggregateLocks(administrator.Store,f.Project,[f.Admin],f.Token);locked.SetResult(connection);
            f.Evidence["projectWait"]=await f.Probe.WaitForProjectAsync(other,connection,f.Project,f.Token);
            await business.SaveAsync(new(editor.RoleId.ToString(CultureInfo.InvariantCulture),revision,new([],new(),[])),f.Token);return true;
        },f.Token)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await t1);await t2;
        Assert.Equal(before,await RowHash(administrator.Session,"yz_role",target,f.Token));f.Evidence["targetBeforeSha256"]=before;f.Evidence["targetUnchanged"]=true;
    });

    /// <summary>准备发现的旧后台不能在锁后被当成最终后台；冲突时外层仍持锁，实测锁清单不得出现新后台。</summary>
    [AgentIntegrationFact]
    public Task Changed_manager_admin_conflicts_without_appending_unprepared_admin_lock()=>Run("rr-target-admin-change",async f=>
    {
        var editor=await f.Actor(false);var administrator=await f.Actor(true);var target=await f.Actor(false);await editor.GrantAsync(ManagerPage,"is_update");
        var request=await editor.IdentityAsync();var admin=await administrator.IdentityAsync();var nextAdmin=await f.ExtraAdmin(administrator.Store);
        await f.ActualPlans([f.Admin,nextAdmin],[editor.RoleId,administrator.RoleId,target.RoleId]);
        var source=f.Scope(editor,request).GetRequiredService<YzManagerService>();var mover=f.Scope(administrator,admin).GetRequiredService<YzManagerService>();
        var snapshot=Signal<ulong>();var locked=Signal<ulong>();var conflict=Signal<ulong>();var inspected=Signal<bool>();var committed=Signal<string>();
        var t1=f.Track(Task.Run(()=>editor.Session.InTransactionAsync<bool>(async()=>
        {
            var connection=await f.Probe.RequireRepeatableReadAsync(editor.Session.CurrentTransaction!,f.Token);
            var old=Assert.Single(await editor.Store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",target.AccountId}},f.Token));Assert.Equal(f.Admin.ToString(CultureInfo.InvariantCulture),SqlRowStore.Text(old,"yz_admin_type_id"));snapshot.SetResult(connection);
            await locked.Task.WaitAsync(f.Token);
            try{await source.Update6031906822946816Async(new(){Id=target.AccountId,RealName=new("不应提交的新后台写入")},null,request.Actor,f.Token);throw new InvalidOperationException("后台变化后Service意外接受写入，本测试外层事务强制回滚。");}
            catch(InvalidOperationException error)when(error.Message.StartsWith("YZP_CONFLICT",StringComparison.Ordinal))
            {conflict.SetResult(connection);await inspected.Task.WaitAsync(f.Token);throw;}
        },f.Token)));
        var t2=f.Track(Task.Run(()=>administrator.Session.InTransactionAsync(async()=>
        {
            var other=await snapshot.Task.WaitAsync(f.Token);var connection=await f.Probe.RequireRepeatableReadAsync(administrator.Session.CurrentTransaction!,f.Token);Assert.NotEqual(other,connection);
            await AggregateLocks(administrator.Store,f.Project,[f.Admin,nextAdmin],f.Token);locked.SetResult(connection);
            f.Evidence["projectWait"]=await f.Probe.WaitForProjectAsync(other,connection,f.Project,f.Token);
            await mover.Update6031906822946816Async(new(){Id=target.AccountId,YzAdminTypeId=new(nextAdmin),YzRoleIds=new(JsonSerializer.SerializeToElement(Array.Empty<string>()))},null,admin.Actor,f.Token);
            committed.SetResult(await RowHash(administrator.Session,"yz_manager",target.AccountId,f.Token));return true;
        },f.Token)));
        try
        {
            await t2;var connection=await conflict.Task.WaitAsync(f.Token);var locks=await f.Probe.LocksAsync(connection,f.Token);f.Evidence["locksBeforeRollback"]=locks;
            var admins=locks.Where(row=>row.Table=="yz_admin_type"&&row.Type=="RECORD"&&row.Status=="GRANTED").ToArray();Assert.DoesNotContain(admins,row=>row.Data is null);
            Assert.Contains(admins,row=>row.Index=="PRIMARY"&&row.Data==f.Admin.ToString(CultureInfo.InvariantCulture));Assert.DoesNotContain(admins,row=>row.Data==nextAdmin.ToString(CultureInfo.InvariantCulture));
        }
        finally{inspected.TrySetResult(true);}
        var error=await Assert.ThrowsAsync<InvalidOperationException>(async()=>await t1);Assert.StartsWith("YZP_CONFLICT",error.Message);
        var after=await RowHash(administrator.Session,"yz_manager",target.AccountId,f.Token);Assert.Equal(await committed.Task.WaitAsync(f.Token),after);
        f.Evidence["targetCommittedSha256"]=after;f.Evidence["unpreparedAdminNotLocked"]=true;
    });

    /// <summary>无关业务realm持续持账号与授权主键锁时，平台角色引用检查及另一工作台保存仍应完成。</summary>
    [AgentIntegrationFact]
    public Task Unrelated_realm_row_locks_do_not_block_role_references_and_workbench_save()=>Run("rr-unrelated-realm",async f=>
    {
        var deleting=await f.Actor(true);var writing=await f.Actor(true);var unrelated=await f.Actor(false,true);await unrelated.GrantAsync(unrelated.Registry.Demo.ConfigPages[0].Id,"is_query");
        var target=await f.ExtraRole(deleting.Store);var permissionTarget=await f.ExtraRole(writing.Store);
        await f.ActualPlans([f.Admin,long.Parse(unrelated.Registry.Demo.YzAdminTypes[0].Id,CultureInfo.InvariantCulture)],[deleting.RoleId,writing.RoleId,unrelated.RoleId,target,permissionTarget]);
        var deleteIdentity=await deleting.IdentityAsync();var source=f.Scope(deleting,deleteIdentity).GetRequiredService<YzRoleService>();var writer=f.Scope(writing,await writing.IdentityAsync()).GetRequiredService<RolePermissionBusiness>();
        var revision=Revision(await writer.SnapshotAsync(permissionTarget.ToString(CultureInfo.InvariantCulture),f.Token));
        var ownGrant=SqlRowStore.Id(Assert.Single(await unrelated.Store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",unrelated.RoleId}},f.Token)));
        var held=Signal<ulong>();var release=Signal<bool>();var deleteConnection=Signal<ulong>();var writeConnection=Signal<ulong>();
        var holder=f.Track(Task.Run(()=>unrelated.Session.InTransactionAsync(async()=>
        {
            var connection=await f.Probe.RequireRepeatableReadAsync(unrelated.Session.CurrentTransaction!,f.Token);
            await unrelated.Store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"id",unrelated.AccountId}},f.Token);
            await unrelated.Store.ReadForUpdateAsync("yz_role_permission",new Dictionary<string,object?>{{"id",ownGrant}},f.Token);held.SetResult(connection);
            await release.Task.WaitAsync(f.Token);return true;
        },f.Token)));
        await held.Task.WaitAsync(f.Token);
        var remove=f.Track(Task.Run(()=>deleting.Session.InTransactionAsync(async()=>
        {var current=await f.Probe.RequireRepeatableReadAsync(deleting.Session.CurrentTransaction!,f.Token);deleteConnection.SetResult(current);
            var other=await writeConnection.Task.WaitAsync(f.Token);Assert.NotEqual(current,other);Assert.NotEqual(current,await held.Task.WaitAsync(f.Token));return await source.Delete1692532858529329154Async(target,null,deleteIdentity.Actor,f.Token);},f.Token)));
        var save=f.Track(Task.Run(()=>writing.Session.InTransactionAsync(async()=>
        {var current=await f.Probe.RequireRepeatableReadAsync(writing.Session.CurrentTransaction!,f.Token);writeConnection.SetResult(current);
            var other=await deleteConnection.Task.WaitAsync(f.Token);Assert.NotEqual(current,other);Assert.NotEqual(current,await held.Task.WaitAsync(f.Token));
            f.Evidence["simultaneousConnections"]=new[]{current,other,await held.Task};await writer.SaveAsync(new(permissionTarget.ToString(CultureInfo.InvariantCulture),revision,new([],new(){[writing.RolePage]=["is_query"]},[])),f.Token);return true;},f.Token)));
        try
        {
            Assert.True(await remove.WaitAsync(f.Token));Assert.True(await save.WaitAsync(f.Token));
            var locks=await f.Probe.LocksAsync(await held.Task,f.Token);f.Evidence["unrelatedLocksStillHeld"]=locks;
            Assert.Contains(locks,row=>row.Table=="yz_manager"&&row.Type=="RECORD"&&row.Index=="PRIMARY"&&row.Status=="GRANTED"&&row.Data==unrelated.AccountId.ToString(CultureInfo.InvariantCulture));
            Assert.Contains(locks,row=>row.Table=="yz_role_permission"&&row.Type=="RECORD"&&row.Index=="PRIMARY"&&row.Status=="GRANTED"&&row.Data==ownGrant.ToString(CultureInfo.InvariantCulture));
            Assert.NotNull(Assert.Single(await deleting.Store.ReadIncludingDeletedAsync("yz_role",new Dictionary<string,object?>{{"id",target}},f.Token))["delete_no"]);
            Assert.Single(await writing.Store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",permissionTarget}},f.Token));f.Evidence["bothOperationsCommittedBeforeRelease"]=true;
        }
        catch
        {
            using var observation=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            if(deleteConnection.Task.IsCompletedSuccessfully)f.Evidence["deleteWaits"]=await f.Probe.WaitsAsync(await deleteConnection.Task,await held.Task,observation.Token);
            if(writeConnection.Task.IsCompletedSuccessfully)f.Evidence["workbenchWaits"]=await f.Probe.WaitsAsync(await writeConnection.Task,await held.Task,observation.Token);throw;
        }
        finally{release.TrySetResult(true);}
        await holder;
    });

    private const string ManagerPage="6031906822946816";
    /// <summary>失败先保留原异常并排空事务；清理失败与原异常同时报告，不用清理错误覆盖原证据。</summary>
    private static async Task Run(string name,Func<RrCase,Task> action)
    {
        var f=new RrCase(name);Exception? failure=null;try{await f.Initialize();await action(f);}catch(Exception error){failure=error;f.Evidence["failure"]=new{type=error.GetType().Name,hresult=error.HResult,mysql=(error as MySqlConnector.MySqlException)?.Number};}
        try{await f.DisposeAsync();}catch(Exception cleanup){if(failure is not null)throw new AggregateException(failure,cleanup);throw;}
        if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
    /// <summary>手工持锁仅为并发调度；项目→后台都按数值稳定顺序，实际业务随后仍走原协调器。</summary>
    private static async Task AggregateLocks(SqlRowStore store,string project,long[] admins,CancellationToken token)
    {Assert.Single(await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(project,CultureInfo.InvariantCulture)}},token));foreach(var id in admins.Distinct().Order())Assert.Single(await store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",id}},token));}
    /// <summary>异步调度信号不作锁等待证据，等待是否发生只由Probe的数据库观测决定。</summary>
    private static TaskCompletionSource<T> Signal<T>()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>读取真实工作台返回revision，不自行拼接权限摘要。</summary>
    private static string Revision(object snapshot)=>JsonSerializer.SerializeToElement(snapshot,JsonDefaults.Options).GetProperty("revision").GetString()!;
    /// <summary>只比较摘要，测试失败日志也不会打印账号的密码等列值。</summary>
    private static string RowsHash(IEnumerable<Dictionary<string,object?>> rows)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows.OrderBy(row=>SqlRowStore.Id(row)).Select(row=>row.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToDictionary()),JsonDefaults.Options)));
    /// <summary>全物理行只在内存中规范化为hash；固定两表和参数ID，不接受任意表或客户端SQL。</summary>
    private static async Task<string> RowHash(DatabaseSession session,string table,long id,CancellationToken token)
    {
        if(table is not ("yz_role" or "yz_manager"))throw new InvalidOperationException("未知整行验收表。");
        async Task<string> Read(DbConnection connection,DbTransaction? tx)
        {
            await using var command=connection.CreateCommand();command.Transaction=tx;command.CommandText="SELECT * FROM `"+table+"` WHERE `id`=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=id;command.Parameters.Add(parameter);
            await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new InvalidOperationException("自有验收记录不存在。");var row=new SortedDictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
            if(await reader.ReadAsync(token))throw new InvalidOperationException("自有主键返回多行。");return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row,JsonDefaults.Options)));
        }
        if(session.CurrentTransaction is{} current)return await Read(current.Connection!,current);await using var opened=session.Provider.CreateConnection(session.Options);await opened.OpenAsync(token);return await Read(opened,null);
    }

    /// <summary>只持有本用例随机账号/角色/session和额外role/admin；不写config_*或结构，不清理共享图。</summary>
    private sealed class RrCase(string name):IAsyncDisposable
    {
        private readonly CancellationTokenSource deadline=new(TimeSpan.FromSeconds(45));private readonly List<RoleSecurityFixture> actors=[];private readonly List<Task> tasks=[];private readonly List<IServiceScope> scopes=[];
        private readonly List<(string Table,long Id,string Name)> extras=[];private readonly string output=Path.Combine(Repository(),".local","security-realm-rr",name+"-"+Guid.NewGuid().ToString("N"));
        private SecurityRealmRrProbe? probe;private DatabaseSession? cleanupSession;private SqlRowStore? cleanupStore;internal SecurityRealmRrProbe Probe=>probe??throw new InvalidOperationException("观测器尚未初始化。");internal MetadataRegistry Registry{get;private set;}=null!;
        internal string Project=>Registry.Platform.YzProjectId;internal long Admin=>long.Parse(Registry.Platform.YzAdminTypes[0].Id,CultureInfo.InvariantCulture);internal CancellationToken Token=>deadline.Token;
        internal ConcurrentDictionary<string,object> Evidence{get;}=new(StringComparer.Ordinal);
        /// <summary>创建业务夹具前只做结构存在性检查，不以不存在的参数计划宣称真实路径达标。</summary>
        internal async Task Initialize()
        {
            Directory.CreateDirectory(output);using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!,Token));
            var options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var path=Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;Registry=new(path,Path.Combine(Path.GetDirectoryName(path)!,"demo.json"));probe=await SecurityRealmRrProbe.OpenAsync(options,Token);Evidence["server"]=new{pin=probe.ServerPin,version=probe.Version,database="metarill_test"};
            var shapes=await Probe.IndexShapesAsync(Token);Evidence["indexShapes"]=shapes;
            if(shapes.Any(shape=>shape.LeadingIndexes.Length==0))throw new InvalidOperationException("安全关联索引结构缺失，停止RR验收并保留SHOW INDEX摘要；尚未创建业务夹具。");
            cleanupSession=new DatabaseSession(options);cleanupStore=new(cleanupSession,Registry.Platform);
        }
        /// <summary>真实对象建好后、启动任何并发前，实际后台和角色全矩阵计划必须明确有界。</summary>
        internal async Task ActualPlans(long[] admins,long[] roles)
        {
            var plans=await Probe.IndexPlansAsync(Registry,admins.Distinct().ToArray(),roles.Distinct().ToArray(),Token);Evidence["actualFixtureIndexPlans"]=plans;
            if(plans.Any(plan=>plan.ChosenIndex is null||!plan.LeadingIndexes.Contains(plan.ChosenIndex,StringComparer.Ordinal)||plan.Access is not ("ref" or "range" or "const" or "eq_ref")))throw new InvalidOperationException("实际fixture参数的安全查询计划未达标，停止并保留EXPLAIN，不注入索引提示或造数据调整分布。");
        }
        /// <summary>沿现有真实角色夹具建立独立Session和登录；不需要HTTP时不额外启动TestServer。</summary>
        internal async Task<RoleSecurityFixture> Actor(bool super,bool demo=false){var actor=await RoleSecurityFixture.CreateAsync(demo,super,false);actors.Add(actor);return actor;}
        /// <summary>每条实际调用保持同Session、真实认证上下文和产品服务注册，生命周期延后至事务排空。</summary>
        internal IServiceProvider Scope(RoleSecurityFixture actor,AuthenticatedIdentity identity){var scope=actor.RequestScope(identity);scopes.Add(scope);return scope.ServiceProvider;}
        /// <summary>异步业务必须登记，任何退出都先取消并排空后才清理owned行。</summary>
        internal Task<T> Track<T>(Task<T> task){tasks.Add(task);return task;}
        /// <summary>无账号引用的随机目标角色只用于引用检查；创建意图先记录以处理不确定回执。</summary>
        internal Task<long> ExtraRole(SqlRowStore store)=>Extra(store,"yz_role");
        /// <summary>同项目的随机后台供合法账号迁后台场景，绝不修改已有后台。</summary>
        internal Task<long> ExtraAdmin(SqlRowStore store)=>Extra(store,"yz_admin_type");
        /// <summary>固定两表的私有fixture初始化，不走配置表，不把初始化当产品Service验收。</summary>
        private async Task<long> Extra(SqlRowStore store,string table)
        {
            var id=RoleSecurityFixture.NextFixtureId();var marker="mt_rr_"+Guid.NewGuid().ToString("N")[..18];extras.Add((table,id,marker));var row=new Dictionary<string,object?>{{"id",id},{"name",marker},{"sort_no",1},{"yz_project_id",long.Parse(Project,CultureInfo.InvariantCulture)}};
            if(table=="yz_role"){row["yz_admin_type_id"]=Admin;row["is_super"]=0;row["is_built_in"]=0;}await store.InsertAsync(table,row,Token);return id;
        }
        /// <summary>先排空本用例所有任务，超时保留现场；只有已知自有行才可按精确主键/FK清理。</summary>
        public async ValueTask DisposeAsync()
        {
            deadline.Cancel();var draining=true;foreach(var task in tasks)try{await task.WaitAsync(TimeSpan.FromSeconds(10));}catch(TimeoutException){draining=false;}catch{if(!task.IsCompleted)draining=false;}
            Evidence["owned"]=new{accounts=actors.Select(actor=>actor.AccountId).ToArray(),roles=actors.Select(actor=>actor.RoleId).ToArray(),extra=extras.Select(row=>new{table=row.Table,id=row.Id}).ToArray()};
            if(!draining){Evidence["cleanup"]="事务未排空，保留owned现场，禁止并发清理。";await Save();if(probe is not null)await probe.DisposeAsync();throw new InvalidOperationException("RR业务事务尚未排空，已保留现场。");}
            foreach(var scope in scopes)scope.Dispose();
            try
            {
                foreach(var row in extras.Where(row=>row.Table=="yz_role"))await Cleanup(row);
                foreach(var actor in actors)await actor.DisposeAsync();
                foreach(var row in extras.Where(row=>row.Table=="yz_admin_type"))await Cleanup(row);
                if(cleanupStore is not null)foreach(var actor in actors)
                {
                    foreach(var(table,key,id)in new[]{("yz_manager","id",actor.AccountId),("yz_role","id",actor.RoleId),("sys_session","account_id",actor.AccountId),("yz_role_permission","yz_role_id",actor.RoleId)})
                        if((await cleanupStore.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{key,id}})).Count!=0)throw new InvalidOperationException("自有角色夹具清理后仍有残留，已保留精确主键回执。");
                }
                Evidence["cleanup"]="仅自有主键/role外键清理完成且零残留核验通过。";
            }
            catch{Evidence["cleanup"]="自有清理失败，保留记录等待核验。";throw;}
            finally{await Save();cleanupSession?.Dispose();if(probe is not null)await probe.DisposeAsync();deadline.Dispose();}
        }
        /// <summary>先验证随机名称归属，角色授权只删除该owned角色的精确FK；未知名称不删。</summary>
        private async Task Cleanup((string Table,long Id,string Name) row)
        {
            var session=cleanupSession??throw new InvalidOperationException("没有自有清理Session。");
            await session.InTransactionAsync(async()=>
            {
                await Probe.RequireRepeatableReadAsync(session.CurrentTransaction!,CancellationToken.None);
                var transaction=session.CurrentTransaction!;
                await using(var owner=transaction.Connection!.CreateCommand())
                {
                    owner.Transaction=transaction;owner.CommandText="SELECT `name` FROM `"+row.Table+"` WHERE `id`=@id FOR UPDATE";var identifier=owner.CreateParameter();identifier.ParameterName="id";identifier.Value=row.Id;owner.Parameters.Add(identifier);
                    var actual=await owner.ExecuteScalarAsync();if(actual is null)return true;if(Convert.ToString(actual,CultureInfo.InvariantCulture)!=row.Name)throw new InvalidOperationException("额外fixture归属不符，禁止删除。");
                }
                async Task Delete(string table,string key)
                {
                    await using var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;command.CommandText="DELETE FROM `"+table+"` WHERE `"+key+"`=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=row.Id;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();
                }
                if(row.Table=="yz_role")await Delete("yz_role_permission","yz_role_id");await Delete(row.Table,"id");
                await using var verify=transaction.Connection!.CreateCommand();verify.Transaction=transaction;verify.CommandText="SELECT COUNT(*) FROM `"+row.Table+"` WHERE `id`=@id";var id=verify.CreateParameter();id.ParameterName="id";id.Value=row.Id;verify.Parameters.Add(id);
                if(Convert.ToInt32(await verify.ExecuteScalarAsync(),CultureInfo.InvariantCulture)!=0)throw new InvalidOperationException("自有fixture清理未通过核验。");return true;
            });
        }
        /// <summary>只保存结构化锁元数据、随机主键、指纹与异常类型；不存连接串、令牌或全行内容。</summary>
        private Task Save()=>File.WriteAllTextAsync(Path.Combine(output,"receipt.json"),JsonSerializer.Serialize(Evidence,new JsonSerializerOptions(JsonDefaults.Options){WriteIndented=true}));
        /// <summary>源码定位只用于本机私有证据目录，不上传或作为数据库授权。</summary>
        private static string Repository([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    }
}
