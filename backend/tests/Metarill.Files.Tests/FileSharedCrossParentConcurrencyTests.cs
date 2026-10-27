using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>共享稀疏父范围使用真实文件观察器、旧RR快照及独立竞争连接，不替代正式账号或浏览器验收。</summary>
[Collection(FilesIntegrationCollection.Name)]
public sealed class FileSharedCrossParentConcurrencyTests
{
    /// <summary>字符串所选ID与字符串父键。</summary>
    [FilesIntegrationFact] public Task String_string()=>Run("string","string");
    /// <summary>字符串所选ID与整数父键。</summary>
    [FilesIntegrationFact] public Task String_long()=>Run("string","int64");
    /// <summary>整数所选ID与字符串父键。</summary>
    [FilesIntegrationFact] public Task Long_string()=>Run("int64","string");
    /// <summary>整数所选ID与整数父键。</summary>
    [FilesIntegrationFact] public Task Long_long()=>Run("int64","int64");
    /// <summary>字符串短主叶由仓库提供共同祖先。</summary>
    [FilesIntegrationFact] public Task Short_string()=>Run("string","int64",true);
    /// <summary>整数短主叶由仓库提供共同祖先。</summary>
    [FilesIntegrationFact] public Task Short_long()=>Run("int64","string",true);
    /// <summary>固定派生名称目标保持物理父键。</summary>
    [FilesIntegrationFact] public Task Derived_targets()=>Run("string","int64",false,true);

    /// <summary>全局共同祖先与独立叶子逐个验证真实RR、当前读锁及文件来源项目隔离。</summary>
    [FilesIntegrationFact]public Task Global_root()=>Run("string","string",globalMask:1);
    [FilesIntegrationFact]public Task Global_middle()=>Run("string","int64",globalMask:2,noProject:true);
    [FilesIntegrationFact]public Task Global_place()=>Run("int64","string",globalMask:4);
    [FilesIntegrationFact]public Task Global_warehouse()=>Run("int64","int64",globalMask:8,noProject:true);
    [FilesIntegrationFact]public Task Global_all()=>Run("int64","int64",globalMask:15,noProject:true);
    [FilesIntegrationFact]public Task Global_short_root()=>Run("string","int64",true,globalMask:1);
    [FilesIntegrationFact]public Task Global_short_all()=>Run("int64","string",true,globalMask:11,noProject:true);

