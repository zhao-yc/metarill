using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>R1实际物理父范围使用真实文件观察器、旧RR快照及独立竞争连接，不替代正式账号或浏览器验收。</summary>
[Collection(FilesIntegrationCollection.Name)]
public sealed class FileRedundantSharedParentConcurrencyTests
{
    /// <summary>字符串所选ID与字符串父键。</summary>
    [FilesIntegrationFact] public Task String_string()=>Run("string","string");
    /// <summary>字符串所选ID与整数父键。</summary>
    [FilesIntegrationFact] public Task String_long()=>Run("string","int64");
    /// <summary>整数所选ID与字符串父键。</summary>
    [FilesIntegrationFact] public Task Long_string()=>Run("int64","string");
    /// <summary>整数所选ID与整数父键。</summary>
    [FilesIntegrationFact] public Task Long_long()=>Run("int64","int64");
    /// <summary>字符串短分支保留实际保存的祖先父键。</summary>
    [FilesIntegrationFact] public Task Short_string()=>Run("string","int64",true);
    /// <summary>整数短分支保留实际保存的祖先父键。</summary>
    [FilesIntegrationFact] public Task Short_long()=>Run("int64","string",true);
    /// <summary>固定派生名称目标保持物理父键。</summary>
    [FilesIntegrationFact] public Task Derived_targets()=>Run("string","int64",false,true);

    /// <summary>显式全局根目标逐页验证真实RR、当前读锁及文件来源项目隔离。</summary>
    [FilesIntegrationFact]public Task Global_root()=>Run("string","string",globalMask:1);
    /// <summary>全局中间目标可无项目列，仍使用当前实际路径。</summary>
    [FilesIntegrationFact]public Task Global_middle()=>Run("string","int64",globalMask:2,noProject:true);
    /// <summary>全局城市末端使用已保存的物理父键。</summary>
    [FilesIntegrationFact]public Task Global_place()=>Run("int64","string",globalMask:4);
    /// <summary>全局仓库末端逐页验证删除锁与回滚。</summary>
    [FilesIntegrationFact]public Task Global_warehouse()=>Run("int64","int64",globalMask:8,noProject:true);
    /// <summary>全部目标显式全局且无项目列，来源项目仍隔离。</summary>
    [FilesIntegrationFact]public Task Global_all()=>Run("int64","int64",globalMask:15,noProject:true);
    /// <summary>短主叶配合全局国家，不借用仓库推导实际父。</summary>
    [FilesIntegrationFact]public Task Global_short_root()=>Run("string","int64",true,globalMask:1);
    /// <summary>全部参与层全局的短分支仍逐页验证物理父。</summary>
    [FilesIntegrationFact]public Task Global_short_all()=>Run("int64","string",true,globalMask:11,noProject:true);

