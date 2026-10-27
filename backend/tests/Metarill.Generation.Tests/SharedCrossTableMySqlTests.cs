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

/// <summary>共享上级级联使用真正生成HTTP和Server-200隔离库验证；固定身份不代替正式Web验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossTableMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001,Consumer=9007199254752001;
    /// <summary>四种所选／父键交叉，另含共享主叶及固定派生目标。</summary>
    [StandaloneExportMySqlFact] public Task String_string()=>Run("string","string");
    [StandaloneExportMySqlFact] public Task String_long()=>Run("string","int64");
    [StandaloneExportMySqlFact] public Task Long_string()=>Run("int64","string");
    [StandaloneExportMySqlFact] public Task Long_long()=>Run("int64","int64");
    [StandaloneExportMySqlFact] public Task Short_string()=>Run("string","int64",true);
    [StandaloneExportMySqlFact] public Task Short_long()=>Run("int64","string",true);
    [StandaloneExportMySqlFact] public Task Derived_targets()=>Run("string","int64",false,true);
    /// <summary>固定实例pin、数据库与账号配置核对后才建立本轮随机表。</summary>
    private static async Task Run(string type,string parentType,bool shorter=false,bool derived=false)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("跨表缓存实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建缓存样本。");}await Exercise(options,type,parentType,shorter,derived);
    }
    /// <summary>共享组先于其它表单依赖关联归一化，所有DML和名称传播仍在原事务内。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool shorter,bool derived)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_scc_"+nonce[..12];var c=SharedCrossTableGenerationTests.Catalog(prefix,type,parentType,shorter,derived);var source=prefix+"_source";var audit=prefix+"_audit";var company=prefix+"_company";var item=prefix+"_item";var roles=new[]{"country","area","place","warehouse"};var leaf=shorter?"areaId":"placeId";
        // 普通List按countryId限制候选，名称排序在级联前，防止用归一化之前的旧父范围验证。
        c.ConfigTables.Add(new(){Id="10",TableName=item,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="101",ConfigTableId="10",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="102",ConfigTableId="10",ColumnName="name",LogicalType="string"},new(){Id="103",ConfigTableId="10",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="104",ConfigTableId="10",ColumnName="delete_no",LogicalType="datetime"},new(){Id="106",ConfigTableId="10",ColumnName="country_id",LogicalType=type},new(){Id="105",ConfigTableId="2",ColumnName="a_item_id",LogicalType="int64",LinkConfigTableId="10",ControlKind="List",ControlDetail="{\"tableId\":\"10\",\"nameField\":\"name\",\"whereSql\":\"country_id=@formParameters.countryId\"}",Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("1001")}}]);c.ConfigPages.Add(new(){Id="1001",ConfigTableId="10",WhereSql="yz_project_id=@current_yz_project_id",IsSave=1});foreach(var id in new[]{"102","106"})c.ConfigPageColumns.Add(new(){Id="1001"+id,ConfigPageId="1001",ConfigColumnId=id});c.ConfigPageColumns.Add(new(){Id="201105",ConfigPageId="201",ConfigColumnId="105"});
        // 分页验收会发送共同国家的级联筛选，必须先在原页面元数据明确启用。
        c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        c.ConfigTables.Add(new(){Id="9",TableName=audit,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="92",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="93",ConfigTableId="9",ColumnName="delete_no",LogicalType="datetime"},new(){Id="94",ConfigTableId="9",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="95",ConfigTableId="9",ColumnName="source_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("source.country_name")}}]);c.ConfigPages.Add(new(){Id="901",ConfigTableId="9",WhereSql="yz_project_id=@current_yz_project_id",IsSave=1});foreach(var id in new[]{"94","95"})c.ConfigPageColumns.Add(new(){Id="901"+id,ConfigPageId="901",ConfigColumnId=id});foreach(var r in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))r.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/shared-cross-coordination/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-cross-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var auth=new Auth();var observer=new Observer(source,audit,store);WebApplication? app=null;var created=false;var passed=false;
        await Receipt("intent",new{nonce,type,parentType,shorter,derived,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);
            object Key(long id)=>type=="string"?Id(id):id;object Parent(long id)=>parentType=="string"?Id(id):id;long Node(int role,int group=0)=>First+group*10+role;long Company(int role,int group=0)=>First+500+group*10+role;long Item(int group)=>First+700+group;
            for(var group=0;group<3;group++)
            {
                for(var role=0;role<4;role++)
                {
                    var row=D("id",Node(role,group),"yz_project_id",Project,"status",1);if(derived&&role<3){await store.InsertAsync(company,D("id",Company(role,group),"name",new[]{"国","区","城"}[role]+group,"yz_project_id",Project));row["company_id"]=Company(role,group);}else row["name"]=new[]{"国","区","城","仓"}[role]+group;if(role>0)row[prefix+"_"+(role==1?"country":"area")+"_id"]=Parent(Node(role==1?0:1,group));await store.InsertAsync(prefix+"_"+roles[role],row);
                }
                await store.InsertAsync(item,D("id",Item(group),"name","候选"+group,"yz_project_id",Project,"country_id",Key(Node(0,group))));
            }
            Dictionary<string,object?> Source(long id,long project=Project){var row=D("id",id,"name","原来源","yz_project_id",project);foreach(var role in Enumerable.Range(0,4).Where(r=>!shorter||r!=2)){row[roles[role]+"_id"]=Key(Node(role));row[roles[role]+"_name"]=new[]{"国","区","城","仓"}[role]+"0";}return row;}
            await store.InsertAsync(source,Source(Consumer));await store.InsertAsync(source,Source(Consumer+1000,Project+1));var deleted=Source(Consumer+1001);deleted["delete_no"]=new DateTime(2026,1,1);await store.InsertAsync(source,deleted);await store.InsertAsync(audit,D("id",Consumer,"yz_project_id",Project,"source_id",Consumer,"source_name","国0"));
            var model=GenerationModel.Create(c,"Example.SharedCrossProbe");var generator=new CSharpGenerator();generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);var sourceType=model.Tables.Single(t=>t.Source.Id=="2").Name;var hand=Path.Combine(directory,"generated/Services/"+sourceType+"Service.cs");var hook="using Metarill.Core.Security;\nusing "+model.Namespace+".Generated.Entities;\nnamespace "+model.Namespace+".Generated.Services;\npublic sealed partial class "+sourceType+"Service\n{\n    /// <summary>真实手写钩子改变末级，组协调必须看到钩子后的值。</summary>\n    partial void BeforeSave("+sourceType+" entity,RuntimeActor actor,bool creating,ISet<string> submittedFields){if(entity.Name==\"hook-new-leaf\"){entity."+(shorter?"AreaId":"PlaceId")+"="+(type=="string"?"\""+Id(Node(shorter?1:2,2))+"\"":Id(Node(shorter?1:2,2))+"L")+";submittedFields.Add(\""+(shorter?"area_id":"place_id")+"\");}}\n}\n";await File.WriteAllTextAsync(hand,hook);generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);Assert.Equal(hook,await File.ReadAllTextAsync(hand));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var plan=(RelationMaintenanceCatalog)assembly.GetType(model.Namespace+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(997));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddScoped<IRuntimeAuthorization>(_=>auth);assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> Call(string table,string page,string action,object body){using var response=await client.PostAsJsonAsync("/api/admin/auto/"+table+"/"+page+"/"+action,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Snapshot(){var data=new List<object>();foreach(var table in c.ConfigTables.OrderBy(t=>t.TableName,StringComparer.Ordinal))data.Add(new{table.TableName,rows=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x))});return JsonSerializer.Serialize(data);}
            Dictionary<string,object?> Selection(int a,int b)=>D("name","普通保存",leaf,Id(Node(shorter?1:2,a)),"warehouseId",Id(Node(3,b)),"aItemId",null);
            var request=Selection(0,0);request["name"]="生成新增";request["aItemId"]=Id(Item(0));var added=Data(await Call(source,"201","add",request));var addedId=added.GetProperty("id").GetString()!;var id=long.Parse(addedId,CultureInfo.InvariantCulture);
            async Task<JsonElement> Update(Dictionary<string,object?> body){body["id"]=addedId;return await Call(source,"201","update",body);}
            async Task<Dictionary<string,object?>> Row()=>Assert.Single(await store.ReadAsync(source,D("id",id)));
            async Task ExpectGroup(int group,bool a,bool b){var row=await Row();Assert.Equal(Id(Node(0,group)),Convert.ToString(row["country_id"],CultureInfo.InvariantCulture));Assert.Equal(Id(Node(1,group)),Convert.ToString(row["area_id"],CultureInfo.InvariantCulture));Assert.Equal("国"+group,row["country_name"]);Assert.Equal("区"+group,row["area_name"]);foreach(var role in new[]{2,3}.Where(r=>!shorter||r!=2)){if(role==2?a:b){Assert.Equal(Id(Node(role,group)),Convert.ToString(row[roles[role]+"_id"],CultureInfo.InvariantCulture));Assert.Equal((role==2?"城":"仓")+group,row[roles[role]+"_name"]);}else{Assert.True(row[roles[role]+"_id"] is null or "");Assert.True(row[roles[role]+"_name"] is null or "");}}}
            await ExpectGroup(0,true,true);Data(await Update(JsonSerializer.Deserialize<Dictionary<string,object?>>(added.GetRawText())!));Data(await Call(source,"201","copy/"+addedId,new{}));
            // 实际分页和COUNT按启用的共同国家筛选，排除外项目与逻辑删除行。
            var filtered=new PageRequest{PageSize=1,Filters=new(){{leaf,JsonSerializer.SerializeToElement(new{countryId=Id(Node(0))})}}};var pageRows=Data(await Call(source,"201","page",filtered));Assert.Equal("3",pageRows.GetProperty("total").GetString());Assert.Single(pageRows.GetProperty("records").EnumerateArray());Assert.Equal("3",Data(await Call(source,"201","count",filtered)).ToString());
            // 整对象回传旧叶时，只变化的仓库路径生效，旧城市自动清空；普通List显式清空。
            Data(await Update(Selection(0,1)));await ExpectGroup(1,false,true);Data(await Update(Selection(0,0)));Data(await Update(D("aItemId",Id(Item(0)))));
            // 两个主动新选择不一致、旧普通选择器在新父范围失效，都必须整笔回滚。
            var before=await Snapshot();Assert.Equal(500,(await Update(Selection(1,2))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());Assert.Equal(500,(await Update(D(leaf,Id(Node(shorter?1:2,1))))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
            auth.DenyWarehouse=true;Data(await Update(D(leaf,Id(Node(shorter?1:2,1)),"aItemId",null)));auth.DenyWarehouse=false;await ExpectGroup(1,true,false);Data(await Update(D("aItemId",Id(Item(1)))));await ExpectGroup(1,true,false);
            Data(await Update(Selection(0,0)));Data(await Update(D("name","hook-new-leaf","aItemId",null)));await ExpectGroup(2,true,false);
            Data(await Update(Selection(0,0)));Data(await Update(D("countryId",Id(Node(0,1)))));var cleared=await Row();Assert.Equal("国1",cleared["country_name"]);foreach(var name in new[]{"area","place","warehouse"}.Where(n=>!shorter||n!="place"))Assert.True(cleared[name+"_id"] is null or "");
            Data(await Update(Selection(0,0)));if(!shorter){Data(await Update(D("placeId",null)));await ExpectGroup(0,false,true);}Data(await Update(new[]{"countryId","areaId",leaf}.Distinct().ToDictionary(k=>k,k=>(object?)null)));cleared=await Row();Assert.True(cleared["warehouse_id"] is null or "");Assert.Null(cleared["country_id"]);
            if(type=="string"){Data(await Update(Selection(0,0)));Data(await Update(new[]{"countryId","areaId",leaf}.Distinct().ToDictionary(k=>k,k=>(object?)"")));cleared=await Row();Assert.Equal("",cleared["country_id"]);Assert.True(cleared["warehouse_id"] is null or "");}
            Data(await Update(Selection(0,0)));before=await Snapshot();Assert.Equal(500,(await Update(D(leaf,Id(Node(shorter?1:2,2)),"name","source-tail-failure","aItemId",null))).GetProperty("code").GetInt32());Assert.True(observer.SawSourceCoordinated);Assert.Equal(before,await Snapshot());
            auth.DenyConsumerWrites=true;async Task<JsonElement> Rename(string name)=>derived?await Call(company,"601","update",D("id",Id(Company(0)),"name",name)):await Call(prefix+"_country","301","update",D("id",Id(Node(0)),"name",name));var notifications=observer.SourceCount;Data(await Rename("共享改名"));Assert.Equal("共享改名",Assert.Single(await store.ReadAsync(source,D("id",Consumer)))["country_name"]);Assert.Equal("共享改名",Assert.Single(await store.ReadAsync(audit))["source_name"]);Assert.Equal(notifications,observer.SourceCount);foreach(var excluded in new[]{Consumer+1000,Consumer+1001})Assert.Equal("国0",(await store.ReadIncludingDeletedAsync(source)).Single(r=>SqlRowStore.Id(r)==excluded)["country_name"]);
            before=await Snapshot();Assert.Equal(500,(await Rename("target-tail-failure")).GetProperty("code").GetInt32());Assert.True(observer.SawSourceAndDownstream);Assert.Equal(before,await Snapshot());
            // 维护按独立实际ID刷新每个缓存，公共列登记一次；重复执行必须零写入。
            var nonCacheBefore=JsonSerializer.Serialize((await store.ReadIncludingDeletedAsync(source)).OrderBy(x=>SqlRowStore.Id(x)).Select(x=>x.Where(p=>!p.Key.EndsWith("_name",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value)));var cacheNames=roles.Where(r=>!shorter||r!="place").Select(r=>r+"_name").ToArray();foreach(var row in await store.ReadAsync(source,D("yz_project_id",Project)))await store.UpdateAsync(source,SqlRowStore.Id(row),cacheNames.ToDictionary(n=>n,n=>(object?)"待回填"));await store.UpdateAsync(audit,Consumer,D("source_name","待下游"));var result=await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2");Assert.Equal(shorter?3:4,result.JoinColumnCount);Assert.True(result.AffectedRows>0);Assert.Equal("共享改名",Assert.Single(await store.ReadAsync(audit))["source_name"]);Assert.Equal(0,(await new RelationMaintenanceExecutor(session).BackfillAsync(plan,"2")).AffectedRows);Assert.Equal(notifications,observer.SourceCount);Assert.Equal(nonCacheBefore,JsonSerializer.Serialize((await store.ReadIncludingDeletedAsync(source)).OrderBy(x=>SqlRowStore.Id(x)).Select(x=>x.Where(p=>!p.Key.EndsWith("_name",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value))));foreach(var row in await store.ReadAsync(source,D("yz_project_id",Project)))foreach(var role in Enumerable.Range(0,4).Where(r=>!shorter||r!=2))Assert.Equal(role==0?"共享改名":new[]{"国","区","城","仓"}[role]+"0",row[roles[role]+"_name"]);
            passed=true;await Receipt("verified",new{nonce,type,parentType,shorter,derived,passed,realGeneratedHttp=true,realMySql=true,sharedClear=true,conflictingSelectionsRejected=true,dependentListValidatedAfterCoordination=true,partialHook=true,nullAndEmpty=true,copy=true,downstream=true,lateSourceRollback=true,lateTargetRollback=true,repeatBackfillZero=true,foreignAndDeletedExcluded=true,formalWeb=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,shorter,derived,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>尾部通知读取同一事务中的实际行后故意失败，验证协调／下游均先完成且随后回滚。</summary>
    private sealed class Observer(string source,string audit,SqlRowStore store):IRecordMutationObserver
    {
        internal int SourceCount;internal bool SawSourceCoordinated,SawSourceAndDownstream;
        /// <summary>不把内部缓存更新误当来源Service业务通知。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default)
        {
            if(c.TableName==source)SourceCount++;if(c.SubmittedFields.Values.Any(x=>Equals(x,"source-tail-failure"))){var row=Assert.Single(await store.ReadAsync(source,D("id",c.RecordId)));SawSourceCoordinated=Equals(row["country_name"],"国2")&&row["warehouse_id"] is null or "";throw new InvalidOperationException("共享来源尾部回滚探针");}if(c.SubmittedFields.Values.Any(x=>Equals(x,"target-tail-failure"))){SawSourceAndDownstream=Equals(Assert.Single(await store.ReadAsync(source,D("id",Consumer)))["country_name"],"target-tail-failure")&&Equals(Assert.Single(await store.ReadAsync(audit))["source_name"],"target-tail-failure");throw new InvalidOperationException("共享目标尾部回滚探针");}
        }
    }
    /// <summary>固定身份只用于生成HTTP专项，正式身份留待独立验收。</summary>
    private sealed class Auth:IRuntimeAuthorization
    {
        internal bool DenyConsumerWrites,DenyWarehouse;
        /// <summary>身份归属固定，不信任请求中的项目或后台。</summary>
        public RuntimeActor RequireActor()=>new(997,"共享级联实库",false,Id(Project),"5");
        /// <summary>目标变更传播不能依赖来源页面的写权限。</summary>
        public void RequirePagePermission(string page,string operation){if(DenyWarehouse&&page=="801"&&operation=="list")throw new UnauthorizedAccessException("无仓库候选权限。");if(DenyConsumerWrites&&page is "201" or "901"&&operation is "create" or "update" or "delete")throw new UnauthorizedAccessException("没有消费者写权限。");}
    }
    /// <summary>原R成功码是读取data的前提。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>所有业务数据参数化，ID不经过浮点数。</summary>
    private static Dictionary<string,object?> D(params object?[] values){var d=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)d.Add((string)values[i]!,values[i+1]);return d;}
    /// <summary>长ID按原接口以字符串返回。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>只有本轮已核对所有权、结构和行摘要的随机表可清理。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=sql;await cmd.ExecuteNonQueryAsync();}
}
