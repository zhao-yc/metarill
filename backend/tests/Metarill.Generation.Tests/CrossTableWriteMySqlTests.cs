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

/// <summary>真实生成写接口与Server-200随机四表，覆盖自动结构、部分选择和同一事务守卫。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableWriteMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>固定实例及隔离库后执行两种ID类型及短名称存储负例，不使用本机Docker。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_partial_selections_generate_managed_columns_and_preserve_transaction_boundaries()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");}
        await Exercise(options,"string",false);await Exercise(options,"int64",false);await Exercise(options,"string",true);
    }
    /// <summary>自有旧表经显式预览升级，生成原接口并验证所有写入链路，失败先存证后清理。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,bool shortName)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_ccw_"+nonce[..12];var catalog=CrossTableReadTests.Catalog(prefix,type,type);var leaf=catalog.ConfigColumns.Single(column=>column.Id=="25");leaf.LogicalType=type;leaf.ColumnLength=type=="string"?"64":"";var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");sourcePage.IsCopy=1;
        if(shortName)catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="country_name",LogicalType="string",ColumnLength="1"});
        var scoped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;scoped.Id="202";scoped.WhereSql+=" AND country_id=@parent_id";catalog.ConfigPages.Add(scoped);foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="202"+field.Id;copy.ConfigPageId="202";catalog.ConfigPageColumns.Add(copy);}
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cross-table-write/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-write-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;var observer=new ProbeObserver();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,shortName,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var previous=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog))!;previous.ConfigColumns.Single(column=>column.Id=="25").ControlKind="";previous.ConfigPages.RemoveAll(page=>page.Id=="202");previous.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="202");var initial=await schema.PreviewAsync(previous);Assert.Equal(4,initial.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(initial)).Succeeded);var source=prefix+"_source";
            await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id) VALUES (@id,'旧行',@project)",("id",First+100),("project",Project));var upgrade=await schema.PreviewAsync(catalog);Assert.Equal(shortName?4:5,upgrade.Changes.Count);Assert.All(upgrade.Changes,change=>Assert.Equal("addColumn",change.Kind));Assert.DoesNotContain((await schema.InspectAsync(previous)).Tables.Single(table=>table.Name==source).Columns,column=>column.Name=="country_id");Assert.True((await schema.ApplyAsync(upgrade)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);var old=Assert.Single(await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source));Assert.Equal("旧行",old["name"]);Assert.Null(old["country_id"]);Assert.Null(old["place_name"]);
            var targets=new[]{"country","area","place"};
            // 固定种子只写本nonce四表，类型差异由同一逻辑类型映射决定。
            async Task Seed(int level,long id,string name,long? parent=null,long project=Project,int status=1){var parentColumn=level==0?"":",`"+prefix+"_"+targets[level-1]+"_id`";await Execute(session,"INSERT INTO `"+prefix+"_"+targets[level]+"` (id,name,yz_project_id,status"+parentColumn+") VALUES (@id,@name,@project,@status"+(level==0?"":",@parent")+")",("id",id),("name",name),("project",project),("status",status),("parent",type=="string"?(object?)parent?.ToString(CultureInfo.InvariantCulture):parent));}
            await Seed(0,First,"国甲");await Seed(0,First+10,"国乙");await Seed(0,First+20,"隐藏国",status:0);await Seed(0,First+30,"外项目",project:Project+1);await Seed(1,First+1,"区甲",First);await Seed(1,First+11,"区乙",First+10);await Seed(1,First+21,"隐藏父",First+20);await Seed(1,First+31,"外父",First+30);await Seed(2,First+2,"城甲",First+1);await Seed(2,First+12,"城乙",First+11);await Seed(2,First+22,"隐藏祖先叶",First+21);await Seed(2,First+32,"外祖先叶",First+31);
            var entityName=GenerationModel.Pascal(source);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossWriteProbe");await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.CrossWriteProbe.Generated.Services;
