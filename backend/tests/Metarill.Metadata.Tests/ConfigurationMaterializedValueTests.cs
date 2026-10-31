using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeSql;
using MySqlConnector;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>真实SqlRowStore/观察器/工作台的离线命令回归；内存命令适配器不证明MySQL锁或隔离行为。</summary>
public sealed partial class ConfigurationMaterializedValueTests
{
    [Fact]
    public void Fixed_values_overwrite_stale_copy_and_preserve_real_null()
    {
        var values=Row(("config_column_column_name","旧值"),("config_column_sort_no",99));
        ConfigurationMaterializedValues.PageColumn(values,Row(("column_name","actual"),("sort_no",null)));
        Assert.Equal("actual",values["config_column_column_name"]);Assert.Null(values["config_column_sort_no"]);
        ConfigurationMaterializedValues.RequirePageColumn(values,Row(("column_name","actual"),("sort_no",null)));
        values["config_column_sort_no"]="";Assert.Throws<InvalidOperationException>(()=>ConfigurationMaterializedValues.RequirePageColumn(values,Row(("column_name","actual"),("sort_no",null))));
    }

    [Fact]
    public async Task Column_rename_and_null_sort_update_only_active_references_and_tail_failure_rolls_back()
    {
        using var f=new Fixture();f.Add("config_page_column",Row(("id",41L),("yz_project_id",1L),("config_column_id",20L),("config_page_id",30L),("config_column_column_name","软删旧值"),("config_column_sort_no",99),("delete_no",DateTime.UtcNow)));
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"column_name","new_column"},{"sort_no",null}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]="new_column",["sort_no"]=null}));return true;
        });
        Assert.Equal("new_column",f.Get("config_page_column",40)["config_column_column_name"]);Assert.Null(f.Get("config_page_column",40)["config_column_sort_no"]);Assert.Equal("软删旧值",f.Get("config_page_column",41)["config_column_column_name"]);
        var before=f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"column_name","rolled_back"}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]="rolled_back"}));
            return await Task.FromException<bool>(new InvalidOperationException("模拟同事务后续观察器失败"));
        }));Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    [Fact]
    public async Task Foreign_reference_is_rejected_before_any_reference_lock_or_write()
    {
        using var f=new Fixture();f.Get("config_page_column",40)["yz_project_id"]=2L;
        var before=f.Snapshot();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
        {await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]="source_column"}));return true;}));
        Assert.Equal(before,f.Snapshot());Assert.DoesNotContain(f.Provider.Commands,sql=>sql.Contains("FROM `config_page_column`",StringComparison.Ordinal)&&sql.EndsWith("FOR UPDATE",StringComparison.Ordinal));
    }

    [Fact]
    public async Task Page_column_omission_keeps_history_but_create_rejects_forged_cache_and_wrong_owner()
    {
        using var f=new Fixture();
        await f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,new(){["name"]="仅名称"}));return true;});
        Assert.Equal("stale_name",f.Get("config_page_column",40)["config_column_column_name"]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,new(),true));return true;}));
        f.Get("config_page",30)["config_table_id"]=999L;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,new()));return true;}));
    }

    [Fact]
    public async Task Real_sort_workbench_and_copy_page_use_current_target_values()
    {
        using var f=new Fixture();var workbench=new MetadataWorkbenchBusiness(f.Store,f.Authorization,f.Registry,f.Ids,f.Observer);
        var snapshot=await workbench.ColumnOrderAsync("10");
        await workbench.SaveColumnOrderAsync(JsonSerializer.SerializeToElement(new[]{new{id="20",sortNo=77}}),snapshot.Revision);
        Assert.Equal(77,Convert.ToInt32(f.Get("config_page_column",40)["config_column_sort_no"],CultureInfo.InvariantCulture));
        // 排序只传播排序，不替用户执行历史名称回填。
        Assert.Equal("stale_name",f.Get("config_page_column",40)["config_column_column_name"]);
        var business=new ConfigurationActionsBusiness(f.Store,f.Registry,new MetadataRepository(f.Store,f.Registry),f.Authorization,f.Ids,f.Observer);
        var result=await business.CopyPageAsync("30");var pageId=long.Parse(result.PageId!,CultureInfo.InvariantCulture);
        Assert.Equal("actual_table",f.Get("config_page",pageId)["config_table_table_name"]);
        var field=f.Provider.Rows["config_page_column"].Single(row=>Convert.ToInt64(row["config_page_id"],CultureInfo.InvariantCulture)==pageId);
        Assert.Equal("source_column",field["config_column_column_name"]);Assert.Equal(77,Convert.ToInt32(field["config_column_sort_no"],CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Automatic_new_column_creation_fields_receive_both_values_without_touching_old_fields()
    {
        using var f=new Fixture();f.Provider.Rows["config_page_column"].Clear();
        await f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["name"]="仅标签"},true));return true;});
        var created=Assert.Single(f.Provider.Rows["config_page_column"]);Assert.Equal("source_column",created["config_column_column_name"]);Assert.Equal(10,Convert.ToInt32(created["config_column_sort_no"],CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("decimal")] [InlineData("int64")] [InlineData("time")] [InlineData("binary")] [InlineData("double")]
    public async Task Unrelated_column_update_preserves_valid_logical_metadata_without_physical_type(string logical)
    {
        using var f=new Fixture();f.Get("config_column",20)["logical_type"]=logical;f.Get("config_column",20)["column_type"]="";
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"control_detail","新控件配置"}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["control_detail"]="新控件配置"}));return true;
        });
        Assert.Equal(logical,f.Get("config_column",20)["logical_type"]);
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE `config_column` SET `logical_type`",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("nvarchar","string")] [InlineData("varchar","string")] [InlineData("text","string")] [InlineData("longtext","string")]
    [InlineData("bigint","int64")] [InlineData("int","int32")] [InlineData("decimal","decimal")] [InlineData("double","decimal")]
    [InlineData("date","date")] [InlineData("time","time")] [InlineData("datetime","datetime")] [InlineData("bool","bool")]
    [InlineData("json","json")] [InlineData("blob","binary")] [InlineData("binary","binary")]
    public async Task Explicit_known_physical_edit_retains_existing_metadata_mapping(string physical,string expected)
    {
        using var f=new Fixture();
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"column_type",physical}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_type"]=physical}));return true;
        });
        Assert.Equal(expected,f.Get("config_column",20)["logical_type"]);
    }

    [Theory]
    [InlineData("logical-null")] [InlineData("logical-empty")] [InlineData("logical-invalid")] [InlineData("logical-name-conflict")] [InlineData("logical-id-int32")] [InlineData("logical-sort-conflict")]
    [InlineData("physical-unknown")] [InlineData("physical-point")] [InlineData("physical-varbinary")] [InlineData("physical-null")] [InlineData("both-unknown")]
    public async Task Invalid_explicit_types_and_identifier_conflicts_roll_back_the_whole_mutation(string scenario)
    {
        using var f=new Fixture();var before=f.Snapshot();var values=new Dictionary<string,object?>();
        if(scenario.StartsWith("logical",StringComparison.Ordinal))values["logical_type"]=scenario switch{"logical-null"=>null,"logical-empty"=>"","logical-invalid"=>"point","logical-id-int32"=>"int32","logical-sort-conflict"=>"int64",_=>"string"};
        if(scenario is "logical-name-conflict" or "logical-id-int32")values["column_name"]="account_id";
        if(scenario=="logical-sort-conflict")values["column_name"]="sort_no";
        if(scenario.StartsWith("physical",StringComparison.Ordinal)||scenario=="both-unknown")values["column_type"]=scenario switch{"physical-point"=>"point","physical-varbinary"=>"varbinary","physical-null"=>null,_=>"unknown"};
        if(scenario=="both-unknown")values["logical_type"]="string";
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,values);
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,values));return true;
        }));
        Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    [Fact]
    public async Task Explicit_logical_type_has_priority_and_tail_failure_still_rolls_back()
    {
        using var f=new Fixture();
        await f.Session.InTransactionAsync(async()=>
        {
            var values=new Dictionary<string,object?>{{"column_type","nvarchar"},{"logical_type","double"}};
            await f.Store.UpdateAsync("config_column",20,values);await f.Observer.BeforeCommitAsync(f.Context("config_column",20,values));return true;
        });
        Assert.Equal("double",f.Get("config_column",20)["logical_type"]);var before=f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {
            var values=new Dictionary<string,object?>{{"column_type","bigint"}};
            await f.Store.UpdateAsync("config_column",20,values);await f.Observer.BeforeCommitAsync(f.Context("config_column",20,values));
            throw new InvalidOperationException("模拟后续观察器失败");
        }));Assert.Equal(before,f.Snapshot());
        f.Get("config_column",20)["logical_type"]="invalid";
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["control_detail"]="不修类型"}));return true;}));
    }

    [Fact]
    public async Task Observer_creation_infers_only_missing_logical_metadata_and_rejects_explicit_empty_or_unknown_types()
    {
        using var f=new Fixture();f.Get("config_column",20)["logical_type"]=null;f.Get("config_column",20)["column_type"]="decimal";
        await f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(),true));return true;});
        Assert.Equal("decimal",f.Get("config_column",20)["logical_type"]);
        foreach(var logical in new string?[]{null,"","invalid"})
        {
            var before=f.Snapshot();
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Session.InTransactionAsync(async()=>
            {
                await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"logical_type",logical}});
                await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["logical_type"]=logical},true));return true;
            }));Assert.Equal(before,f.Snapshot());
        }
        var snapshot=f.Snapshot();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"logical_type",null},{"column_type","point"}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(),true));return true;
        }));Assert.Equal(snapshot,f.Snapshot());
    }

    [Theory]
    [InlineData("client_id","int","int64")] [InlineData("id","int","int64")] [InlineData("sort_no","nvarchar","int32")]
    public async Task Legacy_creation_without_logical_field_keeps_original_identifier_and_sort_inference(string column,string physical,string expected)
    {
        using var f=new Fixture();
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"column_name",column},{"column_type",physical},{"logical_type",null}});
            // 原表单只提交物理类型；logical_type为空是基础设施原行状态，不把它伪装成显式类型请求。
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]=column,["column_type"]=physical},true));return true;
        });Assert.Equal(expected,f.Get("config_column",20)["logical_type"]);
    }

    /// <summary>普通保存即使遇到有意省略或软删的字段/默认页面，也不能当成创建操作补历史。</summary>
    [Theory]
    [InlineData("config_column",false)] [InlineData("config_column",true)]
    [InlineData("config_page",false)] [InlineData("config_page",true)]
    [InlineData("config_table",false)] [InlineData("config_table",true)]
    public async Task CreationOnly_updates_do_not_fill_missing_or_soft_deleted_children(string table,bool softDeleted)
    {
        using var f=new Fixture();
        f.Add("config_column",Row(("id",21L),("yz_project_id",1L),("config_table_id",10L),("column_name","db_password"),("name","未开放字段"),("logical_type","string"),("column_type","nvarchar")));
        f.Add("config_column",Row(("id",22L),("yz_project_id",1L),("config_table_id",10L),("column_name","id"),("name","标识"),("logical_type","int64"),("column_type","bigint")));
        f.Add("config_column",Row(("id",23L),("yz_project_id",1L),("config_table_id",10L),("column_name","delete_no"),("name","删除"),("logical_type","datetime"),("column_type","datetime")));
        if(softDeleted)f.Get("config_page_column",40)["delete_no"]=DateTime.UtcNow;else f.Provider.Rows["config_page_column"].Clear();
        if(table=="config_table")
        {
            if(softDeleted)f.Get("config_page",30)["delete_no"]=DateTime.UtcNow;else f.Provider.Rows["config_page"].Clear();
            f.Add("config_column",Row(("id",24L),("yz_project_id",1L),("config_table_id",10L),("column_name","name"),("name","软删系统字段"),("logical_type","string"),("column_type","nvarchar"),("delete_no",DateTime.UtcNow)));
        }
        var columnIds=f.Provider.Rows["config_column"].Select(row=>row["id"]).ToArray();var pageIds=f.Provider.Rows["config_page"].Select(row=>row["id"]).ToArray();
        var columns=JsonSerializer.Serialize(f.Provider.Rows["config_column"]);var pages=JsonSerializer.Serialize(f.Provider.Rows["config_page"]);var fields=JsonSerializer.Serialize(f.Provider.Rows["config_page_column"]);
        var id=table=="config_column"?20L:table=="config_page"?30L:10L;var values=table=="config_column"?new Dictionary<string,object?>{{"control_detail","仅改控件配置"}}:new(){{"name","仅改名称"}};
        await f.Session.InTransactionAsync(async()=>{await f.Store.UpdateAsync(table,id,values);await f.Observer.BeforeCommitAsync(f.Context(table,id,values));return true;});
        Assert.Equal(columnIds,f.Provider.Rows["config_column"].Select(row=>row["id"]).ToArray());Assert.Equal(pageIds,f.Provider.Rows["config_page"].Select(row=>row["id"]).ToArray());
        if(table!="config_column")Assert.Equal(columns,JsonSerializer.Serialize(f.Provider.Rows["config_column"]));
        if(table!="config_page")Assert.Equal(pages,JsonSerializer.Serialize(f.Provider.Rows["config_page"]));
        Assert.Equal(fields,JsonSerializer.Serialize(f.Provider.Rows["config_page_column"]));Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("INSERT ",StringComparison.Ordinal));
    }

    /// <summary>新列只能加入自己的映射，不能补同表其它列的历史空洞或复活软删映射。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreationOnly_new_column_adds_only_itself_to_each_page_and_preserves_sibling_holes(bool softDeleted)
    {
        using var f=new Fixture();
        f.Add("config_column",Row(("id",21L),("yz_project_id",1L),("config_table_id",10L),("column_name","db_password"),("name","未开放字段"),("logical_type","string"),("column_type","nvarchar")));
        f.Add("config_page",Row(("id",31L),("yz_project_id",1L),("config_table_id",10L),("name","另一页"),("yz_admin_type_id",90L),("config_table_table_name","actual_table")));
        f.Add("config_page_column",Row(("id",41L),("yz_project_id",1L),("config_column_id",20L),("config_page_id",31L),("config_column_column_name","source_column"),("config_column_sort_no",10)));
        if(softDeleted)f.Add("config_page_column",Row(("id",42L),("yz_project_id",1L),("config_column_id",21L),("config_page_id",30L),("config_column_column_name","db_password"),("delete_no",DateTime.UtcNow)));
        var originals=f.Provider.Rows["config_page_column"].Select(row=>JsonSerializer.Serialize(row)).ToArray();
        var values=Row(("id",23L),("yz_project_id",1L),("config_table_id",10L),("column_name","new_column"),("name","新字段"),("logical_type","string"),("column_type","nvarchar"),("sort_no",20));
        await f.Session.InTransactionAsync(async()=>{await f.Store.InsertAsync("config_column",values);await f.Observer.BeforeCommitAsync(f.Context("config_column",23,values,true));return true;});
        var created=f.Provider.Rows["config_page_column"].Where(row=>Convert.ToInt64(row["config_column_id"],CultureInfo.InvariantCulture)==23).ToArray();
        Assert.Equal(2,created.Length);Assert.Equal(new[]{30L,31L},created.Select(row=>Convert.ToInt64(row["config_page_id"],CultureInfo.InvariantCulture)).Order().ToArray());
        Assert.All(created,row=>{Assert.Equal("new_column",row["config_column_column_name"]);Assert.Equal(20,Convert.ToInt32(row["config_column_sort_no"],CultureInfo.InvariantCulture));});
        Assert.Equal(originals,f.Provider.Rows["config_page_column"].Where(row=>Convert.ToInt64(row["config_column_id"],CultureInfo.InvariantCulture)!=23).Select(row=>JsonSerializer.Serialize(row)).ToArray());
        Assert.DoesNotContain(f.Provider.Rows["config_page_column"],row=>Convert.ToInt64(row["config_column_id"],CultureInfo.InvariantCulture)==21&&row.GetValueOrDefault("delete_no") is null);
    }

    /// <summary>新表显式初始化自己的默认页；普通新增页不补字段，不修其它既有页面。</summary>
    [Theory]
    [InlineData("config_table")] [InlineData("config_page")]
    public async Task CreationOnly_table_initializes_default_page_but_ordinary_page_does_not_add_fields(string table)
    {
        using var f=new Fixture();f.Get("config_page_column",40)["delete_no"]=DateTime.UtcNow;var oldField=JsonSerializer.Serialize(f.Get("config_page_column",40));
        var id=table=="config_table"?11L:31L;var values=CreationValues(table,id);
        await f.Session.InTransactionAsync(async()=>{await f.Store.InsertAsync(table,values);await f.Observer.BeforeCommitAsync(f.Context(table,id,values,true));return true;});
        if(table=="config_table")
        {
            var columns=f.Provider.Rows["config_column"].Where(row=>Convert.ToInt64(row["config_table_id"],CultureInfo.InvariantCulture)==11).ToArray();
            Assert.Equal(new[]{"delete_no","id","insert_manager_id","insert_time","name","update_manager_id","update_time","yz_project_id"},columns.Select(row=>(string)row["column_name"]!).Order(StringComparer.Ordinal));
            var page=Assert.Single(f.Provider.Rows["config_page"],row=>Convert.ToInt64(row["config_table_id"],CultureInfo.InvariantCulture)==11);var pageId=Convert.ToInt64(page["id"],CultureInfo.InvariantCulture);
            Assert.Equal(8,f.Provider.Rows["config_page_column"].Count(row=>Convert.ToInt64(row["config_page_id"],CultureInfo.InvariantCulture)==pageId));
        }
        else
        {
            Assert.DoesNotContain(f.Provider.Rows["config_page_column"],row=>Convert.ToInt64(row["config_page_id"],CultureInfo.InvariantCulture)==31);
        }
        Assert.Equal(oldField,JsonSerializer.Serialize(f.Get("config_page_column",40)));Assert.Single(f.Provider.Rows["config_page_column"],row=>Convert.ToInt64(row["config_page_id"],CultureInfo.InvariantCulture)==30);
    }

    /// <summary>源新增、默认对象与页面字段必须一起回滚，不能把本次创建变为历史补齐。</summary>
    [Theory]
    [InlineData("config_table")] [InlineData("config_page")] [InlineData("config_column")]
    public async Task CreationOnly_new_metadata_and_its_links_roll_back_on_tail_failure(string table)
    {
        using var f=new Fixture();var before=f.Snapshot();var id=table=="config_table"?11L:table=="config_page"?31L:21L;var values=CreationValues(table,id);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {
            await f.Store.InsertAsync(table,values);await f.Observer.BeforeCommitAsync(f.Context(table,id,values,true));throw new InvalidOperationException("模拟尾部观察器失败");
        }));Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    /// <summary>测试通过真实SqlRowStore插入源对象，不直接构造观察器的副作用结果。</summary>
    private static Dictionary<string,object?> CreationValues(string table,long id)=>table switch
    {
        "config_table"=>Row(("id",id),("yz_project_id",1L),("table_name","fresh_table"),("name","新表"),("yz_admin_type_id",90L)),
        "config_page"=>Row(("id",id),("yz_project_id",1L),("config_table_id",10L),("config_table_table_name","actual_table"),("name","新页"),("yz_admin_type_id",90L)),
        "config_column"=>Row(("id",id),("yz_project_id",1L),("config_table_id",10L),("column_name","fresh_column"),("name","新列"),("logical_type","string"),("column_type","nvarchar")),
        _=>throw new NotSupportedException("未登记测试类型。")
    };

    /// <summary>原新增列按全部活动页数决定显示，不是只给头两页；软删页不参与数量，旧列洞不补。</summary>
    [Theory]
    [InlineData(0,"plain")][InlineData(1,"plain")][InlineData(2,"plain")][InlineData(3,"plain")]
    [InlineData(0,"sort")][InlineData(1,"sort")][InlineData(2,"sort")][InlineData(3,"sort")]
    [InlineData(0,"join")][InlineData(1,"join")][InlineData(2,"join")][InlineData(3,"join")]
    public async Task OriginalCreation_column_defaults_follow_active_page_count_and_fixed_source_rules(int count,string kind)
    {
        using var f=new Fixture();f.Provider.Rows["config_page"].Clear();f.Provider.Rows["config_page_column"].Clear();
        for(var index=0;index<count;index++)f.Add("config_page",CreationValues("config_page",30+index));
        var deleted=CreationValues("config_page",39);deleted["delete_no"]=DateTime.UtcNow;f.Add("config_page",deleted);
        var values=CreationValues("config_column",21);values["column_name"]=kind=="sort"?"sort_no":"new_column";values["logical_type"]=kind=="sort"?"int32":"string";values["column_type"]=kind=="sort"?"int":"nvarchar";
        values["sort_no"]=null;values["is_join_column"]=kind=="join"?1:0;values["default_value"]="源默认不继承";values["is_db_not_null"]=1;values["is_must"]=1;values["is_read_only"]=1;
        await f.Session.InTransactionAsync(async()=>{await f.Store.InsertAsync("config_column",values);await f.Observer.BeforeCommitAsync(f.Context("config_column",21,values,true));return true;});
        var fields=f.Provider.Rows["config_page_column"];Assert.Equal(count,fields.Count);
        Assert.All(fields,field=>{Assert.Equal(21L,SqlRowStore.Id(field,"config_column_id"));Assert.NotEqual(39L,SqlRowStore.Id(field,"config_page_id"));AssertOriginalFieldDefaults(field,count<=2,kind=="join",kind=="sort");Assert.Null(field["config_column_sort_no"]);});
        Assert.DoesNotContain(fields,field=>SqlRowStore.Id(field,"config_column_id")==20);
        Assert.Contains(f.Provider.Commands,sql=>sql.Contains("FROM `config_page`",StringComparison.Ordinal)&&sql.EndsWith(" FOR UPDATE",StringComparison.Ordinal));
    }

    /// <summary>页面配置沿用原show值；主键、审计等名称不会自行改成只读，生成系统字段写保护仍由既有生成器承担。</summary>
    [Theory]
    [InlineData("id")][InlineData("yz_project_id")][InlineData("delete_no")][InlineData("insert_time")][InlineData("update_manager_id")]
    public void OriginalCreation_does_not_invent_readonly_from_system_column_names(string name)
    {
        var field=ConfigurationPageFieldDefaults.BuildPageField(40,Row(("id",30L)),Row(("id",20L),("column_name",name),("name","源字段"),("sort_no",null)),1,true);
        AssertOriginalFieldDefaults(field,true,false,false);Assert.Null(field["config_column_sort_no"]);
    }

    /// <summary>显式从表建页即使已有三页仍显示全部列；普通列、排序、联接与系统列共用同一固定默认工厂。</summary>
    [Fact]
    public async Task OriginalCreation_explicit_from_table_initializes_all_fields_independently_of_page_count()
    {
        using var f=new Fixture();AddCreationColumns(f);
        f.Add("config_page",CreationValues("config_page",31));f.Add("config_page",CreationValues("config_page",32));var before=JsonSerializer.Serialize(f.Provider.Rows["config_page_column"]);
        var result=await Actions(f).CreatePageAsync("10");var pageId=long.Parse(result.PageId,CultureInfo.InvariantCulture);
        var fields=f.Provider.Rows["config_page_column"].Where(field=>SqlRowStore.Id(field,"config_page_id")==pageId).ToArray();Assert.Equal(5,fields.Length);
        Assert.All(fields,field=>{var column=f.Get("config_column",SqlRowStore.Id(field,"config_column_id"));AssertOriginalFieldDefaults(field,true,SqlRowStore.Id(column,"is_join_column")==1,SqlRowStore.Text(column,"column_name")=="sort_no");Assert.Equal(8L,SqlRowStore.Id(field,"insert_manager_id"));});
        Assert.Null(fields.Single(field=>SqlRowStore.Id(field,"config_column_id")==21)["config_column_sort_no"]);
        Assert.Equal(before,JsonSerializer.Serialize(f.Provider.Rows["config_page_column"].Where(field=>SqlRowStore.Id(field,"config_page_id")==30)));
        Assert.Equal("yz_project_id=@current_yz_project_id",f.Get("config_page",pageId)["where_sql"]);
    }

    /// <summary>复制只认活动源映射，既没有配置的列和软删字段都不能在新页复活；原覆盖配置保留。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task OriginalCreation_copy_is_an_exact_active_field_copy_without_filling_holes(bool keepActive)
    {
        using var f=new Fixture();AddCreationColumns(f);var source=f.Get("config_page_column",40);source["is_in_grid"]=0;source["is_in_edit"]=0;source["is_read_only"]=1;source["is_must"]=1;source["default_value"]="原页覆盖";
        if(!keepActive)source["delete_no"]=DateTime.UtcNow;
        f.Add("config_page_column",Row(("id",41L),("yz_project_id",1L),("config_page_id",30L),("config_column_id",21L),("delete_no",DateTime.UtcNow),("default_value","软删不复活")));
        var original=JsonSerializer.Serialize(f.Provider.Rows["config_page_column"]);var columns=JsonSerializer.Serialize(f.Provider.Rows["config_column"]);var page=JsonSerializer.Serialize(f.Get("config_page",30));
        var result=await Actions(f).CopyPageAsync("30");var pageId=long.Parse(result.PageId,CultureInfo.InvariantCulture);var fields=f.Provider.Rows["config_page_column"].Where(field=>SqlRowStore.Id(field,"config_page_id")==pageId).ToArray();
        Assert.Equal(keepActive?1:0,fields.Length);Assert.Equal(keepActive?1:0,result.FieldIds!.Count);
        if(keepActive){var copy=Assert.Single(fields);foreach(var name in new[]{"is_in_grid","is_in_edit","is_read_only","is_must","default_value"})Assert.Equal(source[name],copy[name]);Assert.Equal("source_column",copy["config_column_column_name"]);}
        Assert.Equal(original,JsonSerializer.Serialize(f.Provider.Rows["config_page_column"].Where(field=>SqlRowStore.Id(field,"config_page_id")==30)));Assert.Equal(columns,JsonSerializer.Serialize(f.Provider.Rows["config_column"]));Assert.Equal(page,JsonSerializer.Serialize(f.Get("config_page",30)));
    }

    /// <summary>显式初始化及复制都加入调用事务，尾部失败回滚页、字段和审计，而不是单独提交默认映射。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task OriginalCreation_explicit_initialization_and_copy_roll_back_on_tail_failure(bool copy)
    {
        using var f=new Fixture();AddCreationColumns(f);var before=f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync<bool>(async()=>
        {if(copy)await Actions(f).CopyPageAsync("30");else await Actions(f).CreatePageAsync("10");throw new InvalidOperationException("模拟创建尾部失败");}));
        Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    /// <summary>内部初始化没有独立事务或跨项目旁路，不能被当作普通历史库补齐接口。</summary>
    [Fact]
    public async Task OriginalCreation_initializer_requires_the_existing_transaction_and_real_project()
    {
        using var f=new Fixture();var before=f.Snapshot();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Observer.InitializePageFieldsAsync(30,1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>{await f.Observer.InitializePageFieldsAsync(30,2);return true;}));Assert.Equal(before,f.Snapshot());
    }

    /// <summary>额外列仅是本机离线传输中的元数据输入，不预制任何页面字段结果。</summary>
    private static void AddCreationColumns(Fixture f)
    {
        foreach(var(id,name,type)in new[]{(21L,"sort_no","int32"),(22L,"joined_name","string"),(23L,"id","int64"),(24L,"insert_time","datetime")})
        {var values=CreationValues("config_column",id);values["column_name"]=name;values["sort_no"]=null;values["logical_type"]=type;values["is_join_column"]=id==22?1:0;f.Add("config_column",values);}
    }
    private static ConfigurationActionsBusiness Actions(Fixture f)=>new(f.Store,f.Registry,new MetadataRepository(f.Store,f.Registry),f.Authorization,f.Ids,f.Observer);
    /// <summary>断言原字段覆盖值，不用渲染后真假转换掩盖原始null与0的区别。</summary>
    private static void AssertOriginalFieldDefaults(IReadOnlyDictionary<string,object?> field,bool show,bool join,bool sort)
    {
        Flag("is_in_grid",show?1:0);Flag("is_in_edit",show?1:0);Flag("is_read_only",join?1:0);Flag("is_must",sort?1:0);Assert.Equal(sort?"99":"",field["default_value"]);
        foreach(var key in new[]{"is_important","is_in_filter","is_auto_focus","sum_kind","is_print","is_auto_save_after_change"})Flag(key,0);
        void Flag(string key,int expected){Assert.True(field.TryGetValue(key,out var value));Assert.NotNull(value);Assert.Equal(expected,Convert.ToInt32(value,CultureInfo.InvariantCulture));}
    }

    /// <summary>真实仓储与观察器拒绝活动child，不按父索引状态、开关或存在性缩小原V8规则。</summary>
    [Theory]
    [InlineData("active")][InlineData("disabled")][InlineData("deleted-index")][InlineData("missing-index")]
    public async Task IndexDeleteParity_active_child_blocks_and_rolls_back_source_and_related_metadata(string state)
    {
        using var f=new Fixture();AddDeleteIndex(f,state);var before=f.Snapshot();DbTransaction? expectedTransaction=null;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            expectedTransaction=f.Session.CurrentTransaction;
            await f.Store.UpdateAsync("config_table",10,new Dictionary<string,object?>{{"name","不得部分保留"}});
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await f.Observer.BeforeCommitAsync(new("offline","config_column",20,"1",new Dictionary<string,object?>(),true,expectedTransaction!));return true;
        }));
        Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
        var read=Assert.Single(f.Provider.Reads,command=>command.Sql.Contains("FROM `config_index_column`",StringComparison.Ordinal));
        Assert.Contains("`delete_no` IS NULL",read.Sql);Assert.Contains("`config_column_id`=@p0",read.Sql);Assert.EndsWith(" FOR UPDATE",read.Sql);
        Assert.DoesNotContain("`config_column_id`=20",read.Sql);Assert.DoesNotContain("JOIN",read.Sql,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20L,read.Values["p0"]);Assert.Same(expectedTransaction,read.Transaction);
        Assert.True(f.Provider.Commands.FindIndex(sql=>sql.Contains("FROM `yz_project`",StringComparison.Ordinal))<f.Provider.Commands.IndexOf(read.Sql));
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE `config_index_column`",StringComparison.Ordinal)||sql.StartsWith("UPDATE `config_page_column`",StringComparison.Ordinal));
    }

    /// <summary>只看活动且引用本列的child；墓碑不复活，也不改与当前列无关的索引。</summary>
    [Theory]
    [InlineData("none")][InlineData("deleted-child")][InlineData("other-column")]
    public async Task IndexDeleteParity_no_active_reference_allows_column_and_page_mapping_delete(string state)
    {
        using var f=new Fixture();if(state!="none")AddDeleteIndex(f,"active");
        if(state=="deleted-child")f.Get("config_index_column",60)["delete_no"]=DateTime.UtcNow;
        if(state=="other-column")f.Get("config_index_column",60)["config_column_id"]=21L;
        var indexes=JsonSerializer.Serialize(f.Provider.Rows.GetValueOrDefault("config_index_column"));
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await f.Observer.BeforeCommitAsync(new("offline","config_column",20,"1",new Dictionary<string,object?>(),true,f.Session.CurrentTransaction!));return true;
        });
        Assert.NotNull(f.Get("config_column",20)["delete_no"]);Assert.NotNull(f.Get("config_page_column",40)["delete_no"]);
        Assert.Equal(indexes,JsonSerializer.Serialize(f.Provider.Rows.GetValueOrDefault("config_index_column")));
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE `config_index_column`",StringComparison.Ordinal));
    }

    /// <summary>整表合法聚合先删本表索引及字段，再逐列检查；SQL顺序不是MySQL实际锁顺序证明。</summary>
    [Fact]
    public async Task IndexDeleteParity_table_removes_own_indexes_before_checking_each_column()
    {
        using var f=new Fixture();AddDeleteIndex(f,"active");
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_table",10,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await f.Observer.BeforeCommitAsync(new("offline","config_table",10,"1",new Dictionary<string,object?>(),true,f.Session.CurrentTransaction!));return true;
        });
        foreach(var(table,id)in new[]{("config_index_column",60L),("config_index",50L),("config_column",20L),("config_page",30L),("config_page_column",40L)})Assert.NotNull(f.Get(table,id)["delete_no"]);
        var childDelete=f.Provider.Commands.FindIndex(sql=>sql.StartsWith("UPDATE `config_index_column`",StringComparison.Ordinal));
        var indexDelete=f.Provider.Commands.FindIndex(sql=>sql.StartsWith("UPDATE `config_index`",StringComparison.Ordinal));
        var referenceRead=f.Provider.Commands.FindIndex(sql=>sql.Contains("FROM `config_index_column`",StringComparison.Ordinal)&&sql.Contains("`config_column_id`=",StringComparison.Ordinal));
        var columnDelete=f.Provider.Commands.FindIndex(sql=>sql.StartsWith("UPDATE `config_column`",StringComparison.Ordinal));
        Assert.True(childDelete>=0&&childDelete<indexDelete&&indexDelete<referenceRead&&referenceRead<columnDelete);
        Assert.All(f.Provider.Reads.Where(read=>read.Sql.Contains("FROM `config_index",StringComparison.Ordinal)||read.Sql.Contains("FROM `config_column`",StringComparison.Ordinal)),read=>Assert.EndsWith(" FOR UPDATE",read.Sql));
    }

    /// <summary>table上下文不是删除引用的特权，残存其它索引或悬空child须连前面已清理的自有索引一起回滚。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task IndexDeleteParity_table_with_remaining_external_or_orphan_child_rolls_back(bool orphan)
    {
        using var f=new Fixture();AddDeleteIndex(f,"active");
        if(!orphan)f.Add("config_index",Row(("id",51L),("yz_project_id",1L),("config_table_id",11L),("is_enabled",1)));
        f.Add("config_index_column",Row(("id",61L),("yz_project_id",1L),("config_index_id",51L),("config_column_id",20L)));
        var before=f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_table",10,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await f.Observer.BeforeCommitAsync(new("offline","config_table",10,"1",new Dictionary<string,object?>(),true,f.Session.CurrentTransaction!));return true;
        }));
        Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
        Assert.Contains(f.Provider.Commands,sql=>sql.StartsWith("UPDATE `config_index_column`",StringComparison.Ordinal));
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE `config_column`",StringComparison.Ordinal));
    }

    /// <summary>全部直接从属都须证明本项目归属，不能用查询过滤隐藏foreign行后提交悬空活动引用。</summary>
    [Theory]
    [InlineData("index")][InlineData("column")][InlineData("child-other-column")]
    public async Task IndexDeleteParity_table_rejects_foreign_direct_dependents_without_partial_write(string kind)
    {
        using var f=new Fixture();AddDeleteIndex(f,"active");
        if(kind=="index")f.Get("config_index",50)["yz_project_id"]=2L;
        else if(kind=="column")f.Get("config_column",20)["yz_project_id"]=2L;
        else
        {
            f.Get("config_index_column",60)["yz_project_id"]=2L;
            f.Get("config_index_column",60)["config_column_id"]=999L;
        }
        var before=f.Snapshot();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_table",10,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await f.Observer.BeforeCommitAsync(new("offline","config_table",10,"1",new Dictionary<string,object?>(),true,f.Session.CurrentTransaction!));return true;
        }));
        Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
        var table=kind=="index"?"config_index":kind=="column"?"config_column":"config_index_column";
        var reads=f.Provider.Reads.Where(read=>read.Sql.Contains("FROM `"+table+"`",StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(reads);Assert.All(reads,read=>{Assert.DoesNotContain("`yz_project_id`=",read.Sql);Assert.EndsWith(" FOR UPDATE",read.Sql);});
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE `"+table+"`",StringComparison.Ordinal));
    }

    /// <summary>所有入口必须使用实际写事务；另一个Session的事务不能借观察器完成删除联动。</summary>
    [Fact]
    public async Task IndexDeleteParity_rejects_a_transaction_from_another_session_before_reading_references()
    {
        using var f=new Fixture();using var other=new Fixture();
        await other.Session.InTransactionAsync(async()=>
        {
            await f.Session.InTransactionAsync(async()=>
            {
                await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Observer.BeforeCommitAsync(new("offline","config_column",20,"1",new Dictionary<string,object?>(),true,other.Session.CurrentTransaction!)));return true;
            });return true;
        });
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.Contains("config_index_column",StringComparison.Ordinal));
    }

    /// <summary>仅建立离线参数化仓储的数据输入，不伪造观察器执行后的结果。</summary>
    private static void AddDeleteIndex(Fixture f,string state)
    {
        if(state!="missing-index")f.Add("config_index",Row(("id",50L),("yz_project_id",1L),("config_table_id",10L),("is_enabled",state=="disabled"?0:1),("delete_no",state=="deleted-index"?DateTime.UtcNow:null)));
        f.Add("config_index_column",Row(("id",60L),("yz_project_id",1L),("config_index_id",50L),("config_column_id",20L)));
    }

    private static Dictionary<string,object?> Row(params(string Key,object? Value)[] fields)=>fields.ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal);
    private sealed class Fixture:IDisposable
    {
        internal readonly MemoryProvider Provider=new();internal DatabaseSession Session{get;}internal MetadataRegistry Registry{get;}internal SqlRowStore Store{get;}internal ConfigurationMutationObserver Observer{get;}internal HttpRuntimeAuthorization Authorization{get;}internal IIdGenerator Ids{get;}=new SnowflakeIdGenerator(829);
        internal Fixture([CallerFilePath]string file="")
        {
            var backend=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../.."));Registry=new(Path.Combine(backend,"metadata/platform.json"),Path.Combine(backend,"metadata/demo.json"));
            var options=new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1"};
            Session=new(options,null,Provider);Store=new(Session,Registry.Platform);Observer=new(Store,Registry,Ids);
            var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(new(8,"离线配置测试",true,"1",""),new HashSet<string>(),1,null);Authorization=new(new HttpContextAccessor{HttpContext=http},Registry);
            Add("yz_project",Row(("id",1L)));Add("yz_admin_type",Row(("id",90L),("yz_project_id",1L)));
            Add("config_table",Row(("id",10L),("yz_project_id",1L),("table_name","actual_table"),("name","表"),("yz_admin_type_id",90L)));
            Add("config_column",Row(("id",20L),("yz_project_id",1L),("config_table_id",10L),("column_name","source_column"),("name","源字段"),("sort_no",10),("logical_type","string"),("column_type","nvarchar")));
            Add("config_page",Row(("id",30L),("yz_project_id",1L),("config_table_id",10L),("name","原页"),("yz_admin_type_id",90L),("config_table_table_name","old_table")));
            Add("config_page_column",Row(("id",40L),("yz_project_id",1L),("config_column_id",20L),("config_page_id",30L),("config_column_column_name","stale_name"),("config_column_sort_no",99)));
        }
        internal void Add(string table,Dictionary<string,object?> row){if(!Provider.Rows.TryGetValue(table,out var rows))Provider.Rows[table]=rows=[];rows.Add(row);}
        internal Dictionary<string,object?> Get(string table,long id)=>Provider.Rows[table].Single(row=>Convert.ToInt64(row["id"],CultureInfo.InvariantCulture)==id);
        internal RecordMutationContext Context(string table,long id,Dictionary<string,object?> submitted,bool creating=false)=>new("offline",table,id,"1",submitted,false,Session.CurrentTransaction!,creating);
        internal string Snapshot()=>JsonSerializer.Serialize(Provider.Rows);
        public void Dispose()=>Session.Dispose();
    }

    /// <summary>仅解释SqlRowStore固定生成的参数化单表命令；不支持任意SQL，也不建立网络连接。</summary>
    private sealed class MemoryProvider:IDatabaseProvider
    {
        internal Dictionary<string,List<Dictionary<string,object?>>> Rows{get;set;}=new(StringComparer.Ordinal);internal List<string> Commands{get;}=[];internal List<(string Sql,Dictionary<string,object?> Values,DbTransaction? Transaction)> Reads{get;}=[];internal int Rollbacks;internal Func<string,DbParameterCollection,bool>? SkipWrite;
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options)=>new MemoryConnection(this);public string QuoteIdentifier(string id)=>new MySqlDatabaseProvider().QuoteIdentifier(id);public string ParameterPlaceholder(string name)=>"@"+name;public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);public string BuildTextSearchPredicate(string column,string parameter,bool numeric)=>new MySqlDatabaseProvider().BuildTextSearchPredicate(column,parameter,numeric);public string BuildBoundedRead(string sql,int maximum)=>new MySqlDatabaseProvider().BuildBoundedRead(sql,maximum);
        internal DbDataReader Read(string sql,DbParameterCollection parameters,DbTransaction? transaction)
        {
            Reads.Add((sql,parameters.Cast<DbParameter>().ToDictionary(parameter=>parameter.ParameterName,parameter=>parameter.Value),transaction));
            Commands.Add(sql);if(sql.EndsWith(" ORDER BY `id` DESC LIMIT 50",StringComparison.Ordinal))return Search(sql,parameters);var match=Regex.Match(sql,@"^SELECT (.+) FROM `([^`]+)`(?: WHERE (.*?))?(?: ORDER BY `id`)?(?: FOR UPDATE)?$");if(!match.Success)throw new NotSupportedException(sql);
            var names=match.Groups[1].Value.Split(',').Select(value=>value.Trim('`')).ToArray();var data=new DataTable();foreach(var name in names)data.Columns.Add(name,typeof(object));
            foreach(var row in Rows.GetValueOrDefault(match.Groups[2].Value)??[])
            {
                var conditions=match.Groups[3].Value;var accepted=true;
                foreach(Match item in Regex.Matches(conditions,@"`([^`]+)`( IS NULL|=@([a-zA-Z0-9_]+))"))
                {var actual=row.GetValueOrDefault(item.Groups[1].Value);var expected=item.Groups[2].Value==" IS NULL"?null:parameters[item.Groups[3].Value].Value;accepted&=Equal(actual,expected);}
                var selected=Regex.Match(conditions,@"`id` IN \((@[a-zA-Z0-9_]+(?:,@[a-zA-Z0-9_]+)*)\)");
                if(selected.Success)accepted&=selected.Groups[1].Value.Split(',').Any(name=>Equal(row.GetValueOrDefault("id"),parameters[name.TrimStart('@')].Value));
                if(accepted)data.Rows.Add(names.Select(name=>row.GetValueOrDefault(name)??DBNull.Value).ToArray());
            }
            return data.CreateDataReader();
        }
        /// <summary>只解释新增首页搜索的精确固定SQL；由原生产方言生成，未知条件不静默放过。</summary>
        private DbDataReader Search(string sql,DbParameterCollection parameters)
        {
            var match=Regex.Match(sql,@"^SELECT (.+) FROM `(config_table|config_page)` WHERE `delete_no` IS NULL AND `yz_project_id`=@project(?: AND \((.+)\))? ORDER BY `id` DESC LIMIT 50$");
            if(!match.Success)throw new NotSupportedException("未登记的受控搜索SQL。");
            var table=match.Groups[2].Value;var expected=table=="config_table"?"CAST(`id` AS CHAR) LIKE @keyword OR `name` LIKE @keyword OR `table_name` LIKE @keyword":"CAST(`id` AS CHAR) LIKE @keyword OR `name` LIKE @keyword OR `config_table_name` LIKE @keyword";
            var conditions=match.Groups[3].Value;if(conditions.Length>0&&conditions!=expected)throw new NotSupportedException("搜索条件不符合固定模型。");
            var columns=match.Groups[1].Value.Split(',').Select(value=>value.Trim('`')).ToArray();var data=new DataTable();foreach(var column in columns)data.Columns.Add(column,typeof(object));
            bool NameMatch(Dictionary<string,object?> row)
            {
                if(conditions.Length==0)return true;
                var expression="\\A"+Regex.Escape(Convert.ToString(parameters["keyword"].Value,CultureInfo.InvariantCulture)!).Replace("%",".*").Replace("_",".")+"\\z";
                return (table=="config_table"?new[]{"id","name","table_name"}:new[]{"id","name","config_table_name"}).Any(name=>Regex.IsMatch(Convert.ToString(row.GetValueOrDefault(name),CultureInfo.InvariantCulture)??"",expression,RegexOptions.CultureInvariant));
            }
            foreach(var row in (Rows.GetValueOrDefault(table)??[]).Where(row=>row.GetValueOrDefault("delete_no") is null&&Equal(row.GetValueOrDefault("yz_project_id"),parameters["project"].Value)&&NameMatch(row)).OrderByDescending(row=>SqlRowStore.Id(row)).Take(50))
                data.Rows.Add(columns.Select(column=>row.GetValueOrDefault(column)??DBNull.Value).ToArray());
            return data.CreateDataReader();
        }
        internal int Write(string sql,DbParameterCollection parameters)
        {
            Commands.Add(sql);if(SkipWrite?.Invoke(sql,parameters)==true)return 0;var update=Regex.Match(sql,@"^UPDATE `([^`]+)` SET (.+) WHERE `id`=@record_id$");
            if(update.Success)
            {
                var row=(Rows.GetValueOrDefault(update.Groups[1].Value)??[]).SingleOrDefault(row=>Equal(row.GetValueOrDefault("id"),parameters["record_id"].Value));if(row is null)return 0;
                foreach(Match field in Regex.Matches(update.Groups[2].Value,@"`([^`]+)`=@([a-zA-Z0-9_]+)"))row[field.Groups[1].Value]=Unwrap(parameters[field.Groups[2].Value].Value);return 1;
            }
            var insert=Regex.Match(sql,@"^INSERT INTO `([^`]+)` \((.+)\) VALUES \((.+)\)$");if(!insert.Success)throw new NotSupportedException(sql);
            var names=insert.Groups[2].Value.Split(',').Select(name=>name.Trim('`')).ToArray();var values=insert.Groups[3].Value.Split(',').Select(name=>Unwrap(parameters[name.TrimStart('@')].Value)).ToArray();var target=insert.Groups[1].Value;if(!Rows.TryGetValue(target,out var list))Rows[target]=list=[];list.Add(names.Select((name,index)=>(name,index)).ToDictionary(item=>item.name,item=>values[item.index],StringComparer.Ordinal));return 1;
        }
        private static object? Unwrap(object? value)=>value is DBNull?null:value;
        private static bool Equal(object? first,object? second)=>string.Equals(Convert.ToString(Unwrap(first),CultureInfo.InvariantCulture),Convert.ToString(Unwrap(second),CultureInfo.InvariantCulture),StringComparison.Ordinal)&&((Unwrap(first)is null)==(Unwrap(second)is null));
    }
    private sealed class MemoryConnection(MemoryProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new MemoryTransaction(this,provider);protected override DbCommand CreateDbCommand()=>new MemoryCommand(provider,this);
    }
    private sealed class MemoryTransaction(MemoryConnection connection,MemoryProvider provider):DbTransaction
    {
        private readonly Dictionary<string,List<Dictionary<string,object?>>> before=provider.Rows.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(row=>new Dictionary<string,object?>(row,StringComparer.Ordinal)).ToList(),StringComparer.Ordinal);
        public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback(){provider.Rollbacks++;provider.Rows=before;}
    }
    private sealed class MemoryCommand(MemoryProvider provider,MemoryConnection connection):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override object? ExecuteScalar()=>throw new NotSupportedException();public override int ExecuteNonQuery()=>provider.Write(CommandText,Parameters);protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Read(CommandText,Parameters,DbTransaction);
    }
}
