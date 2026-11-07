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
public sealed class PageConditionMySqlTests
{
    /// <summary>冗余页执行原HTTP查询、写入、候选及Excel协议。</summary>
    [StandaloneExportMySqlFact]
    public Task Redundant_page_conditions_execute_original_http_and_mysql()=>Run(false);
    /// <summary>非冗余页通过正式读取计划执行相同范围，不扩大VO。</summary>
    [StandaloneExportMySqlFact]
    public Task Nonredundant_page_conditions_execute_original_http_and_mysql()=>Run(true);
    /// <summary>两种存储模式分别独占随机三表，实例pin、结构与数据摘要保护沿原夹具。</summary>
    private async Task Run(bool nonRedundant)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var connection=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||connection.Database!="metarill_business_test"||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("生成读取专项仅允许指定隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await q.ExecuteReaderAsync();Assert.True(await r.ReadAsync()&&r.GetString(0)==options.ExpectedDatabase&&r.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不符，未创建样本。");}
        var nonce=Guid.NewGuid().ToString("N");var catalog=PageConditionFixture.Catalog("mt_pgc_"+nonce[..10],nonRedundant);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var directory=Path.Combine(Path.GetTempPath(),"metarill-page-condition-mysql",nonce);var evidence=Path.Combine(Root(),".local/page-conditions/mysql-"+nonce);Directory.CreateDirectory(evidence);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var store=new SqlRowStore(session,catalog);var created=false;var verified=false;string? fingerprint=null;string? rowsHash=null;WebApplication? app=null;Exception? primaryFailure=null;
        const long first=9007199254741001,second=9007199254741002;var ownIds=Enumerable.Range(0,9).Select(index=>first+index).ToList();
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var preview=await schema.PreviewAsync(catalog);Assert.Equal(3,preview.Changes.Count);Assert.All(preview.Changes,change=>Assert.Equal("createTable",change.Kind));await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(preview)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            await session.InTransactionAsync(async()=>
            {
                await Group(71,54,"父甲");await Group(72,54,"父乙");await Group(73,55,"外项目父");await Group(74,54,"已删除父",true);
                await Party(101,54,"候选甲","甲注释",10m);await Party(102,54,"候选乙","乙注释",20m);
                await Party(103,55,"候选甲","外项目",20m);await Party(104,54,"隐藏候选","关闭",20m);
                await Party(105,54,"候选零","金额不符",0m);await Party(106,54,"候选删","删除",20m,true);
                await Source(first,54,71,101,"可见甲",2,10);await Source(second,54,71,102,"可见乙",1,20);
                await Source(first+2,54,71,101,"不匹配名字",2,30);await Source(first+3,54,71,101,"可见负额",2,-1);
                await Source(first+4,54,71,101,"可见空状态",null,12);await Source(first+5,54,72,101,"可见外父",2,99);
                await Source(first+6,55,71,103,"可见外项目",2,99);await Source(first+7,54,71,101,"可见已删",2,99,true);
                await Source(first+8,54,71,101,"可见",2,99);return true;
            });rowsHash=await RowsHash();
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NonRedGenerated");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var authorization=new NonRedGeneratedAuthorization();var observer=new NonRedRollbackObserver();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IRuntimeAuthorization>(authorization);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(943));builder.Services.AddSingleton<IRecordMutationObserver>(observer);var exportRoot=Path.Combine(directory,"private-exports");var exports=new ExcelPageExportService(new ExportOptions{RootDirectory=exportRoot,MaxRows=20});builder.Services.AddSingleton<IPageExportService>(exports);assembly.GetType("Example.NonRedGenerated.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(options=>options.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(UnauthorizedAccessException){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(R<string>.Error("读取专项拒权",403));}catch(PageImportException error){await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}catch(Exception error)when(error is ArgumentException or InvalidOperationException){await File.AppendAllTextAsync(Path.Combine(evidence,"business-rejections.jsonl"),JsonSerializer.Serialize(new{errorType=error.GetType().FullName,error.Message,error.StackTrace})+"\n");await context.Response.WriteAsJsonAsync(R<string>.Error("读取专项业务拒绝"));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(20)};var endpoint="/api/admin/auto/"+names[0]+"/101";
            // 所有HTTP路径均由元数据正式生成；固定授权器不代表真实登录或浏览器验收。
            var page=await Post("/page",new PageRequest{ParentId="71",PageSize=0});Success(page);
            Assert.Equal("2",page.GetProperty("data").GetProperty("total").ToString());
            Assert.Equal(new[]{first.ToString(),second.ToString()},page.GetProperty("data").GetProperty("records").EnumerateArray().Select(r=>r.GetProperty("id").GetString()).Order());
            Assert.Equal(30m,page.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").GetDecimal());
            Assert.Equal("2",(await Post("/count",new PageRequest{ParentId="71"})).GetProperty("data").ToString());
            var selectedEndpoint="/api/admin/auto/"+names[0]+"/102/selection/selected";
            Success(await Send(selectedEndpoint,new{parentId="71",selectedIds=new[]{first.ToString(),second.ToString()}}));
            foreach(var outside in Enumerable.Range(2,7).Select(i=>first+i))
            {
                Assert.Equal(500,(await Send(selectedEndpoint,new{parentId="71",selectedIds=new[]{first.ToString(),outside.ToString()}})).GetProperty("code").GetInt32());
                using var response=await client.GetAsync(endpoint+"/detail/"+outside+"?parentId=71");Assert.Equal(500,(await Json(response)).GetProperty("code").GetInt32());
            }
            var nullPage=await Send("/api/admin/auto/"+names[0]+"/104/page",new PageRequest{ParentId="71"});Success(nullPage);Assert.Equal("1",nullPage.GetProperty("data").GetProperty("total").ToString());
            var equalsNull=await Send("/api/admin/auto/"+names[0]+"/105/page",new PageRequest{ParentId="71"});Success(equalsNull);Assert.Equal("0",equalsNull.GetProperty("data").GetProperty("total").ToString());
            var target=await Send("/api/admin/auto/"+names[1]+"/201/page",new PageRequest{PageSize=20});Success(target);Assert.Equal("2",target.GetProperty("data").GetProperty("total").ToString());
            var generatedModel=GenerationModel.Create(catalog,"Example.NonRedGenerated");var sourceModel=generatedModel.Tables.Single(t=>t.Source.Id=="1");
            var relation=Assert.Single(RelationCompilation.ForPage(generatedModel,sourceModel.Pages.Single(p=>p.Source.Id=="101")),r=>r.SourceColumn.Source.Id=="15");
            var candidates=await Post("/"+relation.LegacyEndpoint,new{parentId="71"});Success(candidates);Assert.Equal(new[]{"101","102"},candidates.GetProperty("data").EnumerateArray().Select(r=>r.GetProperty("id").ToString()).Order());
            var candidatePage=await Post("/"+relation.Endpoint+"/page",new{parentId="71",pageNo=1,pageSize=1});Success(candidatePage);Assert.Equal("2",candidatePage.GetProperty("data").GetProperty("total").ToString());
            Success(await Post("/"+relation.Endpoint+"/selected",new{parentId="71",selectedIds=new[]{"102","101"}}));
            foreach(var invalid in new[]{"103","104","105","106"})
            {Assert.Equal(500,(await Post("/"+relation.Endpoint+"/selected",new{parentId="71",selectedIds=new[]{"101",invalid}})).GetProperty("code").GetInt32());var before=await RowsHash();Assert.Equal(500,(await Post("/update?parentId=71",new{id=first.ToString(),customerId=invalid})).GetProperty("code").GetInt32());Assert.Equal(before,await RowsHash());}
            // 原导出忽略分页，实际下载XLSX验证范围、精确ID和私有字段不泄漏。
            var issued=await Post("/initExport",new PageRequest{ParentId="71",PageNo=99,PageSize=1});Success(issued);
            using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+issued.GetProperty("data").GetString()))
            {Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var workbook=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=workbook.Worksheet(1);Assert.Equal(3,sheet.RowsUsed().Count());var headers=sheet.Row(1).CellsUsed().ToDictionary(c=>c.GetString(),c=>c.Address.ColumnNumber);Assert.DoesNotContain("private_payload",headers.Keys);Assert.DoesNotContain("yz_project_id",headers.Keys);Assert.DoesNotContain("group_id",headers.Keys);Assert.Equal(new[]{first.ToString(),second.ToString()},new[]{sheet.Cell(2,headers["id"]).GetString(),sheet.Cell(3,headers["id"]).GetString()}.Order());Assert.Equal(XLDataType.Text,sheet.Cell(2,headers["id"]).DataType);}
            authorization.Denied="list";using(var denied=await client.PostAsJsonAsync(endpoint+"/page",new PageRequest{ParentId="71"}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);authorization.Denied=null;
            // 先按旧范围定位；写后回执允许状态改变使记录离开当前页面。
            var changed=await Post("/update?parentId=71",new{id=first.ToString(),status=3,customerId="102"});Success(changed);Assert.Equal(3,changed.GetProperty("data").GetProperty("status").GetInt32());
            Assert.Equal(3,Convert.ToInt32((await Read(first))["status"],CultureInfo.InvariantCulture));
            using(var response=await client.GetAsync(endpoint+"/detail/"+first+"?parentId=71"))Assert.Equal(500,(await Json(response)).GetProperty("code").GetInt32());
            var beforeOutside=await RowsHash();Assert.Equal(500,(await Post("/update?parentId=71",new{id=first.ToString(),status=2})).GetProperty("code").GetInt32());Assert.Equal(beforeOutside,await RowsHash());
            var added=await Post("/add?parentId=71",new{name="隐藏新建",customerId="101",status=2,amount=10});Success(added);var newId=long.Parse(added.GetProperty("data").GetProperty("id").GetString()!,CultureInfo.InvariantCulture);Assert.Equal("隐藏新建",(await Read(newId))["name"]);
            using(var response=await client.GetAsync(endpoint+"/detail/"+newId+"?parentId=71"))Assert.Equal(500,(await Json(response)).GetProperty("code").GetInt32());
            var copied=await Post("/copy/"+second+"?parentId=71",new{});Success(copied);var copyId=copied.GetProperty("data").GetProperty("id").GetString()!;
            using(var deletion=new HttpRequestMessage(HttpMethod.Delete,endpoint+"?parentId=71"){Content=JsonContent.Create(new{id=copyId})})using(var response=await client.SendAsync(deletion))Success(await Json(response));
            // 原Excel中文表头追加、范围内覆盖后离页、范围外同名不覆盖，以及整批原子回滚。
            Success(await Import("101",Row("可见导入","候选甲",2,5)));
            Success(await Import("103",Row("可见乙","候选乙",3,25)));Assert.Equal(3,Convert.ToInt32((await Read(second))["status"],CultureInfo.InvariantCulture));
            var outsideBefore=JsonSerializer.Serialize(await Read(first+2));Success(await Import("103",Row("不匹配名字","候选甲",2,5)));Assert.Equal(outsideBefore,JsonSerializer.Serialize(await Read(first+2)));
            Assert.Equal(2,(await store.ReadIncludingDeletedAsync(names[0])).Count(r=>Equals(r["name"],"不匹配名字")));
            foreach(var bad in new[]{"隐藏候选","候选零","候选删","不存在"})
            {var before=await RowsHash();var failed=await Import("101",Row("可见回滚前行","候选甲",2,5),Row("可见拒绝后行",bad,2,5));Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.Contains("第2行",failed.GetProperty("msg").GetString());Assert.Equal(before,await RowsHash());}
            observer.Fail=true;var beforeObserver=await RowsHash();try{Assert.Equal(500,(await Post("/add?parentId=71",new{name="可见回滚",customerId="101",status=2,amount=10})).GetProperty("code").GetInt32());}finally{observer.Fail=false;}Assert.Equal(beforeObserver,await RowsHash());
            foreach(var row in await store.ReadIncludingDeletedAsync(names[0])){var id=SqlRowStore.Id(row);if(!ownIds.Contains(id))ownIds.Add(id);}rowsHash=await RowsHash();Assert.Empty((await schema.PreviewAsync(catalog)).Changes);verified=true;await Receipt("verified",false);
            /// <summary>保持原导入行标题，字符串是候选名称。</summary>
            static Dictionary<string,object?> Row(string name,string candidate,int status,decimal amount)=>new(){{"记录名",name},{"customer_id",candidate},{"status",status},{"amount",amount}};
            /// <summary>原追加与覆盖路由不增加前端参数。</summary>
            Task<JsonElement> Import(string page,params Dictionary<string,object?>[] rows)=>Send("/api/admin/auto/"+names[0]+"/"+page+"/importDatas"+page,new{parentId="71",requestJson=rows});
            /// <summary>实际物理行用于对照写后状态和范围外数据。</summary>
            async Task<Dictionary<string,object?>> Read(long id)=>(await store.ReadIncludingDeletedAsync(names[0])).Single(r=>SqlRowStore.Id(r)==id);
            /// <summary>逐次解析实际HTTP业务响应。</summary>
            Task<JsonElement> Post(string route,object request)=>Send(endpoint+route,request);
            async Task<JsonElement> Send(string route,object request){using var response=await client.PostAsJsonAsync(route,request);Assert.True(response.IsSuccessStatusCode,"页面条件专项HTTP异常，未输出正文。");return await Json(response);}
            static void Success(JsonElement value)=>Assert.True(value.GetProperty("code").GetInt32()==200,"原接口业务未成功："+(value.TryGetProperty("msg",out var message)?message.GetString():"无提示"));
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
                if(actual.Tables.Count>0){if(fingerprint is null||MySqlSchemaPlanner.Fingerprint(actual)!=fingerprint||rowsHash is null||await RowsHash()!=rowsHash||names.Any(name=>!name.StartsWith("mt_pgc_"+nonce[..10]+"_",StringComparison.Ordinal)))throw new InvalidOperationException("自有结构或数据未匹配，保留样本现场。",primaryFailure);await using var c=cleanup.Provider.CreateConnection(options);await c.OpenAsync();foreach(var name in names){await using var q=c.CreateCommand();q.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await q.ExecuteNonQueryAsync();}}
                Assert.Empty((await inspection.InspectAsync(catalog)).Tables);await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var q=independent.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=q.CreateParameter();parameter.ParameterName="name";parameter.Value=name;q.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
            await TestDirectoryCleanup.DeleteAsync(directory,Path.Combine(Path.GetTempPath(),"metarill-page-condition-mysql"));
        }
        Task Group(long id,long project,string name,bool deleted=false)=>store.InsertAsync(names[2],new Dictionary<string,object?>{{"id",id},{"yz_project_id",project},{"name",name},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        Task Party(long id,long project,string name,string notes,decimal amount,bool deleted=false,long parent=71)
        {
            var row=new Dictionary<string,object?>{{"id",id},{"yz_project_id",project},{"name",name},{"notes",notes},{"amount",amount},{"delete_no",deleted?new DateTime(2026,1,1):null}};
            return store.InsertAsync(names[1],row);
        }
        /// <summary>只有元数据声明的物理字段入库，派生列由正式计划读取。</summary>
        Task Source(long id,long project,long parent,long customer,string name,int? status,decimal amount,bool deleted=false)=>store.InsertAsync(names[0],new Dictionary<string,object?>{{"id",id},{"yz_project_id",project},{"private_payload","不得导出的详情"},{"group_id",parent},{"customer_id",customer},{"name",name},{"status",status},{"amount",amount},{"delete_no",deleted?new DateTime(2026,1,1):null}});
        async Task<string> RowsHash(){var rows=new List<object>();foreach(var name in names)rows.Add((await store.ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)).ToArray());return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));}
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,names,fingerprint,rowsHash,verified,cleaned,independentTableAbsenceChecked=cleaned,ownIds,nonRedundant,testAuthorization=true,originalLoginExecuted=false,browserExecuted=false,grantAttempted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>原R响应按实际HTTP JSON读取，不解析日志或替身对象。</summary>
    private static async Task<JsonElement> Json(HttpResponseMessage response){using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return document.RootElement.Clone();}
    /// <summary>私有收据只写当前clone，凭据不进入输出。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}

