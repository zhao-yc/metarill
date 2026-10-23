using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
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

/// <summary>原复选的派生名称从真实生成HTTP与MySQL验证，随机样本仅在Server-200隔离库。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class DerivedCheckboxNamesMySqlTests
{
    private const long Project=9007199254740993,Parent=9007199254742001,First=9007199254750000;
    /// <summary>显式与自动名称分别验原候选／全选、当前写校验、名称导入及原票据XLSX。</summary>
    [StandaloneExportMySqlFact]
    public async Task Derived_checkbox_names_keep_original_crud_excel_and_current_read()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Server!="deve-server.yinzon.com"||cs.Database!=options.ExpectedDatabase||cs.Port!=3306)throw new InvalidOperationException("派生复选验收只允许Server-200隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未创建样本。");}
        foreach(var automatic in new[]{false,true})await Exercise(options,automatic);
    }
    /// <summary>结构来自元数据，记录意图和清理回执；不触碰其它批次表。</summary>
    private static async Task Exercise(DatabaseOptions options,bool automatic)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_dcb_"+nonce[..12];var catalog=DerivedCheckboxNamesTests.Catalog(prefix,automatic);catalog.ConfigColumns.Single(column=>column.Id=="25").DefaultValue=JsonSerializer.Serialize(new[]{(First+5002).ToString()});
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/derived-checkbox-names/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox-mysql");var directory=Path.Combine(boundary,nonce);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,catalog);WebApplication? app=null;var created=false;var verified=false;var stage="构建生成服务";
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");await Receipt("intent",new{nonce,automatic,names,database=options.ExpectedDatabase});
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedCheckboxHttp");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            stage="显式结构及种子";Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(4,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await Insert(session,prefix+"_parent",[Row("id",Parent,"yz_project_id",Project,"delete_no",null,"name","父甲"),Row("id",Parent+1,"yz_project_id",Project,"delete_no",null,"name","父乙")]);
            var companies=new List<Dictionary<string,object?>>();var targets=new List<Dictionary<string,object?>>();for(var index=0;index<5005;index++){companies.Add(Row("id",First+20000+index,"yz_project_id",Project,"delete_no",null,"name",index==0?"A_%'甲":index==1?"B乙":"X"+index.ToString("D5",CultureInfo.InvariantCulture)));targets.Add(Row("id",First+index,"yz_project_id",index==5002?Project+1:Project,"delete_no",index==5003?new DateTime(2026,1,1):null,"parent_id",index==5004?Parent+1:Parent,"company_id",First+20000+index,"segment",index==5001?2:1,"status",index==5000?0:1));}await Insert(session,prefix+"_company",companies);await Insert(session,prefix+"_target",targets);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var authorization=new Authorization();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=100});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(956));builder.Services.AddSingleton<IRuntimeAuthorization>(authorization);assembly.GetType("Example.DerivedCheckboxHttp.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or JsonException or PageImportException or NotSupportedException){var diagnostic=error.ToString().Replace(options.ConnectionString,"[脱敏]",StringComparison.Ordinal);var password=new MySqlConnectionStringBuilder(options.ConnectionString).Password;if(password.Length>0)diagnostic=diagnostic.Replace(password,"[脱敏]",StringComparison.Ordinal);await Receipt("http-error-"+Guid.NewGuid().ToString("N"),new{diagnostic});context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(60)};
            var endpoint="/api/admin/auto/"+prefix+"_source/201/";async Task<JsonElement> Post(string path,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(path,body)).Content.ReadAsStringAsync());Task<JsonElement> Action(string action,object body)=>Post(endpoint+action+"?parentId="+Parent,body);Task<string> State()=>SourceState();async Task<string> SourceState()=>JsonSerializer.Serialize((await store.ReadIncludingDeletedAsync(prefix+"_source")).OrderBy(row=>SqlRowStore.Id(row)));
            async Task<JsonElement> Options(object? selected=null,int segment=1,long parent=Parent){var body=Row("parentId",parent.ToString(),"formParameters",new{segment,current_yz_project_id=Project+1});if(selected is not null)body.Add("id",selected);return await Post(endpoint+"findNameIdTarget25",body);}
            stage="原候选和5000全选";var all=Data(await Options());Assert.Equal(5000,all.GetArrayLength());Assert.Equal("X04999",all[0].GetProperty("name").GetString());Assert.Equal("A_%'甲",all[4999].GetProperty("text").GetString());Assert.All(all.EnumerateArray(),item=>Assert.Equal(JsonValueKind.String,item.GetProperty("id").ValueKind));
            Assert.Single(Data(await Options(new[]{First.ToString()})).EnumerateArray());Assert.Empty(Data(await Options(Array.Empty<string>())).EnumerateArray());Assert.Single(Data(await Options(null,2)).EnumerateArray());Assert.Single(Data(await Options(null,1,Parent+1)).EnumerateArray());Assert.Equal(500,(await Options(null,1,Parent+999)).GetProperty("code").GetInt32());
            authorization.Denied.UnionWith(["create","update","list"]);Assert.Equal(500,(await Options()).GetProperty("code").GetInt32());authorization.Denied.Remove("create");Assert.Equal(5000,Data(await Options()).GetArrayLength());authorization.Denied.Clear();
            stage="CRUD与只读名称";var selected=new[]{First.ToString(),(First+1).ToString()};var added=Data(await Action("add",new{name="复选来源",segment=1,targetIds=selected}));var id=added.GetProperty("id").GetString()!;Assert.Equal(new[]{"A_%'甲","B乙"},Names(added));
            var echo=added.EnumerateObject().ToDictionary(item=>item.Name,item=>(object?)item.Value.Clone());echo["targetNames"]=new[]{"伪造"};Assert.Equal(new[]{"A_%'甲","B乙"},Names(Data(await Action("update",echo))));var before=await State();
            foreach(var bad in new[]{First+5000,First+5001,First+5002,First+5003,First+5004}){Assert.Equal(500,(await Action("update",new{id,targetIds=new[]{bad.ToString()}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            Assert.Equal(500,(await Action("update",new{id,segment=2})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Post("/api/admin/auto/"+prefix+"_source/202/add?parentId="+Parent,new{name="复选隐藏默认",segment=1})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var companyEndpoint="/api/admin/auto/"+prefix+"_company/401/update";Data(await Post(companyEndpoint,new{id=(First+20000).ToString(),name="Z新名称"}));Assert.Equal(before,await State());var detail=Data(JsonSerializer.Deserialize<JsonElement>(await(await client.GetAsync(endpoint+"detail/"+id+"?parentId="+Parent)).Content.ReadAsStringAsync()));Assert.Equal(new[]{"Z新名称","B乙"},Names(detail));
            if(!automatic)Assert.Equal("1",Data(await Post(endpoint+"page",new PageRequest{ParentId=Parent.ToString(),Filters=new(){{"targetNames",JsonSerializer.SerializeToElement("Z新名称")}}})).GetProperty("total").GetString());
            stage="当前名称导入与原票据";authorization.Denied.UnionWith(["create","update","list"]);Data(await Action("importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{ImportRow("复选名称导入","Z新名称","B乙")}}));before=await State();Assert.Equal(500,(await Action("importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{ImportRow("复选整批首行","Z新名称"),ImportRow("复选整批尾行","不存在")}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());authorization.Denied.Clear();
            var ticket=Data(await Post(endpoint+"initExport",new PageRequest{ParentId=Parent.ToString(),SelectedIds=[id]})).GetString()!;using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket))){Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var book=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.Equal(new[]{"Z新名称","B乙"},JsonSerializer.Deserialize<string[]>(sheet.Row(2).Cell(headers["targetIds"]).GetString()));}
            // 旧RR快照先看旧名称；独立生成HTTP改名后，生成导入必须以当前联接名称解析。
            using(var services=app.Services.CreateScope())
            {var serviceType=assembly.GetType("Example.DerivedCheckboxHttp.Generated.Services."+GenerationModel.Pascal(prefix+"_source")+"Service",true)!;var service=services.ServiceProvider.GetRequiredService(serviceType);await session.InTransactionAsync(async()=>{Assert.Equal("Z新名称",SqlRowStore.Text(Assert.Single(await store.ReadAsync(prefix+"_company",Row("id",First+20000))),"name"));Data(await Post(companyEndpoint,new{id=(First+20000).ToString(),name="RR当前名称"}));var requestType=assembly.GetType("Example.DerivedCheckboxHttp.Generated.DTOs."+GenerationModel.Pascal(prefix+"_source")+"Page201ImportRequest",true)!;var request=Activator.CreateInstance(requestType)!;requestType.GetProperty("ParentId")!.SetValue(request,Parent.ToString());requestType.GetProperty("RequestJson")!.SetValue(request,JsonSerializer.SerializeToElement(new[]{ImportRow("复选旧RR导入","RR当前名称")}));await (Task<string>)serviceType.GetMethod("Import201Async")!.Invoke(service,[request,authorization.RequireActor(),CancellationToken.None])!;return true;});}
            stage="名称歧义与范围移动";await store.UpdateAsync(prefix+"_target",First+2,Row("company_id",First+20000));before=await State();Assert.Equal(500,(await Action("importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{ImportRow("复选重名拒绝","RR当前名称")}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Post(endpoint+"initExport",new PageRequest{ParentId=Parent.ToString(),SelectedIds=[id]})).GetProperty("code").GetInt32());await store.UpdateAsync(prefix+"_target",First+2,Row("company_id",First+20002));
            await store.UpdateAsync(prefix+"_company",First+20000,Row("yz_project_id",Project+1));Assert.Equal(JsonValueKind.Null,Data(await Options(new[]{First.ToString()}))[0].GetProperty("name").ValueKind);Assert.Equal(500,(await Post(endpoint+"initExport",new PageRequest{ParentId=Parent.ToString(),SelectedIds=[id]})).GetProperty("code").GetInt32());await store.UpdateAsync(prefix+"_company",First+20000,Row("yz_project_id",Project));
            stage="完整全选及超限拒绝";var full=Data(await Action("add",new{name="复选完整全选",segment=1,targetIds=all.EnumerateArray().Select(item=>item.GetProperty("id").GetString()).ToArray()}));Assert.Equal(all.EnumerateArray().Select(item=>item.GetProperty("name").GetString()=="A_%'甲"?"RR当前名称":item.GetProperty("name").GetString()),Names(full));await store.UpdateAsync(prefix+"_target",First+5000,Row("status",1));Assert.Equal(500,(await Options()).GetProperty("code").GetInt32());Assert.Single(Data(await Options(new[]{First.ToString()})).EnumerateArray());
            var cleared=Data(await Action("update",new{id,targetIds=(object?)null}));Assert.Equal(JsonValueKind.Null,cleared.GetProperty("targetIds").ValueKind);Assert.Empty(Names(cleared));Assert.Empty(Names(Data(await Action("update",new{id,targetIds=Array.Empty<string>()}))));
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);verified=true;await Receipt("verified",new{nonce,automatic,verified,realMySql=true,generatedHttp=true,originalOptions=true,actual5000Selection=true,currentRrRead=true,wholeImportRollback=true,actualXlsxDownload=true,browser=false,formalWebAuthentication=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,automatic,stage,type=error.GetType().FullName,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){using var cleanup=new DatabaseSession(options);if((await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有随机四表结构漂移，保留现场。");Assert.All(names,name=>Assert.StartsWith(prefix+"_",name));Assert.All(await store.ReadIncludingDeletedAsync(prefix+"_source"),row=>Assert.StartsWith("复选",SqlRowStore.Text(row,"name")));foreach(var name in names){await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await Receipt("cleanup",new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0});}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>原复选Excel使用名称JSON数组，来源父由原请求建立。</summary>
    private static Dictionary<string,object?> ImportRow(string name,params string[] names)=>Row("name",name,"segment",1,"targetIds",JsonSerializer.Serialize(names));
    /// <summary>名称只读数组按原VO返回，null与空数组区分。</summary>
    private static string?[] Names(JsonElement row)=>row.GetProperty("targetNames").ValueKind==JsonValueKind.Null?[]:row.GetProperty("targetNames").EnumerateArray().Select(item=>item.ValueKind==JsonValueKind.Null?null:item.GetString()).ToArray();
    /// <summary>仅固定测试身份与来源减权，目标列表拒绝证明复选不借目标页面权限。</summary>
    private sealed class Authorization:IRuntimeAuthorization
    {
        internal HashSet<string> Denied{get;}=new(StringComparer.Ordinal);
        public RuntimeActor RequireActor()=>new(956,"派生复选验收",false,Project.ToString(),"5");
        public void RequirePagePermission(string page,string operation){if(page=="301"||page=="201"&&Denied.Contains(operation))throw new UnauthorizedAccessException("固定测试减权。");}
    }
    /// <summary>种子只写已创建的本轮表，值全部参数化，批次不超过200行。</summary>
    private static async Task Insert(DatabaseSession session,string table,IReadOnlyList<Dictionary<string,object?>> rows)
    {
        foreach(var batch in rows.Chunk(200)){var columns=batch[0].Keys.ToArray();await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();var values=new List<string>();for(var index=0;index<batch.Length;index++){Assert.Equal(columns,batch[index].Keys);var marks=new List<string>();for(var field=0;field<columns.Length;field++){var name="p"+index+"_"+field;marks.Add("@"+name);var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=batch[index][columns[field]]??DBNull.Value;command.Parameters.Add(parameter);}values.Add("("+string.Join(',',marks)+")");}command.CommandText="INSERT INTO "+session.Provider.QuoteIdentifier(table)+"("+string.Join(',',columns.Select(session.Provider.QuoteIdentifier))+") VALUES "+string.Join(',',values);await command.ExecuteNonQueryAsync();}
    }
    /// <summary>只构造测试内固定字段。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>();for(var index=0;index<values.Length;index+=2)result.Add((string)values[index]!,values[index+1]);return result;}
    /// <summary>任何错误响应都不能被当作数据。</summary>
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.GetRawText());return response.GetProperty("data");}
}