    /// <summary>四键、长短分支、派生及普通／全局目标逐个执行五父页，不复用R0稀疏断言。</summary>
    private static async Task Run(string type,string parent,bool shorter=false,bool derived=false,int globalMask=0,bool noProject=false)
    {
        await using var f=await RedundantFileConcurrencyFixture.CreateAsync(type,parent,shorter,derived,globalMask,noProject);
        var targetRr=0;var sourceRr=0;var reparents=0;
        foreach(var page in new[]{"101","102","103","104","105"})
        {
            await f.NextPageAsync(page);await RunPage(f);targetRr+=f.SelectedTargets.Count*2;sourceRr+=(page=="105"?3:2)*2;if(f.Role>0)reparents++;
        }
        f.Verified=true;await f.ReceiptAsync("verified",new{type,parent,shorter,derived,globalMask,noProject,parentPages=5,targetRrCases=targetRr,sourceRrCases=sourceRr,legalPhysicalParentReparents=reparents,unrelatedTargetChanges=5,writeAndDeleteLocks=true,sourceDeleteLockHeld=true,currentPhysicalParentAuthoritative=true,invalidatedDeleteProofDenied=true,lateWriteAndDeleteRollback=true,normalPhysicalDelete=true,formalIdentity=false,browser=false,performance=false});
    }
    /// <summary>同一父页的旧快照、锁、实际删除证明及尾部回滚共用一个完整状态比较。</summary>
    private static async Task RunPage(RedundantFileConcurrencyFixture f)
    {
        await f.ReceiptAsync("last-page",new{f.PageId,f.Role,f.ScopeField});var anchor=await f.UploadAsync(f.PageId+"-anchor");var replacement=await f.UploadAsync(f.PageId+"-replacement");
        await f.BindAsync(anchor);await f.ReadableAsync(anchor,true);await f.AssertForeignSourceDeniedAsync(replacement);var before=await f.StateAsync();
        foreach(var target in f.SelectedTargets)foreach(var deleting in new[]{false,true})
        {
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InReadSnapshotAsync(async()=>
                {
                    Assert.Equal(System.Data.IsolationLevel.RepeatableRead,f.Session.CurrentTransaction!.IsolationLevel);
                    Assert.True(Convert.ToBoolean(Assert.Single(await f.Store.ReadAsync(target.Table,D("id",target.Id)))["enabled"],CultureInfo.InvariantCulture));
                    using var other=new DatabaseSession(f.Session.Options);var current=new SqlRowStore(other,f.Combined);await current.UpdateAsync(target.Table,target.Id,D("enabled",false));
                    Assert.False(Convert.ToBoolean(Assert.Single(await current.ReadAsync(target.Table,D("id",target.Id)))["enabled"],CultureInfo.InvariantCulture));
                    Assert.True(Convert.ToBoolean(Assert.Single(await f.Store.ReadAsync(target.Table,D("id",target.Id)))["enabled"],CultureInfo.InvariantCulture));
                    await Mutation(replacement,deleting);return true;
                }));
            }
            finally{await f.Store.UpdateAsync(target.Table,target.Id,D("enabled",true));}
            Assert.Equal(before,await f.StateAsync());await f.ReadableAsync(anchor,true);
        }
        // 来源自身也必须当前读：项目、实际物理父键及混合owner均不能借用旧RR记录。
        var source=Assert.Single(await f.Store.ReadAsync(f.Source,D("id",f.Record)));
        var changes=new List<(string Field,object Value)>{("yz_project_id",2L),(f.ScopeField,f.SelectedValue(long.MaxValue))};
        if(f.PageId=="105")changes.Add(("owner_country_id",f.SelectedValue(f.OtherTargets[0].Id)));
        foreach(var change in changes)foreach(var deleting in new[]{false,true})
        {
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InReadSnapshotAsync(async()=>
                {
                    Assert.Equal(System.Data.IsolationLevel.RepeatableRead,f.Session.CurrentTransaction!.IsolationLevel);
                    var old=Assert.Single(await f.Store.ReadAsync(f.Source,D("id",f.Record)));Assert.Equal(SqlRowStore.Text(source,change.Field),SqlRowStore.Text(old,change.Field));
                    using var other=new DatabaseSession(f.Session.Options);var current=new SqlRowStore(other,f.Combined);await current.UpdateAsync(f.Source,f.Record,D(change.Field,change.Value));
                    Assert.Equal(Convert.ToString(change.Value,CultureInfo.InvariantCulture),SqlRowStore.Text(Assert.Single(await current.ReadAsync(f.Source,D("id",f.Record))),change.Field));
                    Assert.Equal(SqlRowStore.Text(old,change.Field),SqlRowStore.Text(Assert.Single(await f.Store.ReadAsync(f.Source,D("id",f.Record))),change.Field));
                    await Mutation(replacement,deleting);return true;
                }));
            }
            finally{await f.Store.UpdateAsync(f.Source,f.Record,D(change.Field,source[change.Field]));}
            Assert.Equal(before,await f.StateAsync());await f.ReadableAsync(anchor,true);
        }
        await f.ReceiptAsync("rr-"+f.PageId,new{f.PageId,actualOldTargetAndSourceObserved=true,targetCases=f.SelectedTargets.Count*2,sourceCases=changes.Count*2,writeAndBeforePhysicalDeleteDenied=true,rollbackSnapshotEqual=true});
        await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {
            await f.Store.UpdateAsync(f.Source,f.Record,D("name","R1真实引用尾部回滚","attachments",Ids(replacement)));await f.Observer().BeforeCommitAsync(f.Context(replacement));
            await AssertLocks(f,includeFile:replacement);await WaitingWriter(f,true);Assert.Empty(await f.Store.ReadAsync("yz_file_ref",D("file_id",anchor)));Assert.Single(await f.Store.ReadAsync("yz_file_ref",D("file_id",replacement)));throw new TailFailure();
        }));
        Assert.Equal(before,await f.StateAsync());await AssertReleased(f,replacement);await WaitingWriter(f,false);Assert.Equal(before,await f.StateAsync());
        await f.BindAsync(replacement);await f.ReadableAsync(replacement,true);await f.ReadableAsync(anchor,false);before=await f.StateAsync();
        // 删除后同一实际父节点被禁用，仍应拒绝解绑并回滚真实DELETE。
        await RejectDelete(D("enabled",false),f.ScopeTarget.Table,f.ScopeTarget.Id);
        if(f.Role>0)
        {
            var field=f.Role==1?"country_id":"area_id";await RejectDelete(D(field,f.ParentValue(long.MaxValue)),f.ScopeTarget.Table,f.ScopeTarget.Id);
            // 合法换上级不改变R1已保存的实际父身份；原缓存祖先不能代替当前路径，也不能误拒绝。
            await AllowDeleteThenRollback(D(field,f.ParentValue(f.OtherTargets[f.Role==1?0:1].Id)),f.ScopeTarget.Table,f.ScopeTarget.Id);
        }
        var unused=f.UnselectedTargets.First();await AllowDeleteThenRollback(D("enabled",false),unused.Table,unused.Id);
        await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {
            var context=f.Context(replacement,true);var observer=f.Observer();await observer.BeforePhysicalDeleteAsync(context);await AssertLocks(f);await WaitingWriter(f,true);
            await f.DeleteRecordAsync();await observer.BeforeCommitAsync(context);await AssertLocks(f);Assert.Empty(await f.ReferencesAsync());throw new TailFailure();
        }));
        Assert.Equal(before,await f.StateAsync());await AssertReleased(f);await WaitingWriter(f,false);Assert.Equal(before,await f.StateAsync());
        await f.Session.InTransactionAsync(async()=>{var context=f.Context(replacement,true);var observer=f.Observer();await observer.BeforePhysicalDeleteAsync(context);await f.DeleteRecordAsync();await observer.BeforeCommitAsync(context);return true;});
        Assert.Empty(await f.Store.ReadIncludingDeletedAsync(f.Source,D("id",f.Record)));Assert.Empty(await f.ReferencesAsync());await f.ReadableAsync(replacement,false);
        await f.ReceiptAsync("page-"+f.PageId,new{f.PageId,f.Role,physicalKeyAuthoritative=true,sourceAndTargetsCurrentRead=true,actualPathAndSourceLocks=true,unrelatedTargetsFree=true,waitingWriterError=1205,writerActuallyChangedThenRolledBack=true,writeTailRollback=true,deleteTailRollback=true,invalidDeleteTargetDenied=true,validReparentDoesNotRedefinePhysicalKey=f.Role>0,normalDelete=true});
        // 不提供请求父值；故意修改真实业务字段后，由观察器当前读独立拒绝。
        async Task Mutation(long file,bool deleting)
        {
            if(deleting){await f.Observer().BeforePhysicalDeleteAsync(f.Context(file,true));throw new InvalidOperationException("失效实际父范围不得取得物理删除证明。");}
            await f.Store.UpdateAsync(f.Source,f.Record,D("name","旧快照不得提交","attachments",Ids(file)));await f.Observer().BeforeCommitAsync(f.Context(file));
        }
        // 准备时合法、DELETE后失效的实际路径必须整笔回滚，不接受已取得证明即跳过复查。
        async Task RejectDelete(Dictionary<string,object?> values,string table,long key)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>{var context=f.Context(replacement,true);var observer=f.Observer();await observer.BeforePhysicalDeleteAsync(context);await f.DeleteRecordAsync();await f.Store.UpdateAsync(table,key,values);await observer.BeforeCommitAsync(context);return true;}));Assert.Equal(before,await f.StateAsync());
        }
        // 正向合同也必须通过：同一真实父键的合法祖先变化及无关分支失效不会误删文件权限；尾部回滚保存样本。
        async Task AllowDeleteThenRollback(Dictionary<string,object?> values,string table,long key)
        {
            await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync<bool>(async()=>{var context=f.Context(replacement,true);var observer=f.Observer();await observer.BeforePhysicalDeleteAsync(context);await f.DeleteRecordAsync();await f.Store.UpdateAsync(table,key,values);await observer.BeforeCommitAsync(context);Assert.Empty(await f.ReferencesAsync());throw new TailFailure();}));Assert.Equal(before,await f.StateAsync());
        }
    }

    /// <summary>真实来源、实际父路径及新文件均须竞争失败；无关分支、未选节点及另一来源须可锁。</summary>
    private static async Task AssertLocks(RedundantFileConcurrencyFixture f,long? includeFile=null)
    {
        await Compete(f,f.Source,f.Record,true);
        foreach(var target in f.SelectedTargets)await Compete(f,target.Table,target.Id,true);
        if(includeFile is{} file)await Compete(f,"yz_file_library",file,true);
        foreach(var target in f.UnselectedTargets)await Compete(f,target.Table,target.Id,false);await Compete(f,f.Warehouse,f.OtherWarehouseId,false);await Compete(f,f.Source,f.OtherRecord,false);
    }
    /// <summary>事务退出后在独立连接确认相同节点可锁，不以没有异常代替释放核验。</summary>
    private static async Task AssertReleased(RedundantFileConcurrencyFixture f,long? file=null)
    {
        await Compete(f,f.Source,f.Record,false);foreach(var target in f.SelectedTargets)await Compete(f,target.Table,target.Id,false);
        if(file is{} id)await Compete(f,"yz_file_library",id,false);
    }
    /// <summary>NOWAIT精确错误证明锁冲突，参数化ID及已冻结自有表名不暴露任意SQL。</summary>
    private static async Task Compete(RedundantFileConcurrencyFixture f,string table,long id,bool locked)
    {
        await using var connection=new MySqlConnection(f.Session.Options.ConnectionString);await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        try
        {
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT id FROM "+f.Session.Provider.QuoteIdentifier(table)+" WHERE id=@id FOR UPDATE NOWAIT";command.Parameters.AddWithValue("id",id);
            if(locked)Assert.Equal(3572,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteScalarAsync())).Number);
            else Assert.Equal(id,Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));
        }
        finally{await transaction.RollbackAsync();}
    }
    /// <summary>真实UPDATE等待至1205；独立非池化连接的会话超时不影响其他测试连接或服务器全局配置。</summary>
    private static async Task WaitingWriter(RedundantFileConcurrencyFixture f,bool blocked)
    {
        var options=new MySqlConnectionStringBuilder(f.Session.Options.ConnectionString){Pooling=false};
        await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();
        await using(var setting=connection.CreateCommand()){setting.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setting.ExecuteNonQueryAsync();}
        await using var transaction=await connection.BeginTransactionAsync();
        try
        {
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandTimeout=10;
            command.CommandText="UPDATE "+f.Session.Provider.QuoteIdentifier(f.ScopeTarget.Table)+" SET enabled=NOT enabled WHERE id=@id";command.Parameters.AddWithValue("id",f.ScopeTarget.Id);
            if(blocked)Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteNonQueryAsync())).Number);
            else
            {
                Assert.Equal(1,await command.ExecuteNonQueryAsync());
                // 释放后确实执行了写入，独立事务finally回滚，不能用空更新冒充写竞争。
                command.CommandText="SELECT enabled FROM "+f.Session.Provider.QuoteIdentifier(f.ScopeTarget.Table)+" WHERE id=@id";
                Assert.False(Convert.ToBoolean(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));
            }
        }
        finally{await transaction.RollbackAsync();}
    }
    /// <summary>文件ID按原字符串数组协议序列化。</summary>
    private static string Ids(long id)=>JsonSerializer.Serialize(new[]{id.ToString(CultureInfo.InvariantCulture)});
    /// <summary>测试值保留明确null，交由现有行存储参数绑定。</summary>
    private static Dictionary<string,object?> D(params object?[] values)=>Enumerable.Range(0,values.Length/2).ToDictionary(i=>(string)values[i*2]!,i=>values[i*2+1]);
    private sealed class TailFailure:Exception;
}
