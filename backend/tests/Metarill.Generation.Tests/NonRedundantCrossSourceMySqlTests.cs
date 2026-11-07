using System.Globalization;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
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

/// <summary>真实生成HTTP验证来源三个层位，非冗余路径名称实时读取，不注入正式Web验收结论。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantCrossSourceMySqlTests
{
    private const long Project=9007199254740993,First=9007199254745001;
    /// <summary>来源根层物理名称，四种叶子／父键。</summary>
    [StandaloneExportMySqlFact] public Task Root_physical()=>Run(0,false);
    /// <summary>来源中层物理名称，四种叶子／父键。</summary>
    [StandaloneExportMySqlFact] public Task Middle_physical()=>Run(1,false);
    /// <summary>来源末层物理名称，四种叶子／父键。</summary>
    [StandaloneExportMySqlFact] public Task Leaf_physical()=>Run(2,false);
    /// <summary>来源根层独立联接名称，四种叶子／父键。</summary>
    [StandaloneExportMySqlFact] public Task Root_derived()=>Run(0,true);
    /// <summary>来源中层独立联接名称，四种叶子／父键。</summary>
    [StandaloneExportMySqlFact] public Task Middle_derived()=>Run(1,true);
    /// <summary>来源末层独立联接名称，四种叶子／父键。</summary>
    [StandaloneExportMySqlFact] public Task Leaf_derived()=>Run(2,true);
    /// <summary>配置和实例pin固定后才创建本轮随机样本，不连接生产业务库。</summary>
    private static async Task Run(int level,bool derived)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("来源图实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建表。");}
        foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"})await Exercise(options,level,derived,type,parent);
    }
    /// <summary>元数据显式同步，所有业务校验使用生成端点；失败保留首因和精确清理收据。</summary>
    private static async Task Exercise(DatabaseOptions options,int level,bool derived,string type,string parentType)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrcs_"+nonce[..12];var c=NonRedundantCrossSourceTests.Catalog(prefix,level,type,parentType,derived);if(level>0&&parentType=="string")c.ConfigColumns.Single(x=>x.Id=="88").ColumnLength="16";var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/nonredundant-cross-source/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-source-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();var source=prefix+"_source";var company=prefix+"_company";var roles=new[]{"country","area","place"};var targets=roles.Select((role,index)=>index==level?source:prefix+"_"+role).ToArray();var observer=new Observer();var snapshot=new Snapshot(options,company,Company(First+51));using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,level,derived,type,parentType,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var plan=await schema.PreviewAsync(c);Assert.Equal(derived?4:3,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);var physical=(await schema.InspectAsync(c)).Tables.Single(t=>t.Name==source).Columns;Assert.DoesNotContain(physical,x=>x.Name is "country_name" or "area_name" or "place_name" or "display_label");
            object Key(long id)=>parentType=="string"?id.ToString(CultureInfo.InvariantCulture):id;
            for(var group=0;group<2;group++)for(var index=0;index<3;index++)
            {
                var id=First+group*10+index;var text=(group==0?"甲":"乙")+new[]{"根","区","城"}[index];var row=D("id",id,"name",index==level&&derived?"业务"+text:text,"yz_project_id",Project,"status",1);if(index>0)row[prefix+"_"+roles[index-1]+"_id"]=Key(id-1);if(index==level){row["code"]="e";row["amount"]=1m;if(derived){await store.InsertAsync(company,D("id",Company(id),"name",text,"yz_project_id",Project));row["company_id"]=Company(id);}}await store.InsertAsync(targets[index],row);
            }
            if(derived)foreach(var value in new[]{(50,"派生新名"),(51,"派生次名")})await store.InsertAsync(company,D("id",Company(First+value.Item1),"name",value.Item2,"yz_project_id",Project));
            var longParent=long.Parse((First+level-1).ToString(CultureInfo.InvariantCulture)+"1",CultureInfo.InvariantCulture);
            if(level>0&&parentType=="string"){var longRow=D("id",longParent,"name","长父","yz_project_id",Project,"status",1);if(level>1)longRow[prefix+"_country_id"]=Key(First);await store.InsertAsync(targets[level-1],longRow);}
            var model=GenerationModel.Create(c,"Example.NrSourceGraph");var table=model.Tables.Single(t=>t.Source.Id=="2");var ownField=table.Columns.Single(x=>x.Source.ColumnName==roles[level]+"_id");new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);
            var truncationHook=level>0&&parentType=="string"?"if(!creating&&entity.Name==\"截断父键\"){entity."+table.Columns.Single(x=>x.Source.Id=="88").Property+"="+JsonSerializer.Serialize(longParent.ToString(CultureInfo.InvariantCulture))+";submitted.Add("+JsonSerializer.Serialize(c.ConfigColumns.Single(x=>x.Id=="88").ColumnName)+");}":"";
            await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using System.Globalization;
