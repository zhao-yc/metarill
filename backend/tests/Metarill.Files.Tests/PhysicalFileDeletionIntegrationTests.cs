using System.Text;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>物理删除只消费本观察器的真实当前锁证明；全部样本属于pin实例的随机业务表。</summary>
[Collection(FilesIntegrationCollection.Name)]
public sealed class PhysicalFileDeletionIntegrationTests
{
    /// <summary>删行／解绑和尾部失败共同回滚，成功后文件才可物理移除；证明不可重放。</summary>
    [FilesIntegrationFact]
    public async Task Physical_delete_and_reference_tombstone_roll_back_together_then_file_can_be_removed()
    {
        await using var f=await FilesFixture.CreateAsync(childScope:true,physicalDelete:true);var (record,file)=await Seed(f);var observer=f.Observer();var before=await Snapshot(f,record);
        await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync(async()=>{var context=Context(f,record);await observer.BeforePhysicalDeleteAsync(context);await Delete(f,record);await observer.BeforeCommitAsync(context);return await Task.FromException<bool>(new TailFailure());}));Assert.Equal(before,await Snapshot(f,record));
        await f.Session.InTransactionAsync(async()=>{var context=Context(f,record);await observer.BeforePhysicalDeleteAsync(context);await Delete(f,record);await observer.BeforeCommitAsync(context);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>observer.BeforeCommitAsync(context));return true;});
        Assert.Empty(await f.Store.ReadAsync(f.TableName,new Dictionary<string,object?>{{"id",record}}));Assert.Empty(await f.ReferencesAsync(record));Assert.True((await f.Service().DeleteAsync(file.FileId)).StorageRemoved);
    }
    /// <summary>缺准备、换观察器／事务／身份、重复准备和源行仍存在均不能清引用。</summary>
    [FilesIntegrationFact]
    public async Task Proof_is_bound_to_original_observer_transaction_identity_and_deleted_real_row()
    {
        await using var f=await FilesFixture.CreateAsync(childScope:true,physicalDelete:true);var (record,file)=await Seed(f);var auth=new MutableAuthorization(f.Actor());var observer=new FileReferenceMutationObserver(f.Store,new([f.Registry.Platform,f.Registry.Demo]),auth,f.Ids,f.Options);var before=await Snapshot(f,record);
        await Reject<UnauthorizedAccessException>(async context=>{await Delete(f,record);await observer.BeforeCommitAsync(context);});
        await Reject<UnauthorizedAccessException>(async context=>{await observer.BeforePhysicalDeleteAsync(context);await Delete(f,record);await f.Observer().BeforeCommitAsync(context);});
        await Reject<InvalidOperationException>(async context=>{await observer.BeforePhysicalDeleteAsync(context);await observer.BeforeCommitAsync(context);});
        await Reject<InvalidOperationException>(async context=>{await observer.BeforePhysicalDeleteAsync(context);await observer.BeforePhysicalDeleteAsync(context);});
        await f.Session.InTransactionAsync(async()=>{await observer.BeforePhysicalDeleteAsync(Context(f,record));return true;});
        await Reject<UnauthorizedAccessException>(async context=>{await Delete(f,record);await observer.BeforeCommitAsync(context);});
        await Reject<UnauthorizedAccessException>(async context=>{await observer.BeforePhysicalDeleteAsync(context);await Delete(f,record);auth.Actor=auth.Actor with{AccountId=auth.Actor.AccountId+1};try{await observer.BeforeCommitAsync(context);}finally{auth.Actor=f.Actor();}});
        await Reject<UnauthorizedAccessException>(context=>observer.BeforePhysicalDeleteAsync(context with{ProjectId="999999"}));
        auth.Deny=true;try{await Reject<UnauthorizedAccessException>(context=>observer.BeforePhysicalDeleteAsync(context));}finally{auth.Deny=false;}
        await Reject<InvalidOperationException>(context=>observer.BeforePhysicalDeleteAsync(context with{Transaction=null!}));
        await Reject<UnauthorizedAccessException>(context=>observer.BeforePhysicalDeleteAsync(context with{TableName=f.ParentTableName}));
        await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"yz_project_id",999999L}});before=await Snapshot(f,record);await Reject<UnauthorizedAccessException>(context=>observer.BeforePhysicalDeleteAsync(context));await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"yz_project_id",long.Parse(f.Actor().ProjectId)}});before=await Snapshot(f,record);
        // 固定页面常量也在准备阶段绑定，不能因已知ID而省略真实页面范围。
        var page=f.Registry.Platform.ConfigPages.Single(page=>page.Id==f.PageId);page.WhereSql+=" AND notes='允许物理删除'";var scoped=new FileReferenceMutationObserver(f.Store,new([f.Registry.Platform,f.Registry.Demo]),auth,f.Ids,f.Options);await Reject<UnauthorizedAccessException>(context=>scoped.BeforePhysicalDeleteAsync(context));
        // 提交的假父ID不参与证明，真实父记录失效仍拒绝。
        var parent=(long)(await f.Store.ReadAsync(f.TableName,new Dictionary<string,object?>{{"id",record}})).Single()["parent_id"]!;await f.Store.UpdateAsync(f.ParentTableName,parent,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});before=await Snapshot(f,record);await Reject<UnauthorizedAccessException>(context=>observer.BeforePhysicalDeleteAsync(context with{SubmittedFields=new Dictionary<string,object?>{{"parent_id",1L}}}));
        async Task Reject<T>(Func<RecordMutationContext,Task> action)where T:Exception{await Assert.ThrowsAsync<T>(()=>f.Session.InTransactionAsync(async()=>{await action(Context(f,record));return true;}));Assert.Equal(before,await Snapshot(f,record));}
    }
    /// <summary>源／父真实锁以及文件删除的等待都由独立NOWAIT确认，不用固定延时冒充已进入数据库。</summary>
    [FilesIntegrationFact]
    public async Task Pending_physical_delete_blocks_blob_removal_and_rollback_preserves_reference()
    {
        await using var f=await FilesFixture.CreateAsync(childScope:true,physicalDelete:true);var (record,file)=await Seed(f);var before=await Snapshot(f,record);var observer=f.Observer();var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var parent=(long)(await f.Store.ReadAsync(f.TableName,new Dictionary<string,object?>{{"id",record}})).Single()["parent_id"]!;
        var deleting=f.Session.InTransactionAsync(async()=>{var context=Context(f,record);await observer.BeforePhysicalDeleteAsync(context);await Locked(f,f.TableName,record);await Locked(f,f.ParentTableName,parent);await Delete(f,record);await observer.BeforeCommitAsync(context);ready.SetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(15));return await Task.FromException<bool>(new TailFailure());});
        Task<FileDeleteResult>? pending=null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));using var other=new DatabaseSession(f.Session.Options);var store=new SqlRowStore(other,f.Registry.Platform);var service=new FileLibraryService(store,new([f.Registry.Platform,f.Registry.Demo]),f.Ids,new MutableAuthorization(f.Actor()),f.Options);pending=service.DeleteAsync(file.FileId);await Locked(f,"yz_file_library",file.FileId,pending);Assert.False(pending.IsCompleted);release.SetResult();await Assert.ThrowsAsync<TailFailure>(()=>deleting);await Assert.ThrowsAsync<InvalidOperationException>(()=>pending);Assert.Equal(before,await Snapshot(f,record));Assert.Equal("ready",(await f.Service().ResolveAsync(file.FileId)).Status);
        }
        finally{release.TrySetResult();try{await deleting;}catch(TailFailure){}if(pending is not null)try{await pending;}catch(InvalidOperationException){}}
    }
    /// <summary>独立连接确认本任务指定行确实被原事务锁住，或在实际异步删除取得锁后再继续。</summary>
    private static async Task Locked(FilesFixture f,string table,long id,Task? pending=null)
    {
        await using var connection=new MySqlConnection(f.Session.Options.ConnectionString);await connection.OpenAsync();var watch=System.Diagnostics.Stopwatch.StartNew();var observed=false;while(!observed&&watch.Elapsed<TimeSpan.FromSeconds(8))
        {
            await using var transaction=await connection.BeginTransactionAsync();try{await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT id FROM "+f.Session.Provider.QuoteIdentifier(table)+" WHERE id=@id FOR UPDATE NOWAIT";command.Parameters.AddWithValue("id",id);await command.ExecuteScalarAsync();}catch(MySqlException error)when(error.Number==3572){observed=true;}finally{await transaction.RollbackAsync();}if(!observed){if(pending is null||pending.IsCompleted)break;await Task.Delay(25);}
        }
        Assert.True(observed,"未实际观察到指定样本行锁，不能认定并发约束已验证。");
    }
    /// <summary>实际文件流上传、父记录、业务引用均使用本夹具分配的ID。</summary>
    private static async Task<(long Record,FileDescriptor File)> Seed(FilesFixture f)
    {
        var parent=f.Ids.NextId();await f.Store.InsertAsync(f.ParentTableName,new Dictionary<string,object?>{{"id",parent},{"yz_project_id",long.Parse(f.Actor().ProjectId)}});var file=await f.ReadyAsync("physical.txt",Encoding.UTF8.GetBytes("物理删除真实文件"));var record=await f.RecordAsync(parent:parent);var value=JsonSerializer.Serialize(new[]{file.FileId.ToString()});await f.Session.InTransactionAsync(async()=>{await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"attachments",value}});await f.Observer().BeforeCommitAsync(new(f.PageId,f.TableName,record,f.Actor().ProjectId,new Dictionary<string,object?>{{"attachments",value}},false,f.Session.CurrentTransaction!));return true;});return(record,file);
    }
    /// <summary>没有调用者原字段快照；准备阶段只信真实数据库行。</summary>
    private static RecordMutationContext Context(FilesFixture f,long record)=>new(f.PageId,f.TableName,record,f.Actor().ProjectId,new Dictionary<string,object?>(),true,f.Session.CurrentTransaction!);
    /// <summary>仅在原事务删除夹具已登记ID，不能另开连接。</summary>
    private static async Task Delete(FilesFixture f,long record){await using var command=f.Session.CurrentTransaction!.Connection!.CreateCommand();command.Transaction=f.Session.CurrentTransaction;command.CommandText="DELETE FROM "+f.Session.Provider.QuoteIdentifier(f.TableName)+" WHERE id=@id";var id=command.CreateParameter();id.ParameterName="id";id.Value=record;command.Parameters.Add(id);Assert.Equal(1,await command.ExecuteNonQueryAsync());}
    private static async Task<string> Snapshot(FilesFixture f,long id)=>JsonSerializer.Serialize(new{source=await f.Store.ReadIncludingDeletedAsync(f.TableName,new Dictionary<string,object?>{{"id",id}}),refs=await f.Store.ReadIncludingDeletedAsync("yz_file_ref",new Dictionary<string,object?>{{"record_id",id}})});
    private sealed class TailFailure:Exception;
    private sealed class MutableAuthorization(RuntimeActor actor):IRuntimeAuthorization{public RuntimeActor Actor{get;set;}=actor;public bool Deny{get;set;}public RuntimeActor RequireActor()=>Actor;public void RequirePagePermission(string page,string operation){if(Deny)throw new UnauthorizedAccessException("测试删除权被撤销。");}}
}
