using System.Data.Common;
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

/// <summary>来源参与跨表链的最终SQL状态、事务与权限。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableSourceGraphMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>固定Server-200实例及隔离库后，两种键与三个来源层位分别运行独立三表样本。</summary>
    [StandaloneExportMySqlFact]
    public async Task Source_graph_uses_actual_stored_path_before_observers()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不符，未建样本。");}foreach(var type in new[]{"string","int64"})for(var level=0;level<3;level++)await Exercise(options,type,level);
    }
    /// <summary>从元数据生成实体与页面，最终SQL范围先于通知，失败保留并只清自有样本。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,int sourceLevel)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_csg_"+nonce[..12];var catalog=CrossTableSourceGraphTests.Catalog(prefix,sourceLevel,type,type);var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cross-table-source-graph/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-source-graph");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var source=prefix+"_source";await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,sourceLevel,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(3,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);object Key(long id)=>type=="string"?id.ToString(CultureInfo.InvariantCulture):id;
            var original=new[]{"country","area","place"};var targets=Enumerable.Range(0,3).Select(index=>index==sourceLevel?source:prefix+"_"+original[index]).ToArray();
            for(var group=0;group<2;group++)for(var index=0;index<3;index++)
            {
                var parent=index==0?"":",`"+prefix+"_"+original[index-1]+"_id`";var extra=index==sourceLevel?",code,amount":"";var sql="INSERT INTO `"+targets[index]+"` (id,name,yz_project_id,status"+parent+extra+") VALUES (@id,@name,@project,1"+(index==0?"":",@parent")+(index==sourceLevel?",'e',1":"")+")";await Execute(session,sql,("id",First+group*10+index),("name",(group==0?"甲":"乙")+new[]{"根","区","城"}[index]),("project",Project),("parent",Key(First+group*10+index-1)));
            }
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SourceGraph");var sourceTable=GenerationModel.Create(catalog,"Example.SourceGraph").Tables.Single(table=>table.Source.Id=="2");var ownField=sourceTable.Columns.Single(column=>column.Source.ColumnName==original[sourceLevel]+"_id");var assignment=type=="string"?"entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)":"entity.Id";
            await File.WriteAllTextAsync(Path.Combine(directory,"generated/Services/SourceGraphHooks.cs"),$$"""
using Metarill.Core.Security;using Example.SourceGraph.Generated.Entities;
namespace Example.SourceGraph.Generated.Services;
public sealed partial class {{sourceTable.Name}}Service
{
    /// <summary>固定标记验证新节点自身引用，不接收客户端身份或表名。</summary>
    partial void BeforeSave({{sourceTable.Name}} entity,RuntimeActor actor,bool creating,ISet<string> submitted){if(creating&&entity.Name=="新自身"){entity.{{ownField.Property}}={{assignment}};submitted.Add("{{ownField.Source.ColumnName}}");} }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(951));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));var observer=new ProbeObserver();builder.Services.AddSingleton<IRecordMutationObserver>(observer);assembly.GetType("Example.SourceGraph.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or JsonException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/";
            async Task<JsonElement> Post(string page,string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+page+"/"+action,body)).Content.ReadAsStringAsync());async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}
            // 先验证新增，再改来源自身；最终SQL范围必须在通知之前复核。
            Task<JsonElement> Call(string action,object body)=>Post("201",action,body);
            var added=Data(await Call("add",new{name="新来源",placeId=(First+2).ToString()}));Assert.Equal("甲根/甲区/甲城",added.GetProperty("placeIdDisplayText").GetString());var own=(First+sourceLevel).ToString(CultureInfo.InvariantCulture);var expected=new[]{"甲根","甲区","甲城"};expected[sourceLevel]="显式新名";var changed=Data(await Call("update",new{id=own,name="显式新名",placeId=(First+2).ToString()}));Assert.Equal(string.Join("/",expected),changed.GetProperty("placeIdDisplayText").GetString());var omitted=Data(await Call("update",new{id=own,name="省略改名"}));Assert.Equal(string.Join("/",expected),omitted.GetProperty("placeIdDisplayText").GetString());
            foreach(var body in new object[]{new{id=own,status=0},new{id=own,amount=0.014m},new{id=own,creating=true},new{name="新自身"}}){var before=await State();var notifications=observer.Count;Assert.Equal(500,(await Call(body.GetType().GetProperty("id") is null?"add":"update",body)).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(notifications,observer.Count);}
            var accent=Data(await Call("update",new{id=own,code="é",amount=0.015m}));Assert.Equal(0.015m,accent.GetProperty("amount").GetDecimal());using(var independent=new DatabaseSession(options)){var stored=(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).Single(row=>SqlRowStore.Id(row).ToString(CultureInfo.InvariantCulture)==own);Assert.Equal(0.02m,Convert.ToDecimal(stored["amount"],CultureInfo.InvariantCulture));}
            if(sourceLevel>0)
            {
                var parentProperty=sourceTable.Columns.Single(column=>column.Source.Id=="88").JsonName;var update=new Dictionary<string,object?>{{"id",own},{parentProperty,(First+10+sourceLevel-1).ToString()},{"placeId",(First+2).ToString()}};var moved=Data(await Call("update",update));var movedNames=Enumerable.Range(0,3).Select(index=>index<sourceLevel?"乙"+new[]{"根","区","城"}[index]:index==sourceLevel?"省略改名":"甲城").ToArray();Assert.Equal(string.Join("/",movedNames),moved.GetProperty("placeIdDisplayText").GetString());var before=await State();Assert.Equal(500,(await Call("update",new Dictionary<string,object?>{{"id",own},{parentProperty,(First+sourceLevel-1).ToString()}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            }
            var beforeObserver=await State();Assert.Equal(500,(await Call("update",new{id=own,name="通知故障",placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(beforeObserver,await State());
            var held=Call("update",new{id=own,name="锁持有",placeId=(First+2).ToString()});await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));try{await using var other=new MySqlConnection(options.ConnectionString);await other.OpenAsync();await using(var timeout=other.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var command=other.CreateCommand();var locked=sourceLevel==0?1:0;var lockedId=First+(sourceLevel>0?10:0)+locked;command.CommandText="UPDATE `"+targets[locked]+"` SET name=name WHERE id=@id";command.Parameters.AddWithValue("id",lockedId);var error=await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteNonQueryAsync());Assert.Equal(1205,error.Number);}finally{observer.Release.TrySetResult();}Data(await held);
            var copied=Data(await Call("copy/"+added.GetProperty("id").GetString(),new{}));Assert.NotEqual(added.GetProperty("id").GetString(),copied.GetProperty("id").GetString());Assert.Single(Data(await Call("selection/selected",new{selectedIds=new[]{own}})).EnumerateArray());
            client.DefaultRequestHeaders.Add("denyTargets","1");var beforeDenied=await State();Assert.Equal(500,(await Call("update",new{id=own,placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(beforeDenied,await State());var cleared=Data(await Call("update",new{id=own,placeId=(string?)null,areaId=(string?)null,countryId=(string?)null}));Assert.Equal("",cleared.GetProperty("placeIdDisplayText").GetString());client.DefaultRequestHeaders.Remove("denyTargets");
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,sourceLevel,verified,realGeneratedHttp=true,realMySql=true,storedSqlScope=true,omittedNamesPreserved=true,sourceParentCoordinated=sourceLevel>0,newSelfRejected=true,permission=true,rollback=true,lock1205=true,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,failedTestPreserved=true}));throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("来源图三表结构漂移，保留现场。");if(existing.Tables.Count>0)foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>固定真实测试身份，头只可撤销权限。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>身份、项目和后台由宿主决定。</summary>
        public RuntimeActor RequireActor()=>new(951,"来源图验收",false,Project.ToString(),"1");
        /// <summary>保留明确来源和目标页面权限，可由测试模拟撤销。</summary>
        public void RequirePagePermission(string page,string operation){if(operation=="list"&&accessor.HttpContext!.Request.Headers.ContainsKey("denyTargets"))throw new UnauthorizedAccessException("固定撤权。");}
    }
    /// <summary>固定标记验证通知顺序、尾部回滚与行锁，不绕过原事务。</summary>
    private sealed class ProbeObserver:IRecordMutationObserver
    {
        public int Count{get;private set;}
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>范围失败不得进入通知；正常写入才可暂停或模拟尾部错误。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){Count++;if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"通知故障"))throw new InvalidOperationException("固定通知故障");if(Equals(name,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(20),cancellationToken);}}
    }
    /// <summary>错误正文不能当业务数据。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>只对自有随机表固定SQL并绑定数据。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] values){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in values){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