/// <summary>常量范围与原父范围共存，冗余和非冗余仅改变存储策略。</summary>
internal static class PageConditionFixture
{
    /// <summary>同一元数据负责建表、生成、查询、导入及导出，不手写业务实体。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string prefix,bool nonRedundant)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog(prefix);var source=catalog.ConfigPages.Single(p=>p.Id=="101");
        if(!nonRedundant)
        {catalog.ConfigTables.Single(t=>t.Id=="1").IsRongyu=1;catalog.ConfigColumns.RemoveAll(c=>c.Id is "16" or "17");catalog.ConfigPageColumns.RemoveAll(f=>f.ConfigColumnId is "16" or "17");}
        catalog.ConfigColumns.Add(new(){Id="140",ConfigTableId="1",ColumnName="amount",Name="amount",LogicalType="decimal"});
        catalog.ConfigPageColumns.Add(new(){Id="101140",ConfigPageId="101",ConfigColumnId="140",IsEdit=1,IsShow=1,Extra=new(){{"sumKind",JsonSerializer.SerializeToElement(1)}}});
        source.PageSize=0;source.OrderBy="id desc";source.IsExport=1;
        source.WhereSql="group_id=@parent_id AND status IN (1,2,NULL) AND name LIKE '可见_%' AND amount>=0 AND id>=9007199254740000";
        catalog.ConfigPages.Single(p=>p.Id=="201").WhereSql="name LIKE '候选%' AND amount>=10 AND amount<30";
        NonRedundantGeneratedFixture.AddSelectionPage(catalog);NonRedundantGeneratedFixture.EnableImports(catalog);
        foreach(var pair in new[]{("104","status IS NULL"),("105","status=NULL")})
        {
            var page=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(source))!;page.Id=pair.Item1;page.WhereSql="group_id=@parent_id AND "+pair.Item2;page.IsNew=0;page.IsSave=0;page.IsDelete=0;page.IsCopy=0;page.IsImport=0;page.IsExport=0;catalog.ConfigPages.Add(page);
            foreach(var original in catalog.ConfigPageColumns.Where(f=>f.ConfigPageId=="101").ToArray()){var field=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(original))!;field.Id=pair.Item1+field.Id;field.ConfigPageId=pair.Item1;catalog.ConfigPageColumns.Add(field);}
        }
        return catalog;
    }
}
