using System.Globalization;
using System.Text;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>真实文件流、引用及四张自有表证明逻辑父范围；不以固定授权器冒充正式宿主登录。</summary>
[Collection(FilesIntegrationCollection.Name)]
public sealed class FileCrossParentIntegrationTests
{
    /// <summary>四种ID与父键组合，只存根／中／末层均能按实际完整路径引用和读取保护文件。</summary>
    [FilesIntegrationFact]
    public async Task Sparse_cross_parents_authorize_real_file_references_without_physical_ancestors()
    {
        foreach(var type in new[]{"string","int64"})foreach(var text in new[]{false,true})
        {
            await using var f=await FilesFixture.CreateAsync(parentKeysAsText:text,crossParent:true,crossType:type);var nodes=await Targets(f,text);var file=await f.ReadyAsync("cross.txt",Encoding.UTF8.GetBytes("逻辑父范围文件"));
            for(var depth=0;depth<3;depth++){var record=await f.RecordAsync();await Select(f,record,depth,nodes[depth]);await Bind(f,record,file.FileId);var row=Assert.Single(await f.Store.ReadAsync(f.TableName,new Dictionary<string,object?>{{"id",record}}));for(var index=0;index<3;index++)if(index!=depth)Assert.Null(row[new[]{"country_id","area_id","place_id"}[index]]);Assert.Equal(file.FileId,(await f.Service(f.Actor(8002)).ResolveAsync(file.FileId)).FileId);}
            Assert.Equal(3,(await f.Store.ReadAsync("yz_file_ref",new Dictionary<string,object?>{{"file_id",file.FileId}})).Count);f.CrossVerified=true;
        }
    }
    /// <summary>隐藏、错误项目、坏存储和混合物理父不授文件读取；写后允许离页的原合同保持。</summary>
    [FilesIntegrationFact]
    public async Task File_scope_rejects_invalid_paths_and_keeps_original_leave_page_contract()
    {
        await using var f=await FilesFixture.CreateAsync(childScope:true,parentKeysAsText:true,crossParent:true);var nodes=await Targets(f,true);var project=f.Actor().ProjectId;await f.Store.InsertAsync(f.ParentTableName,new Dictionary<string,object?>{{"id",nodes[0]},{"yz_project_id",project}});var record=await f.RecordAsync(parent:nodes[0]);await Select(f,record,2,nodes[2]);var file=await f.ReadyAsync("range.txt",Encoding.UTF8.GetBytes("固定范围"));await Bind(f,record,file.FileId);
        await f.Store.UpdateAsync(f.CrossTables[1],nodes[1],new Dictionary<string,object?>{{"enabled",false}});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service(f.Actor(8002)).ResolveAsync(file.FileId));await RejectBind();await f.Store.UpdateAsync(f.CrossTables[1],nodes[1],new Dictionary<string,object?>{{"enabled",true}});
        await f.Store.UpdateAsync(f.CrossTables[0],nodes[0],new Dictionary<string,object?>{{"yz_project_id","999999"}});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service(f.Actor(8002)).ResolveAsync(file.FileId));await RejectBind();await f.Store.UpdateAsync(f.CrossTables[0],nodes[0],new Dictionary<string,object?>{{"yz_project_id",project}});
        await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"country_id",nodes[0].ToString()}});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service(f.Actor(8002)).ResolveAsync(file.FileId));await RejectBind();await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"country_id",null}});
        var extra=f.Ids.NextId();await f.Store.InsertAsync(f.ParentTableName,new Dictionary<string,object?>{{"id",extra},{"yz_project_id",project}});await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"parent_id",extra.ToString()}});await RejectBind();await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"parent_id",nodes[0].ToString()}});
        var page=f.Registry.Platform.ConfigPages.Single(page=>page.Id==f.PageId);page.WhereSql+=" AND notes='可见'";await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"notes","可见"}});var fresh=await f.ReadyAsync("leave.txt",Encoding.UTF8.GetBytes("首次绑定后离页"));await Bind(f,record,fresh.FileId,"离页");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service(f.Actor(8002)).ResolveAsync(fresh.FileId));await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"notes","可见"}});Assert.Equal(fresh.FileId,(await f.Service(f.Actor(8002)).ResolveAsync(fresh.FileId)).FileId);f.CrossVerified=true;
        async Task RejectBind(){var before=JsonSerializer.Serialize(await f.ReferencesAsync(record));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Bind(f,record,file.FileId));Assert.Equal(before,JsonSerializer.Serialize(await f.ReferencesAsync(record)));}
    }
    /// <summary>先建立旧RR快照再隐藏目标，文件写入当前读必须拒绝并回滚来源标量及引用。</summary>
    [FilesIntegrationFact]
    public async Task File_mutations_do_not_authorize_from_old_rr_parent_snapshots()
    {
        await using var f=await FilesFixture.CreateAsync(crossParent:true,crossType:"int64");var nodes=await Targets(f,false);var record=await f.RecordAsync();await Select(f,record,2,nodes[2]);var file=await f.ReadyAsync("rr.txt",Encoding.UTF8.GetBytes("RR父路径"));await Bind(f,record,file.FileId);var before=await State(f,record);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
            {
                Assert.Single(await f.Store.ReadAsync(f.CrossTables[0],new Dictionary<string,object?>{{"id",nodes[0]}}));using var other=new Metarill.Data.DatabaseSession(f.Session.Options);await new SqlRowStore(other,f.Registry.Platform).UpdateAsync(f.CrossTables[0],nodes[0],new Dictionary<string,object?>{{"enabled",false}});await f.Store.UpdateAsync(f.TableName,record,new Dictionary<string,object?>{{"notes","旧快照不得提交"}});await f.Observer().BeforeCommitAsync(Context(f,record,file.FileId));return true;
            }));
        }
        finally{await f.Store.UpdateAsync(f.CrossTables[0],nodes[0],new Dictionary<string,object?>{{"enabled",true}});}
        Assert.Equal(before,await State(f,record));f.CrossVerified=true;
    }
    /// <summary>删除前锁真实来源／三层，删除后目标换父会拒绝解绑；尾部失败和正常物理删均核验引用。</summary>
    [FilesIntegrationFact]
    public async Task Physical_delete_proof_freezes_logical_parent_and_rechecks_real_target_locks()
    {
        await using var f=await FilesFixture.CreateAsync(physicalDelete:true,crossParent:true);var nodes=await Targets(f,false);var alternate=await Targets(f,false);var record=await f.RecordAsync();await Select(f,record,2,nodes[2]);var file=await f.ReadyAsync("delete.txt",Encoding.UTF8.GetBytes("删除父路径"));await Bind(f,record,file.FileId);var observer=f.Observer();var before=await State(f,record);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>{var context=Context(f,record,file.FileId,true);await observer.BeforePhysicalDeleteAsync(context);await Delete(f,record);await f.Store.UpdateAsync(f.CrossTables[2],nodes[2],new Dictionary<string,object?>{{"area_id",alternate[1]}});await observer.BeforeCommitAsync(context);return true;}));Assert.Equal(before,await State(f,record));
        await Assert.ThrowsAsync<TailFailure>(()=>f.Session.InTransactionAsync<bool>(async()=>{var context=Context(f,record,file.FileId,true);await observer.BeforePhysicalDeleteAsync(context);await Locked(f,f.TableName,record);for(var index=0;index<3;index++)await Locked(f,f.CrossTables[index],nodes[index]);await Delete(f,record);await observer.BeforeCommitAsync(context);Assert.Empty(await f.ReferencesAsync(record));throw new TailFailure();}));Assert.Equal(before,await State(f,record));
        await f.Session.InTransactionAsync(async()=>{var context=Context(f,record,file.FileId,true);await observer.BeforePhysicalDeleteAsync(context);await Delete(f,record);await observer.BeforeCommitAsync(context);return true;});Assert.Empty(await f.Store.ReadIncludingDeletedAsync(f.TableName,new Dictionary<string,object?>{{"id",record}}));Assert.Empty(await f.ReferencesAsync(record));f.CrossVerified=true;
    }
    /// <summary>三表可复用相同数值主键，层号保持真实路径；全部种子仅在当前nonce表中。</summary>
    private static async Task<long[]> Targets(FilesFixture f,bool text)
    {
        var id=f.Ids.NextId();var nodes=new[]{id,id,id};for(var index=0;index<3;index++){var values=new Dictionary<string,object?>{{"id",nodes[index]},{"name","本次节点"},{"yz_project_id",text?f.Actor().ProjectId:(object)long.Parse(f.Actor().ProjectId,CultureInfo.InvariantCulture)},{"enabled",true}};if(index>0)values.Add(index==1?"country_id":"area_id",text?nodes[index-1].ToString():(object)nodes[index-1]);await f.Store.InsertAsync(f.CrossTables[index],values);}return nodes;
    }
    /// <summary>模拟业务生成器已经编码好的物理值，不将逻辑祖先回填到来源。</summary>
    private static Task Select(FilesFixture f,long record,int depth,long id)=>f.Store.UpdateAsync(f.TableName,record,new[]{"country_id","area_id","place_id"}.Select((field,index)=>(field,value:index==depth?(f.CrossType=="string"?id.ToString():(object)id):null)).ToDictionary(item=>item.field,item=>item.value));
    /// <summary>调用真实文件观察器并加入同一来源事务，备注可用于验证原写后离页规则。</summary>
    private static Task Bind(FilesFixture f,long record,long file,string? notes=null)=>f.Session.InTransactionAsync(async()=>{var values=new Dictionary<string,object?>{{"attachments",JsonSerializer.Serialize(new[]{file.ToString()})}};if(notes is not null)values.Add("notes",notes);await f.Store.UpdateAsync(f.TableName,record,values);await f.Observer().BeforeCommitAsync(Context(f,record,file));return true;});
    /// <summary>没有提供请求parentId或祖先字段，观察器只可使用实际数据库记录。</summary>
    private static RecordMutationContext Context(FilesFixture f,long record,long file,bool deleting=false)=>new(f.PageId,f.TableName,record,f.Actor().ProjectId,new Dictionary<string,object?>{{"attachments",JsonSerializer.Serialize(new[]{file.ToString()})}},deleting,f.Session.CurrentTransaction!);
    /// <summary>物理删除在原事务执行，不另开连接。</summary>
    private static async Task Delete(FilesFixture f,long record){await using var command=f.Session.CurrentTransaction!.Connection!.CreateCommand();command.Transaction=f.Session.CurrentTransaction;command.CommandText="DELETE FROM `"+f.TableName+"` WHERE id=@id";var id=command.CreateParameter();id.ParameterName="id";id.Value=record;command.Parameters.Add(id);Assert.Equal(1,await command.ExecuteNonQueryAsync());}
    /// <summary>独立NOWAIT失败才认定真实锁已取得。</summary>
    private static async Task Locked(FilesFixture f,string table,long record){await using var connection=new MySqlConnection(f.Session.Options.ConnectionString);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();try{await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT id FROM `"+table+"` WHERE id=@id FOR UPDATE NOWAIT";command.Parameters.AddWithValue("id",record);Assert.Equal(3572,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteScalarAsync())).Number);}finally{await transaction.RollbackAsync();}}
    /// <summary>完整来源与含墓碑引用都参与回滚比较。</summary>
    private static async Task<string> State(FilesFixture f,long record)=>JsonSerializer.Serialize(new{source=await f.Store.ReadIncludingDeletedAsync(f.TableName,new Dictionary<string,object?>{{"id",record}}),refs=await f.Store.ReadIncludingDeletedAsync("yz_file_ref",new Dictionary<string,object?>{{"record_id",record}})});
    private sealed class TailFailure:Exception;
}
