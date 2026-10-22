using System.Globalization;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>来源父归属不改变原级联候选合同；源数据仍由标准页面范围强制隔离。</summary>
public sealed class CascadeTreeSourceParentTests
{
    /// <summary>两种父键类型均实际生成编译，不能仅移除能力提示却留下拒绝代码。</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int64")]
    public async Task Parented_source_compiles_existing_scopes_and_fixed_tree_contract(string type)
    {
        var catalog=Catalog("parented",type);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CascadeParent");var table=model.Tables.Single(t=>t.Source.Id=="2");
        foreach(var page in table.Pages){Assert.Single(CascadeTreeReadCompilation.ForPage(model,table,page));Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-source-parent");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeParent");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CascadeParent.Generated.Controllers.ParentedSourcePage201Controller"));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Reason.Contains("CascadeTree")&&gap.Reason.Contains("完整应用"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>两种来源父键/树键共用三表，归属列仍来自页面whereSql而非级联请求。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type)
    {
        var catalog=CascadeTreeExcelTests.Catalog(prefix,type,type);var owner=prefix+"_owner";catalog.ConfigTables.Add(new(){Id="4",TableName=owner,IsRongyu=1});
        catalog.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="name",LogicalType="string"},new(){Id="43",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="44",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"}]);
        catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName=owner+"_id",Name="归属ID",LogicalType=type,LinkConfigTableId="4",SortNo=4});
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2")){page.WhereSql+=" AND "+owner+"_id=@parent_id";catalog.ConfigPageColumns.Add(new(){Id=page.Id+"28",ConfigPageId=page.Id,ConfigColumnId="28"});}
        var source=catalog.ConfigPages.Single(page=>page.Id=="201");source.IsNew=1;source.IsSave=1;source.IsCopy=1;source.IsDelete=1;
        return catalog;
    }
}

