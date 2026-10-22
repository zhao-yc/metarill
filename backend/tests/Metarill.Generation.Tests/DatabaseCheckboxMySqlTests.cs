using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
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

/// <summary>独占Server-200随机样本验证真实生成HTTP／MySQL；固定测试身份不代表正式登录验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class DatabaseCheckboxMySqlTests
{
    private const long Project=9007199254740993,Parent=9007199254742001,Target=9007199254750000;
    [StandaloneExportMySqlFact]
    public Task Nonredundant_declared_checkbox_keeps_original_scopes_crud_and_excel()=>Verify(true,false);
    [StandaloneExportMySqlFact]
    public Task Nonredundant_automatic_checkbox_keeps_original_scopes_crud_and_excel()=>Verify(true,true);
    [StandaloneExportMySqlFact]
    public Task Redundant_checkbox_keeps_ids_excel_and_transactional_name_cache()=>Verify(false,false);

    /// <summary>原来源父参数不等于任意表单提示；候选、只读、保存、导入导出均核对同一真实父。</summary>
    [StandaloneExportMySqlFact]
    public Task Original_source_parent_is_validated_for_options_mutations_and_excel()=>Verify(true,false,true);

    /// <summary>联接标记名称缓存仍落物理列，沿原复选全流程重新派生，不能客户端覆盖。</summary>
    [StandaloneExportMySqlFact]
    public Task Redundant_joined_name_cache_keeps_original_checkbox_transactions()=>Verify(false,false,false,true);

    /// <summary>实例pin通过后才创建随机三表，失败证据不覆盖，清理前检查结构与本任务行集。</summary>
    private static async Task Verify(bool nonRedundant,bool automatic,bool sourceParent=false,bool joinedCache=false)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!="metarill_business_test")throw new InvalidOperationException("仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var connection=new MySqlConnection(options.ConnectionString))
        {await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不匹配，未创建样本。");}
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_cb_"+nonce[..12];var catalog=DatabaseCheckboxGenerationTests.Catalog(nonRedundant,automatic,prefix);
        if(!automatic)catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="27").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(joinedCache){var cache=catalog.ConfigColumns.Single(column=>column.Id=="27");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.name");}
        var page=catalog.ConfigPages.Single(item=>item.Id=="201");page.WhereSql="yz_project_id=@current_yz_project_id";page.IsCopy=1;page.IsImport=1;page.IsExport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigPageColumns.Add(new(){Id="2004",ConfigPageId="201",ConfigColumnId="26",IsShow=1,IsEdit=1});
        catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="第一段不执行;第二段不执行;parent_id=@formParameters.parentId;name desc";
        if(sourceParent){page.WhereSql+=" AND parent_id=@parentId";catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="头;头;parent_id=@parentId;name desc";}
        // 原复选使用自己的条件，不把绑定页的另一个页面条件偷换成候选范围。
        catalog.ConfigPages.Single(item=>item.Id=="301").WhereSql="yz_project_id=@current_yz_project_id AND name='此目标页面没有开放这些行'";
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-mysql");var directory=Path.Combine(boundary,nonce);var evidence=Path.Combine(Root(),".local/database-checkbox/http-"+nonce);Directory.CreateDirectory(evidence);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var rows=new SqlRowStore(session,catalog);
        var tables=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var ownIds=new HashSet<long>();var targetIds=new HashSet<long>();var authorization=new Authorization();var observer=new FailureObserver();WebApplication? app=null;var created=false;string? fingerprint=null;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(3,plan.Changes.Count);await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            foreach(var id in new[]{Parent,Parent+1})await rows.InsertAsync(prefix+"_parent",new Dictionary<string,object?>{{"id",id},{"name","父"+id},{"yz_project_id",Project}});
            for(var index=0;index<5;index++){targetIds.Add(Target+index);await rows.InsertAsync(prefix+"_target",new Dictionary<string,object?>{{"id",Target+index},{"name",index==0?"甲_%!中文":"乙"+index},{"parent_id",index==2?Parent+1:Parent},{"yz_project_id",index==3?Project+1:Project},{"delete_no",index==4?new DateTime(2026,1,1):null}});}
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CheckboxHttp");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(877));builder.Services.AddSingleton<IRuntimeAuthorization>(authorization);builder.Services.AddSingleton<IRecordMutationObserver>(observer);
            var exports=new ExcelPageExportService(new ExportOptions{RootDirectory=Path.Combine(directory,"private-exports"),MaxRows=30});builder.Services.AddSingleton<IPageExportService>(exports);
            assembly.GetType("Example.CheckboxHttp.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or PageImportException){context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(25)};var endpoint="/api/admin/auto/"+prefix+"_source/201";
            if(sourceParent)
            {
                async Task<JsonElement> ScopedPost(string route,object body){using var response=await client.PostAsJsonAsync(endpoint+route,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
                Assert.Equal(500,(await ScopedPost("/findNameIdTarget25",new{formParameters=new{}})).GetProperty("code").GetInt32());
                Assert.Equal(500,(await ScopedPost("/findNameIdTarget25",new{parentId=(Parent+999).ToString(),id=Array.Empty<string>()})).GetProperty("code").GetInt32());
                Assert.Equal(2,Success(await ScopedPost("/findNameIdTarget25",new{parentId=Parent.ToString(),formParameters=new{}})).GetArrayLength());Assert.Single(Success(await ScopedPost("/findNameIdTarget25",new{parentId=(Parent+1).ToString()})).EnumerateArray());
                var selection=new[]{Target.ToString(),(Target+1).ToString()};var value=Success(await ScopedPost("/add?parentId="+Parent,new{name="父范围来源",targetIds=selection}));var key=value.GetProperty("id").GetString()!;ownIds.Add(long.Parse(key));Assert.Equal(Parent.ToString(),value.GetProperty("parentId").GetString());
                var beforeParent=JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(prefix+"_source"));Assert.Equal(500,(await ScopedPost("/update?parentId="+Parent,new{id=key,targetIds=new[]{(Target+2).ToString()}})).GetProperty("code").GetInt32());Assert.Equal(beforeParent,JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(prefix+"_source")));
                authorization.Denied.UnionWith(["list","create","update"]);Success(await ScopedPost("/importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{new Dictionary<string,object?>{{"name","父范围导入"},{"targetIds","[\"甲_%!中文\"]"}}}}));var importedParent=(await rows.ReadAsync(prefix+"_source")).Single(row=>Equals(row["name"],"父范围导入"));ownIds.Add(SqlRowStore.Id(importedParent));Assert.Equal(Parent,Convert.ToInt64(importedParent["parent_id"],CultureInfo.InvariantCulture));authorization.Denied.Clear();
                var ticketParent=Success(await ScopedPost("/initExport",new PageRequest{ParentId=Parent.ToString(),SelectedIds=[key]}));Assert.Equal(JsonValueKind.String,ticketParent.ValueKind);
                await rows.UpdateAsync(prefix+"_parent",Parent,new Dictionary<string,object?>{{"delete_no",new DateTime(2026,1,1)}});Assert.Equal(500,(await ScopedPost("/findNameIdTarget25",new{parentId=Parent.ToString()})).GetProperty("code").GetInt32());beforeParent=JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(prefix+"_source"));Assert.Equal(500,(await ScopedPost("/update?parentId="+Parent,new{id=key,targetIds=selection})).GetProperty("code").GetInt32());Assert.Equal(beforeParent,JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(prefix+"_source")));
                await Receipt("verified",false);return;
            }
            var selected=new[]{(Target+1).ToString(CultureInfo.InvariantCulture),Target.ToString(CultureInfo.InvariantCulture)};
            var candidate=Success(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()}}));Assert.Equal(2,candidate.GetArrayLength());foreach(var item in candidate.EnumerateArray()){Assert.Equal(JsonValueKind.String,item.GetProperty("id").ValueKind);Assert.Equal(item.GetProperty("id").GetString(),item.GetProperty("value").GetString());}
            Assert.Empty(Success(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()},id=Array.Empty<string>()})).EnumerateArray());
            var narrow=Success(await Post("/findNameIdTarget25",new{formParameters=JsonSerializer.Serialize(new{parentId=Parent.ToString()}),id=new[]{Target.ToString()}}));Assert.Equal(Target.ToString(),Assert.Single(narrow.EnumerateArray()).GetProperty("id").GetString());
            authorization.Denied.UnionWith(["list","create"]);Assert.Equal(2,Success(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()}})).GetArrayLength());authorization.Denied.Add("update");Assert.Equal(500,(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()}})).GetProperty("code").GetInt32());authorization.Denied.Clear();
            var added=Success(await Post("/add",new{name="来源",parentId=Parent.ToString(),targetIds=selected}));var record=added.GetProperty("id").GetString()!;ownIds.Add(long.Parse(record));Assert.Equal(selected,added.GetProperty("targetIds").EnumerateArray().Select(item=>item.GetString()));Assert.Equal(new[]{"乙1","甲_%!中文"},Names(added));
            Assert.Equal(!nonRedundant,(await rows.ReadAsync(prefix+"_source")).Single().ContainsKey("target_names"));
            var before=await Hash();await rows.UpdateAsync(prefix+"_target",Target,new Dictionary<string,object?>{{"name","最新名称"}});var fresh=Success(await Post("/page",new PageRequest{Filters=new(){{"targetIds",JsonSerializer.SerializeToElement(Target.ToString())}}}));Assert.Equal("1",fresh.GetProperty("total").GetString());Assert.Equal(nonRedundant?new[]{"乙1","最新名称"}:new[]{"乙1","甲_%!中文"},Names(fresh.GetProperty("records")[0]));Assert.Equal(before,await Hash());
            if(!automatic){var namesFilter=Success(await Post("/page",new PageRequest{Filters=new(){{"targetNames",JsonSerializer.SerializeToElement(nonRedundant?"最新名称":"_%!")}}}));Assert.Equal("1",namesFilter.GetProperty("total").GetString());}
            // HTTP原页面整实体回传名称只作回显，最终名称从已锁定的同一候选范围派生。
            var whole=added.EnumerateObject().ToDictionary(item=>item.Name,item=>(object?)item.Value.Clone());whole["targetNames"]=new[]{"伪造"};var saved=Success(await Post("/update",whole));Assert.Equal(new[]{"乙1","最新名称"},Names(saved));
            var cleared=Success(await Post("/update",new{id=record,targetIds=(object?)null}));Assert.Equal(JsonValueKind.Null,cleared.GetProperty("targetIds").ValueKind);var empty=Success(await Post("/update",new{id=record,targetIds=Array.Empty<string>()}));Assert.Empty(empty.GetProperty("targetIds").EnumerateArray());
            Success(await Post("/update",new{id=record,targetIds=new[]{Target,Target+1}}));Success(await Post("/update",new{id=record,targetIds=selected}));before=await Hash();
            foreach(var invalid in new object[]{new[]{Target.ToString(),Target.ToString()},new[]{"0"+Target},new[]{"9223372036854775808"},"[","{}",Enumerable.Range(1,5001).Select(id=>id.ToString()).ToArray()}){Assert.Equal(500,(await Post("/update",new{id=record,targetIds=invalid})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());}
            foreach(var invalid in new[]{Target+2,Target+3,Target+4,Target+999}){Assert.Equal(500,(await Post("/update",new{id=record,targetIds=new[]{Target.ToString(),invalid.ToString()}})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());}
            Assert.Equal(500,(await Post("/update",new{id=record,parentId=(Parent+1).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());observer.Fail=true;Assert.Equal(500,(await Post("/update",new{id=record,targetIds=Array.Empty<string>()})).GetProperty("code").GetInt32());observer.Fail=false;Assert.Equal(before,await Hash());
            authorization.Denied.UnionWith(["create","update","list"]);
            var importValue=nonRedundant?"[\"最新名称\",\"乙1\"]":JsonSerializer.Serialize(selected);
            var importedResponse=await Post("/importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"targetIds",importValue},{"name","导入记录"},{"parentId",Parent.ToString()}}}});Success(importedResponse);var imported=(await rows.ReadAsync(prefix+"_source")).Single(row=>Equals(row["name"],"导入记录"));ownIds.Add(SqlRowStore.Id(imported));
            before=await Hash();Assert.Equal(500,(await Post("/importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"name","整批首行"},{"parentId",Parent.ToString()},{"targetIds",importValue}},new Dictionary<string,object?>{{"name","整批尾行"},{"parentId",Parent.ToString()},{"targetIds","[\"不存在\"]"}}}})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());
            authorization.Denied.Clear();var copy=Success(await Post("/copy/"+record,new{}));ownIds.Add(long.Parse(copy.GetProperty("id").GetString()!));
            var ticket=Success(await Post("/initExport",new PageRequest{SelectedIds=[record]}));using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket.GetString()!)))
            {Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var workbook=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);using var cell=JsonDocument.Parse(sheet.Row(2).Cell(headers["targetIds"]).GetString());Assert.Equal(nonRedundant?new[]{"乙1","最新名称"}:selected,cell.RootElement.EnumerateArray().Select(item=>item.GetString()));}
            if(nonRedundant)
            {
                // 目标移组后，旧范围内同名项不能让导出／再导入静默换ID。
                await rows.UpdateAsync(prefix+"_target",Target+2,new Dictionary<string,object?>{{"name","最新名称"},{"parent_id",Parent}});Assert.Equal(500,(await Post("/initExport",new PageRequest{SelectedIds=[record]})).GetProperty("code").GetInt32());
                before=await Hash();Assert.Equal(500,(await Post("/importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"name","重名失败"},{"parentId",Parent.ToString()},{"targetIds","[\"最新名称\"]"}}}})).GetProperty("code").GetInt32());Assert.Equal(before,await Hash());
                await rows.UpdateAsync(prefix+"_target",Target,new Dictionary<string,object?>{{"parent_id",Parent+1}});Assert.Equal(500,(await Post("/initExport",new PageRequest{SelectedIds=[record]})).GetProperty("code").GetInt32());
            }
            // 原下拉按数组成员精确匹配：1不能命中11，历史数值成员也输出字符串ID。
            foreach(var shortId in new long[]{1,11}){await rows.InsertAsync(prefix+"_target",new Dictionary<string,object?>{{"id",shortId},{"name","短ID"+shortId},{"parent_id",Parent+1},{"yz_project_id",Project}});targetIds.Add(shortId);}
            var shortRow=Success(await Post("/add",new{name="短ID行",parentId=(Parent+1).ToString(),targetIds=new[]{"11"}}));var shortKey=long.Parse(shortRow.GetProperty("id").GetString()!);ownIds.Add(shortKey);Assert.Equal(1,await rows.UpdateAsync(prefix+"_source",shortKey,new Dictionary<string,object?>{{"target_ids","[11]"}}));
            var shortStored=(await rows.ReadAsync(prefix+"_source",new Dictionary<string,object?>{{"id",shortKey}})).Single();Assert.Equal("[11]",shortStored["target_ids"]);
            var shortPage=await Post("/page",new PageRequest{Filters=new(){{"targetIds",JsonSerializer.SerializeToElement("11")}}});
            if(nonRedundant){var rawPlan=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.Compile(catalog,"2",["id"],["target_ids"]);var direct=await new Metarill.Data.Queries.MySqlNonRedundantReader(session).ReadPageAsync(rawPlan,[new("target_ids",Metarill.Data.Queries.FixedReadComparison.JsonArrayHasId,"11")],[],1,10,projectId:Project.ToString());Assert.Equal(1,direct.Total);}
            Assert.Equal("0",Success(await Post("/page",new PageRequest{Filters=new(){{"targetIds",JsonSerializer.SerializeToElement("1")}}})).GetProperty("total").GetString());Assert.Equal("1",Success(shortPage).GetProperty("total").GetString());
            if(!automatic)Assert.Equal("1",Success(await Post("/page",new PageRequest{Filters=new(){{"targetNames",JsonSerializer.SerializeToElement("短ID11")}}})).GetProperty("total").GetString());
            // 5000项全选及5001项溢出均走真实原候选，批量只增加本任务随机目标表中的样本。
            var bulkStart=Target+1000;await using(var bulk=new MySqlConnection(options.ConnectionString))
            {await bulk.OpenAsync();await using var command=bulk.CreateCommand();command.CommandText="INSERT INTO "+session.Provider.QuoteIdentifier(prefix+"_target")+" (id,name,parent_id,yz_project_id) SELECT @start+positions.n,CONCAT('全选',positions.n),@parent,@project FROM JSON_TABLE(@items,'$[*]' COLUMNS(n INT PATH '$')) positions";command.Parameters.AddWithValue("start",bulkStart);command.Parameters.AddWithValue("parent",Parent);command.Parameters.AddWithValue("project",Project);command.Parameters.AddWithValue("items",JsonSerializer.Serialize(Enumerable.Range(0,4998)));Assert.Equal(4998,await command.ExecuteNonQueryAsync());}
            targetIds.UnionWith(Enumerable.Range(0,4998).Select(index=>bulkStart+index));var full=Success(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()}}));Assert.Equal(5000,full.GetArrayLength());
            var fullIds=full.EnumerateArray().Select(item=>item.GetProperty("id").GetString()!).ToArray();var fullRow=Success(await Post("/add",new{name="完整全选",parentId=Parent.ToString(),targetIds=fullIds}));ownIds.Add(long.Parse(fullRow.GetProperty("id").GetString()!));Assert.Equal(5000,fullRow.GetProperty("targetIds").GetArrayLength());
            var overflowId=bulkStart+4998;await rows.InsertAsync(prefix+"_target",new Dictionary<string,object?>{{"id",overflowId},{"name","全选溢出"},{"parent_id",Parent},{"yz_project_id",Project}});targetIds.Add(overflowId);Assert.Equal(500,(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()}})).GetProperty("code").GetInt32());Assert.Single(Success(await Post("/findNameIdTarget25",new{formParameters=new{parentId=Parent.ToString()},id=new[]{bulkStart.ToString()}})).EnumerateArray());
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);await Receipt("verified",false);
            async Task<JsonElement> Post(string route,object body){using var response=await client.PostAsJsonAsync(endpoint+route,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Hash()=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes((await rows.ReadIncludingDeletedAsync(prefix+"_source")).OrderBy(row=>SqlRowStore.Id(row)).ToArray())));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.txt"),error.ToString());throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(catalog);
                if(actual.Tables.Count>0)
                {
                    Assert.NotNull(fingerprint);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(actual));Assert.Equal(ownIds.Order(),(await rows.ReadIncludingDeletedAsync(prefix+"_source")).Select(row=>SqlRowStore.Id(row)).Order());Assert.Equal(new[]{Parent,Parent+1},(await rows.ReadIncludingDeletedAsync(prefix+"_parent")).Select(row=>SqlRowStore.Id(row)).Order());Assert.Equal(targetIds.Order(),(await rows.ReadIncludingDeletedAsync(prefix+"_target")).Select(row=>SqlRowStore.Id(row)).Order());
                    await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in tables){Assert.StartsWith(prefix+"_",name);await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}
                }
                await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();foreach(var name in tables){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";command.Parameters.AddWithValue("name",name);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,prefix,tables,ownIds,targetIds,fingerprint,instanceVerified=true,nonRedundant,automatic,sourceParent,joinedCache,cleaned,fixedTestAuthorization=true,browserExecuted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    private static string?[] Names(JsonElement row)=>row.GetProperty("targetNames").ValueKind==JsonValueKind.Null?[]:row.GetProperty("targetNames").EnumerateArray().Select(item=>item.ValueKind==JsonValueKind.Null?null:item.GetString()).ToArray();
    private static JsonElement Success(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.GetRawText());return value.GetProperty("data");}
    /// <summary>仅固定测试动作，目标页一律拒绝以证明候选不借目标独立读取权限。</summary>
    private sealed class Authorization:IRuntimeAuthorization
    {
        internal HashSet<string> Denied{get;}=new(StringComparer.Ordinal);
        public RuntimeActor RequireActor()=>new(877,"数据库复选实库",false,Project.ToString(CultureInfo.InvariantCulture),"1");
        public void RequirePagePermission(string pageId,string operation){if(pageId=="301"||pageId=="201"&&Denied.Contains(operation))throw new UnauthorizedAccessException("测试未授权此页面动作。");}
    }
    private sealed class FailureObserver:IRecordMutationObserver
    {internal bool Fail{get;set;}public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token){if(Fail)throw new InvalidOperationException("测试尾部故障。");return Task.CompletedTask;}}
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
