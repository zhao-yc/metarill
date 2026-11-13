using System.Text.Json;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>实际SqlRowStore/观察器与事务执行分组规则，仅数据库传输使用既有内存驱动。</summary>
public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>普通页面更新不能删除仍在使用的组或保存无法生成的布局，失败还原整行。</summary>
    [Theory]
    [InlineData("remove")][InlineData("noneRemove")][InlineData("invalidJson")][InlineData("longKey")]
    public async Task Layout_standard_page_rejects_bad_definition_or_dangling_assignments(string scenario)
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");
        f.Get("config_page_column",40)["detail_group_key"]="base";var before=f.Snapshot();
        var definition=scenario switch{"invalidJson"=>"{","longKey"=>Layout(new string('a',65)),"noneRemove"=>Layout("other","none"),_=>Layout("other")};
        var values=Row(("detail_layout",definition),("name","失败必须整体回滚"));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_page",30,values);await f.Observer.BeforeCommitAsync(f.Context("config_page",30,values));return true;
        }));
        Assert.Contains("布局",error.Message);Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    /// <summary>字段普通更新拒绝无效组，不能连同其它属性部分提交。</summary>
    [Fact]
    public async Task Layout_standard_field_rejects_missing_group_with_whole_row_rollback()
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");
        f.Get("config_page_column",40)["detail_group_key"]="base";var before=f.Snapshot();
        var values=Row(("detail_group_key","missing"),("is_in_grid",0));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_page_column",40,values);await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,values));return true;
        }));
        Assert.Contains("布局",error.Message);Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    /// <summary>关闭布局保留合法归属；省略不清空，显式null只清空当前归属。</summary>
    [Fact]
    public async Task Layout_none_keeps_assignments_and_explicit_null_clears_only_the_requested_field()
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");
        f.Get("config_page_column",40)["detail_group_key"]="base";
        var values=Row(("detail_layout",Layout("base","none")));
        await f.Session.InTransactionAsync(async()=>{await f.Store.UpdateAsync("config_page",30,values);await f.Observer.BeforeCommitAsync(f.Context("config_page",30,values));return true;});
        Assert.Equal("base",f.Get("config_page_column",40)["detail_group_key"]);
        var unchanged=f.Get("config_page",30)["detail_layout"];values=Row(("detail_group_key",null));
        await f.Session.InTransactionAsync(async()=>{await f.Store.UpdateAsync("config_page_column",40,values);await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,values));return true;});
        Assert.Equal("",f.Get("config_page_column",40)["detail_group_key"]);Assert.Equal(unchanged,f.Get("config_page",30)["detail_layout"]);
    }

    /// <summary>工作台完整迁组可同时移除旧组，不能被普通入口中间态校验误拒绝。</summary>
    [Fact]
    public async Task Layout_aggregate_reassigns_every_field_before_returning_the_new_snapshot()
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");f.Get("config_page_column",40)["detail_group_key"]="base";
        var business=new MetadataWorkbenchBusiness(f.Store,f.Authorization,f.Registry,f.Ids,f.Observer);
        var before=JsonSerializer.SerializeToElement(await business.DetailLayoutSnapshotAsync(JsonSerializer.SerializeToElement(new{pageId="30"})));
        var input=JsonSerializer.SerializeToElement(new{pageId="30",revision=before.GetProperty("revision").GetString(),detailLayout=new{version=1,mode="tabs",groups=new[]{new{key="new_group",name="新组"}}},fields=new[]{new{pageColumnId="40",detailGroupKey="new_group"}}});
        var after=JsonSerializer.SerializeToElement(await business.SaveDetailLayoutAsync(input));
        Assert.NotEqual(before.GetProperty("revision").GetString(),after.GetProperty("revision").GetString());
        Assert.Equal("new_group",Assert.Single(after.GetProperty("fields").EnumerateArray()).GetProperty("detailGroupKey").GetString());
        Assert.Equal("new_group",f.Get("config_page_column",40)["detail_group_key"]);
        using var stored=JsonDocument.Parse((string)f.Get("config_page",30)["detail_layout"]!);
        Assert.Equal("new_group",Assert.Single(stored.RootElement.GetProperty("groups").EnumerateArray()).GetProperty("key").GetString());
        Assert.Contains(f.Provider.Reads,read=>read.Sql.Contains("FROM "+(char)96+"config_page"+(char)96,StringComparison.Ordinal)&&read.Sql.EndsWith(" FOR UPDATE",StringComparison.Ordinal)&&read.Transaction is not null);
    }

    /// <summary>新页面字段也必须引用已有组，原观察器失败回滚本次插入。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Layout_new_field_uses_the_same_group_policy(bool valid)
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");
        f.Add("config_column",Row(("id",21L),("yz_project_id",1L),("config_table_id",10L),("column_name","extra_column"),("name","新增字段"),("logical_type","string")));
        var before=f.Snapshot();
        var values=Row(("id",41L),("yz_project_id",1L),("config_column_id",21L),("config_page_id",30L),("config_column_column_name","extra_column"),("detail_group_key",valid?"base":"missing"));
        async Task Save()=>await f.Session.InTransactionAsync(async()=>{await f.Store.InsertAsync("config_page_column",values);await f.Observer.BeforeCommitAsync(f.Context("config_page_column",41,values,true));return true;});
        if(valid){await Save();Assert.Equal("base",f.Get("config_page_column",41)["detail_group_key"]);}
        else{var error=await Assert.ThrowsAsync<InvalidOperationException>(Save);Assert.Contains("布局",error.Message);Assert.Equal(before,f.Snapshot());}
    }

    /// <summary>墓碑引用不阻止活动页面删组，既有墓碑不被清空或复活。</summary>
    [Fact]
    public async Task Layout_removed_group_ignores_soft_deleted_fields_without_rewriting_them()
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");
        f.Get("config_page_column",40)["detail_group_key"]="base";f.Get("config_page_column",40)["delete_no"]=DateTime.UtcNow;
        var original=JsonSerializer.Serialize(f.Get("config_page_column",40));var values=Row(("detail_layout",Layout("other")));
        await f.Session.InTransactionAsync(async()=>{await f.Store.UpdateAsync("config_page",30,values);await f.Observer.BeforeCommitAsync(f.Context("config_page",30,values));return true;});
        Assert.Equal(original,JsonSerializer.Serialize(f.Get("config_page_column",40)));
    }

    /// <summary>普通改名不应重排历史布局JSON或归属，只有明确布局提交才规范化。</summary>
    [Fact]
    public async Task Layout_omitted_definition_preserves_existing_bytes_on_unrelated_page_edit()
    {
        using var f=new Fixture();var raw=Layout("base").Replace(":",": ",StringComparison.Ordinal);f.Get("config_page",30)["detail_layout"]=raw;
        f.Get("config_page_column",40)["detail_group_key"]="base";var values=Row(("name","只改页面名称"));
        await f.Session.InTransactionAsync(async()=>{await f.Store.UpdateAsync("config_page",30,values);await f.Observer.BeforeCommitAsync(f.Context("config_page",30,values));return true;});
        Assert.Equal(raw,f.Get("config_page",30)["detail_layout"]);Assert.Equal("base",f.Get("config_page_column",40)["detail_group_key"]);
    }

    /// <summary>原快照需保留字段配置columnId，长ID仍以字符串交付，不只回传页面字段ID。</summary>
    [Theory][InlineData(20L)][InlineData(9007199254740993L)]
    public async Task Layout_snapshot_keeps_original_column_id_as_exact_text(long columnId)
    {
        using var f=new Fixture();f.Get("config_column",20)["id"]=columnId;f.Get("config_page_column",40)["config_column_id"]=columnId;
        var business=new MetadataWorkbenchBusiness(f.Store,f.Authorization,f.Registry,f.Ids,f.Observer);
        var snapshot=JsonSerializer.SerializeToElement(await business.DetailLayoutSnapshotAsync(JsonSerializer.SerializeToElement(new{pageId="30"})));
        var field=Assert.Single(snapshot.GetProperty("fields").EnumerateArray());
        Assert.Equal("40",field.GetProperty("pageColumnId").GetString());
        Assert.Equal(columnId.ToString(System.Globalization.CultureInfo.InvariantCulture),field.GetProperty("columnId").GetString());
        Assert.Equal("source_column",field.GetProperty("columnName").GetString());
    }

    /// <summary>第二字段或页面更新零命中时，前面已写字段必须整体回滚，不能返回成功快照。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Layout_aggregate_rolls_back_earlier_assignments_when_a_later_update_misses(bool pageMiss)
    {
        using var f=new Fixture();f.Get("config_page",30)["detail_layout"]=Layout("base");f.Get("config_page_column",40)["detail_group_key"]="base";
        f.Add("config_column",Row(("id",21L),("yz_project_id",1L),("config_table_id",10L),("column_name","notes"),("name","备注"),("sort_no",20)));
        f.Add("config_page_column",Row(("id",41L),("yz_project_id",1L),("config_column_id",21L),("config_page_id",30L),("detail_group_key","base")));
        var business=new MetadataWorkbenchBusiness(f.Store,f.Authorization,f.Registry,f.Ids,f.Observer);
        var snapshot=JsonSerializer.SerializeToElement(await business.DetailLayoutSnapshotAsync(JsonSerializer.SerializeToElement(new{pageId="30"})));var before=f.Snapshot();
        f.Provider.SkipWrite=(sql,parameters)=>sql.StartsWith("UPDATE "+(char)96+(pageMiss?"config_page":"config_page_column")+(char)96+" SET ",StringComparison.Ordinal)&&Convert.ToInt64(parameters["record_id"].Value,System.Globalization.CultureInfo.InvariantCulture)==(pageMiss?30L:41L);
        var input=JsonSerializer.SerializeToElement(new{pageId="30",revision=snapshot.GetProperty("revision").GetString(),detailLayout=new{version=1,mode="tabs",groups=new[]{new{key="new_group",name="新组"}}},fields=new[]{new{pageColumnId="40",detailGroupKey="new_group"},new{pageColumnId="41",detailGroupKey="new_group"}}});
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>business.SaveDetailLayoutAsync(input));
        Assert.Contains("布局",error.Message);Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    /// <summary>独立定义原版合法布局，不调用待测规则构造期望结果。</summary>
    private static string Layout(string key,string mode="tabs")=>JsonSerializer.Serialize(new{version=1,mode,groups=new[]{new{key,name="基础信息"}}});
}
