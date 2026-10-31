using System.Text.Json;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class MetadataWorkbenchIntegrationTests
{
    private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,JsonDefaults.Options);
    /// <summary>创建符合原前端协议的完整布局请求。</summary>
    private static JsonElement LayoutInput(MetadataWorkbenchFixture f,string revision,string name="基础")=>Json(new{pageId=f.PageId.ToString(),revision,detailLayout=new{version=1,mode="tabs",groups=new[]{new{key="base",name}}},fields=new[]{new{pageColumnId=f.FirstFieldId.ToString(),detailGroupKey="base"},new{pageColumnId=f.SecondFieldId.ToString(),detailGroupKey=""}}});
    /// <summary>保存布局并拒绝旧版本、跨页字段和缺失字段，失败不能产生部分更新。</summary>
    [MetadataIntegrationFact]
    public async Task Layout_roundtrip_conflict_and_field_scope_are_atomic()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();var business=f.Business();
        var snapshot=Json(await business.DetailLayoutSnapshotAsync(Json(new{pageId=f.PageId.ToString()})));var revision=snapshot.GetProperty("revision").GetString()!;
        var saved=Json(await business.SaveDetailLayoutAsync(LayoutInput(f,revision)));
        Assert.Equal("tabs",saved.GetProperty("detailLayout").GetProperty("mode").GetString());
        Assert.Equal("base",saved.GetProperty("fields")[0].GetProperty("detailGroupKey").GetString());
        await Assert.ThrowsAsync<MetadataRevisionConflictException>(()=>business.SaveDetailLayoutAsync(LayoutInput(f,revision,"旧值")));
        var current=saved.GetProperty("revision").GetString()!;
        var bad=Json(new{pageId=f.PageId.ToString(),revision=current,detailLayout=new{version=1,mode="none",groups=Array.Empty<object>()},fields=new[]{new{pageColumnId=f.OtherFieldId.ToString(),detailGroupKey=""}}});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.SaveDetailLayoutAsync(bad));
        var after=Json(await business.DetailLayoutSnapshotAsync(Json(new{pageId=f.PageId.ToString()})));Assert.Equal(current,after.GetProperty("revision").GetString());
    }
    /// <summary>标志与Core规范同步，后一个跨页ID无效时前一个合法字段也不得写入。</summary>
    [MetadataIntegrationFact]
    public async Task Bulk_fields_validate_all_ids_before_writing_and_sync_core_flags()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();var business=f.Business();var before=await business.PageFieldsAsync(f.PageId.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.SavePageFieldsAsync(Json(new[]{new{id=f.FirstFieldId.ToString(),isInEdit=0},new{id=f.OtherFieldId.ToString(),isInEdit=0}}),before.Revision));
        Assert.Equal(1,(await business.PageFieldsAsync(f.PageId.ToString())).Rows.Single(row=>row["id"]!.ToString()==f.FirstFieldId.ToString())["isInEdit"]);
        var next=await business.SavePageFieldsAsync(Json(new[]{new{id=f.FirstFieldId.ToString(),isInEdit=0,isInGrid=0,isMust=1}}),before.Revision);
        Assert.NotEqual(before.Revision,next.Revision);
        var catalog=await new MetadataRepository(f.Store,f.Registry).LoadProjectAsync(f.ProjectId.ToString());
        var field=catalog.ConfigPageColumns.Single(row=>row.Id==f.FirstFieldId.ToString());Assert.Equal(0,field.IsEdit);Assert.Equal(0,field.IsShow);Assert.Equal(1,field.IsRequired);
        await Assert.ThrowsAsync<MetadataRevisionConflictException>(()=>business.SavePageFieldsAsync(Json(new[]{new{id=f.FirstFieldId.ToString(),isInEdit=1}}),before.Revision));
    }
    /// <summary>真实项目配置经仓储进入生成目录，空主题回退原默认值，非法主题不得进入模板。</summary>
    [MetadataIntegrationFact]
    public async Task Project_name_and_theme_roundtrip_from_the_owned_project_record()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();var repository=new MetadataRepository(f.Store,f.Registry);
        await f.Store.UpdateAsync("yz_project",f.ProjectId,new Dictionary<string,object?>{{"name","测试品牌名称"},{"primary_color","#137abc"}});
        var catalog=await repository.LoadProjectAsync(f.ProjectId.ToString());Assert.Equal("测试品牌名称",catalog.YzProjectName);Assert.Equal("#137abc",catalog.PrimaryColor);
        await f.Store.UpdateAsync("yz_project",f.ProjectId,new Dictionary<string,object?>{{"primary_color",null}});
        Assert.Equal("#0ab3ad",(await repository.LoadProjectAsync(f.ProjectId.ToString())).PrimaryColor);
        await f.Store.UpdateAsync("yz_project",f.ProjectId,new Dictionary<string,object?>{{"primary_color","red; injected: 1"}});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.LoadProjectAsync(f.ProjectId.ToString()));
    }
    /// <summary>排序拒绝跨表组合且支持字符串整数，旧快照不能覆盖新顺序。</summary>
    [MetadataIntegrationFact]
    public async Task Column_order_is_scoped_and_revision_checked()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();var business=f.Business();var before=await business.ColumnOrderAsync(f.TableId.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.SaveColumnOrderAsync(Json(new[]{new{id=f.FirstColumnId.ToString(),sortNo="99"},new{id=f.OtherColumnId.ToString(),sortNo="1"}}),before.Revision));
        Assert.Equal(10,(await business.ColumnOrderAsync(f.TableId.ToString())).Rows[0]["sortNo"]);
        var after=await business.SaveColumnOrderAsync(Json(new[]{new{id=f.FirstColumnId.ToString(),sortNo="99"},new{id=f.SecondColumnId.ToString(),sortNo="1"}}),before.Revision);
        Assert.Equal(f.SecondColumnId.ToString(),after.Rows[0]["id"]);
        await Assert.ThrowsAsync<MetadataRevisionConflictException>(()=>business.SaveColumnOrderAsync(Json(new[]{new{id=f.FirstColumnId.ToString(),sortNo="0"}}),before.Revision));
    }
    /// <summary>不同连接代表不同进程，数据库行锁确保同一旧版本最多一个写入获胜。</summary>
    [MetadataIntegrationFact]
    public async Task Database_lock_serializes_competing_layout_writers()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();var first=f.Business();var snapshot=Json(await first.DetailLayoutSnapshotAsync(Json(new{pageId=f.PageId.ToString()})));var revision=snapshot.GetProperty("revision").GetString()!;
        using var peerSession=new DatabaseSession(f.Options);var peer=f.Business(peerSession);
        async Task<bool> Save(MetadataWorkbenchBusiness business,string name){try{await business.SaveDetailLayoutAsync(LayoutInput(f,revision,name));return true;}catch(MetadataRevisionConflictException){return false;}}
        var results=await Task.WhenAll(Save(first,"甲"),Save(peer,"乙"));Assert.Single(results,value=>value);
    }
    /// <summary>非管理员与错误当前项目不能读取或写入其他项目配置。</summary>
    [MetadataIntegrationFact]
    public async Task Administrator_and_current_project_are_both_required()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business(administrator:false).ColumnOrderAsync(f.TableId.ToString()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business(project:"1").PageFieldsAsync(f.PageId.ToString()));
    }
    /// <summary>原批量入口只新增配置并复用afterAdd，不开放普通新增页、不执行DDL。</summary>
    [MetadataIntegrationFact]
    public async Task Batch_columns_preview_save_and_after_add_are_one_transaction()
    {
        await using var f=await MetadataWorkbenchFixture.CreateAsync();var business=f.Business();
        var request=Json(new{config_table_id=f.TableId.ToString(),content="编码 code\n启用 is_enabled"});
        var before=await business.ColumnOrderAsync(f.TableId.ToString());var preview=await business.PreviewBatchColumnsAsync(request);
        Assert.True(preview.Valid);Assert.Equal(before.Rows.Count,(await business.ColumnOrderAsync(f.TableId.ToString())).Rows.Count);
        var saved=Json(await business.SaveBatchColumnsAsync(request));Assert.Equal(2,saved.GetProperty("savedCount").GetInt32());Assert.Equal("pending",saved.GetProperty("database").GetString());Assert.Equal("pending",saved.GetProperty("generation").GetString());
        foreach(var id in saved.GetProperty("columnIds").EnumerateArray())
        {
            var fields=await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",long.Parse(id.GetString()!)}});
            Assert.Equal(2,fields.Count);Assert.All(fields,row=>Assert.Equal(f.ProjectId.ToString(),SqlRowStore.Text(row,"yz_project_id")));
        }
        var duplicate=await Assert.ThrowsAsync<BatchColumnValidationException>(()=>business.SaveBatchColumnsAsync(request));Assert.Contains(duplicate.Preview.Rows,row=>row.Errors.Any(error=>error.Code=="ALREADY_EXISTS"));
        var count=(await business.ColumnOrderAsync(f.TableId.ToString())).Rows.Count;
        await Assert.ThrowsAsync<BatchColumnValidationException>(()=>business.SaveBatchColumnsAsync(Json(new{config_table_id=f.TableId.ToString(),content="有效 fresh\n无效 too many"})));
        Assert.Equal(count,(await business.ColumnOrderAsync(f.TableId.ToString())).Rows.Count);
    }

}
