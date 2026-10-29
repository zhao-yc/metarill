using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>缺少专属配置时不连接数据库；完整门禁仍先核批准、冻结包、实例与唯一账号。</summary>
public sealed class TimeValueNativeFactAttribute:FactAttribute
{
    public TimeValueNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_NATIVE_SETTINGS")))Skip="缺专属批准租约，真实TIME持久化/HTTP验收未执行。";}
}

/// <summary>固定原三表完整应用的真实Web与TIME持久化；不注入开发身份或ORM替身。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class TimeValueNativeMySqlTests
{
    internal const int WorkerId=938;
    private const string Route="/api/admin/auto/sample_contract/81002/";
    private static readonly string[] TimeColumns=["clock_zero","clock_tenth","clock_micro","duration"];
    /// <summary>新空库安装、实际参数/回显、反查、原导入导出、事务回滚及重启由独占租约承接。</summary>
    [TimeValueNativeFact]
    public async Task Fixed_time_application_preserves_persisted_values_and_original_protocol()
    {
        var path=Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_NATIVE_SETTINGS")!;
        Assert.Equal(Path.GetFullPath(path),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var settingsDocument=JsonDocument.Parse(await File.ReadAllTextAsync(path));var settings=settingsDocument.RootElement;
        var root=NonRedundantApplicationFixture.Root();var run=TimeValueNativeLease.Validate(settings,CSharpGenerator.SafePath(root,".local/time-values-native/prepared"));
        var options=FreshInstallConfiguration.Read("AgentDatabase","agent");var proof=settings.GetProperty("TimeValuePreparation").Deserialize<GlobalCachedPackageProof>()!;
        var catalog=TimeValueNativePreparation.Verify(proof);var copied=TimeValueNativePreparation.CopyForExecution(proof,CSharpGenerator.SafePath(root,".local/time-values-native/"+run+"/execution/application"));
        var evidence=CSharpGenerator.SafePath(root,".local/time-values-native/"+run+"/evidence");Directory.CreateDirectory(evidence);
        var ownedBoundary=Path.Combine(evidence,"files");var owned=Path.Combine(ownedBoundary,Guid.NewGuid().ToString("N"));
        var hostDirectory=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host");var assembly=CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll");
        var username="time-values-owner";var password=Guid.NewGuid().ToString("N");FreshHost? host=null;var passed=false;var stage="租约/实例";
        using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));var store=new SqlRowStore(session,catalog);
        var fileCatalog=await Model("files");var security=await Model("security");
        var physical=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(catalog,fileCatalog),security),AgentFoundationCatalog.ForApplication(catalog));
        ProcessStartInfo Info(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(assembly);if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["Logging__LogLevel__Default"]="Warning";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;
            info.Environment["Ids__WorkerId"]=WorkerId.ToString(CultureInfo.InvariantCulture);info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";
            info.Environment["Files__LegacyPublicBaseUrl"]="";info.Environment["Files__PublicContentProjectId"]=catalog.YzProjectId;info.Environment["Files__RootDirectory"]=Path.Combine(owned,"uploads");info.Environment["Exports__RootDirectory"]=Path.Combine(owned,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
        }
        try
        {
            stage="零表普通启动";Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));host=await FreshHost.StartAsync(Info(),"/health");
            Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));await host.DisposeAsync();host=null;
            stage="显式安装与真实反查";var schema=new SchemaService(session);await Run(Info("--schema-apply"),"schema-apply");Assert.Empty((await schema.PreviewAsync(physical)).Changes);
            Assert.Equal((long)FreshInstallConfiguration.ExpectedFullTableCount(catalog),await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
            var snapshot=await schema.InspectSnapshotAsync(catalog);var draft=SchemaImportDraftCompiler.Compile(new(snapshot,[]),catalog.YzProjectId);
            Assert.True(draft.ColumnsAndIndexesRepresentable,"真实结构反查有不可表达项，不能忽略。");Assert.Empty(MySqlSchemaPlanner.Compare(snapshot.Schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,options.ExpectedDatabase)));
            Assert.Equal(4,draft.LogicalMetadata.ConfigColumns.Count(column=>column.LogicalType=="time"));await Receipt("schema",new{timeColumns=4,draftRoundtrip=true});
            stage="Production网页登录";await Run(Info("--initialize-security"),"initialize");host=await FreshHost.StartAsync(Info(),"/health");
            using(var anonymous=new HttpClient{BaseAddress=host.Client.BaseAddress})await FreshWebHttp.SendAsync(anonymous,HttpMethod.Post,Route+"page",new{},HttpStatusCode.Unauthorized);
            var token=await FreshWebHttp.LoginAsync(host.Client,username,password,true);
            var customer=(await Call(HttpMethod.Post,"/api/admin/auto/sample_customer/81001/add",new{name="time-native-customer"})).GetProperty("data").GetProperty("id").ToString();
            stage="微秒/默认与参数写后值";var defaults=(await Add(new{name="time-defaults",sampleCustomerId=customer,amount=1})).GetProperty("data");var defaultsId=Id(defaults);
            await AssertTimes(defaults,defaultsId);Assert.Equal("08:09:00",defaults.GetProperty("clockZero").GetString());Assert.Equal("08:09:00.1",defaults.GetProperty("clockTenth").GetString());Assert.Equal("08:09:00.123456",defaults.GetProperty("clockMicro").GetString());Assert.Equal("-30:01:02.123456",defaults.GetProperty("duration").GetString());
            var created=(await Add(new{name="time-native-main",sampleCustomerId=customer,amount=2,clockZero="12:34:56.9876549",clockTenth="12:34:56.9876549",clockMicro="12:34:56.9876549",duration="-30:00:00.9876549"})).GetProperty("data");var id=Id(created);
            await AssertTimes(created,id);Assert.Equal("12:34:56.987654",created.GetProperty("clockMicro").GetString());Assert.Equal("-30:00:00.987654",created.GetProperty("duration").GetString());
            var beforeTimes=TimeSnapshot(await Record(id));
            stage="省略/明确清空/原合法格式";var omitted=(await Call(HttpMethod.Post,Route+"update",new{id=id.ToString(CultureInfo.InvariantCulture),name="time-native-renamed"})).GetProperty("data");Assert.Equal(beforeTimes,TimeSnapshot(await Record(id)));await AssertTimes(omitted,id);
            var cleared=(await Call(HttpMethod.Post,Route+"update",new{id=id.ToString(CultureInfo.InvariantCulture),clockMicro=""})).GetProperty("data");Assert.Null((await Record(id))["clock_micro"]);Assert.Equal(JsonValueKind.Null,cleared.GetProperty("clockMicro").ValueKind);
            var updated=(await Call(HttpMethod.Post,Route+"update",new{id=id.ToString(CultureInfo.InvariantCulture),clockMicro="2026-10-29 11:22:33.123456"})).GetProperty("data");await AssertTimes(updated,id);Assert.Equal("11:22:33.123456",updated.GetProperty("clockMicro").GetString());
            stage="无效字段原R响应且不写入";var stable=JsonSerializer.Serialize(await Record(id));var typeRejected=0;
            foreach(var value in new object[]{"bad-time","2026-10-29","-01:00","24:00","12:34:56Z","12:34:56.1234567890",123,new{bad=1}})
            {
                using var response=await host.Client.PostAsJsonAsync(Route+"update",new{id=id.ToString(CultureInfo.InvariantCulture),clockMicro=value});
                Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);var body=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(400,body.GetProperty("code").GetInt32());Assert.Equal("请求参数不符合页面定义。",body.GetProperty("msg").GetString());Assert.Equal(stable,JsonSerializer.Serialize(await Record(id)));typeRejected++;
            }
            using(var malformed=await host.Client.PostAsync(Route+"update",new StringContent("{",System.Text.Encoding.UTF8,"application/json")))
            {Assert.Equal(HttpStatusCode.OK,malformed.StatusCode);var json=await malformed.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(500,json.GetProperty("code").GetInt32());Assert.Equal("请求字段类型、字段名或必填项不正确。",json.GetProperty("msg").GetString());Assert.Equal(stable,JsonSerializer.Serialize(await Record(id)));}
            await Call(HttpMethod.Post,Route+"update",new{id=id.ToString(CultureInfo.InvariantCulture),duration="839:00:00"},HttpStatusCode.BadRequest);Assert.Equal(stable,JsonSerializer.Serialize(await Record(id)));
            stage="筛选/count/真实读取";var filtered=await Call(HttpMethod.Post,Route+"page",new PageRequest{Filters=new(){{"clockMicro",JsonSerializer.SerializeToElement("11:22:33.123456")}}});Assert.Equal(1L,long.Parse(filtered.GetProperty("data").GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Equal(id.ToString(CultureInfo.InvariantCulture),Assert.Single(filtered.GetProperty("data").GetProperty("records").EnumerateArray()).GetProperty("id").ToString());
            var count=await Call(HttpMethod.Post,Route+"count",new PageRequest{Filters=new(){{"duration",JsonSerializer.SerializeToElement("-30:00:00.987654")}}});Assert.Equal(1L,long.Parse(count.GetProperty("data").ToString(),CultureInfo.InvariantCulture));
            stage="复制持久值";var existing=(await store.ReadIncludingDeletedAsync("sample_contract")).Select(row=>SqlRowStore.Id(row)).ToHashSet();await Call(HttpMethod.Post,Route+"copy/"+id.ToString(CultureInfo.InvariantCulture));var copy=Assert.Single(await store.ReadAsync("sample_contract"),row=>!existing.Contains(SqlRowStore.Id(row)));var copiedId=SqlRowStore.Id(copy);Assert.Equal(TimeSnapshot(await Record(id)),TimeSnapshot(copy));await AssertTimes((await Detail(copiedId)).GetProperty("data"),copiedId);
            stage="同连接两种SQL模式/适配器";await VerifySqlModes(session,id,evidence);Assert.Equal(stable,JsonSerializer.Serialize(await Record(id)));
            stage="日末舍入回滚";await Call(HttpMethod.Post,Route+"update",new{id=id.ToString(CultureInfo.InvariantCulture),clockZero="23:59:59.9999999"},HttpStatusCode.InternalServerError);Assert.Equal(stable,JsonSerializer.Serialize(await Record(id)));
            stage="原中文表头导入/整批回滚";var countBefore=(await store.ReadAsync("sample_contract")).Count;
            await Call(HttpMethod.Post,Route+"importDatas81002",new{requestJson=new[]{Import("time-imported","07:08:09.123456"),Import("time-invalid","bad-time")}},HttpStatusCode.BadRequest);Assert.Equal(countBefore,(await store.ReadAsync("sample_contract")).Count);
            await Call(HttpMethod.Post,Route+"importDatas81002",new{requestJson=new[]{Import("time-imported","07:08:09.123456")}});
            var imported=Assert.Single(await store.ReadAsync("sample_contract",Row("name","time-imported")));var importId=SqlRowStore.Id(imported);Assert.Equal(ClockTimeValue.ParseText("07:08:09.123456"),imported["clock_micro"]);await AssertTimes((await Detail(importId)).GetProperty("data"),importId);
            stage="真实XLSX票据与时间文本";var grant=(await Call(HttpMethod.Post,Route+"initExport",new{selectedIds=new[]{id.ToString(CultureInfo.InvariantCulture)},pageNo=99,pageSize=1})).GetProperty("data").GetString()!;
            using(var download=await host.Client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant))
            {
                Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(ExcelPageExportService.ExcelContentType,download.Content.Headers.ContentType?.MediaType);var bytes=await download.Content.ReadAsByteArrayAsync();
                using var workbook=new XLWorkbook(new MemoryStream(bytes));var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.Equal(2,sheet.RowsUsed().Count());
                var actual=await Record(id);foreach(var column in TimeColumns){var value=(TimeSpan?)actual[column];var expected=value is null?"":column=="duration"?SqlTimeValue.Format(value.Value):ClockTimeValue.FormatExport(value.Value);var cell=sheet.Cell(2,headers[Header(column)]);Assert.Equal(expected,cell.GetString());if(value is not null)Assert.Equal(XLDataType.Text,cell.DataType);}
                await File.WriteAllBytesAsync(Path.Combine(evidence,"time-export-verified.xlsx"),bytes);
            }
            stage="重启与再次详情";token=host.Client.DefaultRequestHeaders.GetValues("loginStateId").Single();await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await AssertTimes((await Detail(id)).GetProperty("data"),id);
            await Call(HttpMethod.Delete,Route,new{id=copiedId.ToString(CultureInfo.InvariantCulture)});Assert.Empty(await store.ReadAsync("sample_contract",Row("id",copiedId)));Assert.NotNull(Assert.Single(await store.ReadIncludingDeletedAsync("sample_contract",Row("id",copiedId)))["delete_no"]);
            await host.DisposeAsync();host=null;TimeValueNativePreparation.Verify(copied,true);TimeValueNativePreparation.Verify(proof);passed=true;
            await Receipt("verified",new{run,passed,productionWeb=true,driver="2.6.1",timeColumns=4,schemaRoundtrip=true,generatedCrud=true,typeRejected,syntaxRejected=true,omissionAndClear=true,copy=true,sqlModeDriverComparisons=2,atomicImport=true,xlsx=true,dayOverflowRollback=true,restart=true,browserExecuted=false,performanceMeasured=false,cleanupByLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,stage,passed=false,errorType=error.GetType().Name,cleanupByLease=true});throw;}
        finally
        {
            var stopped=host is null;try{if(host is not null){await host.DisposeAsync();stopped=true;}if(stopped&&Directory.Exists(owned))await TestDirectoryCleanup.DeleteAsync(owned,ownedBoundary);if(!stopped)throw new InvalidOperationException("自有宿主未确认停止，存储副本保留。");await Receipt("local-cleanup",new{run,stopped,storageRemoved=!Directory.Exists(owned),databaseCleanupByLease=true});}
            catch(Exception error){await Receipt("local-cleanup-failure",new{run,errorType=error.GetType().Name});if(passed)throw;}
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        async Task<MetadataCatalog> Model(string name)=>JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(copied.PackageRoot,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Task<JsonElement> Call(HttpMethod method,string route,object? body=null,HttpStatusCode expected=HttpStatusCode.OK)=>FreshWebHttp.SendAsync(host!.Client,method,route,body,expected);
        Task<JsonElement> Add(object body)=>Call(HttpMethod.Post,Route+"add",body);
        Task<JsonElement> Detail(long id)=>Call(HttpMethod.Get,Route+"detail/"+id.ToString(CultureInfo.InvariantCulture));
        Task<string> Run(ProcessStartInfo info,string name)=>RunOwned(info,evidence,name,options.ConnectionString,password);
        async Task<Dictionary<string,object?>> Record(long id)=>Assert.Single(await store.ReadAsync("sample_contract",Row("id",id)));
        async Task AssertTimes(JsonElement value,long id)
        {
            var actual=await Record(id);foreach(var column in TimeColumns){var time=(TimeSpan?)actual[column];var json=value.GetProperty(GenerationModel.Camel(column));if(time is null)Assert.Equal(JsonValueKind.Null,json.ValueKind);else Assert.Equal(column=="duration"?SqlTimeValue.Format(time.Value):ClockTimeValue.Format(time.Value),json.GetString());}
        }
        string Header(string name){var column=catalog.ConfigColumns.Single(column=>column.ColumnName==name&&catalog.ConfigTables.Single(table=>table.Id==column.ConfigTableId).TableName=="sample_contract");return string.IsNullOrWhiteSpace(column.Name)?GenerationModel.Camel(name):column.Name;}
        Dictionary<string,string> Import(string name,string time)=>new(){{Header("name"),name},{Header("sample_customer_id"),"time-native-customer"},{Header("amount"),"3.5"},{Header("clock_micro"),time},{Header("duration"),"-40:01:02.123456"}};
    }
    /// <summary>只改本连接SQL模式，参数写入在同事务回读后全部回滚，恢复原会话值，不更改全局模式。</summary>
    private static async Task VerifySqlModes(DatabaseSession session,long id,string evidence)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();
        async Task<string> Mode(){await using var command=connection.CreateCommand();command.CommandText="SELECT @@sql_mode";return Convert.ToString(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture)!;}
        async Task Set(string mode){await using var command=connection.CreateCommand();command.CommandText="SET SESSION sql_mode=@mode";var parameter=command.CreateParameter();parameter.ParameterName="mode";parameter.Value=mode;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();}
        var original=await Mode();var normal=string.Join(",",original.Split(',').Where(value=>value!="TIME_TRUNCATE_FRACTIONAL"));var results=new List<object>();
        try
        {
            foreach(var truncate in new[]{false,true})
            {
                await Set(truncate?normal+",TIME_TRUNCATE_FRACTIONAL":normal);await using var transaction=await connection.BeginTransactionAsync();
                await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="UPDATE sample_contract SET clock_zero=@value,clock_tenth=@value,clock_micro=@value,duration=@duration WHERE id=@id";
                var value=ClockTimeValue.ParseText("12:34:56.9876549")!.Value;
                foreach(var (name,item) in new[]{("value",(object)value),("duration",SqlTimeValue.ParseText("-30:00:00.9876549")!.Value),("id",id)}){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=item;command.Parameters.Add(parameter);}
                Assert.Equal(1,await command.ExecuteNonQueryAsync());var values=await session.Provider.ReadPersistedTimeValuesAsync(transaction,"sample_contract",id,TimeColumns);
                Assert.Equal(ClockTimeValue.ParseText(truncate?"12:34:56":"12:34:57"),values[0]);Assert.Equal(ClockTimeValue.ParseText(truncate?"12:34:56.9":"12:34:57"),values[1]);Assert.Equal(ClockTimeValue.ParseText("12:34:56.987654"),values[2]);Assert.Equal(SqlTimeValue.ParseText("-30:00:00.987654"),values[3]);
                results.Add(new{truncate,values=values.Select(time=>time is {} span?SqlTimeValue.Format(span):null).ToArray(),rolledBack=true});await transaction.RollbackAsync();
            }
        }
        finally{await Set(original);Assert.Equal(original,await Mode());}
        await File.WriteAllTextAsync(Path.Combine(evidence,"sql-mode-comparison.json"),JsonSerializer.Serialize(new{comparisons=results,sessionRestored=true,globalModeChanged=false}));
    }
    /// <summary>仅比较实际持久化时间，不把改名/审计列差异当成省略字段修改。</summary>
    private static string TimeSnapshot(IReadOnlyDictionary<string,object?> row)=>JsonSerializer.Serialize(TimeColumns.ToDictionary(column=>column,column=>row[column]));
    /// <summary>正式接口长ID保持字符串精度。</summary>
    private static long Id(JsonElement value)=>long.Parse(value.GetProperty("id").ToString(),CultureInfo.InvariantCulture);
    /// <summary>范围仅限本轮已登记的合成三表记录。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)result[(string)values[i]!]=values[i+1];return result;}
    /// <summary>在任何DDL前读实例及真实登录主体，固定随机库和受限临时账号。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid,CURRENT_USER(),@@sql_mode";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(session.Options.ExpectedDatabase,reader.GetString(0));Assert.Equal(pin.GetProperty("expectedHostname").GetString(),reader.GetString(1));Assert.Equal(pin.GetProperty("expectedServerUuid").GetString(),reader.GetString(2));Assert.StartsWith(new MySqlConnector.MySqlConnectionStringBuilder(session.Options.ConnectionString).UserID+"@",reader.GetString(3));Assert.DoesNotContain("TIME_TRUNCATE_FRACTIONAL",reader.GetString(4).Split(','));
    }
    /// <summary>只执行编译期固定只读计数；不读取SQL正文或其它数据库行。</summary>
    private static async Task<long> Scalar(DatabaseSession session,string sql){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>只拥有本次命令进程；脱敏后落盘，超时结束自己创建的进程树且不自动重试。</summary>
    private static async Task<string> RunOwned(ProcessStartInfo info,string evidence,string name,params string[] secrets)
    {
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var timedOut=false;try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){timedOut=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}var text=await stdout;var error=await stderr;var builder=new MySqlConnector.MySqlConnectionStringBuilder(info.Environment["Database__ConnectionString"]??throw new InvalidOperationException("命令缺少本轮测试连接。"));foreach(var secret in secrets.Append(builder.Password).Where(value=>!string.IsNullOrEmpty(value)).OrderByDescending(value=>value.Length)){text=text.Replace(secret,"[已脱敏]",StringComparison.Ordinal);error=error.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),text+"\n"+error);if(timedOut)throw new TimeoutException("正式TIME字段命令超时，脱敏证据已保留。");Assert.True(process.ExitCode==0,"正式TIME字段命令失败，详见私有脱敏日志。");return text;
    }
}