    /// <summary>全部模型共用同一证据流程；保护授权来自当前记录，不提交请求祖先或parentId。</summary>
    private static async Task Run(string type,string parent,bool shorter=false,bool derived=false,int globalMask=0,bool noProject=false)
    {
        await using var f=await SharedFileConcurrencyFixture.CreateAsync(type,parent,shorter,derived,globalMask,noProject);
        var anchor=await f.UploadAsync("anchor");var replacement=await f.UploadAsync("replacement");
        await f.BindAsync(anchor);await f.ReadableAsync(anchor,true);await f.AssertForeignSourceDeniedAsync(replacement);
        var before=await f.StateAsync();
        foreach(var target in f.SelectedTargets)
        foreach(var deleting in new[]{false,true})
        {
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InReadSnapshotAsync(async()=>
                {
                    Assert.Equal(System.Data.IsolationLevel.RepeatableRead,f.Session.CurrentTransaction!.IsolationLevel);
                    Assert.True(Convert.ToBoolean(Assert.Single(await f.Store.ReadAsync(target.Table,D("id",target.Id)))["enabled"],CultureInfo.InvariantCulture));
                    using var other=new DatabaseSession(f.Session.Options);await new SqlRowStore(other,f.Combined).UpdateAsync(target.Table,target.Id,D("enabled",false));
                    // 明确证明旧快照仍看见允许值，不能把新连接的拒绝误算成当前读证明。
                    Assert.True(Convert.ToBoolean(Assert.Single(await f.Store.ReadAsync(target.Table,D("id",target.Id)))["enabled"],CultureInfo.InvariantCulture));
                    if(deleting)
                    {
                        await f.Observer().BeforePhysicalDeleteAsync(f.Context(anchor,true));
                        throw new InvalidOperationException("已失效的当前父范围必须在物理DELETE前拒绝取得证明。");
                    }
                    await f.Store.UpdateAsync(f.Source,f.Record,D("name","旧快照不得提交","attachments",Ids(replacement)));
                    await f.Observer().BeforeCommitAsync(f.Context(replacement));return true;
                }));
            }
            finally{await f.Store.UpdateAsync(target.Table,target.Id,D("enabled",true));}
            Assert.Equal(before,await f.StateAsync());await f.ReadableAsync(anchor,true);
        }
        await f.ReceiptAsync("old-rr",new{targets=f.SelectedTargets,operations=new[]{"reference-write","before-physical-delete"},sourceAndReferencesRolledBack=true,oldSnapshotActuallyObserved=true});

        await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {
            await f.Store.UpdateAsync(f.Source,f.Record,D("name","真实引用尾部回滚","attachments",Ids(replacement)));
            await f.Observer().BeforeCommitAsync(f.Context(replacement));
            await AssertLocks(f,includeFile:replacement);await WaitingWriter(f,true);
            Assert.Empty(await f.Store.ReadAsync("yz_file_ref",D("file_id",anchor)));
            Assert.Single(await f.Store.ReadAsync("yz_file_ref",D("file_id",replacement)));
            throw new TailFailure();
        }));
        Assert.Equal(before,await f.StateAsync());await AssertReleased(f,replacement);await WaitingWriter(f,false);Assert.Equal(before,await f.StateAsync());
        await f.BindAsync(replacement);await f.ReadableAsync(replacement,true);await f.ReadableAsync(anchor,false);
        before=await f.StateAsync();
        await f.ReceiptAsync("write-locks",new{allSelectedTargetsContended=true,unselectedRecordAndWarehouseFree=true,waitingWriterError=1205,releasedAfterRollback=true,referenceTailRollback=true});

        // 删除前证明后在原事务重挂仓库；短控件的整组合法换根也必须与原删除证明一致。
        var observer=f.Observer();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
        {
            var context=f.Context(replacement,true);await observer.BeforePhysicalDeleteAsync(context);await f.DeleteRecordAsync();
            await f.Store.UpdateAsync(f.Warehouse,f.WarehouseId,D("area_id",f.ParentValue(f.OtherAreaId)));
            await observer.BeforeCommitAsync(context);return true;
        }));
        Assert.Equal(before,await f.StateAsync());
        await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {
            var context=f.Context(replacement,true);var current=f.Observer();await current.BeforePhysicalDeleteAsync(context);
            await AssertLocks(f);await WaitingWriter(f,true);await f.DeleteRecordAsync();await current.BeforeCommitAsync(context);
            await AssertLocks(f,sourceExists:false);Assert.Empty(await f.ReferencesAsync());throw new TailFailure();
        }));
        Assert.Equal(before,await f.StateAsync());await AssertReleased(f);await WaitingWriter(f,false);Assert.Equal(before,await f.StateAsync());
        await f.Session.InTransactionAsync(async()=>
        {
            var context=f.Context(replacement,true);var current=f.Observer();await current.BeforePhysicalDeleteAsync(context);
            await f.DeleteRecordAsync();await current.BeforeCommitAsync(context);return true;
        });
        Assert.Empty(await f.Store.ReadIncludingDeletedAsync(f.Source,D("id",f.Record)));Assert.Empty(await f.ReferencesAsync());
        await f.ReadableAsync(replacement,false);f.Verified=true;
        await f.ReceiptAsync("verified",new{type,parent,shorter,derived,globalMask,noProject,foreignSourceDenied=true,oldRrCases=f.SelectedTargets.Count*2,oldRrWriteCases=f.SelectedTargets.Count,oldRrDeleteCases=f.SelectedTargets.Count,writeAndDeleteLocks=true,physicalDeleteReparentDenied=true,deleteTailRollback=true,normalPhysicalDelete=true,formalIdentity=false,browser=false,performance=false});
    }

    /// <summary>真实来源、每个已存分支的父行和新文件行均须竞争失败；未选末端及另一来源必须可加锁。</summary>
    private static async Task AssertLocks(SharedFileConcurrencyFixture f,long? includeFile=null,bool sourceExists=true)
    {
        if(sourceExists)await Compete(f,f.Source,f.Record,true);
        foreach(var target in f.SelectedTargets)await Compete(f,target.Table,target.Id,true);
        if(includeFile is{} file)await Compete(f,"yz_file_library",file,true);
        await Compete(f,f.Warehouse,f.OtherWarehouseId,false);await Compete(f,f.Source,f.OtherRecord,false);
    }
    /// <summary>事务退出后在独立连接确认相同节点可锁，不以没有异常代替释放核验。</summary>
    private static async Task AssertReleased(SharedFileConcurrencyFixture f,long? file=null)
    {
        await Compete(f,f.Source,f.Record,false);foreach(var target in f.SelectedTargets)await Compete(f,target.Table,target.Id,false);
        if(file is{} id)await Compete(f,"yz_file_library",id,false);
    }
    /// <summary>NOWAIT精确错误证明锁冲突，参数化ID及已冻结自有表名不暴露任意SQL。</summary>
    private static async Task Compete(SharedFileConcurrencyFixture f,string table,long id,bool locked)
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
    private static async Task WaitingWriter(SharedFileConcurrencyFixture f,bool blocked)
    {
        var options=new MySqlConnectionStringBuilder(f.Session.Options.ConnectionString){Pooling=false};
        await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();
        await using(var setting=connection.CreateCommand()){setting.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setting.ExecuteNonQueryAsync();}
        await using var transaction=await connection.BeginTransactionAsync();
        try
        {
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandTimeout=10;
            command.CommandText="UPDATE "+f.Session.Provider.QuoteIdentifier(f.Warehouse)+" SET enabled=NOT enabled WHERE id=@id";command.Parameters.AddWithValue("id",f.WarehouseId);
            if(blocked)Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteNonQueryAsync())).Number);
            else
            {
                Assert.Equal(1,await command.ExecuteNonQueryAsync());
                // 释放后确实执行了写入，独立事务finally回滚，不能用空更新冒充写竞争。
                command.CommandText="SELECT enabled FROM "+f.Session.Provider.QuoteIdentifier(f.Warehouse)+" WHERE id=@id";
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