using Metarill.Core.Security;
namespace Example.NrSourceGraph.Generated.Services;
public sealed partial class {{table.Name}}Service
{
    /// <summary>仅测试受信钩子的新自身引用，最终存储不允许借新节点构造路径。</summary>
    partial void BeforeSave(Example.NrSourceGraph.Generated.Entities.{{table.Name}} entity,RuntimeActor actor,bool creating,ISet<string> submitted)
    {
        {{truncationHook}}
        if(creating&&entity.Name=="新自身"){entity.{{ownField.Property}}={{(ownField.Type=="string?"?"entity.Id.ToString(CultureInfo.InvariantCulture)":"entity.Id")}};submitted.Add({{JsonSerializer.Serialize(ownField.Source.ColumnName)}});}
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(978));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<IRuntimeAuthorization>(x=>new Auth(x.GetRequiredService<IHttpContextAccessor>()));assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(ex.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/201/";
            async Task<JsonElement> Post(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action,body)).Content.ReadAsStringAsync());
            async Task<string> State(){using var other=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(other,c).ReadIncludingDeletedAsync(source)).OrderBy(x=>SqlRowStore.Id(x)));}
            string Expected(string label)=>string.Join("/",new[]{"甲根","甲区","甲城"}.Select((name,index)=>index==level?label:name));
            var own=(First+level).ToString(CultureInfo.InvariantCulture);var leaf=(First+2).ToString(CultureInfo.InvariantCulture);var added=Data(await Post("add",D("name","新来源","placeId",leaf)));Assert.Equal("甲根/甲区/甲城",added.GetProperty("placeIdDisplayText").GetString());
            var update=D("id",own,"name","显式新名","placeId",leaf);if(derived)update["companyId"]=Company(First+50).ToString(CultureInfo.InvariantCulture);var changed=Data(await Post("update",update));var label=derived?"派生新名":"显式新名";Assert.Equal(Expected(label),changed.GetProperty("placeIdDisplayText").GetString());
            var omitted=Data(await Post("update",new{id=own,name="省略改名"}));if(!derived)label="省略改名";Assert.Equal(Expected(label),omitted.GetProperty("placeIdDisplayText").GetString());
            if(derived){var fk=Data(await Post("update",new{id=own,companyId=Company(First+51).ToString(CultureInfo.InvariantCulture)}));label="派生次名";Assert.Equal(Expected(label),fk.GetProperty("placeIdDisplayText").GetString());snapshot.Change=true;var rr=Data(await Post("update",D("id",own,"placeId",leaf)));label="RR源名";Assert.Equal(Expected(label),rr.GetProperty("placeIdDisplayText").GetString());}
            foreach(var bad in new object[]{new{id=own,status=0},new{id=own,amount=0.014m},new{id=own,creating=true},new{name="新自身"}}){var before=await State();var count=observer.Count;var response=await Post(bad.GetType().GetProperty("id") is null?"add":"update",bad);Assert.Equal(500,response.GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(count,observer.Count);}
            var rounded=Data(await Post("update",new{id=own,code="é",amount=0.015m}));Assert.Equal(0.02m,rounded.GetProperty("amount").GetDecimal());using(var other=new DatabaseSession(options))Assert.Equal(0.02m,Convert.ToDecimal((await new SqlRowStore(other,c).ReadIncludingDeletedAsync(source)).Single(x=>SqlRowStore.Id(x).ToString(CultureInfo.InvariantCulture)==own)["amount"],CultureInfo.InvariantCulture));
            if(level>0)
            {
                var parent=table.Columns.Single(x=>x.Source.Id=="88");var moved=Data(await Post("update",D("id",own,parent.JsonName,(First+10+level-1).ToString(CultureInfo.InvariantCulture),"placeId",leaf)));Assert.Equal(string.Join("/",Enumerable.Range(0,3).Select(index=>index<level?"乙"+new[]{"根","区","城"}[index]:index==level?label:"甲城")),moved.GetProperty("placeIdDisplayText").GetString());
                var restored=Data(await Post("update",D("id",own,parent.JsonName,(First+level-1).ToString(CultureInfo.InvariantCulture))));Assert.Equal(Expected(label),restored.GetProperty("placeIdDisplayText").GetString());var before=await State();Assert.Equal(500,(await Post("update",D("id",own,parent.JsonName,"8999999999999999"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            }
            if(level>0&&parentType=="string")
            {
                var before=await State();var notifications=observer.Count;snapshot.NonStrict=true;
                try{var rejected=await Post("update",new{id=own,name="截断父键"});Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Contains("结构外键在存储后",rejected.GetProperty("msg").GetString());Assert.Equal(before,await State());Assert.Equal(notifications,observer.Count);}
                finally{snapshot.NonStrict=false;}
            }
            var preObserver=await State();Assert.Equal(500,(await Post("update",D("id",own,"name","通知故障","placeId",leaf))).GetProperty("code").GetInt32());Assert.Equal(preObserver,await State());
            var holdBody=D("id",own,"name","锁持有");if(derived)holdBody["companyId"]=Company(First+51).ToString(CultureInfo.InvariantCulture);var held=Post("update",holdBody);try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var other=new MySqlConnection(options.ConnectionString);await other.OpenAsync();await using(var timeout=other.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var tx=await other.BeginTransactionAsync();await using var cmd=other.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE `"+(derived?company:targets[level==0?1:0])+"` SET name=name WHERE id=@id";cmd.Parameters.AddWithValue("id",derived?Company(First+51):First+(level==0?1:0));Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>cmd.ExecuteNonQueryAsync())).Number);await tx.RollbackAsync();}finally{observer.Release.TrySetResult();await held;}Data(await held);if(!derived)label="锁持有";
            var copied=Data(await Post("copy/"+added.GetProperty("id").GetString(),new{}));Assert.NotEqual(added.GetProperty("id").GetString(),copied.GetProperty("id").GetString());Assert.Equal(Expected(label),copied.GetProperty("placeIdDisplayText").GetString());Assert.Single(Data(await Post("selection/selected",new{selectedIds=new[]{own}})).EnumerateArray());
            var page=Data(await Post("page",new{pageNo=2,pageSize=1,filters=new{placeId=new{countryId=First.ToString(CultureInfo.InvariantCulture),countryName="忽略客户端名称"}}}));Assert.Equal(3L,Convert.ToInt64(page.GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Single(page.GetProperty("records").EnumerateArray());Assert.Equal(Expected(label),page.GetProperty("records")[0].GetProperty("placeIdDisplayText").GetString());var empty=Data(await Post("page",new{pageNo=99,pageSize=1,filters=new{placeId=new{countryId=First.ToString(CultureInfo.InvariantCulture)}}}));Assert.Equal(3L,Convert.ToInt64(empty.GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Empty(empty.GetProperty("records").EnumerateArray());
            client.DefaultRequestHeaders.Add("denyTargets","1");var beforeDenied=await State();Assert.Equal(500,(await Post("update",D("id",own,"placeId",leaf))).GetProperty("code").GetInt32());Assert.Equal(beforeDenied,await State());Assert.Equal("",Data(await Post("update",new{id=own,placeId=(string?)null,areaId=(string?)null,countryId=(string?)null})).GetProperty("placeIdDisplayText").GetString());client.DefaultRequestHeaders.Remove("denyTargets");
            using(var other=new DatabaseSession(options))foreach(var row in await new SqlRowStore(other,c).ReadIncludingDeletedAsync(source)){Assert.True(roles.Count(role=>row.TryGetValue(role+"_id",out var value)&&value is not null&&Convert.ToString(value,CultureInfo.InvariantCulture)!="")<=1,"只允许一个真实ID位，不能缓存祖先。");}
            passed=true;await Receipt("verified",new{nonce,level,derived,type,parentType,passed,realGeneratedHttp=true,realMySql=true,sourceGraph=true,sparseStorage=true,latestNames=true,realNameLock=true,oldRr=derived,rollback=true,formalWeb=false,browser=false});
        }
        catch(Exception ex){await Receipt("failure",new{nonce,level,derived,type,parentType,error=ex.GetType().Name,ex.Message,ex.StackTrace});throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var state=await new SchemaService(cleanup).InspectAsync(c);if(state.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(state),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(state),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var conn=cleanup.Provider.CreateConnection(options);await conn.OpenAsync();foreach(var name in names){await using var cmd=conn.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var arg=cmd.CreateParameter();arg.ParameterName="name";arg.Value=name;cmd.Parameters.Add(arg);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value)+"\n");
    }
    /// <summary>旧RR由同一写事务建立，公司改名走独立连接且仅触及本nonce行。</summary>
    private sealed class Snapshot(DatabaseOptions options,string table,long id):IRecordMutationGuard
    {
        internal bool Change;internal bool NonStrict;
        /// <summary>只影响本次测试事务连接，驱动释放时正常重置，不修改服务器全局模式。</summary>
        public async Task BeforeWriteAsync(RecordMutationValidation p,CancellationToken ct=default){if(!NonStrict)return;await using var cmd=p.Transaction.Connection!.CreateCommand();cmd.Transaction=p.Transaction;cmd.CommandText="SET SESSION sql_mode=''";await cmd.ExecuteNonQueryAsync(ct);}
        public async Task BeforeMutationAsync(RecordMutationPreparation p,CancellationToken ct=default){if(!Change||p.Operation!=RecordMutationOperation.Update)return;Change=false;await using(var read=p.Transaction.Connection!.CreateCommand()){read.Transaction=p.Transaction;read.CommandText="SELECT name FROM `"+table+"` WHERE id=@id";var arg=read.CreateParameter();arg.ParameterName="id";arg.Value=id;read.Parameters.Add(arg);await read.ExecuteScalarAsync(ct);}await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE `"+table+"` SET name='RR源名' WHERE id=@id";cmd.Parameters.AddWithValue("id",id);Assert.Equal(1,await cmd.ExecuteNonQueryAsync(ct));}
    }
    /// <summary>通知仅在正常写后观察，故障必须回滚整个原事务。</summary>
    private sealed class Observer:IRecordMutationObserver
    {
        internal int Count;internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task BeforeCommitAsync(RecordMutationContext p,CancellationToken ct=default){Interlocked.Increment(ref Count);if(!p.SubmittedFields.TryGetValue("name",out var value))return;if(Equals(value,"通知故障"))throw new InvalidOperationException("测试通知故障");if(Equals(value,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),ct);}}
    }
    /// <summary>身份固定在宿主，头只模拟目标撤权，不接受客户端项目。</summary>
    private sealed class Auth(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(978,"非冗余来源图",false,Project.ToString(CultureInfo.InvariantCulture),"5");
        public void RequirePagePermission(string page,string operation){if(page!="201"&&operation=="list"&&accessor.HttpContext!.Request.Headers.ContainsKey("denyTargets"))throw new UnauthorizedAccessException("没有目标查询权限。");}
    }
    /// <summary>只从原R成功数据读取，保留错误而不将其当成空列表。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.TryGetProperty("code",out var code),"响应不是原R合同。");Assert.Equal(200,code.GetInt32());return value.GetProperty("data");}
    /// <summary>名称表ID与节点ID不混用。</summary>
    private static long Company(long id)=>checked(id+100000);
    /// <summary>字段名来自固定测试模型，业务值不拼入SQL。</summary>
    private static Dictionary<string,object?> D(params object?[] pairs){var d=new Dictionary<string,object?>();for(var i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i]!,pairs[i+1]);return d;}
    /// <summary>仅用于已核对结构与全量行摘要的本轮自有表清理。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
}
