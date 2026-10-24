using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
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

/// <summary>同表派生名称的真实生成HTTP／Server-200双表，固定授权器不代表正式Web登录。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SameTableDerivedTreeMySqlTests
{
    private const long Project=9007199254740993,First=9007199254745001;
    /// <summary>独立书签四种叶子／父键，验证正常DML后的自身名称和完整路径。</summary>
    [StandaloneExportMySqlFact]
    public async Task Derived_bookmark_reads_current_stored_self_and_parent_graph()
    {var options=await Options();foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"})await Exercise(options,type,parent,false);}
    /// <summary>直接父控件两种类型，原拓扑约束与派生名称当前读共同生效。</summary>
    [StandaloneExportMySqlFact]
    public async Task Derived_parent_keeps_cycle_rollback_and_real_name_locks()
    {var options=await Options();foreach(var type in new[]{"string","int64"})await Exercise(options,type,type,true);}
    /// <summary>实例、账号配置和隔离库固定后才允许创建本轮随机表。</summary>
    private static async Task<DatabaseOptions> Options()
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var o=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(o.ConnectionString);if(o.Provider!="mysql"||o.ExpectedDatabase!="metarill_business_test"||cs.Database!=o.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("同表派生验收只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var c=new MySqlConnection(o.ConnectionString);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==o.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");return o;
    }
    /// <summary>每组双表经元数据显式同步，失败保留首因，仅清理结构和全量行摘要均一致的自有样本。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool direct)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_sdt_"+nonce[..12];var table=prefix+"_node";var company=prefix+"_company";var names=new[]{table,company};var catalog=SameTableDerivedTreeTests.Catalog(prefix,type,parentType,direct,true);var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/same-derived-tree/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-same-derived-mysql");var directory=Path.Combine(boundary,nonce);var observer=new Observer();var snapshot=new Snapshot(options,company,Company(First+1));using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,catalog);WebApplication? app=null;var created=false;var passed=false;var field=direct?"parentId":"bookmarkId";var label=direct?"parentName":"bookmarkName";var column=direct?"parent_id":"bookmark_id";var control=direct?"5":"6";var page=direct?"202":"201";
        await Receipt("intent",new{nonce,type,parentType,direct,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(2,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);var columns=(await schema.InspectAsync(catalog)).Tables.Single(t=>t.Name==table).Columns;Assert.DoesNotContain(columns,c=>c.Name=="display_label"||c.Name==column[..^3]+"_name");
            async Task Seed(long id,string tag,string name,long? parent=null,int status=1,long project=Project)
            {await store.InsertAsync(company,D("id",Company(id),"name",name,"yz_project_id",project));var values=D("id",id,"name",tag,"company_id",Company(id),"parent_id",Key(parent,parentType),"yz_project_id",project,"status",status,"amount",1m,"code","é");if(!direct)values.Add("bookmark_id",null);await store.InsertAsync(table,values);}
            await Seed(First,"可根","根公司");await Seed(First+1,"可自身","节点公司",First);await Seed(First+2,"可后代","后代公司",First+1);await Seed(First+3,"可另根","另根公司");await Seed(First+10,"可隐藏","隐藏公司",First,0);await Seed(First+20,"可外部","外部公司",null,project:Project+1);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SameDerivedProbe");var entity=GenerationModel.Pascal(table);var self=(direct?parentType:type)=="string"?"entity.Id.ToString(CultureInfo.InvariantCulture)":"entity.Id";
            await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using System.Globalization;
using Metarill.Core.Security;
namespace Example.SameDerivedProbe.Generated.Services;
public partial class {{entity}}Service
{
    /// <summary>仅测试受信规则显式提交父键，最终拓扑必须能拒绝数据库中形成的环。</summary>
    partial void BeforeSave(Example.SameDerivedProbe.Generated.Entities.{{entity}} entity,RuntimeActor actor,bool creating,ISet<string> submitted)
    {
        if(entity.Name=="可造环"){entity.ParentId={{Id(First+2,parentType)}};submitted.Add("parent_id");}
        if(creating&&entity.Name=="可新自指"){entity.{{(direct?"ParentId":"BookmarkId")}}={{self}};submitted.Add({{JsonSerializer.Serialize(column)}});}
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(974));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<IRuntimeAuthorization>(p=>new Auth(p.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.SameDerivedProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(ex.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+table+"/"+page+"/";
            async Task<JsonElement> Post(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action,body)).Content.ReadAsStringAsync());
            async Task<string> State(){using var other=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(other,catalog).ReadIncludingDeletedAsync(table)).OrderBy(x=>SqlRowStore.Id(x)));}
            var own=(First+1).ToString(CultureInfo.InvariantCulture);var rootId=First.ToString(CultureInfo.InvariantCulture);var added=Data(await Post("add",D("name","可新增","companyId",Company(First).ToString(CultureInfo.InvariantCulture),field,own,label,"伪造名称")));var addedId=added.GetProperty("id").GetString()!;Assert.Equal("节点公司",added.GetProperty(label).GetString());Assert.Equal("根公司/节点公司",added.GetProperty(field+"DisplayText").GetString());
            var path=Data(await Post("cascadeTreePath"+control,new{leafId=own}));Assert.Equal("根公司/节点公司",path.GetProperty("displayText").GetString());var roots=Data(await Post("cascadeTreeOptions"+control,new{levelKey="selfTree-0",selectedPath=new{parentId=""}}));Assert.Contains(roots.EnumerateArray(),x=>x.GetProperty("id").GetString()==rootId);
            if(!direct)
            {
                var changed=Data(await Post("update",new{id=own,name="可物理标识",companyId=Company(First+3).ToString(CultureInfo.InvariantCulture),bookmarkId=own}));Assert.Equal("另根公司",changed.GetProperty(label).GetString());Assert.Equal("根公司/另根公司",changed.GetProperty(field+"DisplayText").GetString());var restored=Data(await Post("update",new{id=own,companyId=Company(First+1).ToString(CultureInfo.InvariantCulture)}));Assert.Equal("节点公司",restored.GetProperty(label).GetString());
                var enabled=Data(await Post("update",new{id=(First+10).ToString(CultureInfo.InvariantCulture),status=1,companyId=Company(First+3).ToString(CultureInfo.InvariantCulture),bookmarkId=(First+10).ToString(CultureInfo.InvariantCulture)}));Assert.Equal("另根公司",enabled.GetProperty(label).GetString());
                var before=await State();Assert.Equal(500,(await Post("update",new{id=own,status=0,bookmarkId=own})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            }
            foreach(var bad in new object[]{D("id",own,"name","可造环"),D("id",own,field,(First+20).ToString(CultureInfo.InvariantCulture)),D("name","可新自指","companyId",Company(First).ToString(CultureInfo.InvariantCulture))}){var before=await State();Assert.Equal(500,(await Post(bad is Dictionary<string,object?> x&&x.ContainsKey("id")?"update":"add",bad)).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            snapshot.Change=true;var current=Data(await Post("update",D("id",addedId,"name","可旧快照",field,own)));Assert.Equal("RR节点公司",current.GetProperty(label).GetString());Assert.Equal("根公司/RR节点公司",current.GetProperty(field+"DisplayText").GetString());
            client.DefaultRequestHeaders.Add("denyTarget","1");Assert.Equal(500,(await Post("update",D("id",addedId,field,own))).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("denyTarget");var copiedId=Data(await Post("copy/"+addedId,new{})).GetProperty("id").GetString()!;
            // 原导入只要求import权限；路径按派生名称反查，后行失败必须整批回滚。
            Dictionary<string,object?> Import(string tag,string name)=>D("节点名",tag,direct?"父节点":"书签",name);
            if(!direct)
            {
                // 新增和复制都是独立根且复用根公司，先证明同名根仍被原歧义保护拒绝。
                var repeated=Data(await Post("cascadeTreeOptions"+control,new{levelKey="selfTree-0",selectedPath=new{parentId=""}}));Assert.Equal(3,repeated.EnumerateArray().Count(row=>row.GetProperty("name").GetString()=="根公司"));var ambiguous=await State();client.DefaultRequestHeaders.Add("importOnly","1");var rejected=await Post("importDatas"+page,new{requestJson=new[]{Import("可歧义","根公司")}});await Receipt("ambiguous-import",new{code=rejected.GetProperty("code").GetInt32(),message=rejected.GetProperty("msg").GetString()});Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Equal(ambiguous,await State());client.DefaultRequestHeaders.Remove("importOnly");
                // 修改真实FK去除根名称冲突，省略书签时名称仍实时计算，不手写回填。
                foreach(var rootRecord in new[]{addedId,copiedId})Data(await Post("update",new{id=rootRecord,companyId=Company(First+2).ToString(CultureInfo.InvariantCulture)}));
            }
            client.DefaultRequestHeaders.Add("importOnly","1");var imported=await Post("importDatas"+page,new{requestJson=new[]{Import("可导入","根公司")}});await Receipt("valid-import",new{code=imported.GetProperty("code").GetInt32(),message=imported.GetProperty("msg").GetString()});Assert.Equal(200,imported.GetProperty("code").GetInt32());var importState=await State();Assert.Equal(500,(await Post("importDatas"+page,new{requestJson=new[]{Import("可回滚","根公司"),Import("可坏路径","不存在")}})).GetProperty("code").GetInt32());Assert.Equal(importState,await State());client.DefaultRequestHeaders.Remove("importOnly");
            var failed=await State();Assert.Equal(500,(await Post("add",D("name","可拒绝","companyId",Company(First).ToString(CultureInfo.InvariantCulture),field,own))).GetProperty("code").GetInt32());Assert.Equal(failed,await State());
            var pending=Post("add",D("name","可持锁","companyId",Company(First).ToString(CultureInfo.InvariantCulture),field,own));try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var tx=await independent.BeginTransactionAsync();await using var update=independent.CreateCommand();update.Transaction=tx;update.CommandText="UPDATE `"+company+"` SET name='竞争名称' WHERE id=@id";update.Parameters.AddWithValue("id",Company(First+1));Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>update.ExecuteNonQueryAsync())).Number);await tx.RollbackAsync();}finally{observer.Release.TrySetResult();await pending;}Data(await pending);
            passed=true;await Receipt("verified",new{nonce,type,parentType,direct,passed,realGeneratedHttp=true,realMySql=true,sourceNameIndependent=true,oldRrCurrentRead=true,currentNameLock=true,importRollback=true,cycleRollback=true,browser=false,formalWeb=false});
        }
        catch(Exception ex){await Receipt("failure",new{nonce,type,parentType,direct,error=ex.GetType().Name,ex.Message,ex.StackTrace,passed=false});throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var state=await new SchemaService(cleanup).InspectAsync(catalog);if(state.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(catalog)).Changes);Assert.Equal(new[]{prefix+"_node",prefix+"_company"},names);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,catalog).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(state),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(state),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(catalog)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,catalog).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value)+"\n");
    }
    /// <summary>旧RR仅影响本次写准备，独立改名只触及本nonce公司记录。</summary>
    private sealed class Snapshot(DatabaseOptions options,string table,long id):IRecordMutationGuard
    {
        internal bool Change;
        public async Task BeforeMutationAsync(RecordMutationPreparation context,CancellationToken token=default)
        {if(!Change||context.Operation!=RecordMutationOperation.Update)return;Change=false;await using(var read=context.Transaction.Connection!.CreateCommand()){read.Transaction=context.Transaction;read.CommandText="SELECT name FROM `"+table+"` WHERE id=@id";var p=read.CreateParameter();p.ParameterName="id";p.Value=id;read.Parameters.Add(p);await read.ExecuteScalarAsync(token);}await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync(token);await using var update=c.CreateCommand();update.CommandText="UPDATE `"+table+"` SET name='RR节点公司' WHERE id=@id";update.Parameters.AddWithValue("id",id);Assert.Equal(1,await update.ExecuteNonQueryAsync(token));}
    }
    /// <summary>只在原提交前观察器中暂停或失败，不在无关阶段伪造成功。</summary>
    private sealed class Observer:IRecordMutationObserver
    {
        internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"可拒绝"))throw new InvalidOperationException("测试观察器拒绝");if(Equals(name,"可持锁")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),token);}}
    }
    /// <summary>身份固定在宿主，HTTP头只撤权，不接受客户端项目或身份。</summary>
    private sealed class Auth(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(974,"同表派生验收",false,Project.ToString(CultureInfo.InvariantCulture),"5");
        public void RequirePagePermission(string page,string operation){var h=accessor.HttpContext!.Request.Headers;if(page=="202"&&operation=="list"&&(h.ContainsKey("denyTarget")||h.ContainsKey("importOnly"))||h.ContainsKey("importOnly")&&operation is "create" or "update")throw new UnauthorizedAccessException("没有指定操作权限。");}
    }
    /// <summary>先验证R，不把错误正文误当成正常数据。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.TryGetProperty("code",out var code),"响应不是原R合同，原始请求／阶段保留在测试源码与TRX。");Assert.Equal(200,code.GetInt32());return value.GetProperty("data");}
    /// <summary>公司ID与节点ID固定一一对应，不使用隐式数值转换。</summary>
    private static long Company(long node)=>checked(node+100000L);
    /// <summary>父键和叶子键按各自声明类型编码。</summary>
    private static object? Key(long? id,string type)=>id is null?null:type=="string"?id.Value.ToString(CultureInfo.InvariantCulture):id.Value;
    /// <summary>写入partial的仅是本测试静态常量。</summary>
    private static string Id(long id,string type)=>type=="string"?JsonSerializer.Serialize(id.ToString(CultureInfo.InvariantCulture)):id.ToString(CultureInfo.InvariantCulture)+"L";
    /// <summary>所有字段来自固定元数据，参数值不拼入SQL。</summary>
    private static Dictionary<string,object?> D(params object?[] pairs){var d=new Dictionary<string,object?>();for(var i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i]!,pairs[i+1]);return d;}
    /// <summary>仅执行明确的自有样本清理语句。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
}
