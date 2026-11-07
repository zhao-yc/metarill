using System.Globalization;
using System.Text.Json;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>原首页搜索实际执行生产业务/仓储，受控ADO严格识别OR、项目、墓碑、排序与50条SQL上限。</summary>
public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>原名称、物理表名、ID子串与页面所属表显示名分别命中；所有结果仍来自当前项目。</summary>
    [Theory]
    [InlineData("标题","10","30")]
    [InlineData("actual_table","10",null)]
    [InlineData("归属显示名",null,"30")]
    [InlineData("4740993","9007199254740993",null)]
    [InlineData("' OR 1=1 --","11",null)]
    public async Task Home_search_preserves_original_fields_and_parameterized_or_matching(string keyword,string? tableId,string? pageId)
    {
        using var f=new Fixture();f.Get("config_table",10)["name"]="标题";
        f.Get("yz_admin_type",90)["name"]="后台";f.Get("config_table",10)["is_rongyu"]=1;
        f.Get("config_page",30)["name"]="标题";f.Get("config_page",30)["config_table_name"]="归属显示名";
        f.Get("config_page",30)["config_table_table_name"]="actual_table";f.Get("config_page",30)["type"]=2;
        f.Add("config_table",Row(("id",9007199254740993L),("yz_project_id",1L),("name","大ID"),("table_name","big_id")));
        f.Add("config_table",Row(("id",11L),("yz_project_id",1L),("name","' OR 1=1 --"),("table_name","quoted")));
        f.Add("config_table",Row(("id",999L),("yz_project_id",2L),("name",keyword),("table_name",keyword)));
        f.Add("config_page",Row(("id",999L),("yz_project_id",1L),("name",keyword),("delete_no",DateTime.UtcNow)));
        var before=f.Snapshot();var result=JsonSerializer.SerializeToElement(await CandidateBusiness(f).SearchAsync(JsonSerializer.SerializeToElement(new{keyword})),JsonDefaults.Options);
        var tables=result.GetProperty("configTables").EnumerateArray().ToArray();var pages=result.GetProperty("configPages").EnumerateArray().ToArray();
        Assert.Equal(tableId is null?Array.Empty<string>():[tableId],tables.Select(row=>row.GetProperty("id").GetString()));
        Assert.Equal(pageId is null?Array.Empty<string>():[pageId],pages.Select(row=>row.GetProperty("id").GetString()));
        if(tableId=="10"){Assert.Equal("actual_table",tables[0].GetProperty("tableName").GetString());Assert.Equal("后台",tables[0].GetProperty("yzAdminTypeName").GetString());Assert.Equal(1,tables[0].GetProperty("isRongyu").GetInt32());}
        if(pageId=="30"){Assert.Equal("actual_table",pages[0].GetProperty("configTableTableName").GetString());Assert.Equal("多选页",pages[0].GetProperty("typeShow").GetString());}
        var searches=f.Provider.Reads.Where(read=>read.Sql.EndsWith(" LIMIT 50",StringComparison.Ordinal)).ToArray();Assert.Equal(2,searches.Length);
        Assert.All(searches,read=>{Assert.Equal("%"+keyword+"%",read.Values["keyword"]);Assert.EndsWith(" ORDER BY "+QuoteCandidate("id")+" DESC LIMIT 50",read.Sql);Assert.Null(read.Transaction);});
        if(keyword.StartsWith("'"))Assert.All(searches,read=>Assert.DoesNotContain(keyword,read.Sql,StringComparison.Ordinal));
        Assert.Equal(before,f.Snapshot());
    }

    /// <summary>筛选、墓碑、项目和倒序先于SQL50条限制，空白/缺省沿用原前50条；原LIKE通配符语义保持。</summary>
    [Theory]
    [InlineData("match")][InlineData("")][InlineData(" ")][InlineData("%")]
    public async Task Home_search_is_bounded_after_scope_and_order_in_sql(string keyword)
    {
        using var f=new Fixture();
        for(long id=100;id<160;id++)f.Add("config_table",Row(("id",id),("yz_project_id",1L),("name","match"),("table_name","t"+id)));
        f.Add("config_table",Row(("id",999L),("yz_project_id",2L),("name","match")));
        f.Add("config_table",Row(("id",998L),("yz_project_id",1L),("name","match"),("delete_no",DateTime.UtcNow)));
        var result=JsonSerializer.SerializeToElement(await CandidateBusiness(f).SearchAsync(JsonSerializer.SerializeToElement(new{keyword})),JsonDefaults.Options);
        var rows=result.GetProperty("configTables").EnumerateArray().ToArray();Assert.Equal(50,rows.Length);
        Assert.Equal(Enumerable.Range(110,50).Reverse().Select(id=>id.ToString(CultureInfo.InvariantCulture)),rows.Select(row=>row.GetProperty("id").GetString()));
        Assert.All(f.Provider.Reads.Where(read=>read.Sql.EndsWith(" LIMIT 50",StringComparison.Ordinal)),read=>Assert.Equal(!string.IsNullOrWhiteSpace(keyword),read.Values.ContainsKey("keyword")));
    }

    /// <summary>名称目标被删除、缺失或跨项目时不回落到旧缓存，ID和当前项目源记录仍保持。</summary>
    [Theory]
    [InlineData("foreign")][InlineData("deleted")][InlineData("missing")]
    public async Task Home_search_does_not_reuse_cached_foreign_or_inactive_target_names(string scenario)
    {
        using var f=new Fixture();f.Get("config_table",10)["name"]="唯一命中";
        f.Get("config_page",30)["name"]="唯一命中";f.Get("config_page",30)["config_table_name"]="禁止显示的旧缓存";
        f.Get("config_page",30)["config_table_id"]=12L;f.Get("config_page",30)["yz_admin_type_id"]=91L;
        f.Add("config_table",Row(("id",12L),("yz_project_id",scenario=="foreign"?2L:1L),("name","禁止显示的目标"),("table_name","PRIVATE_TABLE"),("delete_no",scenario=="deleted"?DateTime.UtcNow:null)));
        f.Add("yz_admin_type",Row(("id",91L),("yz_project_id",scenario=="foreign"?2L:1L),("name","禁止显示的后台"),("delete_no",scenario=="deleted"?DateTime.UtcNow:null)));
        if(scenario=="missing"){f.Provider.Rows["config_table"].Remove(f.Get("config_table",12));f.Provider.Rows["yz_admin_type"].Remove(f.Get("yz_admin_type",91));}
        var result=JsonSerializer.SerializeToElement(await CandidateBusiness(f).SearchAsync(JsonSerializer.SerializeToElement(new{keyword="唯一命中"})),JsonDefaults.Options);
        var page=Assert.Single(result.GetProperty("configPages").EnumerateArray());Assert.Equal("30",page.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null,page.GetProperty("configTableName").ValueKind);Assert.Equal(JsonValueKind.Null,page.GetProperty("configTableTableName").ValueKind);Assert.Equal(JsonValueKind.Null,page.GetProperty("yzAdminTypeName").ValueKind);
        Assert.DoesNotContain("禁止显示",result.GetRawText(),StringComparison.Ordinal);Assert.DoesNotContain("PRIVATE_TABLE",result.GetRawText(),StringComparison.Ordinal);
    }

    /// <summary>新的只读ID集合不能借空集、非法/重复ID或字段表达式扩大成全表；空集不连接数据库。</summary>
    [Theory]
    [InlineData("empty")][InlineData("negative")][InlineData("duplicate")][InlineData("large")][InlineData("column")][InlineData("filter")][InlineData("table")]
    public async Task Home_search_target_reads_reject_invalid_scope_and_never_lock(string scenario)
    {
        using var f=new Fixture();IReadOnlyCollection<long> ids=scenario switch{"empty"=>[], "negative"=>[-1], "duplicate"=>[90,90], "large"=>Enumerable.Range(1,1001).Select(id=>(long)id).ToArray(),_=>[90]};
        var columns=scenario=="column"?new[]{"id","name AS label"}:new[]{"id","name"};
        var filters=scenario=="filter"?new Dictionary<string,object?>{{"foreign_filter",1L}}:null;
        if(scenario=="empty")Assert.Empty(await f.Store.ReadProjectedByIdsAsync("yz_admin_type",columns,ids));
        else
        {
            var error=await Record.ExceptionAsync(async()=>{_ = await f.Store.ReadProjectedByIdsAsync(scenario=="table"?"unknown":"yz_admin_type",columns,ids,filters);});
            Assert.NotNull(error);Assert.True(error is ArgumentException or InvalidOperationException);
        }
        Assert.Empty(f.Provider.Reads);Assert.Empty(f.Provider.Commands);
    }

    /// <summary>首页类型展示沿用发布常量而非另写固定中文；空选项不替代null，未知值保留原文。</summary>
    [Theory]
    [InlineData(1," 自定义选择 ")][InlineData(3,"3")][InlineData(null,null)]
    public async Task Home_search_type_display_uses_published_choice_text(int? type,string? expected)
    {
        using var f=new Fixture();var table=f.Registry.Platform.ConfigTables.Single(row=>row.TableName=="config_page");
        f.Registry.Platform.ConfigColumns.Single(column=>column.ConfigTableId==table.Id&&column.ColumnName=="type").ControlDetail="提示|,默认|0, 自定义选择 |1,多选页|2";
        f.Get("config_page",30)["type"]=type;
        var result=JsonSerializer.SerializeToElement(await CandidateBusiness(f).SearchAsync(JsonSerializer.SerializeToElement(new{keyword="原页"})),JsonDefaults.Options);
        Assert.Equal(expected,Assert.Single(result.GetProperty("configPages").EnumerateArray()).GetProperty("typeShow").GetString());
    }

    /// <summary>未知属性及非文本关键词不进入数据库；客户端不能借首页提交项目、表或SQL。</summary>
    [Theory]
    [InlineData("{\"keyword\":1}",typeof(ArgumentException))][InlineData("{\"keyword\":\"x\",\"projectId\":\"2\"}",typeof(InvalidOperationException))][InlineData("[]",typeof(InvalidOperationException))]
    public async Task Home_search_rejects_unpublished_request_fields_before_sql(string json,Type expected)
    {
        using var f=new Fixture();using var doc=JsonDocument.Parse(json);var request=doc.RootElement.Clone();
        await Assert.ThrowsAsync(expected,()=>CandidateBusiness(f).SearchAsync(request));Assert.Empty(f.Provider.Reads);
    }
}
