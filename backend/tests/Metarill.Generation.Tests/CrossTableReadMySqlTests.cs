using System.Collections.Concurrent;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原跨表请求运行真实生成HTTP与MySQL，四表随机样本不代表完整控件或浏览器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableReadMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>Server-200实例pin和独占库同时满足才创建样本，逐次保留失败与清理证据。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_cross_table_options_and_paths_enforce_each_level_and_one_snapshot()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");}
        foreach(var type in new[]{"string","int64"})await Exercise(options,type);
    }
    /// <summary>同型父键的两轮实库覆盖逐层权限、坏祖先、请求拒绝、固定投影及并发快照。</summary>
    private static async Task Exercise(DatabaseOptions options,string type)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_ccr_"+nonce[..12];var catalog=CrossTableReadTests.Catalog(prefix,type,type);
        // 原跨表完整表单包含各级辅助字段，读阶段也不能漏掉这些尚未接线的写入口。
        foreach(var(id,column,link)in new[]{("26","country_id","3"),("27","country_name",""),("28","area_id","4"),("29","area_name",""),("30","place_name","")}){catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="2",ColumnName=column,LogicalType="string",LinkConfigTableId=link});catalog.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id});}
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");sourcePage.IsNew=1;sourcePage.IsSave=1;sourcePage.IsCopy=1;
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cross-table-read/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-table-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(4,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);var suffixes=new[]{"country","area","place"};
            // 种子全部来自当前随机前缀，父键只以绑定参数写入。
            async Task Seed(int level,long id,string? name,long? parent=null,long project=Project,int status=1,DateTime? deleted=null)
            {
                var parentColumn=level>0?",`"+prefix+"_"+suffixes[level-1]+"_id`":"";var parentValue=level>0?",@parent":"";await Execute(session,"INSERT INTO `"+prefix+"_"+suffixes[level]+"` (id,name,yz_project_id,status,delete_no,secret"+parentColumn+") VALUES (@id,@name,@project,@status,@deleted,@secret"+parentValue+")",("id",id),("name",name),("project",project),("status",status),("deleted",deleted),("secret","不应进入响应"),("parent",type=="string"?(object?)parent?.ToString(CultureInfo.InvariantCulture):parent));
            }
            await Seed(0,First,"国甲");await Seed(0,First+10,"国乙");await Seed(0,First+20,"外项目",project:Project+1);await Seed(0,First+30,"删祖先",deleted:new DateTime(2026,1,1));await Seed(0,First+40,"隐藏祖先",status:0);
            await Seed(1,First+1,"区甲",First);await Seed(1,First+11,"区乙",First+10);await Seed(1,First+21,"外父",First+20);await Seed(1,First+31,"删父",First+30);await Seed(1,First+41,"隐藏父",First+40);await Seed(1,First+51,"隐藏中层",First,status:0);await Seed(1,First+61,"已删中层",First,deleted:new DateTime(2026,1,1));await Seed(1,First+71,"断链",First+9999);
            await Seed(2,First+2,"城甲",First+1);await Seed(2,First+12,"城乙",First+11);foreach(var offset in new[]{20,30,40,50,60,70})await Seed(2,First+offset+2,"坏链",First+offset+1);await Seed(2,First+82,"缺父");await Seed(2,First+92,"外叶",First+1,project:Project+1);await Seed(2,First+102,"删叶",First+1,deleted:new DateTime(2026,1,1));await Seed(2,First+112,"隐藏叶",First+1,status:0);
            await Seed(2,First+122,null,First+11);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossProbe");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.CrossProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+prefix+"_source/201/";
            var commands=new ConcurrentQueue<(string Sql,int Parameters)>();session.Orm.Aop.CommandBefore+=(_,args)=>{if(suffixes.Any(suffix=>args.Command.CommandText.Contains(prefix+"_"+suffix,StringComparison.Ordinal)))commands.Enqueue((args.Command.CommandText,args.Command.Parameters.Count));};
            // 两个原POST正文不增加项目、表名、目标页或来源父参数。
            async Task<JsonElement> Options(string key,object? selected)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+"cascadeTreeOptions25",new{levelKey=key,selectedPath=selected})).Content.ReadAsStringAsync());
            async Task<JsonElement> ReadPath(object? leaf)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+"cascadeTreePath25",new{leafId=leaf})).Content.ReadAsStringAsync());
            var roots=Data(await Options("country",new{})).EnumerateArray().ToArray();Assert.Equal(2,roots.Length);Assert.Equal(new[]{"id","name","text","value"},roots[0].EnumerateObject().Select(field=>field.Name).Order().ToArray());Assert.Equal(First.ToString(),roots.Single(row=>row.GetProperty("name").GetString()=="国甲").GetProperty("id").GetString());
            Assert.Equal(2,Data(await Options("country",null)).GetArrayLength());using(var response=await client.PostAsJsonAsync(endpoint+"cascadeTreeOptions25",new{levelKey="country"}))Assert.Equal(2,Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync())).GetArrayLength());
            var mids=Data(await Options("地区-二",new{countryId=First.ToString(),areaId="",placeId=""}));Assert.Equal((First+1).ToString(),Assert.Single(mids.EnumerateArray()).GetProperty("id").GetString());var leaves=Data(await Options("place",new{countryId=First.ToString(),areaId=(First+1).ToString(),placeId=(First+12).ToString()}));Assert.Equal((First+2).ToString(),Assert.Single(leaves.EnumerateArray()).GetProperty("id").GetString());Assert.Single(Data(await Options("place",new{areaId=(First+1).ToString()})).EnumerateArray());Assert.Empty(Data(await Options("place",new{})).EnumerateArray());
            var path=Data(await ReadPath((First+2).ToString()));Assert.Equal("国甲/区甲/城甲",path.GetProperty("displayText").GetString());Assert.Equal(new[]{"country","地区-二","place"},path.GetProperty("path").EnumerateArray().Select(row=>row.GetProperty("levelKey").GetString()));Assert.Equal(new[]{"id","levelKey","name"},path.GetProperty("path")[0].EnumerateObject().Select(field=>field.Name).Order().ToArray());Assert.Empty(Data(await ReadPath(null)).GetProperty("path").EnumerateArray());
            var emptyName=Data(await Options("place",new{countryId=(First+10).ToString(),areaId=(First+11).ToString()})).EnumerateArray().Single(row=>row.GetProperty("id").GetString()==(First+122).ToString());Assert.Equal("",emptyName.GetProperty("name").GetString());Assert.Equal("",emptyName.GetProperty("text").GetString());var emptyNamePath=Data(await ReadPath((First+122).ToString()));Assert.Equal("",emptyNamePath.GetProperty("path")[2].GetProperty("name").GetString());Assert.Equal("国乙/区乙",emptyNamePath.GetProperty("displayText").GetString());
            foreach(var offset in new[]{20,30,40,50,60,70,80,90,100,110,9990})Assert.Equal(500,(await ReadPath((First+offset+2).ToString())).GetProperty("code").GetInt32());
            Assert.Equal(500,(await Options("place",new{countryId=(First+10).ToString(),areaId=(First+1).ToString()})).GetProperty("code").GetInt32());Assert.Equal(500,(await Options("地区-二",new{countryId=(First+20).ToString()})).GetProperty("code").GetInt32());
            foreach(var denied in new[]{"201","301","401","501"}){client.DefaultRequestHeaders.Add("denyPage",denied);Assert.Equal(500,(await ReadPath((First+2).ToString())).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("denyPage");}
            client.DefaultRequestHeaders.Add("denyPage","501");Assert.Equal(500,(await Options("place",new{})).GetProperty("code").GetInt32());Assert.Equal(500,(await ReadPath(null)).GetProperty("code").GetInt32());Assert.Equal(2,Data(await Options("country",new{})).GetArrayLength());client.DefaultRequestHeaders.Remove("denyPage");
            Assert.All(commands,command=>{Assert.True(command.Parameters>0);Assert.DoesNotContain("secret",command.Sql,StringComparison.OrdinalIgnoreCase);});commands.Clear();Assert.Equal(500,(await Options("unknown",new{})).GetProperty("code").GetInt32());Assert.Equal(500,(await Options("country",new{sql="1=1"})).GetProperty("code").GetInt32());Assert.Equal(500,(await Options("place",JsonSerializer.Deserialize<JsonElement>("{\"areaId\":1,\"areaId\":2}"))).GetProperty("code").GetInt32());Assert.Empty(commands);
            using(var response=await client.PostAsJsonAsync(endpoint+"cascadeTreePath25",new{leafId=First.ToString(),projectId="other"}))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            // 首次叶读取后独立迁移中层，当前请求必须仍看到旧祖先，下一请求再看到新祖先。
            var move=1;session.Orm.Aop.CommandAfter+=(_,args)=>{if(args.Command.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)&&args.Command.CommandText.Contains(prefix+"_place",StringComparison.Ordinal)&&Interlocked.CompareExchange(ref move,0,1)==1){using var independent=new MySqlConnection(options.ConnectionString);independent.Open();using var update=independent.CreateCommand();update.CommandText="UPDATE `"+prefix+"_area` SET `"+prefix+"_country_id`=@parent WHERE id=@id";update.Parameters.AddWithValue("parent",type=="string"?(object)(First+10).ToString(CultureInfo.InvariantCulture):First+10);update.Parameters.AddWithValue("id",First+1);Assert.Equal(1,update.ExecuteNonQuery());}};
            Assert.Equal("国甲/区甲/城甲",Data(await ReadPath((First+2).ToString())).GetProperty("displayText").GetString());Assert.Equal(0,move);Assert.Equal("国乙/区甲/城甲",Data(await ReadPath((First+2).ToString())).GetProperty("displayText").GetString());
            // 原跨表根层是完整数组，5500新增节点用参数化批次准备，不能沿用自关联500项上限。
            foreach(var batch in Enumerable.Range(0,5500).Chunk(250)){var parameters=new List<(string Name,object? Value)>{("name","宽"),("project",Project)};var values=new List<string>();foreach(var index in batch){values.Add($"(@id_{index},@name,@project,1)");parameters.Add(("id_"+index,First+1000+index));}await Execute(session,"INSERT INTO `"+prefix+"_country` (id,name,yz_project_id,status) VALUES "+string.Join(",",values),parameters.ToArray());}
            var wide=Data(await Options("country",new{})).EnumerateArray().ToArray();Assert.Equal(5502,wide.Length);Assert.Contains(wide,row=>row.GetProperty("id").GetString()==(First+6499).ToString());
            verified=true;await File.WriteAllTextAsync(System.IO.Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,verified,realGeneratedHttp=true,realMySql=true,originalProtocol=true,perLevelScopesAndPermissions=true,rrMigrationSnapshot=true,minimalProjection=true,wideCandidates=5502,fullLevelArray=true,nullNameIsOriginalEmptyString=true,browser=false,writeSupport=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(System.IO.Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,structureLockRejected=error is SchemaApplyException schemaError&&schemaError.InnerException?.Message=="其他结构任务正在执行，未取得数据库结构锁。",failedTestPreserved=true}));throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有四表结构漂移，保留现场。");if(existing.Tables.Count>0)foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(System.IO.Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>固定真实测试身份，客户端头只能撤销指定页权限。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>请求不提供项目与后台身份。</summary>
        public RuntimeActor RequireActor()=>new(944,"跨表级联读取验收",false,Project.ToString(),"1");
        /// <summary>逐级撤权，空候选和空路径也不得绕过授权。</summary>
        public void RequirePagePermission(string pageId,string operation){if(accessor.HttpContext!.Request.Headers["denyPage"]==pageId)throw new UnauthorizedAccessException("固定测试撤权。");}
    }
    /// <summary>失败正文不能误当有效业务响应。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>随机样本仅使用固定SQL与参数，不执行外部请求SQL。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
