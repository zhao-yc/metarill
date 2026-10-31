using System.Collections.Concurrent;
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

/// <summary>独立随机四表验证原跨表来源筛选，不把专用HTTP下载或固定身份当生产/浏览器验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableFilterMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>先固定Server-200实例及隔离库，再分别验证字符串和整数来源列。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_deepest_filter_page_count_statistics_selected_and_scalar_xlsx_share_source_predicates()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");}
        foreach(var type in new[]{"string","int64"})await Exercise(options,type);
    }
    /// <summary>每种键类型有独立元数据、生成宿主和样本；失败先保存原异常，结构不符时保留现场。</summary>
    private static async Task Exercise(DatabaseOptions options,string type)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_ccf_"+nonce[..12];var catalog=CrossTableFilterTests.Catalog(prefix,type,type);var page=catalog.ConfigPages.Single(page=>page.Id=="201");var scoped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!;scoped.Id="202";scoped.Extra.Remove("type");scoped.WhereSql+=" AND country_id=@parent_id";catalog.ConfigPages.Add(scoped);foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="202"+field.Id;copy.ConfigPageId="202";catalog.ConfigPageColumns.Add(copy);}
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cross-table-filter/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-filter-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var source=prefix+"_source";
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(4,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            object? Key(long? key)=>type=="string"?(object?)key?.ToString(CultureInfo.InvariantCulture):key;
            async Task Target(int level,long id,string name,long? parent=null){var suffixes=new[]{"country","area","place"};var parentName=level>0?",`"+prefix+"_"+suffixes[level-1]+"_id`":"";await Execute(session,"INSERT INTO `"+prefix+"_"+suffixes[level]+"` (id,name,yz_project_id,status"+parentName+") VALUES (@id,@name,@project,1"+(level>0?",@parent":"")+")",("id",id),("name",name),("project",Project),("parent",Key(parent)));}
            await Target(0,First,"国甲");await Target(1,First+1,"区甲",First);await Target(2,First+2,"城甲",First+1);await Target(0,First+10,"国乙");await Target(1,First+11,"区乙",First+10);await Target(2,First+12,"城乙",First+11);
            async Task Source(int index,string name,long country,long? area,long? place,long amount,long project=Project,int status=1,DateTime? deleted=null,string? rawCountry=null)=>await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id,country_id,country_name,area_id,area_name,place_id,place_name,amount,status,delete_no) VALUES (@id,@name,@project,@country,'旧国',@area,'旧区',@place,'旧城',@amount,@status,@deleted)",("id",First+100+index),("name",name),("project",project),("country",rawCountry??Key(country)),("area",Key(area)),("place",Key(place)),("amount",amount),("status",status),("deleted",deleted));
            await Source(1,"叶甲",First,First+1,First+2,10);await Source(2,"仅根甲",First,null,null,20);await Source(3,"仅中甲",First,First+1,null,30);await Source(4,"叶乙",First+10,First+11,First+12,40);await Source(5,"外项目",First,null,null,999,project:Project+1);await Source(6,"条件隐藏",First,null,null,999,status:0);await Source(7,"逻辑删除",First,null,null,999,deleted:new DateTime(2026,1,1));await Source(8,"历史甲",First,First+99,First+999,50);if(type=="string")await Source(9,"前导零甲",First,null,null,60,rawCountry:"0"+First);
            // 来源筛选沿历史已存ID，不因当前目标隐藏/删除/改名而改成目标链查询。
            await Execute(session,"UPDATE `"+prefix+"_country` SET status=0,name='已改名' WHERE id=@id",("id",First));await Execute(session,"DELETE FROM `"+prefix+"_place` WHERE id=@id",("id",First+2));await Execute(session,"UPDATE `"+prefix+"_area` SET delete_no='2026-01-01' WHERE id=@id",("id",First+1));
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossFilterProbe");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=1000});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(946));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.CrossFilterProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet("/test-export/{ticket}",async(string ticket)=>{var content=await exports.OpenDownloadAsync(ticket);return Results.Stream(content.Stream,content.ContentType,content.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.Add("denyTargets","1");var endpoint="/api/admin/auto/"+source+"/201/";
            async Task<JsonElement> Post(string action,object body,string? otherEndpoint=null){using var response=await client.PostAsJsonAsync((otherEndpoint??endpoint)+action,body);var text=await response.Content.ReadAsStringAsync();Assert.False(string.IsNullOrWhiteSpace(text),"原接口返回空正文。");return JsonSerializer.Deserialize<JsonElement>(text);}
            object Filter(object value)=>new{placeId=value};async Task<long> Count(object value)=>long.Parse(Data(await Post("count",new{filters=Filter(value)})).ToString(),CultureInfo.InvariantCulture);async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}
            var before=await State();var commands=new ConcurrentQueue<(string Sql,int Parameters)>();var capture=true;session.Orm.Aop.CommandBefore+=(_,args)=>{if(capture)commands.Enqueue((args.Command.CommandText,args.Command.Parameters.Count));};
            Assert.Equal(type=="string"?6:5,await Count(new{}));Assert.Equal(4,await Count(new{countryId=First.ToString()}));Assert.Equal(4,await Count(new{countryId=First}));Assert.Equal(2,await Count(new{areaId=(First+1).ToString()}));Assert.Equal(1,await Count(new{placeId=(First+2).ToString()}));Assert.Equal(1,await Count(new{countryId=(First+10).ToString(),areaId=(First+11).ToString(),placeId=(First+2).ToString()}));Assert.Equal(1,await Count(new{countryId=new{ignored="非采用祖先"},placeId=(First+2).ToString()}));Assert.Equal(type=="string"?1:4,await Count(new{countryId="0"+First}));Assert.Equal(type=="string"?6:5,await Count(new{countryName="只回显",placeId=(string?)null}));Assert.Equal(type=="string"?6:5,await Count(Array.Empty<string>()));
            var result=await Post("page",new{pageSize=100,filters=Filter(new{countryId=First.ToString()})});var rows=Data(result).GetProperty("records").EnumerateArray().ToArray();Assert.Equal(4,rows.Length);Assert.Equal(4,long.Parse(Data(result).GetProperty("total").ToString()));Assert.Equal(110,decimal.Parse(result.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Equal(new[]{"历史甲","仅中甲","仅根甲","叶甲"}.Order(),rows.Select(row=>row.GetProperty("name").GetString()).Order());
            var selected=Data(await Post("page",new{pageSize=100,selectedIds=new[]{(First+101).ToString(),(First+104).ToString()},filters=Filter(new{countryId=First.ToString()})})).GetProperty("records").EnumerateArray().ToArray();Assert.Equal((First+101).ToString(),Assert.Single(selected).GetProperty("id").GetString());var confirmed=Data(await Post("selection/selected",new{selectedIds=new[]{(First+104).ToString(),(First+101).ToString()}})).EnumerateArray().ToArray();Assert.Equal(new[]{(First+104).ToString(),(First+101).ToString()},confirmed.Select(row=>row.GetProperty("id").GetString()));
            foreach(var invalid in new object[]{new{placeId="1 OR 1=1"},new{countryId=First.ToString(),table="other"},new{countryId=First.ToString(),projectId="2"},new{countryId=First.ToString(),placeId=new{bad="ID"}}})Assert.Equal(500,(await Post("count",new{filters=Filter(invalid)})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("count",new{filters=new{countryId=First.ToString()}})).GetProperty("code").GetInt32());
            var ticket=Data(await Post("initExport",new{filters=Filter(new{countryId=First.ToString()})})).GetString()!;using(var workbook=new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync("/test-export/"+ticket)))){var sheet=workbook.Worksheet(1);Assert.Equal(5,sheet.LastRowUsed()!.RowNumber());var nameColumn=sheet.Row(1).CellsUsed().Single(cell=>cell.GetString()=="名称").Address.ColumnNumber;Assert.Equal(new[]{"历史甲","仅中甲","仅根甲","叶甲"}.Order(),sheet.RowsUsed().Skip(1).Select(row=>row.Cell(nameColumn).GetString()).Order());}
            capture=false;Assert.NotEmpty(commands);Assert.All(commands,command=>{Assert.Contains(source,command.Sql,StringComparison.Ordinal);foreach(var suffix in new[]{"country","area","place"})Assert.DoesNotContain(prefix+"_"+suffix+"`",command.Sql,StringComparison.Ordinal);Assert.True(command.Parameters>0,"来源查询应绑定参数。");});
            var scopedEndpoint=endpoint.Replace("/201/","/202/");Assert.Equal(4,long.Parse(Data(await Post("count",new{parentId=First.ToString(),filters=Filter(new{countryId=First.ToString()})},scopedEndpoint)).ToString()));Assert.Equal(0,long.Parse(Data(await Post("count",new{parentId=First.ToString(),filters=Filter(new{countryId=(First+10).ToString()})},scopedEndpoint)).ToString()));client.DefaultRequestHeaders.Add("denySource","1");Assert.Equal(500,(await Post("count",new{filters=Filter(new{countryId=First.ToString()})})).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("denySource");Assert.Equal(before,await State());
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,verified,realGeneratedHttp=true,realMySql=true,deepestPriority=true,historicalSourceIds=true,rawStringComparison=true,sourceScopes=true,sourceOnlyParameterizedSql=true,scalarXlsxRows=4,crossTablePathExcel=false,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,failedTestPreserved=true}));throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("跨表筛选四表结构漂移，保留现场。");if(existing.Tables.Count>0)foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>宿主固定身份，请求头只能撤销来源或目标动作，不能选择项目。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>身份由宿主固定，不从筛选或请求项目字段取得。</summary>
        public RuntimeActor RequireActor()=>new(946,"跨表筛选验收",false,Project.ToString(),"1");
        /// <summary>目标list全撤后来源筛选仍可用，来源list撤权必须拒绝。</summary>
        public void RequirePagePermission(string page,string operation){var headers=accessor.HttpContext!.Request.Headers;if(headers.ContainsKey("denyTargets")&&page is "301" or "401" or "501"||headers.ContainsKey("denySource")&&page is "201" or "202")throw new UnauthorizedAccessException("固定撤权。");}
    }
    /// <summary>错误响应不能当数据。</summary>
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.ToString());return response.GetProperty("data");}
    /// <summary>测试只对自有随机表使用固定SQL，所有数据值通过参数绑定。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
