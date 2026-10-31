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

/// <summary>跨表原模板与空单元格清空；无效非空路径使整批回滚，不能清空前行后继续。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableExcelClearMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>固定Server-200实例及隔离库，两种来源键各有独立随机样本。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_columns_and_empty_cells_preserve_import_permission_omission_scope_and_rollback()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("只允许Server-200隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不符，未建表。");}foreach(var type in new[]{"string","int64"})await Exercise(options,type);
    }
    /// <summary>真实生成HTTP与当前存储核验，不用普通create/update或目标list权限代替import。</summary>
    private static async Task Exercise(DatabaseOptions options,string type)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_cxc_"+nonce[..12];var catalog=CrossTableExcelImportColumnsTests.Catalog(prefix,type);var scoped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;scoped.Id="202";scoped.Extra.Remove("type");scoped.WhereSql+=" AND country_id=@parent_id";catalog.ConfigPages.Add(scoped);foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="202"+field.Id;copy.ConfigPageId="202";catalog.ConfigPageColumns.Add(copy);}
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cross-table-excel/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-clear");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var source=prefix+"_source";await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(4,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await Execute(session,"INSERT INTO `"+prefix+"_country` (id,name,yz_project_id,status) VALUES (@id,'国甲',@project,1)",("id",First),("project",Project));await Execute(session,"INSERT INTO `"+prefix+"_area` (id,name,yz_project_id,status,`"+prefix+"_country_id`) VALUES (@id,'区甲',@project,1,@parent)",("id",First+1),("project",Project),("parent",First));await Execute(session,"INSERT INTO `"+prefix+"_place` (id,name,yz_project_id,status,`"+prefix+"_area_id`) VALUES (@id,'城甲',@project,1,@parent)",("id",First+2),("project",Project),("parent",First+1));
            object Key(long value)=>type=="string"?value.ToString(CultureInfo.InvariantCulture):value;foreach(var pair in new[]{(Id:First+100,Name:"甲"),(Id:First+101,Name:"乙")})await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id,status,amount,country_id,country_name,area_id,area_name,place_id,place_name) VALUES (@id,@name,@project,1,1,@country,'旧国',@area,'旧区',@place,'旧城')",("id",pair.Id),("name",pair.Name),("project",Project),("country",Key(First)),("area",Key(First+1)),("place",Key(First+2)));
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossExcelClear");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(948));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.CrossExcelClear.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or PageImportException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+source+"/201/importDatas201";
            async Task<JsonElement> Import(object[] rows,string? parent=null,string? other=null)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(other??endpoint,new{requestJson=rows,parentId=parent})).Content.ReadAsStringAsync());async Task<IReadOnlyList<Dictionary<string,object?>>> Rows(){using var independent=new DatabaseSession(options);return await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source);}async Task<string> State()=>JsonSerializer.Serialize((await Rows()).OrderBy(row=>SqlRowStore.Id(row)));
            Assert.Equal(200,(await Import([new Dictionary<string,object?>{{"名称","甲"},{"级联",""}}])).GetProperty("code").GetInt32());var cleared=(await Rows()).Single(row=>Equals(row["name"],"甲"));foreach(var key in new[]{"country_id","country_name","area_id","area_name","place_id","place_name"})Assert.Null(cleared[key]);
            Assert.Equal(200,(await Import([new Dictionary<string,object?>{{"名称","乙"},{"金额",42}}])).GetProperty("code").GetInt32());var preserved=(await Rows()).Single(row=>Equals(row["name"],"乙"));Assert.Equal(First.ToString(),Convert.ToString(preserved["country_id"],CultureInfo.InvariantCulture));Assert.Equal((First+2).ToString(),Convert.ToString(preserved["place_id"],CultureInfo.InvariantCulture));Assert.Equal("旧国",preserved["country_name"]);
            Assert.Equal(200,(await Import([new Dictionary<string,object?>{{"名称","新建空"},{"级联",null}}])).GetProperty("code").GetInt32());Assert.Null((await Rows()).Single(row=>Equals(row["name"],"新建空"))["country_id"]);var before=await State();Assert.Equal(500,(await Import([new Dictionary<string,object?>{{"名称","乙"},{"级联",null}},new Dictionary<string,object?>{{"名称","无效路径"},{"级联","不存在/错误/路径"}}])).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Import([new Dictionary<string,object?>{{"名称","乙"},{"countryId",First.ToString()}}])).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var scopedEndpoint=endpoint.Replace("/201/importDatas201","/202/importDatas202");Assert.Equal(200,(await Import([new Dictionary<string,object?>{{"名称","父范围空"},{"级联",null}}],First.ToString(),scopedEndpoint)).GetProperty("code").GetInt32());var constrained=(await Rows()).Single(row=>Equals(row["name"],"父范围空"));Assert.Equal(First.ToString(),Convert.ToString(constrained["country_id"],CultureInfo.InvariantCulture));Assert.Equal("国甲",constrained["country_name"]);Assert.Null(constrained["area_id"]);Assert.Null(constrained["place_id"]);client.DefaultRequestHeaders.Add("denyImport","1");before=await State();Assert.Equal(500,(await Import([new Dictionary<string,object?>{{"名称","拒权"},{"级联",null}}])).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,verified,realGeneratedHttp=true,realMySql=true,originalColumns=true,clearAll=true,omissionPreserved=true,parentScopePreserved=true,sourceImportOnly=true,batchRollback=true,invalidPathBatchRejected=true,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,failedTestPreserved=true}));throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("跨表空单元格四表结构漂移，保留现场。");if(existing.Tables.Count>0)foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>只授权来源import；普通CRUD与目标list从未授权。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>项目和身份由宿主固定，不取请求项目字段。</summary>
        public RuntimeActor RequireActor()=>new(948,"跨表空单元格验收",false,Project.ToString(),"1");
        /// <summary>显式撤销import时拒绝，不能借普通写权限执行导入。</summary>
        public void RequirePagePermission(string page,string operation){if(page is "301" or "401" or "501"||page is "201" or "202"&&(operation!="import"||accessor.HttpContext!.Request.Headers.ContainsKey("denyImport")))throw new UnauthorizedAccessException("固定撤权。");}
    }
    /// <summary>只对自有随机表使用固定SQL，所有值参数绑定。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
