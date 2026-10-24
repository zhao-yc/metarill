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
using Metarill.Data.Relations;
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

/// <summary>固定Server-200隔离库中验证实际生成R1跨表标量缓存；不是正式网页登录或浏览器验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableNameCacheMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001,Consumer=9007199254752001;
    /// <summary>默认名称与字符串所选ID，父键分别取字符串及整数。</summary>
    [StandaloneExportMySqlFact] public Task String_default_names()=>Run("string",false);
    /// <summary>默认名称与整数所选ID，父键分别取字符串及整数。</summary>
    [StandaloneExportMySqlFact] public Task Long_default_names()=>Run("int64",false);
    /// <summary>自定义caption仍写原prefix_name，字符串所选ID不扩张前端协议。</summary>
    [StandaloneExportMySqlFact] public Task String_custom_names()=>Run("string",true);
    /// <summary>自定义caption与整数所选ID均走同一发布计划。</summary>
    [StandaloneExportMySqlFact] public Task Long_custom_names()=>Run("int64",true);
    /// <summary>固定实例pin、数据库与账号配置核对后才建立本轮随机表。</summary>
    private static async Task Run(string type,bool custom)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("跨表缓存实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建缓存样本。");}foreach(var parent in new[]{"string","int64"})await Exercise(options,type,parent,custom);
    }
    /// <summary>目标变更、跨批源缓存、下游副本及失败回滚共用同一生成服务事务。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool custom)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_ccn_"+nonce[..12];var c=CrossTableNameCacheGenerationTests.Catalog(prefix,type,parentType,custom);var source=prefix+"_source";var audit=prefix+"_audit";var value=custom?"caption":"name";var targets=new[]{"country","area","place"}.Select(x=>prefix+"_"+x).ToArray();
        // 没有软删列的末级沿原DELETE执行物理删除；来源和根仍验证逻辑删除。
        c.ConfigColumns.RemoveAll(x=>x.ConfigTableId=="5"&&x.ColumnName=="delete_no");c.ConfigTables.Add(new(){Id="7",TableName=audit,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="71",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="72",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="73",ConfigTableId="7",ColumnName="delete_no",LogicalType="datetime"},new(){Id="74",ConfigTableId="7",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="75",ConfigTableId="7",ColumnName="source_country_name",LogicalType="string",ColumnLength="50",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("source.country_name")}}]);c.ConfigPages.Add(new(){Id="701",ConfigTableId="7",WhereSql="yz_project_id=@current_yz_project_id"});foreach(var id in new[]{"74","75"})c.ConfigPageColumns.Add(new(){Id="701"+id,ConfigPageId="701",ConfigColumnId=id});
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/cross-name-cache/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-name-cache-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var guard=new NonStrict();var auth=new Auth();var observer=new Observer(source,store);WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,type,parentType,custom,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(5,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);
            object Key(long id)=>type=="string"?Id(id):id;object Parent(long id)=>parentType=="string"?Id(id):id;
            for(var group=0;group<2;group++)for(var role=0;role<3;role++){var row=D("id",First+group*10,value,new[]{"国","区","城"}[role]+group,"yz_project_id",Project,"status",1);if(role>0)row[prefix+"_"+new[]{"country","area"}[role-1]+"_id"]=Parent(First+group*10);await store.InsertAsync(targets[role],row);}
            Dictionary<string,object?> Source(long id,long project=Project)=>D("id",id,"name","来源"+id,"yz_project_id",project,"country_id",Key(First),"area_id",Key(First),"place_id",Key(First),"country_name","国0","area_name","区0","place_name","城0");
            for(var i=0;i<131;i++)await store.InsertAsync(source,Source(Consumer+i));await store.InsertAsync(source,Source(Consumer+1000,Project+1));var deleted=Source(Consumer+1001);deleted["delete_no"]=new DateTime(2026,1,1);await store.InsertAsync(source,deleted);var empty=Source(Consumer+1002);foreach(var key in new[]{"country_id","area_id","place_id"})empty[key]=null;await store.InsertAsync(source,empty);if(type=="string"){var bad=Source(Consumer+1003);bad["country_id"]=Id(First)+" ";bad["country_name"]="坏历史";await store.InsertAsync(source,bad);}await store.InsertAsync(audit,D("id",Consumer,"yz_project_id",Project,"source_id",Consumer,"source_country_name","旧下游"));
            var model=GenerationModel.Create(c,"Example.CrossCacheProbe");new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var plan=(RelationMaintenanceCatalog)assembly.GetType(model.Namespace+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(997));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(guard);builder.Services.AddScoped<IRuntimeAuthorization>(_=>auth);assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> Call(string table,string page,string action,object body,HttpMethod? method=null){using var req=new HttpRequestMessage(method??HttpMethod.Post,"/api/admin/auto/"+table+"/"+page+"/"+action){Content=JsonContent.Create(body)};using var response=await client.SendAsync(req);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            Task<JsonElement> Rename(int role,object? text)=>Call(targets[role],(role+3).ToString(CultureInfo.InvariantCulture)+"01","update",D("id",Id(First),value,text));
            async Task<Dictionary<string,object?>> Raw(long id)=>(await store.ReadIncludingDeletedAsync(source)).Single(x=>SqlRowStore.Id(x)==id);
            async Task<string> Snapshot(){var data=new List<object>();foreach(var table in c.ConfigTables.OrderBy(t=>t.TableName,StringComparer.Ordinal))data.Add(new{table.TableName,rows=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x))});return JsonSerializer.Serialize(data);}
            var added=Data(await Call(source,"201","add",new{name="实际生成新增",placeId=Id(First)}));var addedId=added.GetProperty("id").GetString()!;Assert.Equal("国0/区0/城0",added.GetProperty("placeIdDisplayText").GetString());var idsBefore=await Identifiers();var sourceNotifications=observer.SourceCount;auth.DenyConsumerWrites=true;Data(await Rename(0,"新国"));Assert.Equal(sourceNotifications,observer.SourceCount);await Names("新国","区0","城0");Assert.Equal("新国",SqlRowStore.Text(Assert.Single(await store.ReadAsync(audit)),"source_country_name"));Assert.Equal("国0",(await Raw(Consumer+1000))["country_name"]);Assert.Equal("国0",(await Raw(Consumer+1001))["country_name"]);if(type=="string")Assert.Equal("坏历史",(await Raw(Consumer+1003))["country_name"]);
            var before=await Snapshot();var count=observer.Count;Assert.Equal(500,(await Rename(0,"尾部异常")).GetProperty("code").GetInt32());Assert.True(observer.SawPatchedBeforeFailure);Assert.Equal(before,await Snapshot());Assert.Equal(count+1,observer.Count);
            guard.Enabled=true;try{var rejected=await Rename(0,new string('长',80));Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Contains("缓存在存储后",rejected.GetProperty("msg").GetString());Assert.Equal(before,await Snapshot());}finally{guard.Enabled=false;}
            Data(await Rename(0,null));await Names(null,"区0","城0");Data(await Rename(0,"终国"));await Names("终国","区0","城0");Data(await Call(targets[0],"301","",new{id=Id(First)},HttpMethod.Delete));await Names(null,"区0","城0");await store.UpdateAsync(targets[0],First,D("delete_no",null));Data(await Rename(0,"复国"));await Names("复国","区0","城0");Data(await Call(targets[2],"501","",new{id=Id(First)},HttpMethod.Delete));Assert.DoesNotContain(await store.ReadIncludingDeletedAsync(targets[2]),x=>SqlRowStore.Id(x)==First);await Names("复国","区0",null);Assert.Equal(idsBefore,await Identifiers());
            // 用后批的过长真实目标制造静默截断，首批缓存及下游必须一起回滚。
            foreach(var row in await store.ReadAsync(source,D("yz_project_id",Project)))await store.UpdateAsync(source,SqlRowStore.Id(row),D("country_name","待回填","area_name","待回填","place_name","待回填"));await store.UpdateAsync(audit,Consumer,D("source_country_name","待下游"));var late=Source(Consumer+2000);late["country_id"]=Key(First+10);await store.InsertAsync(source,late);await store.UpdateAsync(targets[0],First+10,D(value,new string('后',80)));before=await Snapshot();await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync(async()=>{await using var cmd=session.CurrentTransaction!.Connection!.CreateCommand();cmd.Transaction=session.CurrentTransaction;cmd.CommandText="SET SESSION sql_mode=''";await cmd.ExecuteNonQueryAsync();return await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2");}));Assert.Equal(before,await Snapshot());await store.UpdateAsync(targets[0],First+10,D(value,"后批正常"));
            var stable=await Identifiers();var backfill=await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2");Assert.Equal(3,backfill.JoinColumnCount);Assert.True(backfill.AffectedRows>128);Assert.Equal(stable,await Identifiers());Assert.Equal("复国",SqlRowStore.Text(Assert.Single(await store.ReadAsync(audit)),"source_country_name"));Assert.Equal(0,(await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2")).AffectedRows);Assert.Equal(sourceNotifications,observer.SourceCount);Assert.Null((await Raw(Consumer+1002))["country_name"]);if(type=="string"){Assert.Null((await Raw(Consumer+1003))["country_name"]);Assert.Equal(Id(First)+" ",(await Raw(Consumer+1003))["country_id"]);}
            passed=true;await Receipt("verified",new{nonce,type,parentType,custom,passed,realGeneratedHttp=true,realMySql=true,scalarCaches=3,activeSources=132,downstream=true,noConsumerHooks=true,softAndPhysicalDelete=true,nonStrictRollback=true,lateBackfillRollback=true,idPreserved=true,repeatBackfillZero=true,formalWeb=false,browser=false});
            /// <summary>只比较外键、业务与审计字段，缓存变化不能掩盖ID或来源内容被改写。</summary>
            async Task<string> Identifiers()=>JsonSerializer.Serialize((await store.ReadIncludingDeletedAsync(source)).OrderBy(x=>SqlRowStore.Id(x)).Select(x=>x.Where(p=>p.Key is not("country_name" or "area_name" or "place_name")).ToDictionary(p=>p.Key,p=>p.Value)));
            /// <summary>分批消费者与新生成记录都应读到各角色的独立名称。</summary>
            async Task Names(string? country,string? area,string? place){foreach(var row in (await store.ReadAsync(source,D("yz_project_id",Project))).Where(x=>SqlRowStore.Id(x)<Consumer+1000||SqlRowStore.Id(x).ToString(CultureInfo.InvariantCulture)==addedId)){Assert.Equal(country,row["country_name"]);Assert.Equal(area,row["area_name"]);Assert.Equal(place,row["place_name"]);}}
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,custom,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>通知位于缓存传播之后；故障必须回滚目标、来源和下游，而非重入消费者Service。</summary>
    private sealed class Observer(string source,SqlRowStore store):IRecordMutationObserver
    {
        internal int Count,SourceCount;internal bool SawPatchedBeforeFailure;
        /// <summary>只对固定测试标志验证顺序和整笔回滚。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default){Count++;if(c.TableName==source)SourceCount++;if(c.SubmittedFields.Values.Any(x=>Equals(x,"尾部异常"))){SawPatchedBeforeFailure=(await store.ReadAsync(source,D("id",Consumer))).Single()["country_name"] is string text&&text=="尾部异常";throw new InvalidOperationException("缓存测试尾部异常");}}
    }
    /// <summary>非严格模式只限当前变更连接，释放后由驱动重置。</summary>
    private sealed class NonStrict:IRecordMutationGuard
    {
        internal bool Enabled;
        /// <summary>不改变准备阶段的身份或元数据。</summary>
        public Task BeforeMutationAsync(RecordMutationPreparation p,CancellationToken token=default)=>Task.CompletedTask;
        /// <summary>测试静默截断，不修改服务器全局模式。</summary>
        public async Task BeforeWriteAsync(RecordMutationValidation p,CancellationToken token=default){if(!Enabled)return;await using var cmd=p.Transaction.Connection!.CreateCommand();cmd.Transaction=p.Transaction;cmd.CommandText="SET SESSION sql_mode=''";await cmd.ExecuteNonQueryAsync(token);}
    }
    /// <summary>固定身份仅用于生成HTTP测试；可撤消费者写权限，不能从客户端传入项目或图。</summary>
    private sealed class Auth:IRuntimeAuthorization
    {
        internal bool DenyConsumerWrites;
        /// <summary>项目和后台固定在宿主。</summary>
        public RuntimeActor RequireActor()=>new(997,"跨表缓存实库",false,Id(Project),"5");
        /// <summary>内部传播不得借消费者的页面写入口。</summary>
        public void RequirePagePermission(string page,string operation){if(DenyConsumerWrites&&page is "201" or "701"&&operation is "create" or "update" or "delete")throw new UnauthorizedAccessException("没有消费者写权限。");}
    }
    /// <summary>原R成功码是读取data的前提。</summary>
    private static JsonElement Data(JsonElement value){Assert.Equal(200,value.GetProperty("code").GetInt32());return value.GetProperty("data");}
    /// <summary>所有业务数据参数化，ID不经过浮点数。</summary>
    private static Dictionary<string,object?> D(params object?[] values){var d=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)d.Add((string)values[i]!,values[i+1]);return d;}
    /// <summary>长ID按原接口以字符串返回。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>只有本轮已核对所有权、结构和行摘要的随机表可清理。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=sql;await cmd.ExecuteNonQueryAsync();}
}
