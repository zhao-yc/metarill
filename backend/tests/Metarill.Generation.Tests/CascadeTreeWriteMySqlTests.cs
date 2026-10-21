using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
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

/// <summary>真实生成控制器与Server-200独占随机表；固定测试身份不代表正式登录或浏览器验收。</summary>
public sealed class CascadeTreeWriteMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;

    [StandaloneExportMySqlFact]
    public async Task Original_self_tree_writes_revalidate_current_ancestors_maintain_names_and_rollback()
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
        foreach(var parentType in new[]{"int64","string"})foreach(var implicitName in new[]{false,true})await Exercise(options,parentType,implicitName);
    }

    /// <summary>两个物理父键模式各自使用新的随机两表，精确清理并另起连接核验。</summary>
    private static async Task Exercise(DatabaseOptions options,string parentType,bool implicitName)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_caw_"+nonce[..12];var catalog=CascadeTreeWriteTests.Catalog(prefix,parentType,parentType);catalog.ConfigColumns.Single(column=>column.Id=="25").DefaultValue=First.ToString(CultureInfo.InvariantCulture);
        // 不另配名称页面字段时，原响应仍应携带受管叶子名称；真实写入链与回显必须一致。
        if(implicitName)catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="26");
        // 同表父字段也复用原级联，修改父关系不得形成新环。
        var parentDefinition=catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail;var parentField=catalog.ConfigColumns.Single(column=>column.Id=="35");parentField.ControlKind="CascadeTree";parentField.ControlDetail=parentDefinition;
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="parent_"+prefix+"_tree_name",LogicalType="string",ColumnLength="10",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1)}});catalog.ConfigPageColumns.Add(new(){Id="3008",ConfigPageId="301",ConfigColumnId="38"});
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少仓库路径。");var evidence=Path.Combine(root,".local/cascade-write/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-write-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;
        var pause=new PauseObserver();var snapshot=new SnapshotGuard(options,prefix);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,parentType,implicitName,names,database=options.ExpectedDatabase,verified=false}));
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
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeWriteProbe");
            var typeName=string.Concat((prefix+"_source").Split('_').Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
            await File.WriteAllTextAsync(Path.Combine(directory,"CascadeHook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.CascadeWriteProbe.Generated.Services;
public partial class {{typeName}}Service
{
    /// <summary>手写钩子改写最终关联后仍须通过生成规则，名称也不能绕过服务器重算。</summary>
    partial void BeforeSave(Example.CascadeWriteProbe.Generated.Entities.{{typeName}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(entity.Name=="手写无效"){entity.TreeId={{(parentType=="string"?"\""+(First+10).ToString()+"\"":(First+10).ToString()+"L")}};submittedFields.Add("tree_id");}
        if(entity.Name=="手写刷新"){entity.TreeName="伪造钩子名";submittedFields.Add("tree_name");}
    }
}
""");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            builder.Services.AddSingleton<IRecordMutationObserver>(pause);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.CascadeWriteProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
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
            // 原完整表单中的名称只作回显，实际名称必须来自当前叶子。
            async Task<JsonElement> Write(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action,body)).Content.ReadAsStringAsync());
            var defaulted=Data(await Write("add",new{name="默认关联"}));Assert.Equal(First.ToString(),defaulted.GetProperty("treeId").ToString());Assert.Equal("根",defaulted.GetProperty("treeName").GetString());
            var explicitNull=Data(await Write("add",new{name="覆盖默认",treeId=(string?)null,treeName="伪造"}));Assert.Equal(JsonValueKind.Null,explicitNull.GetProperty("treeId").ValueKind);Assert.Equal(JsonValueKind.Null,explicitNull.GetProperty("treeName").ValueKind);
            var added=Data(await Write("add",new{name="源记录",treeId=(First+1).ToString(),treeName="前端伪造"}));var id=added.GetProperty("id").GetString()!;Assert.Equal("子",added.GetProperty("treeName").GetString());
            async Task<JsonElement> Detail()=>Data(JsonSerializer.Deserialize<JsonElement>(await client.GetStringAsync(endpoint+"detail/"+id)));
            await Execute(session,"UPDATE `"+tree+"` SET name=@name WHERE id=@id",("name","叶改名"),("id",First+1));
            var omitted=Data(await Write("update",new{id,name="省略关联",treeName="仍是伪造"}));Assert.Equal("子",omitted.GetProperty("treeName").GetString());
            var refreshed=Data(await Write("update",new{id,treeId=(First+1).ToString()}));Assert.Equal("叶改名",refreshed.GetProperty("treeName").GetString());var checkpoint=(await Detail()).GetRawText();
            foreach(var invalid in new[]{First+10,First+11,First+20,First+21,First+30,First+31,First+40,First+999}){Assert.Equal(500,(await Write("update",new{id,treeId=invalid.ToString(),treeName="伪造"})).GetProperty("code").GetInt32());Assert.Equal(checkpoint,(await Detail()).GetRawText());}
            Assert.Equal(500,(await Write("update",new{id,name="手写无效",treeId=(First+1).ToString()})).GetProperty("code").GetInt32());Assert.Equal(checkpoint,(await Detail()).GetRawText());
            client.DefaultRequestHeaders.Add("denyListPage","201");Assert.Equal("叶改名",Data(await Write("update",new{id,treeId=(First+1).ToString()})).GetProperty("treeName").GetString());client.DefaultRequestHeaders.Remove("denyListPage");
            client.DefaultRequestHeaders.Add("denyPage","301");Assert.Equal(500,(await Write("update",new{id,treeId=(First+1).ToString()})).GetProperty("code").GetInt32());
            var clear=Data(await Write("update",new{id,treeId=(string?)null,treeName="伪造"}));Assert.Equal(JsonValueKind.Null,clear.GetProperty("treeId").ValueKind);Assert.Equal(JsonValueKind.Null,clear.GetProperty("treeName").ValueKind);client.DefaultRequestHeaders.Remove("denyPage");
            // 原组件清空统一发空文本；Optional将可空整数的空文本映射为显式null，字符串保持空文本。
            var empty=Data(await Write("update",new{id,treeId="",treeName="伪造"}));
            if(parentType=="string"){Assert.Equal("",empty.GetProperty("treeId").GetString());Assert.Equal("",empty.GetProperty("treeName").GetString());Assert.Equal(500,(await Write("update",new{id,treeId="0001"})).GetProperty("code").GetInt32());}
            else{Assert.Equal(JsonValueKind.Null,empty.GetProperty("treeId").ValueKind);Assert.Equal(JsonValueKind.Null,empty.GetProperty("treeName").ValueKind);}
            Data(await Write("update",new{id,treeId=(First+1).ToString()}));
            await Execute(session,"UPDATE `"+tree+"` SET name=@name WHERE id=@id",("name","再改名"),("id",First+1));Assert.Equal("再改名",Data(await Write("update",new{id,name="手写刷新"})).GetProperty("treeName").GetString());
            await Execute(session,"UPDATE `"+tree+"` SET name=@name WHERE id=@id",("name","复制新名"),("id",First+1));
            using(var copied=await client.PostAsJsonAsync(endpoint+"copy/"+id,new{})){var row=Data(JsonSerializer.Deserialize<JsonElement>(await copied.Content.ReadAsStringAsync()));Assert.NotEqual(id,row.GetProperty("id").GetString());Assert.Equal("复制新名",row.GetProperty("treeName").GetString());}
            async Task<long> SourceCount(){await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM `"+prefix+"_source`";return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
            var beforeCopy=await SourceCount();await Execute(session,"UPDATE `"+tree+"` SET status=0 WHERE id=@id",("id",First+1));
            using(var rejectedCopy=await client.PostAsJsonAsync(endpoint+"copy/"+id,new{}))Assert.Equal(500,JsonSerializer.Deserialize<JsonElement>(await rejectedCopy.Content.ReadAsStringAsync()).GetProperty("code").GetInt32());
            Assert.Equal(beforeCopy,await SourceCount());await Execute(session,"UPDATE `"+tree+"` SET status=1 WHERE id=@id",("id",First+1));
            // 读取事务已有RR旧快照，当前读仍必须保存刚提交的新名称并拒绝新失效的祖先。
            snapshot.Mode="rename";Assert.Equal("当前新名",Data(await Write("update",new{id,treeId=(First+1).ToString()})).GetProperty("treeName").GetString());
            snapshot.Mode="hide-parent";Assert.Equal(500,(await Write("update",new{id,treeId=(First+1).ToString()})).GetProperty("code").GetInt32());await Execute(session,"UPDATE `"+tree+"` SET status=1 WHERE id=@id",("id",First));
            Assert.Equal(500,(await Write("update",new{id,name="提交前失败",treeId=(First+3).ToString()})).GetProperty("code").GetInt32());
            var detail=await Detail();Assert.Equal((First+1).ToString(),detail.GetProperty("treeId").ToString());Assert.Equal("当前新名",detail.GetProperty("treeName").GetString());Assert.NotEqual("提交前失败",detail.GetProperty("name").GetString());
            await Execute(session,"UPDATE `"+tree+"` SET name=@name WHERE id=@id",("name","超过十个字符的叶子名称不能保存"),("id",First+3));Assert.Equal(500,(await Write("update",new{id,treeId=(First+3).ToString()})).GetProperty("code").GetInt32());
            // 校验后至源提交期间祖先行锁保留，不使用全局观测权限。
            var pending=Write("update",new{id,name="并发持锁",treeId=(First+1).ToString()});
            try
            {
                await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();
                await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}
                await using var transaction=await independent.BeginTransactionAsync();await using var write=independent.CreateCommand();write.Transaction=transaction;write.CommandText="UPDATE `"+tree+"` SET status=0 WHERE id=@id";write.Parameters.AddWithValue("id",First);
                var blocked=await Assert.ThrowsAsync<MySqlException>(()=>write.ExecuteNonQueryAsync());Assert.Equal(1205,blocked.Number);await transaction.RollbackAsync();
            }
            finally{pause.Release.TrySetResult();await pending;}
            Assert.Equal(200,(await pending).GetProperty("code").GetInt32());await Execute(session,"UPDATE `"+tree+"` SET name=@name WHERE id=@id",("name","锁后可写"),("id",First));
            var hierarchy="/api/admin/auto/"+prefix+"_tree/301/update";var parentJson="parent"+string.Concat((prefix+"_tree_id").Split('_').Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
            foreach(var selected in new[]{First,First+1})
            {
                using var response=await client.PostAsJsonAsync(hierarchy,new Dictionary<string,object?>{{"id",First.ToString()},{parentJson,selected.ToString()}});var rejected=JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Contains("自身或后代",rejected.GetProperty("msg").GetString());
            }
            // 合法迁父保存原叶子父名称，随后显式null可恢复根节点。
            using(var response=await client.PostAsJsonAsync(hierarchy,new Dictionary<string,object?>{{"id",(First+3).ToString()},{parentJson,(First+2).ToString()}}))Assert.Equal(200,JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()).GetProperty("code").GetInt32());
            using(var response=await client.PostAsJsonAsync(hierarchy,new Dictionary<string,object?>{{"id",(First+3).ToString()},{parentJson,null}}))Assert.Equal(200,JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()).GetProperty("code").GetInt32());

            verified=true;
            await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,parentType,verified,originalProtocol=true,realMySql=true,realGeneratedHttp=true,fixedTestActor=true,browser=false,fullControl=false,writeAndNames=true,currentReadAndParentLock=true}));
        }
        finally
        {
            pause.Release.TrySetResult();
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

    /// <summary>真实RR事务先建立旧快照，再由独立连接修改当前叶子或祖先状态。</summary>
    private sealed class SnapshotGuard(DatabaseOptions options,string prefix):IRecordMutationGuard
    {
        public string? Mode{get;set;}
        /// <summary>仅指定测试请求触发，当前读必须越过这里建立的快照。</summary>
        public async Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)
        {
            var mode=Mode;if(mode is null||preparation.Operation!=RecordMutationOperation.Update)return;Mode=null;
            await using var read=preparation.Transaction.Connection!.CreateCommand();read.Transaction=preparation.Transaction;read.CommandText="SELECT @@transaction_isolation";Assert.Equal("REPEATABLE-READ",Convert.ToString(await read.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture));
            read.CommandText="SELECT name FROM `"+prefix+"_tree` WHERE id=@id";var parameter=read.CreateParameter();parameter.ParameterName="id";parameter.Value=First+1;read.Parameters.Add(parameter);await read.ExecuteScalarAsync(cancellationToken);
            using var independent=new DatabaseSession(options);await Execute(independent,mode=="rename"?"UPDATE `"+prefix+"_tree` SET name='当前新名' WHERE id=@id":"UPDATE `"+prefix+"_tree` SET status=0 WHERE id=@id",("id",mode=="rename"?First+1:First));
        }
    }
    /// <summary>故障位于源写入之后，持锁阶段有界释放并验证回滚。</summary>
    private sealed class PauseObserver:IRecordMutationObserver
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>只拦截本轮自有的指定记录，不影响其他写入。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
        {
            if(!context.SubmittedFields.TryGetValue("name",out var name))return;
            if(Equals(name,"提交前失败"))throw new InvalidOperationException("级联验收故障");
            if(Equals(name,"并发持锁")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),cancellationToken);}
        }
    }

    /// <summary>固定测试身份；请求头仅撤销权限，不能建立账号或项目身份。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(944,"级联读取验收",false,Project.ToString(),"1");
        public void RequirePagePermission(string pageId,string operation){if(accessor.HttpContext!.Request.Headers["denyPage"]==pageId || operation=="list"&&accessor.HttpContext.Request.Headers["denyListPage"]==pageId)throw new UnauthorizedAccessException("没有页面读取权限。");}
    }
    private static JsonElement Data(JsonElement response){Assert.Equal(200,response.GetProperty("code").GetInt32());return response.GetProperty("data");}
    /// <summary>自有随机表的固定SQL通过参数绑定准备数据，不接受HTTP输入。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;
        foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();
    }
}
