using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
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

/// <summary>真实生成控制器与Server-200独占随机表；固定测试身份不代表正式登录或浏览器验收。</summary>
public sealed class CascadeTreeReadMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;

    [StandaloneExportMySqlFact]
    public async Task Original_self_tree_http_contract_keeps_project_soft_delete_page_conditions_and_minimal_projection()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var connectionOptions=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||connectionOptions.Database!=options.ExpectedDatabase||connectionOptions.Server!="deve-server.yinzon.com"||connectionOptions.Port!=3306)throw new InvalidOperationException("级联验收仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));
        await using(var connection=new MySqlConnection(options.ConnectionString))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未创建样本。");
        }
        foreach(var parentType in new[]{"int64","string"})await Exercise(options,parentType);
    }

    /// <summary>两个物理父键模式各自使用新的随机两表，精确清理并另起连接核验。</summary>
    private static async Task Exercise(DatabaseOptions options,string parentType)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_cas_"+nonce[..12];var catalog=CascadeTreeReadTests.Catalog(prefix,parentType);
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少仓库路径。");var evidence=Path.Combine(root,".local/cascade-read/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;
        using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,parentType,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(2,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            var tree=prefix+"_tree";var parentColumn="parent_"+prefix+"_tree_id";
            async Task Seed(long id,string name,long? parent,long project=Project,int status=1,DateTime? deleted=null)
            {
                await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(tree)+" (id,name,yz_project_id,"+session.Provider.QuoteIdentifier(parentColumn)+",status,delete_no,secret) VALUES (@id,@name,@project,@parent,@status,@deleted,@secret)",("id",id),("name",name),("project",project),("parent",parentType=="string"?parent?.ToString(CultureInfo.InvariantCulture):parent),("status",status),("deleted",deleted),("secret","不应进入响应"));
            }
            await Seed(First,"根",null);await Seed(First+1,"子",First);await Seed(First+2,"孙",First+1);await Seed(First+3,"同层",First);
            await Seed(First+10,"外项目",null,Project+1);await Seed(First+11,"外父子",First+10);await Seed(First+20,"已删除",null,deleted:new DateTime(2026,1,1));await Seed(First+21,"删父子",First+20);
            await Seed(First+30,"条件隐藏",null,status:0);await Seed(First+31,"条件隐藏子",First+30);await Seed(First+40,"循环甲",First+41);await Seed(First+41,"循环乙",First+40);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeProbe");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.CascadeProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();
            app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+prefix+"_source/201/";
            var commands=new ConcurrentQueue<(string Sql,int Parameters)>();session.Orm.Aop.CommandBefore+=(_,args)=>{if(args.Command.CommandText.Contains(prefix+"_tree",StringComparison.Ordinal))commands.Enqueue((args.Command.CommandText,args.Command.Parameters.Count));};
            async Task<JsonElement> Post(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action+"25",body)).Content.ReadAsStringAsync());
            var roots=Data(await Post("cascadeTreeOptions",new{levelKey="selfTree-0",selectedPath=new{parentId=""}}));var rootOption=Assert.Single(roots.EnumerateArray());Assert.Equal(First.ToString(),rootOption.GetProperty("id").GetString());Assert.True(rootOption.GetProperty("hasChildren").GetBoolean());
            Assert.Equal(new[]{"hasChildren","id","name","text","value"},rootOption.EnumerateObject().Select(property=>property.Name).Order().ToArray());
            var children=Data(await Post("cascadeTreeOptions",new{levelKey="selfTree-1",selectedPath=new{parentId=First.ToString()}})).EnumerateArray().ToArray();Assert.Equal(2,children.Length);Assert.Contains(children,row=>row.GetProperty("id").GetString()==(First+1).ToString()&&row.GetProperty("hasChildren").GetBoolean());
            var path=Data(await Post("cascadeTreePath",new{leafId=(First+2).ToString()}));Assert.Equal("根/子/孙",path.GetProperty("displayText").GetString());Assert.Equal(new[]{First,First+1,First+2},path.GetProperty("path").EnumerateArray().Select(row=>long.Parse(row.GetProperty("id").GetString()!)));
            Assert.DoesNotContain("secret",path.GetRawText());Assert.DoesNotContain("status",path.GetRawText());Assert.DoesNotContain("yzProjectId",path.GetRawText());
            Assert.Empty(Data(await Post("cascadeTreePath",new{leafId=""})).GetProperty("path").EnumerateArray());
            foreach(var invalid in new[]{First+10,First+11,First+20,First+21,First+30,First+31,First+40,First+999})
            {
                Assert.Equal(500,(await Post("cascadeTreePath",new{leafId=invalid.ToString()})).GetProperty("code").GetInt32());
                Assert.Equal(500,(await Post("cascadeTreeOptions",new{levelKey="selfTree-1",selectedPath=new{parentId=invalid.ToString()}})).GetProperty("code").GetInt32());
            }
            foreach(var deny in new[]{"201","301"})
            {
                client.DefaultRequestHeaders.Add("denyPage",deny);Assert.Equal(500,(await Post("cascadeTreePath",new{leafId=""})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("cascadeTreeOptions",new{levelKey="selfTree-0",selectedPath=new{parentId=""}})).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("denyPage");
            }
            using(var rejected=await client.PostAsJsonAsync(endpoint+"cascadeTreePath25",new{leafId=First.ToString(),sql="1=1"}))Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
            var beforeInvalid=commands.Count;Assert.Equal(500,(await Post("cascadeTreeOptions",new{levelKey="selfTree-0",selectedPath=new{parentId="1 OR 1=1"}})).GetProperty("code").GetInt32());Assert.Equal(beforeInvalid,commands.Count);
            Assert.NotEmpty(commands);Assert.All(commands,command=>{Assert.True(command.Parameters>0);Assert.DoesNotContain("secret",command.Sql,StringComparison.OrdinalIgnoreCase);});
            // 同层501项必须整体拒绝，不把截断响应当全部候选。
            for(var i=0;i<500;i++)await Seed(First+100+i,"新增根"+i,null);
            Assert.Equal(500,(await Post("cascadeTreeOptions",new{levelKey="selfTree-0",selectedPath=new{parentId=""}})).GetProperty("code").GetInt32());verified=true;
            await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,parentType,verified,originalProtocol=true,realMySql=true,realGeneratedHttp=true,fixedTestActor=true,browser=false,fullControl=false}));
        }
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var cleanupSchema=new SchemaService(cleanup);if((await cleanupSchema.PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有级联样本结构漂移，保留现场。");
                foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));
                await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}
                await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0}));
            }
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>固定测试身份只允许list；请求头仅撤销权限，不能建立账号或项目身份。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(944,"级联读取验收",false,Project.ToString(),"1");
        public void RequirePagePermission(string pageId,string operation){if(operation!="list"||accessor.HttpContext!.Request.Headers["denyPage"]==pageId)throw new UnauthorizedAccessException("没有页面读取权限。");}
    }
    private static JsonElement Data(JsonElement response){Assert.Equal(200,response.GetProperty("code").GetInt32());return response.GetProperty("data");}
    /// <summary>自有随机表的固定SQL通过参数绑定准备数据，不接受HTTP输入。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;
        foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();
    }
}
