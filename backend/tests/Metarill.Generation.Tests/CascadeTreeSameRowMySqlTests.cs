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

/// <summary>固定Server-200自有随机表，真实生成HTTP验证同表最终状态；不代表正式登录或原浏览器。</summary>
public sealed class CascadeTreeSameRowMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254745001;
    /// <summary>两种ID模式覆盖图依赖、数据库比较/舍入、复制和导入，另测非严格模式FK截断仍整笔拒绝。</summary>
    [StandaloneExportMySqlFact]
    public async Task Final_database_state_controls_same_row_paths_without_old_row_or_memory_predicate_bypass()
    {
        var options=await VerifiedOptions();
        foreach(var type in new[]{"string","int64"})await Exercise(options,type,false);await Exercise(options,"string",true);
    }
    /// <summary>非冗余同表沿同一真实矩阵复验；只存所选ID，不要求或读取名称物理列。</summary>
    [StandaloneExportMySqlFact]
    public async Task Nonredundant_same_row_validates_final_graph_without_storing_leaf_name()
    {
        var options=await VerifiedOptions();foreach(var type in new[]{"string","int64"})await Exercise(options,type,false,true);await Exercise(options,"string",true,true);
    }
    /// <summary>建表前验证逻辑库、主机和实例，配置与凭据不输出。</summary>
    private static async Task<DatabaseOptions> VerifiedOptions()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var builder=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||builder.Database!=options.ExpectedDatabase||builder.Server!="deve-server.yinzon.com"||builder.Port!=3306)throw new InvalidOperationException("同表级联验收仅允许Server-200隔离库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString))
        {await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例身份不符，未建样本。");}
        return options;
    }
    /// <summary>每组只创建一张固定元数据表，最后核对原结构并独立连接核验清理。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,bool truncation,bool nonRedundant=false)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_csm_"+nonce[..12];var table=prefix+"_node";var catalog=CascadeTreeSameRowTests.Catalog(prefix,type,type);if(nonRedundant)catalog.ConfigTables.Single().IsRongyu=0;if(truncation)catalog.ConfigColumns.Single(column=>column.Id=="6").ColumnLength="4";
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/cascade-same-row/mysql-"+nonce);Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"metadata.json"),JsonSerializer.Serialize(catalog));await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,truncation,nonRedundant,names=new[]{table},verified=false}));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-same-row-mysql");var directory=Path.Combine(boundary,nonce);using var session=new DatabaseSession(options);var schema=new SchemaService(session);var observer=new ProbeObserver();WebApplication? app=null;var created=false;var verified=false;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Single(plan.Changes);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            async Task Seed(long id,string name,long? parent,long? bookmark=null,int status=1,long project=Project,DateTime? deleted=null)
            {
                if(truncation)bookmark=null;
                await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(table)+" (id,yz_project_id,delete_no,name,parent_id,bookmark_id"+(nonRedundant?"":",bookmark_name")+",status,amount,code) VALUES (@id,@project,@deleted,@name,@parent,@bookmark"+(nonRedundant?"":",@label")+",@status,@amount,@code)",("id",id),("project",project),("deleted",deleted),("name",name),("parent",Key(parent,type)),("bookmark",Key(bookmark,type)),("label",bookmark==id?name:null),("status",status),("amount",1m),("code","é"));
            }
            await Seed(First,"可根",null);await Seed(First+1,"可自身",First,First+1);await Seed(First+2,"可后代",First+1);await Seed(First+3,"可另根",null);await Seed(First+10,"可隐藏",First,First+10,0);await Seed(First+20,"可外部",null,project:Project+1);await Seed(First+30,"可墓碑",null,deleted:new DateTime(2026,1,1));if(truncation)await Seed(9007,"可伪根",null);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SameRowProbe");var entityName=GenerationModel.Pascal(table);var self=type=="string"?"entity.Id.ToString(CultureInfo.InvariantCulture)":"entity.Id";
            await File.WriteAllTextAsync(Path.Combine(directory,"generated/Services/"+entityName+"Service.cs"),$$"""
using System.Globalization;
using Metarill.Core.Security;
using Example.SameRowProbe.Generated.Entities;
namespace Example.SameRowProbe.Generated.Services;
public partial class {{entityName}}Service
{
    /// <summary>手写规则显式提交图依赖，最终校验不能再使用旧实体。</summary>
    partial void BeforeSave({{entityName}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(entity.Name=="可重挂"){entity.ParentId={{LiteralId(First+3,type)}};submittedFields.Add("parent_id");}
        if(entity.Name=="可隐藏父"){entity.ParentId={{LiteralId(First+10,type)}};submittedFields.Add("parent_id");}
        if(entity.Name=="可造环"){entity.ParentId={{LiteralId(First+2,type)}};submittedFields.Add("parent_id");}
        if(entity.Name=="可改作用域"){entity.Status=0;submittedFields.Add("status");}
        if(creating&&entity.Name=="可新自指"){entity.BookmarkId={{self}};submittedFields.Add("bookmark_id");}
    }
}
""");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var host=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});host.Logging.ClearProviders();host.WebHost.UseUrls("http://127.0.0.1:"+port);host.Services.AddSingleton(session);host.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(945));host.Services.AddSingleton<IRecordMutationObserver>(observer);if(truncation)host.Services.AddSingleton<IRecordMutationGuard>(new NonStrictGuard());host.Services.AddHttpContextAccessor();host.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.SameRowProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[host.Services,null]);host.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(value=>value.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=host.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or PageImportException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+table+"/201/";
            // 固定原页面接口，读取全部自有行摘要核对整笔回滚，包含外部项目和墓碑。
            async Task<JsonElement> Post(string action,object body,string page="201")=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint.Replace("/201/","/"+page+"/")+action,body)).Content.ReadAsStringAsync());
            async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(table)).OrderBy(row=>SqlRowStore.Id(row)));}
            async Task<Dictionary<string,object?>> Stored(long id){using var independent=new DatabaseSession(options);return Assert.Single(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(table),row=>SqlRowStore.Id(row)==id);}
            var own=(First+1).ToString(CultureInfo.InvariantCulture);var rootId=First.ToString(CultureInfo.InvariantCulture);
            if(truncation)
            {
                async Task<string> SqlMode(){await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT @@sql_mode";return Convert.ToString(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture)!;}
                var initialMode=await SqlMode();var before=await State();var notifications=observer.Count;var result=await Post("add",new{name="可截断",bookmarkId=rootId});Assert.Equal(500,result.GetProperty("code").GetInt32());Assert.Contains("存储",result.GetProperty("msg").GetString());Assert.Equal(before,await State());Assert.Equal(notifications,observer.Count);Assert.Equal(initialMode,await SqlMode());
            }
            else
            {
                var added=Data(await Post("add",new{name="可新增",bookmarkId=rootId,bookmarkName="伪造"}));var addedId=added.GetProperty("id").GetString()!;Assert.Equal("可根",added.GetProperty("bookmarkName").GetString());
                if(nonRedundant)Assert.DoesNotContain("bookmark_name",(await Stored(long.Parse(addedId,CultureInfo.InvariantCulture))).Keys);
                var empty=Data(await Post("add",new{name="可无关联"}));var emptyId=empty.GetProperty("id").GetString()!;
                foreach(var id in new[]{emptyId,addedId}){var before=await State();Assert.Equal(500,(await Post("update",new{id,parentId=id},"202")).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
                // 64层仍可用，第65层通过改父写入也必须整笔拒绝。
                for(var depth=0;depth<64;depth++)await Seed(First+100+depth,"可层"+depth,depth==0?null:First+99+depth);
                var beforeDepth=await State();Assert.Equal(500,(await Post("update",new{id=emptyId,parentId=(First+163).ToString()},"202")).GetProperty("code").GetInt32());Assert.Equal(beforeDepth,await State());
                Data(await Post("update",new{id=emptyId,parentId=(First+162).ToString()},"202"));Assert.Equal(64,Data(await Post("cascadeTreePath6",new{leafId=emptyId})).GetProperty("path").GetArrayLength());Data(await Post("update",new{id=emptyId,parentId=(string?)null},"202"));
                // 当前节点本身可到64层，但被目标条件隐藏的后代不能因此落到第65层。
                await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(table)+" SET status=0 WHERE id=@id",("id",First+2));var branchState=await State();Assert.Equal(500,(await Post("update",new{id=own,parentId=(First+162).ToString()},"202")).GetProperty("code").GetInt32());Assert.Equal(branchState,await State());await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(table)+" SET status=1 WHERE id=@id",("id",First+2));
                Data(await Post("update",new{id=own,parentId=(First+161).ToString()},"202"));Assert.Equal(64,Data(await Post("cascadeTreePath6",new{leafId=(First+2).ToString()})).GetProperty("path").GetArrayLength());Data(await Post("update",new{id=own,parentId=rootId},"202"));
                var changed=Data(await Post("update",new{id=own,name="可改名",bookmarkId=own,code="E"}));Assert.Equal("可改名",changed.GetProperty("bookmarkName").GetString());Assert.Equal("可根/可改名",changed.GetProperty("bookmarkIdDisplayText").GetString());
                // 当前记录同时是目标；改名触发统一传播，自指缓存不能因省略FK保留旧名。其它目标原生SQL变更后的省略语义另由写入矩阵验证。
                var omitted=Data(await Post("update",new{id=own,name="可仅改名"}));Assert.Equal("可仅改名",omitted.GetProperty("bookmarkName").GetString());Assert.Equal("可根/可仅改名",omitted.GetProperty("bookmarkIdDisplayText").GetString());
                foreach(var body in new object[]{new{id=own,status=0},new{id=own,name="可隐藏父"},new{id=own,name="可造环"},new{id=own,name="可改作用域"},new{id=own,name="不可见"},new{id=own,bookmarkId=own,amount=.014m},new{id=own,bookmarkId=(First+20).ToString()},new{id=own,bookmarkId=(First+30).ToString()},new{id=own,bookmarkId=own,code="' OR 1=1 --"}})
                {var before=await State();var notifications=observer.Count;Assert.Equal(500,(await Post("update",body)).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(notifications,observer.Count);}
                var rounded=Data(await Post("update",new{id=own,bookmarkId=own,amount=.015m,code="e"}));Assert.Equal("可仅改名",rounded.GetProperty("bookmarkName").GetString());Assert.Equal(.02m,Convert.ToDecimal((await Stored(First+1))["amount"],CultureInfo.InvariantCulture));
                var moved=Data(await Post("update",new{id=own,name="可重挂"}));Assert.Equal("可另根/可重挂",moved.GetProperty("bookmarkIdDisplayText").GetString());Assert.Equal((First+3).ToString(),Convert.ToString((await Stored(First+1))["parent_id"],CultureInfo.InvariantCulture));
                client.DefaultRequestHeaders.Add("denyTarget","1");Assert.Equal(500,(await Post("update",new{id=own,bookmarkId=own})).GetProperty("code").GetInt32());Data(await Post("update",new{id=own,bookmarkId=(string?)null}));var enabled=Data(await Post("update",new{id=(First+10).ToString(),status=1}));Assert.Equal("可隐藏",enabled.GetProperty("bookmarkName").GetString());client.DefaultRequestHeaders.Remove("denyTarget");
                Data(await Post("update",new{id=own,bookmarkId=own}));var beforeNew=await State();Assert.Equal(500,(await Post("add",new{name="可新自指"})).GetProperty("code").GetInt32());Assert.Equal(beforeNew,await State());
                var failed=await State();var notify=observer.Count;Assert.Equal(500,(await Post("update",new{id=own,name="可后段故障",bookmarkId=own})).GetProperty("code").GetInt32());Assert.Equal(failed,await State());Assert.Equal(notify+1,observer.Count);
                Data(await Post("copy/"+own,new{}));
                // 导入动作不额外要求普通新增/编辑或目标list，覆盖与追加仍走相同最终校验。
                client.DefaultRequestHeaders.Add("importOnly","1");Dictionary<string,object?> Row(string name,string path)=>new(){["节点名"]=name,["书签"]=path};Assert.Equal(200,(await Post("importDatas201",new{requestJson=new[]{Row("可导入甲","可根"),Row("可导入乙","可根")}})).GetProperty("code").GetInt32());var state=await State();Assert.Equal(500,(await Post("importDatas201",new{requestJson=new[]{Row("可应回滚","可根"),Row("可失败","不存在")}})).GetProperty("code").GetInt32());Assert.Equal(state,await State());Assert.Equal(200,(await Post("importDatas203",new{requestJson=new[]{Row("可导入甲","可另根")}},"203")).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("importOnly");
                // 最终校验后仍保留目标行锁到源提交，观察只使用自己的UPDATE和回滚。
                var pending=Post("update",new{id=addedId,name="可持锁",bookmarkId=rootId});try
                {await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var transaction=await independent.BeginTransactionAsync();await using var rename=independent.CreateCommand();rename.Transaction=transaction;rename.CommandText="UPDATE "+session.Provider.QuoteIdentifier(table)+" SET name=@name WHERE id=@id";rename.Parameters.AddWithValue("name","可竞争根");rename.Parameters.AddWithValue("id",First);var error=await Assert.ThrowsAsync<MySqlException>(()=>rename.ExecuteNonQueryAsync());Assert.Equal(1205,error.Number);await transaction.RollbackAsync();}
                finally{observer.Release.TrySetResult();await pending;}Assert.Equal(200,(await pending).GetProperty("code").GetInt32());await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(table)+" SET name=@name WHERE id=@id",("name","可根更新"),("id",First));
            }
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,truncation,nonRedundant,verified,realGeneratedHttp=true,realMySql=true,fixedTestActor=true,browser=false,fullApplication=false,finalStorageScope=true,singleNormalWrite=true}));
        }
        catch(Exception error)
        {
            // 首因先保存，避免清理保护异常覆盖实际失败阶段。
            await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{nonce,type,truncation,nonRedundant,errorType=error.GetType().Name,error.Message,applicationStarted=app is not null,verified=false}));throw;
        }
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){using var cleanup=new DatabaseSession(options);var cleanupSchema=new SchemaService(cleanup);if((await cleanupSchema.PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有同表样本结构漂移，保留现场。");await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(table));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=command.CreateParameter();p.ParameterName="name";p.Value=table;command.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names=new[]{table},verified,independentTableAbsenceChecked=true,remaining=0}));}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>延迟及故障仅在通知阶段触发，验证最终路径失败不会通知、成功仍持有锁。</summary>
    private sealed class ProbeObserver:IRecordMutationObserver
    {
        public int Count;public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>有界暂停不会被观察超时误当作事务终止。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){Interlocked.Increment(ref Count);if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"可后段故障"))throw new InvalidOperationException("敏感内部故障");if(Equals(name,"可持锁")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),cancellationToken);}}
    }
    /// <summary>仅自己的测试写事务关闭严格模式，证明FK存储被截断后也不能替换成另一有效节点。</summary>
    private sealed class NonStrictGuard:IRecordMutationGuard
    {
        /// <summary>不修改服务器全局设置，不增授任何权限。</summary>
        public Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)=>Task.CompletedTask;
        /// <summary>SET SESSION绑定本次事务连接，连接释放后由驱动正常重置。</summary>
        public async Task BeforeWriteAsync(RecordMutationValidation validation,CancellationToken cancellationToken=default){await using var command=validation.Transaction.Connection!.CreateCommand();command.Transaction=validation.Transaction;command.CommandText="SET SESSION sql_mode=''";await command.ExecuteNonQueryAsync(cancellationToken);}
    }
    /// <summary>固定服务端身份，独立撤销目标list与来源写入动作。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>项目与后台归属不从请求绑定。</summary>
        public RuntimeActor RequireActor()=>new(945,"同表级联验收",false,Project.ToString(CultureInfo.InvariantCulture),"1");
        /// <summary>导入只有import动作，不能借内部操作标志回到create/update。</summary>
        public void RequirePagePermission(string pageId,string operation){var headers=accessor.HttpContext!.Request.Headers;if(pageId=="202"&&operation=="list"&&(headers.ContainsKey("denyTarget")||headers.ContainsKey("importOnly"))||headers.ContainsKey("importOnly")&&operation is "create" or "update")throw new UnauthorizedAccessException("没有指定页面操作权限。");}
    }
    /// <summary>保持高位ID的原字符串响应，先确认R成功。</summary>
    private static JsonElement Data(JsonElement value){Assert.Equal(200,value.GetProperty("code").GetInt32());return value.GetProperty("data");}
    /// <summary>字符串外键与整数外键使用同一逻辑ID。</summary>
    private static object? Key(long? id,string type)=>id is null?null:type=="string"?id.Value.ToString(CultureInfo.InvariantCulture):id.Value;
    /// <summary>只把固定样本ID生成到测试partial，不接受请求代码。</summary>
    private static string LiteralId(long id,string type)=>type=="string"?JsonSerializer.Serialize(id.ToString(CultureInfo.InvariantCulture)):id.ToString(CultureInfo.InvariantCulture)+"L";
    /// <summary>固定自有表SQL全部使用参数绑定，清理前另核对结构归属。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] values){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var value in values){var parameter=command.CreateParameter();parameter.ParameterName=value.Name;parameter.Value=value.Value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
