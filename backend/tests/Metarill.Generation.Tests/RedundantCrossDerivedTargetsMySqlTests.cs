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

/// <summary>固定Server-200隔离库验证R1跨表到R0物理／派生名称；不是正式网页登录或浏览器验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RedundantCrossDerivedTargetsMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001,Consumer=9007199254752001;
    /// <summary>字符串键及R0物理名称。</summary>
    [StandaloneExportMySqlFact] public Task Physical_string_string()=>Run("string","string","physical");
    /// <summary>字符串所选ID、整数父键与全派生名称。</summary>
    [StandaloneExportMySqlFact] public Task Derived_string_long()=>Run("string","int64","derived");
    /// <summary>整数所选ID、字符串父键与混合层。</summary>
    [StandaloneExportMySqlFact] public Task Mixed_long_string()=>Run("int64","string","mixed");
    /// <summary>整数键与全派生名称。</summary>
    [StandaloneExportMySqlFact] public Task Derived_long_long()=>Run("int64","int64","derived");
    /// <summary>同一R0表在根与叶的角色不互相覆盖。</summary>
    [StandaloneExportMySqlFact] public Task Repeated_target()=>Run("string","int64","repeated");
    /// <summary>固定实例pin、数据库与账号配置核对后才建立本轮随机表。</summary>
    private static async Task Run(string type,string parentType,string scenario)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("跨表缓存实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建缓存样本。");}await Exercise(options,type,parentType,scenario);
    }
    /// <summary>改名、切换R0外键、跨128行批次、下游副本和事务尾部失败共用生成服务。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,string scenario)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_rcd_"+nonce[..12];var c=RedundantCrossDerivedTargetsTests.Catalog(prefix,type,parentType,scenario);foreach(var page in c.ConfigPages)page.IsSave=1;c.ConfigPages.Single(x=>x.Id=="201").IsCopy=1;var source=prefix+"_source";var audit=prefix+"_audit";var company=prefix+"_company";var repeated=scenario=="repeated";var roles=new[]{"country","area","place"};var targets=roles.Select((x,i)=>prefix+"_"+(repeated&&i==2?"country":x)).ToArray();var derived=Enumerable.Range(0,3).Select(i=>c.ConfigColumns.Single(x=>x.ConfigTableId==(repeated&&i==2?"3":(i+3).ToString(CultureInfo.InvariantCulture))&&x.ColumnName=="name").Extra.ContainsKey("columnFullName")).ToArray();c.ConfigColumns.RemoveAll(x=>(x.ConfigTableId=="6"||x.ConfigTableId=="5")&&x.ColumnName=="delete_no");var changedRole=scenario=="mixed"?1:0;var cache=roles[changedRole]+"_name";
        // 所有缓存均由元数据生成，下游刻意引用本模型的派生层。
        c.ConfigTables.Add(new(){Id="7",YzProjectId=c.YzProjectId,TableName=audit,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="71",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="72",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="73",ConfigTableId="7",ColumnName="delete_no",LogicalType="datetime"},new(){Id="74",ConfigTableId="7",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="75",ConfigTableId="7",ColumnName="source_name",LogicalType="string",ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("source."+cache)}}]);c.ConfigPages.Add(new(){Id="701",YzProjectId=c.YzProjectId,YzAdminTypeId="5",ConfigTableId="7",WhereSql="yz_project_id=@current_yz_project_id"});foreach(var id in new[]{"74","75"})c.ConfigPageColumns.Add(new(){Id="701"+id,ConfigPageId="701",ConfigColumnId=id});foreach(var col in c.ConfigColumns)col.YzProjectId=c.YzProjectId;
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/redundant-cross-derived/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var auth=new Auth();var observer=new Observer(source,cache,store);WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,type,parentType,scenario,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);
            object Key(long id)=>type=="string"?Id(id):id;object Parent(long id)=>parentType=="string"?Id(id):id;long Node(int role,int group=0)=>First+group*10+(repeated&&role==2?1:0);long Company(int role,int group=0)=>6000+group*10+role;var expected=new string?[]{"国0","区0","城0"};
            for(var group=0;group<2;group++)for(var role=0;role<3;role++)
            {
                var text=new[]{"国","区","城"}[role]+group;if(derived[role])await store.InsertAsync(company,D("id",Company(role,group),"name",text,"yz_project_id",Project));var row=D("id",Node(role,group),"yz_project_id",Project,"status",1);if(derived[role])row["company_id"]=Company(role,group);else row["name"]=text;if(role>0)row[prefix+"_"+roles[role-1]+"_id"]=Parent(Node(role-1,group));await store.InsertAsync(targets[role],row);
            }
            if(derived.Any(x=>x))await store.InsertAsync(company,D("id",6900L,"name","备选名称","yz_project_id",Project));
            Dictionary<string,object?> Source(long id,long project=Project)=>D("id",id,"name","来源"+id,"yz_project_id",project,"country_id",Key(Node(0)),"area_id",Key(Node(1)),"place_id",Key(Node(2)),"country_name","国0","area_name","区0","place_name","城0");
            for(var i=0;i<130;i++)await store.InsertAsync(source,Source(Consumer+i));await store.InsertAsync(source,Source(Consumer+1000,Project+1));var deleted=Source(Consumer+1001);deleted["delete_no"]=new DateTime(2026,1,1);await store.InsertAsync(source,deleted);await store.InsertAsync(audit,D("id",Consumer,"yz_project_id",Project,"source_id",Consumer,"source_name",expected[changedRole]));
            var model=GenerationModel.Create(c,"Example.RedundantDerivedProbe");new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var plan=(RelationMaintenanceCatalog)assembly.GetType(model.Namespace+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(997));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddScoped<IRuntimeAuthorization>(_=>auth);assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> Call(string table,string page,string action,object body,HttpMethod? method=null){using var req=new HttpRequestMessage(method??HttpMethod.Post,"/api/admin/auto/"+table+"/"+page+"/"+action){Content=JsonContent.Create(body)};using var response=await client.SendAsync(req);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Snapshot(){var data=new List<object>();foreach(var table in c.ConfigTables.OrderBy(t=>t.TableName,StringComparer.Ordinal))data.Add(new{table.TableName,rows=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x))});return JsonSerializer.Serialize(data);}
            // 原新增、全实体只读回传与复制仍经真实控制器，不接受客户端缓存名称。
            var added=Data(await Call(source,"201","add",new{name="实际生成新增",placeId=Id(Node(2))}));var addedId=added.GetProperty("id").GetString()!;Assert.Equal("国0/区0/城0",added.GetProperty("placeIdDisplayText").GetString());Data(await Call(source,"201","update",added));var copied=Data(await Call(source,"201","copy/"+addedId,new{}));Assert.Equal("国0/区0/城0",copied.GetProperty("placeIdDisplayText").GetString());var idsBefore=await Identifiers();var sourceNotifications=observer.SourceCount;auth.DenyConsumerWrites=true;
            async Task<JsonElement> Rename(string text)=>derived[changedRole]?await Call(company,"601","update",D("id",Id(Company(changedRole)),"name",text)):await Call(targets[changedRole],(changedRole+3).ToString(CultureInfo.InvariantCulture)+"01","update",D("id",Id(Node(changedRole)),"name",text));
            Data(await Rename("更新名称"));expected[changedRole]="更新名称";await Names();Assert.Equal("更新名称",SqlRowStore.Text(Assert.Single(await store.ReadAsync(audit)),"source_name"));Assert.Equal(sourceNotifications,observer.SourceCount);var foreign=(await store.ReadIncludingDeletedAsync(source)).Single(x=>SqlRowStore.Id(x)==Consumer+1000);Assert.Equal(new[]{"国0","区0","城0"}[changedRole],foreign[cache]);Assert.Equal(new[]{"国0","区0","城0"}[changedRole],(await store.ReadIncludingDeletedAsync(source)).Single(x=>SqlRowStore.Id(x)==Consumer+1001)[cache]);
            if(derived[changedRole])
            {
                var targetPage=(repeated&&changedRole==2?3:changedRole+3).ToString(CultureInfo.InvariantCulture)+"01";Data(await Call(targets[changedRole],targetPage,"update",D("id",Id(Node(changedRole)),"companyId","6900")));expected[changedRole]="备选名称";await Names();Assert.Equal("备选名称",SqlRowStore.Text(Assert.Single(await store.ReadAsync(audit)),"source_name"));Data(await Call(targets[changedRole],targetPage,"update",D("id",Id(Node(changedRole)),"companyId",Id(Company(changedRole)))));expected[changedRole]="更新名称";await Names();
            }
            var before=await Snapshot();var count=observer.Count;Assert.Equal(500,(await Rename("尾部异常")).GetProperty("code").GetInt32());Assert.True(observer.SawPatchedBeforeFailure);Assert.Equal(before,await Snapshot());Assert.Equal(count+1,observer.Count);Assert.Equal(idsBefore,await Identifiers());
            // 原维护执行器修复历史错误，128行批次之后的记录和下游一起收敛；重复执行零写入。
            foreach(var row in await store.ReadAsync(source,D("yz_project_id",Project)))await store.UpdateAsync(source,SqlRowStore.Id(row),D("country_name","待回填","area_name","待回填","place_name","待回填"));await store.UpdateAsync(audit,Consumer,D("source_name","待下游"));var backfill=await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2");Assert.Equal(3,backfill.JoinColumnCount);Assert.True(backfill.AffectedRows>128);await Names();Assert.Equal(expected[changedRole],SqlRowStore.Text(Assert.Single(await store.ReadAsync(audit)),"source_name"));Assert.Equal(0,(await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2")).AffectedRows);Assert.Equal(idsBefore,await Identifiers());Assert.Equal(sourceNotifications,observer.SourceCount);
            // 删除真实终点只清缓存；派生R0名称无物理列，R0物理名称照常保留，原关系ID保持。
            var nodePage=(changedRole+3).ToString(CultureInfo.InvariantCulture)+"01";Data(await Call(targets[changedRole],nodePage,"",new{id=Id(Node(changedRole))},HttpMethod.Delete));expected[changedRole]=null;await Names();Assert.Null(Assert.Single(await store.ReadAsync(audit))["source_name"]);await store.UpdateAsync(targets[changedRole],Node(changedRole),D("delete_no",null));Data(await Rename("恢复名称"));expected[changedRole]="恢复名称";await Names();var deleteTable=derived[changedRole]?company:targets[2];var deletePage=derived[changedRole]?"601":"501";var deleteId=derived[changedRole]?Company(changedRole):Node(2);Data(await Call(deleteTable,deletePage,"",new{id=Id(deleteId)},HttpMethod.Delete));Assert.DoesNotContain(await store.ReadIncludingDeletedAsync(deleteTable),x=>SqlRowStore.Id(x)==deleteId);expected[derived[changedRole]?changedRole:2]=null;await Names();Assert.Equal(idsBefore,await Identifiers());foreach(var table in c.ConfigTables.Where(t=>t.IsRongyu==0&&c.ConfigColumns.Any(x=>x.ConfigTableId==t.Id&&x.ColumnName=="name"&&x.Extra.ContainsKey("columnFullName"))))Assert.DoesNotContain((await schema.InspectAsync(c)).Tables.Single(t=>t.Name==table.TableName).Columns,x=>x.Name=="name");
            passed=true;await Receipt("verified",new{nonce,type,parentType,scenario,passed,realGeneratedHttp=true,realMySql=true,sourceRows=132,derivedR0Names=derived.Count(x=>x),downstream=true,noConsumerHooks=true,lateTransactionRollback=true,idPreserved=true,repeatBackfillZero=true,foreignSourceExcluded=true,softAndPhysicalDelete=true,formalWeb=false,browser=false});
            /// <summary>缓存以外的关系、来源、删除和审计字段不能被内部传播重写。</summary>
            async Task<string> Identifiers()=>JsonSerializer.Serialize((await store.ReadIncludingDeletedAsync(source)).OrderBy(x=>SqlRowStore.Id(x)).Select(x=>x.Where(p=>p.Key is not("country_name" or "area_name" or "place_name")).ToDictionary(p=>p.Key,p=>p.Value)));
            /// <summary>所有当前活动来源和新增／复制行都按各层独立核验。</summary>
            async Task Names(){foreach(var row in await store.ReadAsync(source,D("yz_project_id",Project)))for(var role=0;role<3;role++)Assert.Equal(expected[role],row[roles[role]+"_name"]);}
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,scenario,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>通知位于缓存传播之后；故障必须回滚目标、来源和下游，而非重入消费者Service。</summary>
    private sealed class Observer(string source,string cache,SqlRowStore store):IRecordMutationObserver
    {
        internal int Count,SourceCount;internal bool SawPatchedBeforeFailure;
        /// <summary>只对固定测试标志验证顺序和整笔回滚。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default){Count++;if(c.TableName==source)SourceCount++;if(c.SubmittedFields.Values.Any(x=>Equals(x,"尾部异常"))){SawPatchedBeforeFailure=(await store.ReadAsync(source,D("id",Consumer))).Single()[cache] is string text&&text=="尾部异常";throw new InvalidOperationException("缓存测试尾部异常");}}
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
