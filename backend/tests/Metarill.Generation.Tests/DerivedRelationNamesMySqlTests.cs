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

/// <summary>固定Server-200随机四表的真实生成HTTP；身份明确为测试宿主，不冒充正式Web认证。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class DerivedRelationNamesMySqlTests
{
    private const long First=9007199254742001,Project=54,Group=First+90000;
    /// <summary>名称派生在SQL分页前执行，原接口、表单范围、名称导入及当前读共同验证。</summary>
    [StandaloneExportMySqlFact]
    public async Task Derived_names_share_candidate_import_export_and_current_read_scopes()
    {
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=json.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Server!="deve-server.yinzon.com"||cs.Database!=options.ExpectedDatabase||cs.Port!=3306)throw new InvalidOperationException("派生关系验收只允许Server-200隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));
        await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未创建样本。");}
        foreach(var contextual in new[]{false,true})await Exercise(options,contextual);
    }
    /// <summary>所有表来自元数据；原始失败、精确对象意图和清理回执分别保留。</summary>
    private static async Task Exercise(DatabaseOptions options,bool contextual)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_drname_"+nonce[..12];var catalog=DerivedRelationNamesTests.Catalog(prefix,contextual);var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/derived-relation-names/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-names-mysql");var directory=Path.Combine(boundary,nonce);var source=prefix+"_order";var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);WebApplication? app=null;var created=false;var verified=false;var stage="构建实际生成服务";
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
        await Receipt("intent",new{nonce,contextual,names,database=options.ExpectedDatabase});
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedNamesHttp");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            stage="显式结构与固定种子";Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(4,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await Insert(session,prefix+"_group",[Row("id",Group,"yz_project_id",Project,"delete_no",null,"name","父甲"),Row("id",Group+1,"yz_project_id",Project,"delete_no",null,"name","父乙")]);
            var companies=new List<Dictionary<string,object?>>();var parties=new List<Dictionary<string,object?>>();
            for(var index=0;index<5103;index++){var name=index==0?"100%_业务'甲":index is 2 or 3?"重名公司":"公司"+index.ToString("D5",CultureInfo.InvariantCulture);companies.Add(Row("id",First+20000+index,"yz_project_id",Project,"delete_no",null,"name",name));parties.Add(Party(First+index,First+20000+index));}
            companies.Add(Row("id",First+30000,"yz_project_id",Project+1,"delete_no",null,"name","外项目公司秘密"));
            parties.Add(Party(First+6000,First+20000,status:0));parties.Add(Party(First+6001,First+20000,project:Project+1));parties.Add(Party(First+6002,First+20000,deleted:DateTime.UtcNow));parties.Add(Party(First+6003,First+30000));
            if(contextual){parties.Add(Party(First+6004,First+20001,group:Group+1));parties.Add(Party(First+6005,First+20001,segment:2));}
            await Insert(session,prefix+"_company",companies);await Insert(session,prefix+"_party",parties);
            Dictionary<string,object?> Party(long id,long company,int status=1,long project=Project,long group=Group,int segment=1,DateTime? deleted=null){var row=Row("id",id,"yz_project_id",project,"delete_no",deleted,"name","原客户物理名","notes","说明","amount",12m,"company_id",company,"status",status);if(contextual){row.Add("group_id",group);row.Add("segment",segment);}return row;}
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=100});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(957));builder.Services.AddScoped<IRuntimeAuthorization,ProbeAuthorization>();assembly.GetType("Example.DerivedNamesHttp.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or PageImportException or NotSupportedException){var diagnostic=error.ToString().Replace(options.ConnectionString,"[已脱敏连接配置]",StringComparison.Ordinal);var password=new MySqlConnectionStringBuilder(options.ConnectionString).Password;if(password.Length>0)diagnostic=diagnostic.Replace(password,"[已脱敏]",StringComparison.Ordinal);await Receipt("http-error-"+Guid.NewGuid().ToString("N"),new{type=error.GetType().FullName,diagnostic});context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(45)};
            var endpoint="/api/admin/auto/"+source+"/101/";var candidate=endpoint+"findNameIdCustomer15";var form=contextual?new Dictionary<string,object?>{{"segment",1},{"current_yz_project_id",Project+1}}:null;
            async Task<JsonElement> Post(string path,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(path,body)).Content.ReadAsStringAsync());
            Task<JsonElement> Page(string keyword)=>Post(candidate+"/page",new{parentId=Group.ToString(CultureInfo.InvariantCulture),formParameters=form,keyword,pageNo=1,pageSize=100});
            async Task<string> State()=>JsonSerializer.Serialize((await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));
            stage="SQL派生候选与原名称合同";
            var legacy=await Post(candidate,new{parentId=Group.ToString(CultureInfo.InvariantCulture),formParameters=form});Assert.Equal(500,legacy.GetProperty("code").GetInt32());
            var all=Data(await Page(""));Assert.Equal(JsonValueKind.String,all.GetProperty("total").ValueKind);Assert.Equal(5104L,Total(all));Assert.Equal(100,all.GetProperty("records").GetArrayLength());
            foreach(var literal in new[]{"%","_","'"})Assert.Equal(1,Total(Data(await Page(literal))));Assert.Equal(0,Total(Data(await Page("%' OR 1=1 --"))));Assert.Equal(0,Total(Data(await Page("外项目公司秘密"))));
            var selected=Data(await Post(candidate+"/selected",new{selectedIds=new[]{(First+5102).ToString()},parentId=Group.ToString(),formParameters=form}));Assert.Equal("公司05102",Assert.Single(selected.EnumerateArray()).GetProperty("name").GetString());
            foreach(var invalid in new[]{First+6000,First+6001,First+6002}.Concat(contextual?new[]{First+6004,First+6005}:[]))Assert.Equal(500,(await Post(candidate+"/selected",new{selectedIds=new[]{invalid.ToString()},parentId=Group.ToString(),formParameters=form})).GetProperty("code").GetInt32());
            var hiddenName=Data(await Post(candidate+"/selected",new{selectedIds=new[]{(First+6003).ToString()},parentId=Group.ToString(),formParameters=form}));Assert.Equal(JsonValueKind.Null,Assert.Single(hiddenName.EnumerateArray()).GetProperty("name").ValueKind);
            client.DefaultRequestHeaders.Add("denyTarget","1");try{Assert.Equal(500,(await Page("公司")).GetProperty("code").GetInt32());}finally{client.DefaultRequestHeaders.Remove("denyTarget");}
            client.DefaultRequestHeaders.Add("denySource","1");try{Assert.Equal(500,(await Page("公司")).GetProperty("code").GetInt32());}finally{client.DefaultRequestHeaders.Remove("denySource");}
            var beforeHidden=await State();Assert.Equal(500,(await Post("/api/admin/auto/"+source+"/104/add?parentId="+Group,new{name="隐藏默认越界"})).GetProperty("code").GetInt32());Assert.Equal(beforeHidden,await State());
            stage="原CRUD与名称重算";var added=Data(await Post(endpoint+"add?parentId="+Group,new{name="关系来源",customerId=(First+5102).ToString()}));var id=added.GetProperty("id").GetString()!;Assert.Equal("公司05102",added.GetProperty("customerCompanyName").GetString());
            var echo=added.EnumerateObject().ToDictionary(item=>item.Name,item=>(object?)item.Value.Clone());echo["customerCompanyName"]="伪造名称";Assert.Equal("公司05102",Data(await Post(endpoint+"update?parentId="+Group,echo)).GetProperty("customerCompanyName").GetString());
            if(contextual){var before=await State();Assert.Equal(500,(await Post(endpoint+"update?parentId="+Group,new{id,segment=2})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            var physicalBefore=await State();await PostCompany("最新公司名");Assert.Equal(physicalBefore,await State());var detail=JsonSerializer.Deserialize<JsonElement>(await(await client.GetAsync(endpoint+"detail/"+id+"?parentId="+Group)).Content.ReadAsStringAsync());Assert.Equal("最新公司名",Data(detail).GetProperty("customerCompanyName").GetString());
            var rows=Data(await Post(endpoint+"page",new PageRequest{ParentId=Group.ToString(),Filters=new(){{"customerCompanyName",JsonSerializer.SerializeToElement("最新公司名")}}}));Assert.Single(rows.GetProperty("records").EnumerateArray());
            stage="原导入与整批回滚";
            Dictionary<string,object?> ImportRow(string name,string customer){var row=Row("记录名",name,"客户",customer);if(contextual)row.Add("分组条件",1);return row;}
            async Task<JsonElement> Import(string action,params Dictionary<string,object?>[] requestRows)=>await Post(endpoint+action,new{parentId=Group.ToString(),requestJson=requestRows});
            client.DefaultRequestHeaders.Add("importOnly","1");try{Assert.Equal(200,(await Import("importDatas101",ImportRow("按派生名导入","最新公司名"))).GetProperty("code").GetInt32());}finally{client.DefaultRequestHeaders.Remove("importOnly");}
            var beforeImport=await State();Assert.Equal(500,(await Import("importDatas101",ImportRow("应回滚的第一行","最新公司名"),ImportRow("重名失败第二行","重名公司"))).GetProperty("code").GetInt32());Assert.Equal(beforeImport,await State());
            // 使用同版实际生成服务建立旧RR，再通过独立HTTP改名，验证导入不能读旧名称。
            var serviceType=assembly.GetType("Example.DerivedNamesHttp.Generated.Services."+GenerationModel.Pascal(source)+"Service",true)!;
            var actor=new RuntimeActor(957,"派生关系验收",false,Project.ToString(),"5");using var serviceScope=app.Services.CreateScope();var service=serviceScope.ServiceProvider.GetRequiredService(serviceType);var contextAccessor=serviceScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();contextAccessor.HttpContext=new DefaultHttpContext();
            stage="原导出凭证与实际XLSX";
            var grant=Data(await Post(endpoint+"initExport",new PageRequest{ParentId=Group.ToString()})).GetString()!;
            using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(grant))){Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var stream=new MemoryStream(await download.Content.ReadAsByteArrayAsync());using var book=new XLWorkbook(stream);var sheet=book.Worksheets.Single();var headers=sheet.Row(1).CellsUsed().Select(cell=>cell.GetString()).ToArray();var relationColumn=Array.IndexOf(headers,"客户")+1;Assert.True(relationColumn>0);Assert.All(sheet.RowsUsed().Skip(1),row=>Assert.Equal("最新公司名",row.Cell(relationColumn).GetString()));}

            stage="旧RR快照中的当前名称解析";
            await session.InTransactionAsync(async()=>
            {
                Assert.Equal("最新公司名",SqlRowStore.Text(Assert.Single(await new SqlRowStore(session,catalog).ReadAsync(prefix+"_company",Row("id",First+25102))),"name"));
                await PostCompany("RR新公司名");
                var requestType=assembly.GetType("Example.DerivedNamesHttp.Generated.DTOs."+GenerationModel.Pascal(source)+"Page101ImportRequest",true)!;var request=Activator.CreateInstance(requestType)!;requestType.GetProperty("ParentId")!.SetValue(request,Group.ToString());requestType.GetProperty("RequestJson")!.SetValue(request,JsonSerializer.SerializeToElement(new[]{ImportRow("旧RR下解析新名","RR新公司名")}));
                await (Task<string>)serviceType.GetMethod("Import101Async")!.Invoke(service,[request,actor,CancellationToken.None])!;return true;
            });
            contextAccessor.HttpContext=null;
            Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==source).Columns,column=>column.Name=="customer_company_name");Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==prefix+"_party").Columns,column=>column.Name=="company_name");
            verified=true;await Receipt("verified",new{nonce,contextual,verified,realMySql=true,generatedHttp=true,seededPrimaryCandidates=5103,scopeTotal=Total(all),literalKeyword=true,derivedBeforePagination=true,originalReadOnlyEcho=true,hiddenDefaultValidation=true,sourceAndTargetRevocation=true,actualCurrentRead=true,wholeImportRollback=true,actualXlsxDownload=true,browser=false,formalWebAuthentication=false});
            async Task PostCompany(string name){var result=await Post("/api/admin/auto/"+prefix+"_company/401/update",new{id=(First+25102).ToString(),name});Assert.Equal(200,result.GetProperty("code").GetInt32());}
        }
        catch(Exception error){await Receipt("failure",new{nonce,contextual,stage,type=error.GetType().FullName,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){using var cleanup=new DatabaseSession(options);if((await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有随机四表结构漂移，保留现场。");foreach(var name in names){await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await Receipt("cleanup",new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0});}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>固定测试身份和显式减权，仅用于验证真实生成入口，不作为正式认证。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor context):IRuntimeAuthorization
    {
        /// <summary>项目与后台来自测试宿主，客户端只能减权。</summary>
        public RuntimeActor RequireActor()=>new(957,"派生关系验收",false,Project.ToString(),"5");
        /// <summary>分别撤销来源、目标或普通写入，检查原导入不会借用其它动作。</summary>
        public void RequirePagePermission(string page,string operation){var headers=context.HttpContext!.Request.Headers;if(headers.ContainsKey("denySource")&&page=="101"||headers.ContainsKey("denyTarget")&&page=="201"||headers.ContainsKey("importOnly")&&(operation is "create" or "update"||page=="201"&&operation=="list"))throw new UnauthorizedAccessException("固定测试减权。");}
    }
    /// <summary>种子只写已创建的本轮表，值全部参数化，批次不超过200行。</summary>
    private static async Task Insert(DatabaseSession session,string table,IReadOnlyList<Dictionary<string,object?>> rows)
    {
        foreach(var batch in rows.Chunk(200)){var columns=batch[0].Keys.ToArray();await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();var values=new List<string>();for(var index=0;index<batch.Length;index++){Assert.Equal(columns,batch[index].Keys);var marks=new List<string>();for(var field=0;field<columns.Length;field++){var name="p"+index+"_"+field;marks.Add("@"+name);var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=batch[index][columns[field]]??DBNull.Value;command.Parameters.Add(parameter);}values.Add("("+string.Join(',',marks)+")");}command.CommandText="INSERT INTO "+session.Provider.QuoteIdentifier(table)+"("+string.Join(',',columns.Select(session.Provider.QuoteIdentifier))+") VALUES "+string.Join(',',values);await command.ExecuteNonQueryAsync();}
    }
    /// <summary>只构造测试内固定字段。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>();for(var index=0;index<values.Length;index+=2)result.Add((string)values[index]!,values[index+1]);return result;}
    /// <summary>原宿主把long总数序列化为字符串，不能在验收中误改分页协议。</summary>
    private static long Total(JsonElement page)=>long.Parse(page.GetProperty("total").GetString()!,CultureInfo.InvariantCulture);
    /// <summary>任何错误响应都不能被当作数据。</summary>
    private static JsonElement Data(JsonElement response){Assert.Equal(200,response.GetProperty("code").GetInt32());return response.GetProperty("data");}
}
