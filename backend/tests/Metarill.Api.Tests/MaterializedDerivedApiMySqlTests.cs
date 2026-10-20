using System.Globalization;
using System.Data.Common;
using MySqlConnector;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>实例已pin的随机元数据项目真实HTTP验收；不执行Schema/DDL，不触碰浏览器业务表。</summary>
public sealed class MaterializedDerivedApiMySqlTests
{
    /// <summary>沿真实开放页面与发布按钮验证回执、落库、GET、固定FK和所有配置传播旁路。</summary>
    [MySqlApiFact]
    public async Task Standard_http_receipts_fk_changes_and_configuration_actions_keep_materialized_values_current()
    {
        var gate=new PreparationProbe();await using var f=await MaterializedDerivedApiFixture.CreateAsync(services=>WrapGuard(services,gate));
        var table=await f.CreateTable("a");var other=await f.CreateTable("b");
        await using var statements=new OwnStatementTrace(f.Options);await statements.InitializeAsync();gate.Project=f.Project.ToString(CultureInfo.InvariantCulture);gate.Trace=statements;
        f.OrmCommands.Clear();var page=await f.CreatePage(table,afterResponse:statements.CompleteAsync);gate.Trace=null;await f.SaveEvidence("statement-trace",statements.Report());var pageReceipt=f.LastPageReceipt;
        var tableName=SqlRowStore.Text(await f.Row("config_table",table),"table_name");
        Assert.Equal(tableName,pageReceipt.GetProperty("configTableTableName").GetString());
        await AssertPage(f,page,tableName);
        var targetReads=f.OrmCommands.Where(sql=>sql.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)&&sql.Contains("FROM `config_table`",StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(2,targetReads.Length);Assert.Single(targetReads,sql=>sql.Contains("count(",StringComparison.OrdinalIgnoreCase));
        var narrow=Assert.Single(targetReads,sql=>sql.Contains("FOR UPDATE",StringComparison.OrdinalIgnoreCase));
        var selected=narrow[..narrow.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.Contains("`table_name`",selected);Assert.DoesNotContain("`name`",selected);Assert.DoesNotContain("`yz_project_id`",selected);
        await f.SaveEvidence("page-create-target-sql",new{count=targetReads.Length,commands=targetReads});
        var columns=await f.AddColumns(table,"字段甲 alpha_value\n字段乙 beta_value");var first=columns[0];var second=columns[1];
        var pageFields=await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",page}});
        var field=SqlRowStore.Id(pageFields.Single(row=>SqlRowStore.Id(row,"config_column_id")==first));
        var duplicate=SqlRowStore.Id(pageFields.Single(row=>SqlRowStore.Id(row,"config_column_id")==second));
        await AssertField(f,field,page,first);
        // 保留原页字段唯一定义：先删除本样本目标字段，再执行同表合法FK切换。
        await f.Delete(MaterializedDerivedApiFixture.FieldRoute,duplicate,page.ToString(CultureInfo.InvariantCulture));
        var changed=MaterializedDerivedApiFixture.Data(await f.Post(MaterializedDerivedApiFixture.FieldRoute+"/update?parentId="+page,new{id=field.ToString(CultureInfo.InvariantCulture),configColumnId=second.ToString(CultureInfo.InvariantCulture)}));
        Assert.Equal("beta_value",changed.GetProperty("configColumnColumnName").GetString());await AssertField(f,field,page,second);await f.Capture();
        f.OrmCommands.Clear();var omitted=MaterializedDerivedApiFixture.Data(await f.Post(MaterializedDerivedApiFixture.FieldRoute+"/update?parentId="+page,new{id=field.ToString(CultureInfo.InvariantCulture),name="仅标签变化"}));
        Assert.Equal("beta_value",omitted.GetProperty("configColumnColumnName").GetString());Assert.DoesNotContain(f.OrmCommands,sql=>sql.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)&&sql.Contains("FROM `config_column`",StringComparison.OrdinalIgnoreCase));
        var prior=await f.Snapshot();
        Assert.Equal(500,(await f.Post(MaterializedDerivedApiFixture.FieldRoute+"/update?parentId="+page,new{id=field.ToString(CultureInfo.InvariantCulture),configColumnId=(string?)null})).GetProperty("code").GetInt32());
        Assert.Equal(prior,await f.Snapshot());
        MaterializedDerivedApiFixture.Data(await f.Post(MaterializedDerivedApiFixture.ColumnRoute+"/update?parentId="+table,new{id=second.ToString(CultureInfo.InvariantCulture),columnName="renamed_beta"}));
        await AssertField(f,field,page,second);
        // 排序工作台是原直接更新旁路，仍要同步每个活动页面字段缓存。
        var order=await f.Get("/admin/custom/index/config_columns?configTableId="+table);
        using(var request=new HttpRequestMessage(HttpMethod.Post,"/api/admin/custom/index/save_config_columns")){request.Headers.Add("X-Metarill-Metadata-Revision",order.GetProperty("map").GetProperty("revision").GetString());request.Content=JsonContent.Create(new[]{new{id=second.ToString(CultureInfo.InvariantCulture),sortNo=777}});using var response=await f.Client.SendAsync(request);MaterializedDerivedApiFixture.Data(await MaterializedDerivedApiFixture.Body(response));}
        Assert.Equal(777,Convert.ToInt32((await f.Row("config_page_column",field))["config_column_sort_no"],CultureInfo.InvariantCulture));await AssertField(f,field,page,second);
        // 原前端实际通过发布按钮ID进入网关；客户端不能自选动作路由。
        var button=(await f.Store.ReadAsync("config_button",new Dictionary<string,object?>{{"yz_project_id",long.Parse(f.Registry.Platform.YzProjectId,CultureInfo.InvariantCulture)},{"config_page_id",6191175925956608L},{"sql","/admin/custom/config_page/copy_page"}})).Single();
        var copied=MaterializedDerivedApiFixture.Data(await f.Post("/admin/sys/config_button/execute",new{buttonId=SqlRowStore.Id(button).ToString(CultureInfo.InvariantCulture),entityId=page.ToString(CultureInfo.InvariantCulture)}));
        var copiedPage=long.Parse(copied.GetProperty("configuration").GetProperty("pageId").GetString()!,CultureInfo.InvariantCulture);await f.Capture();await AssertPage(f,copiedPage,tableName);
        foreach(var row in await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",copiedPage}}))await AssertField(f,SqlRowStore.Id(row),copiedPage,SqlRowStore.Id(row,"config_column_id"));
        var added=(await f.AddColumns(table,"新增自动 auto_cache"))[0];
        foreach(var current in new[]{page,copiedPage}){var automatic=(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",current},{"config_column_id",added}})).Single();await AssertField(f,SqlRowStore.Id(automatic),current,added);}
        var renamed=f.Marker+"_renamed_a";f.AllowRename(renamed);
        MaterializedDerivedApiFixture.Data(await f.Post(MaterializedDerivedApiFixture.TableRoute+"/update",new{id=table.ToString(CultureInfo.InvariantCulture),tableName=renamed}));
        await AssertPage(f,page,renamed);await AssertPage(f,copiedPage,renamed);
        // 页面换表仍先拒绝现存旧字段，失败时缓存和其他字段也不得写入。
        prior=await f.Snapshot();Assert.Equal(500,(await f.Post(MaterializedDerivedApiFixture.PageRoute+"/update",new{id=page.ToString(CultureInfo.InvariantCulture),configTableId=other.ToString(CultureInfo.InvariantCulture)})).GetProperty("code").GetInt32());Assert.Equal(prior,await f.Snapshot());
        foreach(var row in await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",page}}))await f.Delete(MaterializedDerivedApiFixture.FieldRoute,SqlRowStore.Id(row),page.ToString(CultureInfo.InvariantCulture));
        var moved=MaterializedDerivedApiFixture.Data(await f.Post(MaterializedDerivedApiFixture.PageRoute+"/update",new{id=page.ToString(CultureInfo.InvariantCulture),configTableId=other.ToString(CultureInfo.InvariantCulture)}));
        var otherName=SqlRowStore.Text(await f.Row("config_table",other),"table_name");Assert.Equal(otherName,moved.GetProperty("configTableTableName").GetString());await f.Capture();await AssertPage(f,page,otherName);
        var active=(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",page}})).First();var activeId=SqlRowStore.Id(active);
        prior=await f.Snapshot();Assert.Equal(500,(await f.Post(MaterializedDerivedApiFixture.FieldRoute+"/update?parentId="+page,new{id=activeId.ToString(CultureInfo.InvariantCulture),configColumnId=second.ToString(CultureInfo.InvariantCulture)})).GetProperty("code").GetInt32());Assert.Equal(prior,await f.Snapshot());
        var echo=MaterializedDerivedApiFixture.Data(await f.Get(MaterializedDerivedApiFixture.PageRoute+"/detail/"+page));var forged=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(echo.GetRawText())!;forged["configTableTableName"]=JsonSerializer.SerializeToElement("伪造只读回显");
        var echoed=MaterializedDerivedApiFixture.Data(await f.Post(MaterializedDerivedApiFixture.PageRoute+"/update",forged));Assert.Equal(otherName,echoed.GetProperty("configTableTableName").GetString());await AssertPage(f,page,otherName);
        var foreign=await f.ForeignTable();prior=await f.Snapshot();Assert.Equal(500,(await f.Post(MaterializedDerivedApiFixture.PageRoute+"/add",new{name="越界新页",configTableId=foreign.ToString(CultureInfo.InvariantCulture),yzAdminTypeId=f.Admin.ToString(CultureInfo.InvariantCulture),mobileListPageStyleType=2,cols=2})).GetProperty("code").GetInt32());Assert.Equal(prior,await f.Snapshot());
        await f.CompleteAsync();
    }

    /// <summary>尾部真实事务观察器拒绝后，源字段和全部派生引用必须一起回滚。</summary>
    [MySqlApiFact]
    public async Task Later_observer_failure_rolls_back_the_real_source_and_all_materialized_references()
    {
        var failure=new TailFailure();await using var f=await MaterializedDerivedApiFixture.CreateAsync(services=>services.AddScoped<IRecordMutationObserver>(_=>failure));
        var table=await f.CreateTable("tail");var columns=await f.AddColumns(table,"回滚字段 rollback_value");var column=columns.Single();
        var page=await f.CreatePage(table);await f.Capture();var before=await f.Snapshot();failure.Project=f.Project.ToString(CultureInfo.InvariantCulture);failure.Record=column;failure.Enabled=true;
        var response=await f.Post(MaterializedDerivedApiFixture.ColumnRoute+"/update?parentId="+table,new{id=column.ToString(CultureInfo.InvariantCulture),columnName="must_roll_back"});failure.Enabled=false;
        Assert.Equal(500,response.GetProperty("code").GetInt32());Assert.Contains("派生验收尾部拒绝",response.GetProperty("msg").GetString());Assert.Equal(before,await f.Snapshot());
        await f.SaveEvidence("tail-rollback",new{before=JsonSerializer.Deserialize<JsonElement>(before),after=JsonSerializer.Deserialize<JsonElement>(await f.Snapshot()),rejected=response});
        foreach(var row in await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",column}}))await AssertField(f,SqlRowStore.Id(row),SqlRowStore.Id(row,"config_page_id"),column);
        await f.CompleteAsync();
    }

    /// <summary>双连接实证先争用项目锁，源记录仍可锁；解除后换FK与目标改名均完成且缓存一致。</summary>
    [MySqlApiFact]
    public async Task Two_connections_lock_project_before_source_and_concurrent_rename_and_fk_move_finish_consistently()
    {
        var probe=new PreparationProbe();await using var f=await MaterializedDerivedApiFixture.CreateAsync(services=>WrapGuard(services,probe));
        var first=await f.CreateTable("lock_a");var second=await f.CreateTable("lock_b");var page=await f.CreatePage(first);
        foreach(var row in await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",page}}))await f.Delete(MaterializedDerivedApiFixture.FieldRoute,SqlRowStore.Id(row),page.ToString(CultureInfo.InvariantCulture));
        probe.Project=f.Project.ToString(CultureInfo.InvariantCulture);probe.Signal=true;
        using var ownerSession=new DatabaseSession(f.Options);var ownerStore=new SqlRowStore(ownerSession,f.Registry.Platform);
        var locked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var inspect=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var sourceFree=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? owner=null;Task<HttpResponseMessage>? updating=null;Task<HttpResponseMessage>? renaming=null;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var renamed=f.Marker+"_locked_new";f.AllowRename(renamed);
        try
        {
            owner=Task.Run(()=>ownerSession.InTransactionAsync(async()=>
            {
                await ownerStore.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",f.Project}},timeout.Token);locked.SetResult();await inspect.Task.WaitAsync(timeout.Token);
                Assert.Single(await ownerStore.ReadForUpdateAsync("config_page",new Dictionary<string,object?>{{"id",page},{"yz_project_id",f.Project}},timeout.Token));sourceFree.SetResult();await release.Task.WaitAsync(timeout.Token);return true;
            },timeout.Token));
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            updating=f.Client.PostAsJsonAsync("/api"+MaterializedDerivedApiFixture.PageRoute+"/update",new{id=page.ToString(CultureInfo.InvariantCulture),configTableId=second.ToString(CultureInfo.InvariantCulture)},timeout.Token);
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            renaming=f.Client.PostAsJsonAsync("/api"+MaterializedDerivedApiFixture.TableRoute+"/update",new{id=second.ToString(CultureInfo.InvariantCulture),tableName=renamed},timeout.Token);
            inspect.SetResult();await sourceFree.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.False(updating.IsCompleted);Assert.False(renaming.IsCompleted);
            release.SetResult();await owner.WaitAsync(TimeSpan.FromSeconds(10));
            using var update=await updating.WaitAsync(TimeSpan.FromSeconds(15));using var rename=await renaming.WaitAsync(TimeSpan.FromSeconds(15));var updateBody=await MaterializedDerivedApiFixture.Body(update);var renameBody=await MaterializedDerivedApiFixture.Body(rename);MaterializedDerivedApiFixture.Data(updateBody);MaterializedDerivedApiFixture.Data(renameBody);
            await f.Capture();await AssertPage(f,page,renamed);Assert.Equal(second,Convert.ToInt64((await f.Row("config_page",page))["config_table_id"],CultureInfo.InvariantCulture));
            await f.SaveEvidence("concurrent-lock",new{projectLockedBeforeSource=true,sourceLockAcquiredWhileHttpWaited=true,update=updateBody,rename=renameBody,final=await f.Row("config_page",page)});
        }
        finally
        {
            inspect.TrySetResult();release.TrySetResult();
            if(owner is not null)try{await owner.WaitAsync(TimeSpan.FromSeconds(10));}catch{timeout.Cancel();}
            foreach(var task in new[]{updating,renaming})if(task is not null)try{using var response=await task.WaitAsync(TimeSpan.FromSeconds(10));}catch{timeout.Cancel();}
        }
        await f.CompleteAsync();
    }

    /// <summary>旧按钮直调别名必须由实际MVC路由匹配，并调用同一受权复制业务。</summary>
    [MySqlApiFact]
    public async Task Original_copy_page_alias_matches_real_http_and_copies_current_materialized_values()
    {
        await using var f=await MaterializedDerivedApiFixture.CreateAsync();var table=await f.CreateTable("alias");await f.AddColumns(table,"别名字段 alias_value");var page=await f.CreatePage(table);
        // 保留真实旧URL/body；该用例不绕到网关，也不直接调用Business。
        var copied=MaterializedDerivedApiFixture.Data(await f.Post("/admin/custom/config_page/copy_page",new{id=page.ToString(CultureInfo.InvariantCulture)}));
        var copy=long.Parse(copied.GetProperty("configuration").GetProperty("pageId").GetString()!,CultureInfo.InvariantCulture);await f.Capture();await AssertPage(f,copy,SqlRowStore.Text(await f.Row("config_table",table),"table_name"));
        foreach(var row in await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",copy}}))await AssertField(f,SqlRowStore.Id(row),copy,SqlRowStore.Id(row,"config_column_id"));
        await f.SaveEvidence("legacy-alias-receipt",copied);await f.CompleteAsync();
    }

    /// <summary>测试只包装既有唯一守卫；真实身份/项目锁逻辑继续由原守卫执行。</summary>
    private static void WrapGuard(IServiceCollection services,PreparationProbe probe)
    {
        var original=Assert.Single(services,descriptor=>descriptor.ServiceType==typeof(IRecordMutationGuard));services.Remove(original);services.AddScoped<IRecordMutationGuard>(provider=>new ObservedGuard(provider.GetRequiredService<ConfigurationMutationGuard>(),probe));
    }
    private sealed class PreparationProbe{internal string Project="";internal bool Signal;internal OwnStatementTrace? Trace;internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);}
    private sealed class ObservedGuard(ConfigurationMutationGuard inner,PreparationProbe probe):IRecordMutationGuard
    {
        public async Task BeforeMutationAsync(RecordMutationPreparation value,CancellationToken token=default)
        {
            if(value.Actor.ProjectId==probe.Project&&value.TableName=="config_page")
            {
                if(probe.Trace is not null&&value.Operation==RecordMutationOperation.Create)await probe.Trace.StartAsync(value.Transaction,token);
                if(probe.Signal&&value.Operation==RecordMutationOperation.Update)probe.Entered.TrySetResult();
            }
            await inner.BeforeMutationAsync(value,token);
        }
    }
    private sealed class TailFailure:IRecordMutationObserver
    {
        internal bool Enabled;internal string Project="";internal long Record;
        public Task BeforeCommitAsync(RecordMutationContext value,CancellationToken token=default)
        {if(Enabled&&value.ProjectId==Project&&value.TableName=="config_column"&&value.RecordId==Record)throw new InvalidOperationException("派生验收尾部拒绝");return Task.CompletedTask;}
    }

    /// <summary>只读取服务器已经启用的本连接语句历史；不改consumer、全局参数，不读取其他连接SQL。</summary>
    private sealed class OwnStatementTrace(DatabaseOptions options):IAsyncDisposable
    {
        private MySqlConnection? monitor;private bool available;private string state="not-started";private ulong thread;private ulong start;private readonly List<object> rows=[];
        internal async Task InitializeAsync()
        {
            try
            {
                monitor=new MySqlConnection(options.ConnectionString);await monitor.OpenAsync();await using var command=monitor.CreateCommand();command.CommandText="SELECT ENABLED FROM performance_schema.setup_consumers WHERE NAME='events_statements_history_long'";available=string.Equals(Convert.ToString(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture),"YES",StringComparison.Ordinal);state=available?"available":"not-enabled";
            }
            catch(MySqlException exception){state="unavailable-"+exception.Number;available=false;}
        }
        internal async Task StartAsync(DbTransaction transaction,CancellationToken token)
        {
            if(!available)return;
            if(transaction.Connection is not MySqlConnection source)throw new InvalidOperationException("真实事务连接不是MySqlConnector。");
            await using var command=monitor!.CreateCommand();command.CommandText="SELECT THREAD_ID FROM performance_schema.threads WHERE PROCESSLIST_ID=@id";command.Parameters.AddWithValue("id",source.ServerThread);var value=await command.ExecuteScalarAsync(token);if(value is null||value is DBNull){state="own-thread-unavailable";available=false;return;}thread=Convert.ToUInt64(value,CultureInfo.InvariantCulture);
            command.CommandText="SELECT COALESCE(MAX(EVENT_ID),0) FROM performance_schema.events_statements_history_long WHERE THREAD_ID=@thread";command.Parameters.Clear();command.Parameters.AddWithValue("thread",thread);start=Convert.ToUInt64(await command.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);state="started";
        }
        internal async Task CompleteAsync()
        {
            if(!available||state!="started")return;
            await using var command=monitor!.CreateCommand();command.CommandText="SELECT EVENT_ID,SQL_TEXT,ERRORS,ROWS_AFFECTED FROM performance_schema.events_statements_history_long WHERE THREAD_ID=@thread AND EVENT_ID>@start ORDER BY EVENT_ID";command.Parameters.AddWithValue("thread",thread);command.Parameters.AddWithValue("start",start);
            await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())rows.Add(new{eventId=reader.GetUInt64(0),sql=reader.IsDBNull(1)?null:reader.GetString(1),errors=reader.GetUInt64(2),affected=reader.GetUInt64(3)});state=rows.Count>0?"captured-own-thread":"history-not-retained";
        }
        internal object Report()=>new{state,threadId=thread,startEventId=start,count=rows.Count,statements=rows.ToArray(),limitation=rows.Count==0?"服务器未提供可用语句历史；仅能报告真实ORM命令次数与静态传播成本，不开启采集。":null};
        public async ValueTask DisposeAsync(){if(monitor is not null)await monitor.DisposeAsync();}
    }

    /// <summary>同一缓存同时核对HTTP详情、实际持久化值与真实目标；不以回显单独证明落库。</summary>
    private static async Task AssertPage(MaterializedDerivedApiFixture f,long page,string expected)
    {Assert.Equal(expected,(await f.Row("config_page",page))["config_table_table_name"]);Assert.Equal(expected,MaterializedDerivedApiFixture.Data(await f.Get(MaterializedDerivedApiFixture.PageRoute+"/detail/"+page)).GetProperty("configTableTableName").GetString());}
    /// <summary>双派生字段共同核验真实目标、实际库行和页面详情，保持可空排序值语义。</summary>
    private static async Task AssertField(MaterializedDerivedApiFixture f,long field,long page,long column)
    {
        var source=await f.Row("config_column",column);var actual=await f.Row("config_page_column",field);var detail=MaterializedDerivedApiFixture.Data(await f.Get(MaterializedDerivedApiFixture.FieldRoute+"/detail/"+field+"?parentId="+page));
        Assert.Equal(source["column_name"],actual["config_column_column_name"]);Assert.Equal(Convert.ToString(source.GetValueOrDefault("sort_no"),CultureInfo.InvariantCulture),Convert.ToString(actual.GetValueOrDefault("config_column_sort_no"),CultureInfo.InvariantCulture));
        Assert.Equal(Convert.ToString(source["column_name"],CultureInfo.InvariantCulture),detail.GetProperty("configColumnColumnName").GetString());
        if(source.GetValueOrDefault("sort_no") is null)Assert.Equal(JsonValueKind.Null,detail.GetProperty("configColumnSortNo").ValueKind);else Assert.Equal(Convert.ToInt32(source["sort_no"],CultureInfo.InvariantCulture),detail.GetProperty("configColumnSortNo").GetInt32());
    }
}