public partial class {{entityName}}Service
{
    /// <summary>手写钩子仍在最终路径及作用域检查之前，不作为跨项目授权。</summary>
    partial void BeforeSave(Example.CrossWriteProbe.Generated.Entities.{{entityName}} entity,RuntimeActor actor,bool creating,ISet<string> submitted)
    {
        if(entity.Name=="钩子选中"){entity.PlaceId={{(type=="string"?"\""+(First+12).ToString()+"\"":(First+12).ToString()+"L")}};submitted.Add("place_id");}
        if(entity.Name=="钩子越界"){entity.PlaceId={{(type=="string"?"\""+(First+32).ToString()+"\"":(First+32).ToString()+"L")}};submitted.Add("place_id");}
        if(entity.Name=="钩子归属"){entity.YzProjectId={{(Project+1).ToString()}}L;submitted.Add("yz_project_id");}
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.CrossWriteProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or MySqlException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error is MySqlException?"数据库拒绝写入":error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/201/";
            // 请求沿原实体JSON；类型为long的字段仍从原字符串输入，响应ID仍为字符串。
            async Task<JsonElement> Post(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action,body)).Content.ReadAsStringAsync());
            async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}
            if(shortName)
            {
                // 只在一条自有INSERT前临时切换会话模式，CommandAfter在同一事务连接立即恢复；不改全局。
                var modes=new Dictionary<DbCommand,string>();var active=true;session.Orm.Aop.CommandBefore+=(_,args)=>{if(active&&args.Command.CommandText.TrimStart().StartsWith("INSERT",StringComparison.OrdinalIgnoreCase)&&args.Command.CommandText.Contains(source,StringComparison.Ordinal)){using var command=args.Command.Connection!.CreateCommand();command.Transaction=args.Command.Transaction;command.CommandText="SELECT @@SESSION.sql_mode";modes[args.Command]=Convert.ToString(command.ExecuteScalar(),CultureInfo.InvariantCulture)!;command.CommandText="SET SESSION sql_mode=''";command.ExecuteNonQuery();}};session.Orm.Aop.CommandAfter+=(_,args)=>{if(modes.Remove(args.Command,out var original)){using var command=args.Command.Connection!.CreateCommand();command.Transaction=args.Command.Transaction;command.CommandText="SET SESSION sql_mode=@mode";var parameter=command.CreateParameter();parameter.ParameterName="mode";parameter.Value=original;command.Parameters.Add(parameter);command.ExecuteNonQuery();}};
                var originalModes=await ReadModes();var before=await State();var rejected=await Post("add",new{name="截断验证",placeId=(First+2).ToString()});active=false;Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Contains("存储后发生变化",rejected.GetProperty("msg").GetString());Assert.Empty(modes);Assert.Equal(originalModes,await ReadModes());Assert.Equal(before,await State());
            }
            else
            {
                var saved=Data(await Post("add",new{name="完整选择",placeId=(First+2).ToString(),countryId=(First+10).ToString(),countryName="伪造",placeName="伪造"}));var id=saved.GetProperty("id").GetString()!;Assert.Equal(First.ToString(),saved.GetProperty("countryId").GetString());Assert.Equal((First+1).ToString(),saved.GetProperty("areaId").GetString());Assert.Equal("国甲",saved.GetProperty("countryName").GetString());Assert.Equal("城甲",saved.GetProperty("placeName").GetString());
                var onlyRoot=Data(await Post("add",new{name="部分根",countryId=First.ToString()}));Assert.Equal("国甲",onlyRoot.GetProperty("countryName").GetString());Assert.Equal(JsonValueKind.Null,onlyRoot.GetProperty("areaId").ValueKind);var onlyMid=Data(await Post("add",new{name="部分中",areaId=(First+1).ToString()}));Assert.Equal(First.ToString(),onlyMid.GetProperty("countryId").GetString());Assert.Equal(JsonValueKind.Null,onlyMid.GetProperty("placeId").ValueKind);
                await Execute(session,"UPDATE `"+prefix+"_country` SET name='新国甲' WHERE id=@id",("id",First));var unchanged=Data(await Post("update",new{id,name="只改名称"}));Assert.Equal("国甲",unchanged.GetProperty("countryName").GetString());var refresh=Data(await Post("update",new{id,placeId=(First+2).ToString()}));Assert.Equal("新国甲",refresh.GetProperty("countryName").GetString());
                var fromFull=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(refresh.GetRawText())!;fromFull["countryName"]=JsonSerializer.SerializeToElement("伪造");fromFull["countryId"]=JsonSerializer.SerializeToElement((First+10).ToString());Data(await Post("update",fromFull));Data(await Post("copy/"+id,new{}));var partial=Data(await Post("update",new{id,placeId=(string?)null}));Assert.Equal((First+1).ToString(),partial.GetProperty("areaId").GetString());Assert.Equal(JsonValueKind.Null,partial.GetProperty("placeName").ValueKind);
                client.DefaultRequestHeaders.Add("denyTargets","1");var cleared=Data(await Post("update",new{id,countryId="",areaId="",placeId=""}));Assert.Equal(type=="string"?JsonValueKind.String:JsonValueKind.Null,cleared.GetProperty("countryId").ValueKind);var before=await State();Assert.Equal(500,(await Post("add",new{name="拒绝目标",countryId=First.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());client.DefaultRequestHeaders.Remove("denyTargets");
                client.DefaultRequestHeaders.Add("denyLeaf","1");Data(await Post("add",new{name="仅根授权",countryId=First.ToString()}));client.DefaultRequestHeaders.Remove("denyLeaf");
                foreach(var invalid in new[]{First+22,First+32,First+9999}){before=await State();Assert.Equal(500,(await Post("add",new{name="拒绝祖先",placeId=invalid.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
                var hook=Data(await Post("add",new{name="钩子选中"}));Assert.Equal((First+10).ToString(),hook.GetProperty("countryId").GetString());foreach(var name in new[]{"钩子越界","钩子归属","通知故障"}){before=await State();Assert.Equal(500,(await Post("add",new{name,placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
                var scopedEndpoint=endpoint.Replace("/201/","/202/");async Task<JsonElement> Scoped(string parent,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(scopedEndpoint+"add?parentId="+parent,body)).Content.ReadAsStringAsync());before=await State();Assert.Equal(500,(await Scoped((First+10).ToString(),new{name="错父",placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());var bound=Data(await Scoped(First.ToString(),new{name="同父",placeId=(First+2).ToString()}));Assert.Equal(First.ToString(),bound.GetProperty("countryId").GetString());
                var pending=Post("add",new{name="锁持有",placeId=(First+2).ToString()});try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var transaction=await independent.BeginTransactionAsync();await using var update=independent.CreateCommand();update.Transaction=transaction;update.CommandText="UPDATE `"+prefix+"_area` SET name='竞争中层' WHERE id=@id";update.Parameters.AddWithValue("id",First+1);Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>update.ExecuteNonQueryAsync())).Number);await transaction.RollbackAsync();}finally{observer.Release.TrySetResult();await pending;}Data(await pending);await Execute(session,"UPDATE `"+prefix+"_area` SET name='提交后中层' WHERE id=@id",("id",First+1));
            }
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,shortName,verified,realGeneratedHttp=true,realMySql=true,implicitPhysicalColumns=true,partialSelections=!shortName,managedNames=!shortName,sourceScopes=!shortName,currentLocks=!shortName,storedPostCheck=shortName,sessionAndGlobalModeUnchanged=shortName,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{error=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,structureLockRejected=error is SchemaApplyException schemaError&&schemaError.InnerException?.Message=="其他结构任务正在执行，未取得数据库结构锁。",failedTestPreserved=true}));throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有跨表写四表结构漂移，保留现场。");if(existing.Tables.Count>0)foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>固定身份不代表正式登录，请求只可撤销动作。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>项目与后台由宿主确定。</summary>
        public RuntimeActor RequireActor()=>new(944,"跨表保存验收",false,Project.ToString(),"1");
        /// <summary>验证根级不附加未来叶list，清空无需目标list。</summary>
        public void RequirePagePermission(string page,string operation){var headers=accessor.HttpContext!.Request.Headers;if(headers.ContainsKey("denyTargets")&&page is "301" or "401" or "501"||headers.ContainsKey("denyLeaf")&&page=="501")throw new UnauthorizedAccessException("固定目标撤权。");}
    }
    /// <summary>后段故障与有界暂停均在原事务中，验证回滚及锁保持。</summary>
    private sealed class ProbeObserver:IRecordMutationObserver
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>仅固定标记触发，不改变普通写入语义。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"通知故障"))throw new InvalidOperationException("固定通知故障");if(Equals(name,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),cancellationToken);}}
    }
    /// <summary>错误正文不能当作业务数据。</summary>
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.ToString());return response.GetProperty("data");}
    /// <summary>独立连接读取会话及全局模式，核对短列负例没有遗留模式变化。</summary>
    private static async Task<(string Session,string Global)> ReadModes()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT @@SESSION.sql_mode,@@GLOBAL.sql_mode";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());return (reader.GetString(0),reader.GetString(1));
    }
    /// <summary>自有随机表固定SQL均参数化，连接字符串不写入回执。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
