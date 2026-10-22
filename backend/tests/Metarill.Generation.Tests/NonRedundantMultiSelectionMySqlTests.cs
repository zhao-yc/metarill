using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using ClosedXML.Excel;
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

/// <summary>实际生成Controller／Service在固定测试身份下执行真实HTTP/MySQL，不冒充正式认证或浏览器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantMultiSelectionMySqlTests
{
    private const long Project=9007199254740993,Parent=9007199254742001,Target=9007199254750000;
    /// <summary>只存ID数组，名称实时回读；父变化、撤权与尾部故障不能部分提交。</summary>
    [StandaloneExportMySqlFact]
    public Task Generated_multi_crud_reads_current_names_and_rolls_back_invalid_parent_permission_and_tail_failure()=>VerifyAsync(false);
    /// <summary>无名称配置列仍使用自动显示数组，不写元数据或物理名称。</summary>
    [StandaloneExportMySqlFact]
    public Task Automatic_names_keep_same_http_contract_without_declared_name_column()=>VerifyAsync(true);
    /// <summary>两种目录各用独占随机三表运行同一原协议，证据与清理分别记录。</summary>
    private static async Task VerifyAsync(bool automatic)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var cs=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("多选实库只允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var connection=new MySqlConnection(options.ConnectionString))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不匹配，未创建样本。");
        }
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrm_"+nonce[..12];var catalog=NonRedundantMultiSelectionTests.Catalog(prefix);if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
        var page=catalog.ConfigPages.Single(item=>item.Id=="201");page.WhereSql="yz_project_id=@current_yz_project_id";page.IsCopy=1;page.IsExport=1;page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigPageColumns.Add(new(){Id="2004",ConfigPageId="201",ConfigColumnId="26",IsShow=1,IsEdit=1});
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-multi-http");var directory=Path.Combine(boundary,nonce);
        var evidence=Path.Combine(Root(),".local/nonredundant-multi/http-"+nonce);Directory.CreateDirectory(evidence);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var rows=new SqlRowStore(session,catalog);
        var tableNames=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var ownIds=new HashSet<long>();var authorization=new Authorization();var observer=new FailureObserver();WebApplication? app=null;var created=false;string? fingerprint=null;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(3,plan.Changes.Count);
            await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            foreach(var id in new[]{Parent,Parent+1})await rows.InsertAsync(prefix+"_parent",new Dictionary<string,object?>{{"id",id},{"name","父"+id},{"yz_project_id",Project}});
            for(var index=0;index<5;index++)await rows.InsertAsync(prefix+"_target",new Dictionary<string,object?>{{"id",Target+index},{"name",index==0?"甲_%!中文":"乙"+index},{"parent_id",index==2?Parent+1:Parent},{"yz_project_id",index==3?Project+1:Project},{"delete_no",index==4?new DateTime(2026,1,1):null}});
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NonRedMultiHttp");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(871));builder.Services.AddSingleton<IRuntimeAuthorization>(authorization);builder.Services.AddSingleton<IRecordMutationObserver>(observer);var exports=new ExcelPageExportService(new ExportOptions{RootDirectory=Path.Combine(directory,"private-exports"),MaxRows=20});builder.Services.AddSingleton<IPageExportService>(exports);
            assembly.GetType("Example.NonRedMultiHttp.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or PageImportException){context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(20)};var endpoint="/api/admin/auto/"+prefix+"_source/201";
            var selected=new[]{(Target+1).ToString(CultureInfo.InvariantCulture),Target.ToString(CultureInfo.InvariantCulture)};
            var added=Success(await Post("/add",new{name="来源",parentId=Parent.ToString(CultureInfo.InvariantCulture),targetIds=selected}));var record=added.GetProperty("id").GetString()!;ownIds.Add(long.Parse(record,CultureInfo.InvariantCulture));
            Assert.Equal(selected,added.GetProperty("targetIds").EnumerateArray().Select(value=>value.GetString()));Assert.Equal(new[]{"乙1","甲_%!中文"},Names(added));
            Assert.DoesNotContain("target_names",(await rows.ReadAsync(prefix+"_source")).Single().Keys);
            var before=await Hash();Assert.Equal(1,await rows.UpdateAsync(prefix+"_target",Target,new Dictionary<string,object?>{{"name","新名称"}}));
            var fresh=Success(await Post("/page",new PageRequest{Filters=new(){{"targetNames",JsonSerializer.SerializeToElement("新名称")}}}));Assert.Equal("1",fresh.GetProperty("total").GetString());Assert.Equal(new[]{"乙1","新名称"},Names(fresh.GetProperty("records")[0]));Assert.Equal(before,await Hash());
            var full=added.EnumerateObject().ToDictionary(item=>item.Name,item=>(object?)item.Value.Clone());full["targetNames"]=new[]{"伪造名称"};full["name"]="整表单";var saved=Success(await Post("/update",full));Assert.Equal(new[]{"乙1","新名称"},Names(saved));
            var nullValue=Success(await Post("/update",new{id=record,targetIds=(object?)null}));Assert.Equal(JsonValueKind.Null,nullValue.GetProperty("targetIds").ValueKind);Assert.Empty(Names(nullValue));
            var empty=Success(await Post("/update",new{id=record,targetIds=Array.Empty<string>()}));Assert.Empty(Names(empty));
            Success(await Post("/update",new{id=record,targetIds=" [ \""+Target+"\" ] "}));
            Success(await Post("/update",new{id=record,targetIds=selected}));before=await Hash();
            foreach(var invalid in new object[]{new[]{Target.ToString(),Target.ToString()},new[]{"0"+Target},new[]{"9223372036854775808"},new[]{1},"[","{}",Enumerable.Repeat(Target.ToString(),201).ToArray()})
            {Assert.Equal(500,(await Post("/update",new{id=record,targetIds=invalid})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());}
            foreach(var invalid in new[]{Target+2,Target+3,Target+4,Target+999})
            {Assert.Equal(500,(await Post("/update",new{id=record,targetIds=new[]{Target.ToString(),invalid.ToString()}})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());}
            Assert.Equal(500,(await Post("/update",new{id=record,parentId=(Parent+1).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());
            authorization.DenyTarget=true;Assert.Equal(500,(await Post("/update",new{id=record,targetIds=selected})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());
            Success(await Post("/update",new{id=record,name="省略多选仍可保存"}));authorization.DenyTarget=false;
            before=await Hash();observer.Fail=true;Assert.Equal(500,(await Post("/update",new{id=record,name="尾部失败",targetIds=Array.Empty<string>()})).GetProperty("code").GetInt32());observer.Fail=false;Assert.Equal(before,await Hash());
            // 只有来源import动作，原JSON名称数组按整行父依赖解析；不借create/update或目标list授权。
            authorization.DenyTarget=true;authorization.DenySourceCrud=true;
            var importResponse=await Post("/importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"targetIds","[\"新名称\",\"乙1\"]"},{"name","导入记录"},{"parentId",Parent.ToString()}}}});Assert.Equal(200,importResponse.GetProperty("code").GetInt32());
            var imported=(await rows.ReadAsync(prefix+"_source")).Single(row=>Equals(row["name"],"导入记录"));var importedId=SqlRowStore.Id(imported);ownIds.Add(importedId);Assert.Equal("[\""+Target+"\",\""+(Target+1)+"\"]",imported["target_ids"]);
            Assert.Equal(200,(await Post("/importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"name","导入记录"},{"targetIds","[\"乙1\"]"}}}})).GetProperty("code").GetInt32());Assert.Equal(importedId,SqlRowStore.Id((await rows.ReadAsync(prefix+"_source")).Single(row=>Equals(row["name"],"导入记录"))));
            before=await Hash();Assert.Equal(500,(await Post("/importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"name","尾部整批一"},{"targetIds","[\"新名称\"]"},{"parentId",Parent.ToString()}},new Dictionary<string,object?>{{"name","尾部整批二"},{"targetIds","[\"不存在\"]"},{"parentId",Parent.ToString()}}}})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());
            authorization.DenyTarget=false;authorization.DenySourceCrud=false;
            var copy=Success(await Post("/copy/"+record,new{}));ownIds.Add(long.Parse(copy.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));Assert.Equal(new[]{"乙1","新名称"},Names(copy));
            authorization.DenyTarget=true;var ticket=Success(await Post("/initExport",new PageRequest{SelectedIds=[record]}));using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket.GetString()!)))
            {
                Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var workbook=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);
                var source=sheet.RowsUsed().Skip(1).Single(row=>row.Cell(headers["name"]).GetString()=="省略多选仍可保存");Assert.Contains("乙1",source.Cell(headers["targetIds"]).GetString());Assert.Contains("新名称",source.Cell(headers["targetIds"]).GetString());using var values=JsonDocument.Parse(source.Cell(headers["targetIds"]).GetString());Assert.Equal(new[]{"乙1","新名称"},values.RootElement.EnumerateArray().Select(value=>value.GetString()));
            }
            authorization.DenyTarget=false;
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);await Receipt("verified",false);
            async Task<JsonElement> Post(string route,object request){using var response=await client.PostAsJsonAsync(endpoint+route,request);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Hash()=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes((await rows.ReadIncludingDeletedAsync(prefix+"_source")).OrderBy(row=>SqlRowStore.Id(row)).ToArray())));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.txt"),error.ToString());throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);
                if(actual.Tables.Count>0)
                {
                    Assert.NotNull(fingerprint);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(actual));
                    Assert.Equal(ownIds.Order(),(await rows.ReadIncludingDeletedAsync(prefix+"_source")).Select(row=>SqlRowStore.Id(row)).Order());
                    Assert.Equal(new[]{Parent,Parent+1},(await rows.ReadIncludingDeletedAsync(prefix+"_parent")).Select(row=>SqlRowStore.Id(row)).Order());
                    Assert.Equal(Enumerable.Range(0,5).Select(index=>Target+index),(await rows.ReadIncludingDeletedAsync(prefix+"_target")).Select(row=>SqlRowStore.Id(row)).Order());
                    await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in tableNames){Assert.StartsWith(prefix+"_",name);await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}
                }
                await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();foreach(var name in tableNames){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";command.Parameters.AddWithValue("name",name);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,prefix,tableNames,ownIds,fingerprint,instanceVerified=true,cleaned,fixedTestAuthorization=true,browserExecuted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>普通数组VO原样读取，空集合与缺失目标的null位置不做拼接丢失。</summary>
    private static string?[] Names(JsonElement row)=>row.GetProperty("targetNames").EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.Null?null:value.GetString()).ToArray();
    /// <summary>完整生成服务使用统一R响应，失败正文只含本测试样本字段。</summary>
    private static JsonElement Success(JsonElement value){Assert.Equal(200,value.GetProperty("code").GetInt32());return value.GetProperty("data");}
    /// <summary>仅测试固定身份与受控拒绝；不声称真实登录认证。</summary>
    private sealed class Authorization:IRuntimeAuthorization
    {
        internal bool DenyTarget{get;set;}
        internal bool DenySourceCrud{get;set;}
        public RuntimeActor RequireActor()=>new(871,"非冗余多选实库",false,Project.ToString(CultureInfo.InvariantCulture),"1");
        public void RequirePagePermission(string pageId,string operation){if(pageId=="201"&&DenySourceCrud&&operation is "create" or "update")throw new UnauthorizedAccessException("测试只授权来源import。");if(pageId=="301"&&DenyTarget)throw new UnauthorizedAccessException("测试撤销目标查询权。");}
    }
    /// <summary>标准观察者尾部故障验证主记录事务，不模拟数据库写入。</summary>
    private sealed class FailureObserver:IRecordMutationObserver
    {
        internal bool Fail{get;set;}
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token){if(Fail)throw new InvalidOperationException("测试尾部故障。");return Task.CompletedTask;}
    }
    /// <summary>私有证据定位当前普通clone，不向源码写日志或凭据。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
