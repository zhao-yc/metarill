using System.Text.Json;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>真实业务与SqlRowStore经既有ADO夹具执行；只模拟传输，SQL列、条件和返回值均实际验证。</summary>
public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>大ID、同名排序、墓碑和外项目候选不因窄读取变化；不从数据库拉回隐藏明细。</summary>
    [Fact]
    public async Task Candidate_tables_preserve_original_options_and_order_with_only_id_and_name_reads()
    {
        using var f=new Fixture();
        f.Add("config_table",Row(("id",9007199254740993L),("yz_project_id",1L),("name","同名"),("table_name","second")));
        f.Add("config_table",Row(("id",99L),("yz_project_id",2L),("name","外项目")));
        f.Add("config_table",Row(("id",98L),("yz_project_id",1L),("name","已删除"),("delete_no",DateTime.UtcNow)));
        f.Get("config_table",10)["remarks"]="不得作为候选读取的明细";
        var before=f.Snapshot();var options=await CandidateBusiness(f).TablesAsync(JsonSerializer.SerializeToElement(new{}));
        Assert.Equal(new[]{"9007199254740993","10"},options.Select(option=>option.Id));
        Assert.Equal(new[]{"同名","表"},options.Select(option=>option.Name));
        Assert.All(options,option=>{Assert.Equal(option.Id,option.Value);Assert.Equal(option.Name,option.Text);});
        var read=Assert.Single(f.Provider.Reads);Assert.Equal("SELECT "+string.Join(",",new[]{"id","name"}.Select(QuoteCandidate))+" FROM "+QuoteCandidate("config_table")+" WHERE "+QuoteCandidate("delete_no")+" IS NULL AND "+QuoteCandidate("yz_project_id")+"=@p0",read.Sql);
        Assert.Equal(1L,read.Values["p0"]);Assert.Null(read.Transaction);Assert.Equal(before,f.Snapshot());
    }

    /// <summary>原单选/多选类型、活动父表归属、名字和ID排序均在原行为内；只取消费列。</summary>
    [Theory]
    [InlineData("SelectPage",1)][InlineData("MultiSelectPage",2)]
    public async Task Candidate_pages_preserve_type_parent_and_project_without_fetching_sql(string kind,int type)
    {
        using var f=new Fixture();f.Get("config_page",30)["type"]=type;
        f.Get("config_page",30)["where_sql"]="PRIVATE_SQL_MUST_NOT_BE_READ";
        f.Add("config_page",Row(("id",9007199254740993L),("yz_project_id",1L),("config_table_id",10L),("name","原页"),("type",type)));
        f.Add("config_page",Row(("id",32L),("yz_project_id",1L),("config_table_id",10L),("name","另一类型"),("type",3-type)));
        f.Add("config_table",Row(("id",11L),("yz_project_id",1L),("name","已删表"),("delete_no",DateTime.UtcNow)));
        f.Add("config_table",Row(("id",12L),("yz_project_id",2L),("name","外项目表")));
        f.Add("config_page",Row(("id",33L),("yz_project_id",1L),("config_table_id",11L),("name","已删父"),("type",type)));
        f.Add("config_page",Row(("id",34L),("yz_project_id",1L),("config_table_id",12L),("name","外父"),("type",type)));
        f.Add("config_page",Row(("id",35L),("yz_project_id",2L),("config_table_id",10L),("name","外项目"),("type",type)));
        f.Add("config_page",Row(("id",36L),("yz_project_id",1L),("config_table_id",10L),("name","已删页"),("type",type),("delete_no",DateTime.UtcNow)));
        var before=f.Snapshot();var result=await CandidateBusiness(f).PagesAsync(JsonSerializer.SerializeToElement(new{controlKind=kind}));
        Assert.Equal(new[]{"9007199254740993","30"},result.Select(option=>option.Id));
        Assert.All(result,option=>{Assert.Equal("原页",option.Name);Assert.Equal(option.Id,option.Value);Assert.Equal(option.Name,option.Text);});
        Assert.Equal(2,f.Provider.Reads.Count);
        Assert.StartsWith("SELECT "+QuoteCandidate("id")+" FROM "+QuoteCandidate("config_table"),f.Provider.Reads[0].Sql);
        Assert.StartsWith("SELECT "+string.Join(",",new[]{"id","name","type","config_table_id"}.Select(QuoteCandidate))+" FROM "+QuoteCandidate("config_page"),f.Provider.Reads[1].Sql);
        Assert.All(f.Provider.Reads,read=>{Assert.Contains(QuoteCandidate("delete_no")+" IS NULL",read.Sql);Assert.Contains(QuoteCandidate("yz_project_id")+"=@p0",read.Sql);Assert.Equal(1L,read.Values["p0"]);Assert.Null(read.Transaction);});
        Assert.Equal(before,f.Snapshot());
    }

    /// <summary>字段明细继承仍原文返回，只以同事务的父表ID验证归属，不取父表的其它内容。</summary>
    [Fact]
    public async Task Candidate_control_metadata_preserves_original_detail_and_transaction_without_whole_rows()
    {
        using var f=new Fixture();var detail="{\"whereSql\":\"status=1\",\"orderBy\":\"sort_no\"}";
        f.Get("config_column",20)["control_kind"]="List";f.Get("config_column",20)["control_detail"]=detail;
        var before=f.Snapshot();var result=await CandidateBusiness(f).ControlMetadataAsync(JsonSerializer.SerializeToElement(new{id="20"}));
        Assert.Equal(new ConfigurationControlMetadata("List","source_column","nvarchar","源字段",detail),result);
        Assert.Equal(2,f.Provider.Reads.Count);
        Assert.StartsWith("SELECT "+string.Join(",",new[]{"config_table_id","control_kind","column_name","column_type","name","control_detail"}.Select(QuoteCandidate))+" FROM "+QuoteCandidate("config_column"),f.Provider.Reads[0].Sql);
        Assert.StartsWith("SELECT "+QuoteCandidate("id")+" FROM "+QuoteCandidate("config_table"),f.Provider.Reads[1].Sql);
        var transaction=f.Provider.Reads[0].Transaction;Assert.NotNull(transaction);Assert.All(f.Provider.Reads,read=>{Assert.Same(transaction,read.Transaction);Assert.DoesNotContain("FOR UPDATE",read.Sql);});
        Assert.Equal(before,f.Snapshot());
    }

    /// <summary>投影不允许省略父级归属检查，跨项目、墓碑或缺失父表仍整体拒绝。</summary>
    [Theory]
    [InlineData("foreign")][InlineData("deleted")][InlineData("missing")]
    public async Task Candidate_control_metadata_keeps_parent_rejection_with_narrow_reads(string kind)
    {
        using var f=new Fixture();
        if(kind=="foreign")f.Get("config_table",10)["yz_project_id"]=2L;
        else if(kind=="deleted")f.Get("config_table",10)["delete_no"]=DateTime.UtcNow;
        else f.Provider.Rows["config_table"].Clear();
        var before=f.Snapshot();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>CandidateBusiness(f).ControlMetadataAsync(JsonSerializer.SerializeToElement(new{id="20"})));
        Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    /// <summary>非法列或表达式在返回任何异步数据库工作前同步拒绝，不允许空投影退化为整行。</summary>
    [Theory]
    [InlineData("empty")][InlineData("duplicate")][InlineData("unknown")][InlineData("expression")]
    [InlineData("wildcard")][InlineData("case")][InlineData("filter")][InlineData("table")][InlineData("null")]
    public void Candidate_projection_rejects_invalid_columns_before_database_work(string kind)
    {
        using var f=new Fixture();IReadOnlyList<string> columns=kind switch
        {
            "empty"=>[],"duplicate"=>["id","id"],"unknown"=>["unknown_column"],"expression"=>["id AS other"],
            "wildcard"=>["*"],"case"=>["ID"],"null"=>null!,_=>["id"]
        };
        var filters=kind=="filter"?new Dictionary<string,object?>{{"unknown_filter",1L}}:null;
        if(kind is "filter" or "table")Assert.Throws<InvalidOperationException>(()=>{_ = f.Store.ReadProjectedAsync(kind=="table"?"unknown_table":"config_table",columns,filters);});
        else if(kind=="null")Assert.Throws<ArgumentNullException>(()=>{_ = f.Store.ReadProjectedAsync("config_table",columns);});
        else Assert.Throws<ArgumentException>(()=>{_ = f.Store.ReadProjectedAsync("config_table",columns);});
        Assert.Empty(f.Provider.Reads);Assert.Empty(f.Provider.Commands);
    }

    /// <summary>取消先于列检查或连接；已有全实体与锁读入口继续返回完整发布物理列。</summary>
    [Fact]
    public async Task Candidate_projection_does_not_change_original_complete_reads_or_write_locks()
    {
        using var f=new Fixture();using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>{_ = f.Store.ReadProjectedAsync("config_column",["id"],cancellationToken:cancellation.Token);});
        Assert.Empty(f.Provider.Commands);
        var expected=Metarill.Core.Metadata.MetadataStoragePolicy.ForTable(f.Registry.Platform,f.Registry.Platform.ConfigTables.Single(table=>table.TableName=="config_column")).PhysicalColumns.Select(column=>column.ColumnName).Order(StringComparer.Ordinal).ToArray();
        var full=Assert.Single(await f.Store.ReadAsync("config_column"));Assert.Equal(expected,full.Keys.Order(StringComparer.Ordinal));
        await f.Session.InTransactionAsync(async()=>
        {
            var locked=Assert.Single(await f.Store.ReadForUpdateAsync("config_column",new Dictionary<string,object?>{{"id",20L}}));
            Assert.Equal(expected,locked.Keys.Order(StringComparer.Ordinal));return true;
        });
        Assert.Null(f.Provider.Reads[0].Transaction);Assert.NotNull(f.Provider.Reads[1].Transaction);Assert.EndsWith(" FOR UPDATE",f.Provider.Reads[1].Sql);
    }

    /// <summary>原默认单选类型与坏控件类型的参数前检保持，不能因投影切换扩大候选。</summary>
    [Fact]
    public async Task Candidate_pages_default_type_and_invalid_kind_contract_are_preserved()
    {
        using var f=new Fixture();f.Get("config_page",30)["type"]=1;
        var business=CandidateBusiness(f);Assert.Equal("30",Assert.Single(await business.PagesAsync(JsonSerializer.SerializeToElement(new{}))).Id);
        f.Provider.Reads.Clear();f.Provider.Commands.Clear();
        foreach(var request in new[]{JsonSerializer.SerializeToElement(new{controlKind="List"}),JsonSerializer.SerializeToElement(new{controlKind=2})})
            await Assert.ThrowsAsync<InvalidOperationException>(()=>business.PagesAsync(request));
        Assert.Empty(f.Provider.Commands);
    }

    /// <summary>只构造原有工作台依赖，不替换被测业务的认证或查询入口。</summary>
    private static ConfigurationCandidatesBusiness CandidateBusiness(Fixture f)
    {
        var metadata=new MetadataRepository(f.Store,f.Registry);
        var guard=new SecurityRealmMutationGuard(f.Store,f.Registry,f.Authorization,
            new(f.Store,f.Registry,new PlatformSecurityCurrentResources(f.Store,metadata),new PlatformSecurityCurrentSessionPolicy(f.Store)));
        return new(f.Store,metadata,f.Registry,f.Authorization,new(f.Store,f.Registry,f.Authorization,guard));
    }
    /// <summary>断言SQL字段的反引号仅用于本固定MySQL夹具，不参与产品SQL生成。</summary>
    private static string QuoteCandidate(string name)=>"`"+name+"`";
}
