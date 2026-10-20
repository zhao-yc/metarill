using System.Globalization;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式生成代码的真实HTTP/MySQL单值CRUD；固定测试授权器不等同于原登录或浏览器验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantGeneratedMySqlTests
{
    /// <summary>元数据建表→正式生成与编译→原HTTP/完整表单/回滚→原范围统计→精确清理。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_http_contract_executes_nonredundant_crud_scopes_echo_and_statistics()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var connection=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||connection.Database!="metarill_business_test"||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("生成读取专项仅允许指定隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await q.ExecuteReaderAsync();Assert.True(await r.ReadAsync()&&r.GetString(0)==options.ExpectedDatabase&&r.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不符，未创建样本。");}
        var nonce=Guid.NewGuid().ToString("N");var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_"+nonce[..10]);catalog.ConfigPages[0].PageSize=0;NonRedundantGeneratedFixture.AddSelectionPage(catalog);catalog.ConfigPages[0].IsExport=1;NonRedundantGeneratedFixture.EnableImports(catalog);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-generated-mysql",nonce);var evidence=Path.Combine(Root(),".local/nonredundant-generated/mysql-"+nonce);Directory.CreateDirectory(evidence);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var store=new SqlRowStore(session,catalog);var created=false;var verified=false;string? fingerprint=null;string? rowsHash=null;WebApplication? app=null;Exception? primaryFailure=null;
        const long first=9007199254741001,second=9007199254741002;var ownIds=new List<long>{first,second,first+2,first+3,first+4,first+5,first+6};
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var preview=await schema.PreviewAsync(catalog);Assert.Equal(3,preview.Changes.Count);Assert.All(preview.Changes,change=>Assert.Equal("createTable",change.Kind));await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(preview)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            await session.InTransactionAsync(async()=>{await Group(71,54,"父甲");await Group(72,54,"父乙");await Group(73,55,"外项目父");await Group(74,54,"已删除父",true);await Party(101,54,"A甲","A注释",10m);await Party(102,54,"B乙","B注释",20m);await Party(103,55,"外项目","不可用",999m);await Party(104,54,"已删除","不可用",999m,true);await Source(first,54,71,101,"记录甲",2);await Source(second,54,71,102,"记录乙",1);await Source(first+2,54,72,101,"其它父",2);await Source(first+3,55,71,103,"其它项目",2);await Source(first+4,54,71,101,"已删除",2,true);await Source(first+5,54,73,101,"旧坏数据外部父",2);await Source(first+6,54,74,101,"旧坏数据删除父",2);return true;});rowsHash=await RowsHash();
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NonRedGenerated");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var authorization=new NonRedGeneratedAuthorization();var observer=new NonRedRollbackObserver();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IRuntimeAuthorization>(authorization);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(943));builder.Services.AddSingleton<IRecordMutationObserver>(observer);var exportRoot=Path.Combine(directory,"private-exports");var exports=new ExcelPageExportService(new ExportOptions{RootDirectory=exportRoot,MaxRows=2});builder.Services.AddSingleton<IPageExportService>(exports);assembly.GetType("Example.NonRedGenerated.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(options=>options.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(UnauthorizedAccessException){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(R<string>.Error("读取专项拒权",403));}catch(PageImportException error){await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}catch(Exception error)when(error is ArgumentException or InvalidOperationException){await context.Response.WriteAsJsonAsync(R<string>.Error("读取专项业务拒绝"));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(20)};var endpoint="/api/admin/auto/"+names[0]+"/101";
            var page=await Post("/page",new PageRequest{ParentId="71",PageSize=1});var records=page.GetProperty("data").GetProperty("records");Assert.Equal(2,long.Parse(page.GetProperty("data").GetProperty("total").ToString(),CultureInfo.InvariantCulture));var row=Assert.Single(records.EnumerateArray());Assert.Equal(first.ToString(CultureInfo.InvariantCulture),row.GetProperty("id").GetString());Assert.Equal("A甲",row.GetProperty("customerName").GetString());Assert.False(row.TryGetProperty("yzProjectId",out _));Assert.False(row.TryGetProperty("groupId",out _));Assert.Equal(30m,page.GetProperty("map").GetProperty("statistics").GetProperty("customerAmount").GetProperty("total").GetDecimal());Assert.Equal(50m,page.GetProperty("map").GetProperty("statistics").GetProperty("status").GetProperty("total").GetDecimal());
            var unpaged=await Post("/page",new PageRequest{ParentId="71",PageSize=0});Assert.Equal(2,unpaged.GetProperty("data").GetProperty("records").GetArrayLength());Assert.Equal("0",unpaged.GetProperty("data").GetProperty("pages").ToString());
            var generatedModel=GenerationModel.Create(catalog,"Example.NonRedGenerated");var sourceModel=generatedModel.Tables.Single(table=>table.Source.Id=="1");var relation=Assert.Single(RelationCompilation.ForPage(generatedModel,sourceModel.Pages[0]),item=>item.SourceColumn.Source.Id=="15");var optionsPayload=await Post("/"+relation.LegacyEndpoint,new{parentId="71"});Assert.Equal(200,optionsPayload.GetProperty("code").GetInt32());Assert.Equal(new[]{"101","102"},optionsPayload.GetProperty("data").EnumerateArray().Select(item=>item.GetProperty("id").ToString()).Order());
            var filtered=await Post("/page",new PageRequest{ParentId="71",Filters=new(){{"customerNotes",JsonSerializer.SerializeToElement("B")}},PageSize=1});Assert.Equal(second.ToString(CultureInfo.InvariantCulture),Assert.Single(filtered.GetProperty("data").GetProperty("records").EnumerateArray()).GetProperty("id").GetString());Assert.Equal(20m,filtered.GetProperty("map").GetProperty("statistics").GetProperty("customerAmount").GetProperty("total").GetDecimal());
            var count=await Post("/count",new PageRequest{ParentId="71",SelectedIds=[first.ToString(CultureInfo.InvariantCulture)]});Assert.Equal("1",count.GetProperty("data").ToString());Assert.Equal(500,(await Post("/page",new PageRequest{ParentId="71",Filters=new(){{"yzProjectId",JsonSerializer.SerializeToElement("55")}}})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("/page",new PageRequest{ParentId="71",OrderBy=new(){Column="groupId",Order="ascending"}})).GetProperty("code").GetInt32());
            using(var wrongParent=await client.GetAsync(endpoint+"/detail/"+first+"?parentId=72")){var payload=await Json(wrongParent);Assert.Equal(500,payload.GetProperty("code").GetInt32());}
            var issued=await Post("/initExport",new PageRequest{ParentId="71",PageNo=99,PageSize=1});Assert.Equal(200,issued.GetProperty("code").GetInt32());var exportPlan=(Metarill.Data.Queries.NonRedundantReadPlan)assembly.GetType("Example.NonRedGenerated.Generated.Services."+GenerationModel.Pascal(names[0])+"Service",true)!.GetField("ExportReadPlan101",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;Assert.DoesNotContain("private_payload",exportPlan.OutputTypes.Keys);var grant=issued.GetProperty("data").GetString()!;Assert.Equal(64,grant.Length);
            using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant))
            {Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(ExcelPageExportService.ExcelContentType,download.Content.Headers.ContentType!.MediaType);using var workbook=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.DoesNotContain("yz_project_id",headers.Keys);Assert.DoesNotContain("group_id",headers.Keys);Assert.DoesNotContain("private_payload",headers.Keys);Assert.DoesNotContain(sheet.CellsUsed(),cell=>cell.GetString()=="不得导出的详情");Assert.Equal(3,sheet.RowsUsed().Count());Assert.Equal("A甲",sheet.Cell(2,headers["customer_id"]).GetString());Assert.Equal("A注释",sheet.Cell(2,headers["customer_notes"]).GetString());Assert.Equal(10m,sheet.Cell(2,headers["customer_amount"]).GetValue<decimal>());Assert.Equal(first.ToString(CultureInfo.InvariantCulture),sheet.Cell(2,headers["id"]).GetString());Assert.Equal(XLDataType.Text,sheet.Cell(2,headers["id"]).DataType);}
            var filteredExport=await Post("/initExport",new PageRequest{ParentId="71",Filters=new(){{"customerNotes",JsonSerializer.SerializeToElement("B")}}});Assert.Equal(200,filteredExport.GetProperty("code").GetInt32());
            using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+filteredExport.GetProperty("data").GetString())){using var workbook=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));Assert.Equal(2,workbook.Worksheet(1).RowsUsed().Count());Assert.Contains(workbook.Worksheet(1).Row(2).CellsUsed(),cell=>cell.GetString()=="B注释");}
            var fileCount=Directory.GetFiles(exportRoot,"*.xlsx").Length;authorization.Denied="export";using(var denied=await client.PostAsJsonAsync(endpoint+"/initExport",new PageRequest{ParentId="71"}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);authorization.Denied="list";using(var denied=await client.PostAsJsonAsync(endpoint+"/initExport",new PageRequest{ParentId="71"}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);authorization.Denied=null;Assert.Equal(fileCount,Directory.GetFiles(exportRoot,"*.xlsx").Length);
            var selectionEndpoint="/api/admin/auto/"+names[0]+"/102";
            var selectedRows=await Select("/selection/selected",new{parentId="71",selectedIds=new[]{second.ToString(CultureInfo.InvariantCulture),first.ToString(CultureInfo.InvariantCulture),second.ToString(CultureInfo.InvariantCulture)}});Assert.Equal(200,selectedRows.GetProperty("code").GetInt32());Assert.Equal(new[]{second.ToString(CultureInfo.InvariantCulture),first.ToString(CultureInfo.InvariantCulture)},selectedRows.GetProperty("data").EnumerateArray().Select(item=>item.GetProperty("id").GetString()));Assert.Equal("B注释",selectedRows.GetProperty("data")[0].GetProperty("customerNotes").GetString());Assert.False(selectedRows.GetProperty("data")[0].TryGetProperty("groupId",out _));
            Assert.Empty((await Select("/selection/selected",new{parentId="71",selectedIds=Array.Empty<string>()})).GetProperty("data").EnumerateArray());
            foreach(var invalid in new[]{first+2,first+3,first+4,first+999}){var invalidRows=await Select("/selection/selected",new{parentId="71",selectedIds=new[]{first.ToString(CultureInfo.InvariantCulture),invalid.ToString(CultureInfo.InvariantCulture)}});Assert.Equal(500,invalidRows.GetProperty("code").GetInt32());Assert.Equal(JsonValueKind.Null,invalidRows.GetProperty("data").ValueKind);}
            foreach(var parent in new string?[]{null,"73","74","999"})
            {Assert.Equal(500,(await Select("/page",new PageRequest{ParentId=parent})).GetProperty("code").GetInt32());Assert.Equal(500,(await Select("/count",new PageRequest{ParentId=parent})).GetProperty("code").GetInt32());Assert.Equal(500,(await Select("/selection/selected",new{parentId=parent,selectedIds=Array.Empty<string>()})).GetProperty("code").GetInt32());using var detail=await client.GetAsync(selectionEndpoint+"/detail/"+first+"?parentId="+parent);Assert.Equal(500,(await Json(detail)).GetProperty("code").GetInt32());}
            Assert.Equal(500,(await Select("/page",new PageRequest{ParentId="71",SelectedIds=Enumerable.Repeat(first.ToString(CultureInfo.InvariantCulture),201).ToList()})).GetProperty("code").GetInt32());Assert.Equal(500,(await Select("/count",new PageRequest{ParentId="71",PageNo=1000001})).GetProperty("code").GetInt32());Assert.Equal(500,(await Select("/selection/selected",new{parentId="71",selectedIds=Enumerable.Repeat(first.ToString(CultureInfo.InvariantCulture),201).ToArray()})).GetProperty("code").GetInt32());
            authorization.Denied="list";using(var deniedSelection=await client.PostAsJsonAsync(selectionEndpoint+"/selection/selected",new{parentId="71",selectedIds=new[]{first.ToString(CultureInfo.InvariantCulture)}}))Assert.Equal(HttpStatusCode.Forbidden,deniedSelection.StatusCode);authorization.Denied=null;
            async Task<JsonElement> Select(string route,object request){using var response=await client.PostAsJsonAsync(selectionEndpoint+route,request);Assert.True(response.IsSuccessStatusCode,"选择页HTTP未返回业务状态。");return await Json(response);}
            authorization.Denied="list";using(var denied=await client.PostAsJsonAsync(endpoint+"/page",new PageRequest{ParentId="71"}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);authorization.Denied="detail";
            var add=await Post("/add?parentId=71",new{name="新增记录",customerId="101",status=2,customerNotes="伪造备注",customerName="伪造名称"});var added=add.GetProperty("data");var addedId=long.Parse(added.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);ownIds.Add(addedId);Assert.Equal("A甲",added.GetProperty("customerName").GetString());Assert.Equal("A注释",added.GetProperty("customerNotes").GetString());Assert.Equal(10m,added.GetProperty("customerAmount").GetDecimal());authorization.Denied=null;Assert.Equal(500,(await Post("/initExport",new PageRequest{ParentId="71"})).GetProperty("code").GetInt32());Assert.Equal(fileCount,Directory.GetFiles(exportRoot,"*.xlsx").Length);Assert.Empty(Directory.GetFiles(exportRoot,"*.part"));
            var full=added.EnumerateObject().ToDictionary(item=>item.Name,item=>(object?)item.Value.Clone());full["name"]="完整表单更新";full["customerName"]="只读回显伪值";var update=await Post("/update?parentId=71",full);Assert.Equal("A甲",update.GetProperty("data").GetProperty("customerName").GetString());
            var cleared=await Post("/update?parentId=71",new{id=addedId.ToString(CultureInfo.InvariantCulture),customerId=(string?)null});Assert.Equal(JsonValueKind.Null,cleared.GetProperty("data").GetProperty("customerNotes").ValueKind);Assert.Equal("完整表单更新",cleared.GetProperty("data").GetProperty("name").GetString());
            var restored=await Post("/update?parentId=71",new{id=addedId.ToString(CultureInfo.InvariantCulture),customerId="102"});Assert.Equal("B乙",restored.GetProperty("data").GetProperty("customerName").GetString());var copied=await Post("/copy/"+addedId+"?parentId=71",new{});var copyId=long.Parse(copied.GetProperty("data").GetProperty("id").GetString()!,CultureInfo.InvariantCulture);ownIds.Add(copyId);Assert.Equal("B注释",copied.GetProperty("data").GetProperty("customerNotes").GetString());
            using(var deletion=new HttpRequestMessage(HttpMethod.Delete,endpoint+"?parentId=71"){Content=JsonContent.Create(new{id=copyId.ToString(CultureInfo.InvariantCulture)})})using(var response=await client.SendAsync(deletion))Assert.Equal(200,(await Json(response)).GetProperty("code").GetInt32());
            var beforeFailures=await RowsHash();foreach(var invalid in new[]{"103","104"})Assert.Equal(500,(await Post("/add?parentId=71",new{name="拒绝外部关系",customerId=invalid})).GetProperty("code").GetInt32());observer.Fail=true;Assert.Equal(500,(await Post("/add?parentId=71",new{name="提交前必须回滚",customerId="101"})).GetProperty("code").GetInt32());observer.Fail=false;Assert.Equal(beforeFailures,await RowsHash());
            await NonRedundantGeneratedImportProbe.VerifyAsync(client,endpoint,store,names[0],authorization,observer,ownIds,RowsHash);
            var finalRows=await store.ReadIncludingDeletedAsync(names[0]);Assert.Equal(ownIds.Order(),finalRows.Select(item=>SqlRowStore.Id(item)).Order());Assert.NotNull(finalRows.Single(item=>SqlRowStore.Id(item)==copyId)["delete_no"]);rowsHash=await RowsHash();Assert.Empty((await schema.PreviewAsync(catalog)).Changes);verified=true;await Receipt("verified",false);
            async Task<JsonElement> Post(string route,object request){using var response=await client.PostAsJsonAsync(endpoint+route,request);Assert.True(response.IsSuccessStatusCode,"HTTP读取专项返回非业务状态，未输出响应正文。");return await Json(response);}
        }
        catch(Exception error)
        {
            // 失败清理可能因保护现场而再次抛错，先保留最初断言及当时精确数据摘要。
            primaryFailure=error;
            await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().FullName,error.Message,error.StackTrace,actualRowsHash=await RowsHash(),ownIds},new JsonSerializerOptions{WriteIndented=true})+"\n");
            throw;
        }
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);
                if(actual.Tables.Count>0){if(fingerprint is null||MySqlSchemaPlanner.Fingerprint(actual)!=fingerprint||rowsHash is null||await RowsHash()!=rowsHash||names.Any(name=>!name.StartsWith("mt_nrg_"+nonce[..10]+"_",StringComparison.Ordinal)))throw new InvalidOperationException("自有结构或数据未匹配，保留样本现场。",primaryFailure);await using var c=cleanup.Provider.CreateConnection(options);await c.OpenAsync();foreach(var name in names){await using var q=c.CreateCommand();q.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await q.ExecuteNonQueryAsync();}}
                Assert.Empty((await inspection.InspectAsync(catalog)).Tables);await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var q=independent.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=q.CreateParameter();parameter.ParameterName="name";parameter.Value=name;q.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
        Task Group(long id,long project,string name,bool deleted=false)=>store.InsertAsync(names[2],new Dictionary<string,object?>{{"id",id},{"yz_project_id",project},{"name",name},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        Task Party(long id,long project,string name,string notes,decimal amount,bool deleted=false)=>store.InsertAsync(names[1],new Dictionary<string,object?>{{"id",id},{"yz_project_id",project},{"name",name},{"notes",notes},{"amount",amount},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        Task Source(long id,long project,long parent,long customer,string name,int status,bool deleted=false)=>store.InsertAsync(names[0],new Dictionary<string,object?>{{"id",id},{"yz_project_id",project},{"private_payload","不得导出的详情"},{"group_id",parent},{"customer_id",customer},{"name",name},{"status",status},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        async Task<string> RowsHash(){var rows=new List<object>();foreach(var name in names)rows.Add((await store.ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)).ToArray());return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));}
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,names,fingerprint,rowsHash,verified,cleaned,independentTableAbsenceChecked=cleaned,ownIds,testAuthorization=true,originalLoginExecuted=false,browserExecuted=false,grantAttempted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>原R响应按实际HTTP JSON读取，不解析日志或替身对象。</summary>
    private static async Task<JsonElement> Json(HttpResponseMessage response){using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return document.RootElement.Clone();}
    /// <summary>私有收据只写当前clone，凭据不进入输出。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}

/// <summary>固定测试授权器仅证明生成HTTP动作接线，不代表真实账号登录验收。</summary>
internal sealed class NonRedGeneratedAuthorization:IRuntimeAuthorization
{
    internal string? Denied;
    /// <summary>测试身份固定来自服务端，不读取客户端项目字段。</summary>
    public RuntimeActor RequireActor()=>new(1,"生成专项操作者",false,"54","5");
    /// <summary>验证来源页和目标页动作，拒权时在生成控制器入口失败。</summary>
    public void RequirePagePermission(string page,string operation){if(page is not("101" or "102" or "103" or "201" or "301")||Denied==operation)throw new UnauthorizedAccessException("专项拒权。");}
}
/// <summary>真实写入发生后、事务提交前注入故障，确认同一事务没有留下半次创建。</summary>
internal sealed class NonRedRollbackObserver:IRecordMutationObserver
{
    internal bool Fail;
    internal int Calls;
    internal int? FailureAt;
    /// <summary>仅本测试的故障开关触发回滚，不修改真实权限规则。</summary>
    public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){Calls++;if(Fail||FailureAt==Calls)throw new InvalidOperationException("生成专项提交前故障。");return Task.CompletedTask;}
}
