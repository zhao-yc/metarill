using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Relations;
using Metarill.Data.Schema;
using Metarill.Runtime.Maintenance;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1自树物理名称与混用的真实生成HTTP、统一传播及原维护接口，不代替正式宿主身份。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RedundantPhysicalSelfTreeMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    /// <summary>普通R1目标，四种来源／父键。</summary>
    [StandaloneExportMySqlFact]public Task Single()=>Run("single");
    /// <summary>R0目标的物理名称仍可以作为缓存终点。</summary>
    [StandaloneExportMySqlFact]public Task Physical_r0_target()=>Run("r0");
    /// <summary>自定义caption不改变原_name字段合同。</summary>
    [StandaloneExportMySqlFact]public Task Custom_caption()=>Run("caption");
    /// <summary>同页双自树各自维护缓存与清空。</summary>
    [StandaloneExportMySqlFact]public Task Two_self_trees()=>Run("double");
    /// <summary>自树、跨表及多选同页共同变更且保持整笔事务。</summary>
    [StandaloneExportMySqlFact]public Task Mixed_controls()=>Run("mixed");
    /// <summary>自表书签、路径及名称传播使用同一已存记录。</summary>
    [StandaloneExportMySqlFact]public Task Same_table()=>Run("same");
    /// <summary>普通共享跨表五父页保留独立自树。</summary>
    [StandaloneExportMySqlFact]public Task Shared_parents()=>Run("shared");
    /// <summary>全局共享目标不改变自树和来源项目归属。</summary>
    [StandaloneExportMySqlFact]public Task Global_shared_parents()=>Run("globalShared");
    /// <summary>每个键型有独立nonce表；建表之前核对数据库、域名与服务器实例pin。</summary>
    private static async Task Run(string mode)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("自树专项只允许Server-200隔离业务库。");
        foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"}){await Pin(options);await Exercise(options,type,parent,mode);}
    }
    /// <summary>正常生成代码处理CRUD、缓存传播和维护；仅自有原始数据构造跨项目及晚批失败反例。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parent,string mode)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_rst_"+nonce[..12];var c=RedundantPhysicalSelfTreeTests.Catalog(prefix,type,parent,mode);var source=prefix+"_"+(mode=="same"?"node":"source");
        c.ConfigButtons.AddRange([JsonSerializer.SerializeToElement(new{id="91001",sql="/admin/sys/system_maintenance/tables"}),JsonSerializer.SerializeToElement(new{id="91002",sql="/admin/sys/system_maintenance/backfill_join_columns"})]);
        var model=GenerationModel.Create(c,"Example.R1SelfProbe");var sourceModel=model.Tables.Single(t=>t.Source.TableName==source);var page=sourceModel.Pages.Single(p=>p.Source.Id=="201");var self=CascadeTreeWriteCompilation.ForService(model,sourceModel,page).OrderBy(p=>p.Read.Source.Source.Id,StringComparer.Ordinal).First();var target=self.Read.Target;var targetPage=self.Read.TargetPage;var field=self.Read.Source;var cache=self.Name;var value=self.Read.Name;var parentColumn=self.Read.Parent;
        c.ConfigColumns.Single(x=>x.Id==value.Source.Id).ColumnLength="200";var physicalDelete=mode=="caption"&&type=="int64"&&parent=="string";if(physicalDelete){var deleted=c.ConfigColumns.Single(x=>x.ConfigTableId==target.Source.Id&&x.ColumnName=="delete_no");c.ConfigColumns.Remove(deleted);c.ConfigPageColumns.RemoveAll(f=>f.ConfigColumnId==deleted.Id);}
        model=GenerationModel.Create(c,model.Namespace);sourceModel=model.Tables.Single(t=>t.Source.TableName==source);page=sourceModel.Pages.Single(p=>p.Source.Id=="201");self=CascadeTreeWriteCompilation.ForService(model,sourceModel,page).OrderBy(p=>p.Read.Source.Source.Id,StringComparer.Ordinal).First();target=self.Read.Target;targetPage=self.Read.TargetPage;field=self.Read.Source;cache=self.Name;value=self.Read.Name;parentColumn=self.Read.Parent;
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/redundant-self-tree/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-self-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var observer=new Observer(source,cache.Source.ColumnName,store);var guard=new NonStrict();var auth=new Auth();WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,type,parent,mode,physicalDelete,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);
            // 树、跨表各角色均由本轮元数据建表，按各自声明的物理FK类型种子化。
            object ParentValue(long id)=>parentColumn.Type=="string?"?Id(id):id;string NodeName(string text)=>mode=="same"?"可"+text:text;
            foreach(var (nodeId,label,up) in new[]{(First,"根",(long?)null),(First+1,"叶",(long?)First),(First+2,"另一根",(long?)null)})
            {
                var row=D("id",nodeId,"yz_project_id",Project,value.Source.ColumnName,NodeName(label),parentColumn.Source.ColumnName,up is{} p?ParentValue(p):null);if(target.Columns.Any(x=>x.Source.ColumnName=="status"))row["status"]=1;if(target.Columns.Any(x=>x.Source.ColumnName=="amount"))row["amount"]=1m;if(target.Columns.Any(x=>x.Source.ColumnName=="code"))row["code"]="é";await store.InsertAsync(target.Source.TableName,row);
            }
            var cross=CrossTableWriteCompilation.ForService(model,sourceModel,page);var seeded=new HashSet<string>(StringComparer.Ordinal);long CrossId(string id)=>First+100+long.Parse(id,CultureInfo.InvariantCulture);
            foreach(var branch in cross)for(var index=0;index<branch.Levels.Count;index++)
            {
                var level=branch.Levels[index].Read;if(!seeded.Add(level.Target.Source.Id))continue;var row=D("id",CrossId(level.Target.Source.Id),level.Name.Source.ColumnName,"跨表"+level.Target.Source.Id);if(level.Target.Columns.Any(x=>x.Source.ColumnName=="yz_project_id"))row["yz_project_id"]=level.Target.IsGlobal?Project+1:Project;if(level.Target.Columns.Any(x=>x.Source.ColumnName=="status"))row["status"]=1;if(level.Parent is{} pk){var parentId=CrossId(branch.Levels[index-1].Read.Target.Source.Id);row[pk.Source.ColumnName]=pk.Type=="string?"?Id(parentId):(object)parentId;}await store.InsertAsync(level.Target.Source.TableName,row);
            }
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var plans=(RelationMaintenanceCatalog)assembly.GetType(model.Namespace+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(846));builder.Services.AddSingleton<IRuntimeAuthorization>(auth);builder.Services.AddSingleton<ISecurityIdentityAccessor>(auth);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(guard);builder.Services.AddSingleton(new SystemMaintenanceBusiness(session,plans,auth));assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddApplicationPart(typeof(SystemMaintenanceController).Assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or MySqlException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            // 路由、整entity兼容和R响应使用实际生成控制器；不通过反射直接调用服务。
            async Task<JsonElement> Call(string table,string pageId,string action,object body,string? parentId=null,HttpMethod? method=null){using var request=new HttpRequestMessage(method??HttpMethod.Post,"/api/admin/auto/"+table+"/"+pageId+"/"+action+(parentId is null?"":"?parentId="+parentId)){Content=JsonContent.Create(body)};using var response=await client.SendAsync(request);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<JsonElement> Maintenance(string action,object body){using var response=await client.PostAsJsonAsync("/api/admin/sys/system_maintenance/"+action,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Snapshot(){var data=new Dictionary<string,object>();foreach(var table in c.ConfigTables)data[table.TableName]=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x)).ToArray();return JsonSerializer.Serialize(data);}
            Dictionary<string,object?> Selection(){var body=D("name",NodeName("来源"),field.JsonName,Id(First+1));foreach(var branch in cross)body[branch.Levels[^1].Id.JsonName]=Id(CrossId(branch.Levels[^1].Read.Target.Source.Id));if(sourceModel.Columns.Any(x=>x.Source.ColumnName=="owner_country_id"))body["ownerCountryId"]=Id(CrossId("3"));if(mode=="mixed")body["choiceIds"]=new[]{Id(First),Id(First+1)};if(mode=="double")body["alternateId"]=Id(First);return body;}
            var added=Data(await Call(source,"201","add",Selection()));var id=added.GetProperty("id").GetString()!;var number=long.Parse(id,CultureInfo.InvariantCulture);observer.Record=number;Assert.Equal(NodeName("叶"),added.GetProperty(cache.JsonName).GetString());Assert.Contains(NodeName("根"),added.GetProperty(field.JsonName+"DisplayText").GetString());var consumers=new List<long>{number};
            foreach(var sourcePage in sourceModel.Pages.Where(p=>p.Scopes.Any(s=>s.Source=="parent")))
            {
                var scope=sourcePage.Scopes.First(s=>s.Source=="parent");var matching=cross.SelectMany(p=>p.Levels).FirstOrDefault(l=>l.Id.Source.Id==scope.Column.Source.Id);Assert.NotNull(matching);var parentId=Id(CrossId(matching!.Read.Target.Source.Id));var entity=Data(await Call(source,sourcePage.Source.Id,"add",Selection(),parentId));consumers.Add(long.Parse(entity.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));Assert.Equal(NodeName("叶"),entity.GetProperty(cache.JsonName).GetString());
            }
            var copied=Data(await Call(source,"201","copy/"+id,new{}));consumers.Add(long.Parse(copied.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));Assert.Equal(NodeName("叶"),copied.GetProperty(cache.JsonName).GetString());
            // 绕过事件的原生SQL不自动刷新缓存；省略关联及伪造名称不能替代显式选择。
            await store.UpdateAsync(target.Source.TableName,First+1,D(value.Source.ColumnName,NodeName("SQL新叶")));var omitted=Data(await Call(source,"201","update",D("id",id,"name",NodeName("省略关联"),cache.JsonName,"伪造名称")));Assert.Equal(NodeName("叶"),omitted.GetProperty(cache.JsonName).GetString());Assert.Contains(NodeName("SQL新叶"),omitted.GetProperty(field.JsonName+"DisplayText").GetString());
            // 外项目、已删除及字符串坏历史不参与目标事件传播。
            var original=Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",number)));foreach(var (key,project,deleted) in new[]{(First+900,Project+1,false),(First+901,Project,true)}){var row=new Dictionary<string,object?>(original){["id"]=key,["yz_project_id"]=project,[cache.Source.ColumnName]="历史缓存"};if(deleted)row["delete_no"]=new DateTime(2026,1,1);await store.InsertAsync(source,row);}if(type=="string"){var row=new Dictionary<string,object?>(original){["id"]=First+902,[field.Source.ColumnName]="0"+Id(First+1),[cache.Source.ColumnName]="坏历史"};await store.InsertAsync(source,row);}
            var wide=mode=="single"&&parent=="int64"||mode=="caption"&&type=="int64"&&parent=="string";if(wide)for(var index=0;index<131;index++){var row=new Dictionary<string,object?>(original){["id"]=First+1000+index};await store.InsertAsync(source,row);consumers.Add(First+1000+index);}
            async Task Names(string? expected){foreach(var key in consumers){var row=Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",key)));Assert.Equal(expected,row[cache.Source.ColumnName]);Assert.Equal(Id(First+1),SqlRowStore.Text(row,field.Source.ColumnName));if(mode=="double"){Assert.Equal(NodeName("根"),row["alternate_name"]);Assert.Equal(Id(First),SqlRowStore.Text(row,"alternate_id"));}}}
            var count=observer.Count;Data(await Call(target.Source.TableName,targetPage.Source.Id,"update",D("id",Id(First+1),value.JsonName,NodeName("新叶"))));Assert.Equal(count+1,observer.Count);await Names(NodeName("新叶"));Assert.Equal("历史缓存",Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",First+900)))[cache.Source.ColumnName]);Assert.Equal("历史缓存",Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",First+901)))[cache.Source.ColumnName]);if(type=="string")Assert.Equal("坏历史",Assert.Single(await store.ReadIncludingDeletedAsync(source,D("id",First+902)))[cache.Source.ColumnName]);
            var before=await Snapshot();observer.SawPatched=false;var failure=await Call(target.Source.TableName,targetPage.Source.Id,"update",D("id",Id(First+1),value.JsonName,NodeName("尾部失败")));Assert.Equal(500,failure.GetProperty("code").GetInt32());Assert.Contains("自树缓存尾部失败",failure.GetProperty("msg").GetString());Assert.True(observer.SawPatched);Assert.Equal(before,await Snapshot());
            guard.Enabled=true;try{var failed=await Call(target.Source.TableName,targetPage.Source.Id,"update",D("id",Id(First+1),value.JsonName,NodeName(new string('长',90))));Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());}finally{guard.Enabled=false;}
            // 原HTTP维护入口执行冻结目录；撤销按钮不能由固定测试身份绕过。
            auth.DenyMaintenance=true;Assert.Equal(500,(await Maintenance("backfill_join_columns",new{configTableId=sourceModel.Source.Id})).GetProperty("code").GetInt32());auth.DenyMaintenance=false;
            foreach(var key in consumers)await store.UpdateAsync(source,key,D(cache.Source.ColumnName,"待回填"));var maintained=Data(await Maintenance("backfill_join_columns",new{configTableId=sourceModel.Source.Id}));Assert.True(maintained.GetProperty("affectedRows").GetInt64()>0);await Names(NodeName("新叶"));Assert.Equal(0,Data(await Maintenance("backfill_join_columns",new{configTableId=sourceModel.Source.Id})).GetProperty("affectedRows").GetInt64());
            if(wide)
            {
                foreach(var key in consumers)await store.UpdateAsync(source,key,D(cache.Source.ColumnName,"跨批旧缓存"));await store.UpdateAsync(target.Source.TableName,First+2,D(value.Source.ColumnName,NodeName(new string('后',90))));await store.UpdateAsync(source,number,D(field.Source.ColumnName,field.Type=="string?"?(object)Id(First+2):First+2));before=await Snapshot();Assert.Equal(500,(await Maintenance("backfill_join_columns",new{configTableId=sourceModel.Source.Id})).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());await store.UpdateAsync(target.Source.TableName,First+2,D(value.Source.ColumnName,NodeName("另一根")));await store.UpdateAsync(source,number,D(field.Source.ColumnName,field.Type=="string?"?(object)Id(First+1):First+1));Data(await Maintenance("backfill_join_columns",new{configTableId=sourceModel.Source.Id}));await Names(NodeName("新叶"));
            }
            Data(await Call(source,"201","update",D("id",id,field.JsonName,null)));var cleared=Assert.Single(await store.ReadAsync(source,D("id",number)));Assert.Null(cleared[field.Source.ColumnName]);Assert.Null(cleared[cache.Source.ColumnName]);Data(await Call(source,"201","update",D("id",id,field.JsonName,Id(First+1))));
            Data(await Call(target.Source.TableName,targetPage.Source.Id,"",new{id=Id(First+1)},method:HttpMethod.Delete));await Names(null);if(physicalDelete)Assert.Empty(await store.ReadIncludingDeletedAsync(target.Source.TableName,D("id",First+1)));else Assert.NotNull(Assert.Single(await store.ReadIncludingDeletedAsync(target.Source.TableName,D("id",First+1)))["delete_no"]);
            passed=true;await Receipt("verified",new{nonce,type,parent,mode,physicalDelete,passed,realGeneratedHttp=true,originalMaintenanceHttp=true,fullSourcePages=sourceModel.Pages.Count(p=>p.Source.Id=="201"||p.Scopes.Any(s=>s.Source=="parent")),consumers=consumers.Count,lateTargetAndCacheRollback=true,nonStrictRollback=true,lateBackfillRollback=wide,foreignAndDeletedExcluded=true,badCanonicalExcluded=type=="string",copyAndOmissionNull=true,repeatBackfillZero=true,noConsumerHooks=true,formalIdentity=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parent,mode,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){await Pin(options);using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>原观察器最后才通知，验证目标与缓存均已改变，再抛出以检查整笔回滚。</summary>
    private sealed class Observer(string source,string cache,SqlRowStore store):IRecordMutationObserver
    {
        internal long Record;internal int Count;internal bool SawPatched;
        /// <summary>缓存更新不应递归调用消费者Service或观察器。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default){Count++;if(c.SubmittedFields.Values.Any(v=>v is string s&&s.Contains("尾部失败",StringComparison.Ordinal))){SawPatched=SqlRowStore.Text(Assert.Single(await store.ReadAsync(source,D("id",Record))),cache).Contains("尾部失败",StringComparison.Ordinal);throw new InvalidOperationException("自树缓存尾部失败");}}
    }
    /// <summary>静默截断仅改变本次请求连接，校验不一致必须回滚。</summary>
    private sealed class NonStrict:IRecordMutationGuard
    {
        internal bool Enabled;
        /// <summary>写前身份及范围不由测试守卫改写。</summary>
        public Task BeforeMutationAsync(RecordMutationPreparation p,CancellationToken token=default)=>Task.CompletedTask;
        /// <summary>只在已开始的测试事务内修改会话模式，不使用全局变量。</summary>
        public async Task BeforeWriteAsync(RecordMutationValidation p,CancellationToken token=default){if(!Enabled)return;await using var cmd=p.Transaction.Connection!.CreateCommand();cmd.Transaction=p.Transaction;cmd.CommandText="SET SESSION sql_mode=''";await cmd.ExecuteNonQueryAsync(token);}
    }
    /// <summary>固定普通Web测试身份，维护按钮可撤销；无真实登录含义。</summary>
    private sealed class Auth:IRuntimeAuthorization,ISecurityIdentityAccessor
    {
        internal bool DenyMaintenance;
        /// <summary>项目与后台由宿主固定，禁止从客户端提供。</summary>
        public RuntimeActor RequireActor()=>new(997,"R1自树专项",false,Id(Project),"5");
        /// <summary>元数据计划另行证明页面可用性，此处只提供固定动作许可。</summary>
        public void RequirePagePermission(string page,string operation){}
        /// <summary>原维护Business核对真实身份形状与精确按钮；不依赖管理员旁路。</summary>
        public AuthenticatedIdentity RequireIdentity()=>new(RequireActor(),new HashSet<string>(),998,null){PrincipalProjectId=Id(Project),PrincipalAdminTypeId="5",ButtonIds=DenyMaintenance?new HashSet<string>():new HashSet<string>{"91001","91002"}};
    }
    /// <summary>实例pin在每个模型建表前验证，读取结果不写公共日志。</summary>
    private static async Task Pin(DatabaseOptions options){using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"自树实例pin不符，停止写入。");}
    /// <summary>R成功码是解码data的前提，不能把其它500误算成业务验证。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>字典值保持原类型，进入参数化存储。</summary>
    private static Dictionary<string,object?> D(params object?[] values)=>Enumerable.Range(0,values.Length/2).ToDictionary(i=>(string)values[i*2]!,i=>values[i*2+1]);
    /// <summary>大整数ID不经过浮点转换。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>只执行本轮已核对的自有结构清理，名称来自冻结目录。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=sql;await cmd.ExecuteNonQueryAsync();}
}