/// <summary>真实生成HTTP与Server-200三张独占随机表；测试身份不等同正式登录或浏览器。</summary>
public sealed class CascadeTreeSourceParentMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>固定实例身份后才允许建样本；字符串与整数归属分别执行。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_parented_CRUD_filter_export_and_import_preserve_owner_boundaries()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");}
        foreach(var type in new[]{"string","int64"})await Exercise(options,type);
    }
    /// <summary>覆盖完整来源用途、真实路径Excel和父记录事务锁；失败先保存证据再清理。</summary>
    private static async Task Exercise(DatabaseOptions options,string type)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_csp_"+nonce[..12];var catalog=CascadeTreeSourceParentTests.Catalog(prefix,type);var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cascade-source-parent/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-source-parent-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;var observer=new ProbeObserver();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(3,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            var owner=prefix+"_owner";var tree=prefix+"_tree";var source=prefix+"_source";var ownerColumn=owner+"_id";var ownerJson=GenerationModel.Camel(ownerColumn);var a=(First+1000).ToString();var b=(First+1001).ToString();
            object Key(long id)=>type=="string"?id.ToString(CultureInfo.InvariantCulture):id;
            foreach(var item in new[]{(First+1000,Project,(object?)null),(First+1001,Project,(object?)null),(First+1002,Project+1,(object?)null),(First+1003,Project,(object?)new DateTime(2026,1,1))})await Execute(session,"INSERT INTO `"+owner+"` (id,name,yz_project_id,delete_no) VALUES (@id,'父',@project,@deleted)",("id",item.Item1),("project",item.Item2),("deleted",item.Item3));
            await Execute(session,"INSERT INTO `"+tree+"` (id,name,yz_project_id,`parent_"+tree+"_id`,status) VALUES (@id,'根',@project,NULL,1),(@child,'子',@project,@parent,1)",("id",First),("child",First+1),("project",Project),("parent",Key(First)));
            foreach(var item in new[]{(First+2000,First+1000,10L),(First+2001,First+1001,20L)})await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id,tree_id,tree_name,amount,`"+ownerColumn+"`) VALUES (@id,'同名',@project,@tree,'历史名',@amount,@owner)",("id",item.Item1),("project",Project),("tree",Key(First+1)),("amount",item.Item3),("owner",Key(item.Item2)));
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeParentProbe");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=1000});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.CascadeParentProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or PageImportException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet("/test-export/{ticket}",async(string ticket)=>{var content=await exports.OpenDownloadAsync(ticket);return Results.Stream(content.Stream,content.ContentType,content.FileName);});await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+source+"/201/";
            async Task<JsonElement> Post(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action,body)).Content.ReadAsStringAsync());
            async Task<JsonElement> Import(string parent,params object[] rows)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint.Replace("/201/","/202/")+"importDatas202",new{parentId=parent,requestJson=rows})).Content.ReadAsStringAsync());
            async Task<JsonElement> Delete(string parent,string id){using var request=new HttpRequestMessage(HttpMethod.Delete,endpoint.TrimEnd('/')+"?parentId="+parent){Content=JsonContent.Create(new{id})};return JsonSerializer.Deserialize<JsonElement>(await(await client.SendAsync(request)).Content.ReadAsStringAsync());}
            async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}
            object Row(string name,string? path,long amount)=>new Dictionary<string,object?>{{"记录名",name},{"树路径",path},{"金额",amount}};
            var filters=new{treeId=new{treeId=First.ToString(),treeName="不作为查询"}};
            foreach(var parent in new[]{a,b}){var page=await Post("page",new{parentId=parent,filters});var rows=Data(page).GetProperty("records").EnumerateArray().ToArray();Assert.Single(rows);Assert.Equal(parent,rows[0].GetProperty(ownerJson).ToString());Assert.Equal("根/子",rows[0].GetProperty("treeIdDisplayText").GetString());Assert.Equal(parent==a?10:20,decimal.Parse(page.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Equal(1,long.Parse(Data(await Post("count",new{parentId=parent,filters})).ToString()));}
            Assert.Equal(500,(await Post("page",new{})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("count",new{parentId="' OR 1=1"})).GetProperty("code").GetInt32());
            Assert.Equal(500,(await Post("selection/selected",new{parentId=a,selectedIds=new[]{(First+2000).ToString(),(First+2001).ToString()}})).GetProperty("code").GetInt32());var selected=Data(await Post("selection/selected",new{parentId=a,selectedIds=new[]{(First+2000).ToString()}})).EnumerateArray().ToArray();Assert.Single(selected);Assert.Equal((First+2000).ToString(),selected[0].GetProperty("id").GetString());
            using(var response=await client.GetAsync(endpoint+"detail/"+(First+2001)+"?parentId="+a))Assert.Equal(JsonValueKind.Null,JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()).GetProperty("data").ValueKind);
            // 原候选不携来源父键，不读取来源记录；仍必须通过来源页与固定目标页的list权限。
            Assert.Single(Data(await Post("cascadeTreeOptions25",new{levelKey="selfTree-0",selectedPath=new{parentId=""}})).EnumerateArray());Assert.Equal("根/子",Data(await Post("cascadeTreePath25",new{leafId=(First+1).ToString()})).GetProperty("displayText").GetString());
            client.DefaultRequestHeaders.Add("denyTarget","1");Assert.Equal(500,(await Post("cascadeTreeOptions25",new{levelKey="selfTree-0",selectedPath=new{parentId=""}})).GetProperty("code").GetInt32());Data(await Post("page",new{parentId=a,filters}));client.DefaultRequestHeaders.Remove("denyTarget");
            var ticket=Data(await Post("initExport",new{parentId=a,filters})).GetString()!;using(var workbook=new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync("/test-export/"+ticket)))){var sheet=workbook.Worksheet(1);Assert.Equal(2,sheet.LastRowUsed()!.RowNumber());Assert.Contains(sheet.Row(2).CellsUsed(),cell=>cell.GetString()=="根/子");}
            var saved=Data(await Post("add?parentId="+a,new{name="新增",treeId=(First+1).ToString(),amount=3}));Assert.Equal(a,saved.GetProperty(ownerJson).ToString());Assert.Equal("子",saved.GetProperty("treeName").GetString());var id=saved.GetProperty("id").GetString()!;
            var before=await State();Assert.Equal(500,(await Post("update?parentId="+a,new{id=(First+2001).ToString(),treeId=First.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            Assert.Equal(500,(await Post("update?parentId="+a,new Dictionary<string,object?>{{"id",id},{ownerJson,b}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            Data(await Post("copy/"+id+"?parentId="+a,new{}));Data(await Post("update?parentId="+a,new{id,treeId=(string?)null}));
            foreach(var bad in new[]{"",(First+1002).ToString(),(First+1003).ToString(),(First+1004).ToString()}){before=await State();Assert.Equal(500,(await Post("add?parentId="+bad,new{name="拒绝父",treeId=First.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            // 原HTTP父键仅可回显当前上下文，冲突不得写入；DELETE仍限定当前父组并留下墓碑。
            before=await State();Assert.Equal(500,(await Post("add?parentId="+a,new Dictionary<string,object?>{{"name","冲突父键"},{"treeId",First.ToString()},{ownerJson,b}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var rebound=Data(await Post("add?parentId="+a,new Dictionary<string,object?>{{"name","归属回显"},{"treeId",First.ToString()},{ownerJson,a}}));Assert.Equal(a,rebound.GetProperty(ownerJson).ToString());
            before=await State();Assert.Equal(500,(await Delete(a,(First+2001).ToString())).GetProperty("code").GetInt32());Assert.Equal(before,await State());Data(await Delete(a,rebound.GetProperty("id").GetString()!));Assert.NotNull((await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source)).Single(row=>SqlRowStore.Id(row)==long.Parse(rebound.GetProperty("id").GetString()!))["delete_no"]);
            client.DefaultRequestHeaders.Add("importOnly","1");Assert.Equal(200,(await Import(a,Row("同名","根",88))).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("importOnly");
            var persisted=await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source);Assert.Equal(88,Convert.ToInt64(persisted.Single(row=>SqlRowStore.Id(row)==First+2000)["amount"]));Assert.Equal(20,Convert.ToInt64(persisted.Single(row=>SqlRowStore.Id(row)==First+2001)["amount"]));
            before=await State();Assert.Equal(500,(await Import(a,Row("先写","根/子",1),Row("后错","不存在",2))).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Import(a,Row("先写","根/子",1),Row("通知故障","根/子",2))).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            Assert.Equal(500,(await Import((First+1002).ToString(),Row("越权父","根",1))).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var pending=Post("add?parentId="+a,new{name="锁持有",treeId=First.ToString()});try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var transaction=await independent.BeginTransactionAsync();await using var update=independent.CreateCommand();update.Transaction=transaction;update.CommandText="UPDATE `"+owner+"` SET name='竞争父' WHERE id=@id";update.Parameters.AddWithValue("id",First+1000);Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>update.ExecuteNonQueryAsync())).Number);await transaction.RollbackAsync();}finally{observer.Release.TrySetResult();await pending;}Data(await pending);await Execute(session,"UPDATE `"+owner+"` SET name='已释放父' WHERE id=@id",("id",First+1000));
            client.DefaultRequestHeaders.Add("denySource","1");Assert.Equal(500,(await Post("page",new{parentId=a})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("cascadeTreePath25",new{leafId=First.ToString()})).GetProperty("code").GetInt32());
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,verified,realGeneratedHttp=true,realMySql=true,sourceParentScope=true,sharedTargetCandidates=true,importWholeBatchRollback=true,parentLock1205=true,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().Name,error.Message,failedTestPreserved=true}));throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){using var cleanup=new DatabaseSession(options);if((await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有三表结构漂移，保留现场。");foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0}));}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>原事务观察者仅对固定样本触发等待或失败，不伪造数据库结果。</summary>
    private sealed class ProbeObserver:IRecordMutationObserver
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>有界等待保证失败测试不会无限持锁。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"通知故障"))throw new InvalidOperationException("固定通知故障");if(Equals(name,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),cancellationToken);}}
    }
    /// <summary>固定Actor仅由宿主建立，测试头只能减权。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>客户端父ID不作为项目身份。</summary>
        public RuntimeActor RequireActor()=>new(944,"来源父范围验收",false,Project.ToString(),"1");
        /// <summary>独立撤销来源、目标或普通写入动作，保持导入动作独立。</summary>
        public void RequirePagePermission(string pageId,string operation){var headers=accessor.HttpContext!.Request.Headers;if(headers.ContainsKey("denySource")&&pageId is "201" or "202"||headers.ContainsKey("denyTarget")&&pageId=="301"&&operation=="list"||headers.ContainsKey("importOnly")&&(operation is "create" or "update"||pageId=="301"&&operation=="list"))throw new UnauthorizedAccessException("固定测试撤权。");}
    }
    /// <summary>错误响应不能作为业务成功数据使用。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>固定随机样本SQL全部参数化，不接受HTTP动态SQL。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
