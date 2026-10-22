using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Globalization;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Data.Relations;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成五表HTTP证明R1→R0→R1与后续副本、多值、文件同事务传播；全部样本限于已pin的隔离业务库。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class MixedRelationPropagationMySqlTests
{
    private const long Project=54,Actor=894,Company=9007199254740993,Provider=9007199254741993;
    [StandaloneExportMySqlFact]
    public Task Logical_delete_keeps_mixed_scalar_multi_and_files_atomic()=>Verify(true);
    [StandaloneExportMySqlFact]
    public Task Physical_delete_keeps_mixed_scalar_and_multi_atomic()=>Verify(false);
    /// <summary>同一真实事务包含多级副本更新与文件引用；消费者页面拒绝和业务钩子不能阻止内部一致性维护。</summary>
    private static async Task Verify(bool logical)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||new MySqlConnectionStringBuilder(options.ConnectionString).Database!=options.ExpectedDatabase)throw new InvalidOperationException("仅允许隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await Pin();
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_mix_"+nonce[..12];var metadata=Catalog(prefix,logical);var root=NonRedundantApplicationFixture.Root();var files=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(root,"backend/metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var boundary=Path.Combine(Path.GetTempPath(),"metarill-mixed-propagation");var directory=Path.Combine(boundary,nonce);var evidence=Path.Combine(root,".local/mixed-propagation/http-"+nonce);Directory.CreateDirectory(evidence);using var database=new DatabaseSession(options);var schema=new SchemaService(database);var rows=new SqlRowStore(database,metadata);var fileRows=new SqlRowStore(database,files);var ids=new SnowflakeIdGenerator(894);var fileIds=new[]{ids.NextId(),ids.NextId()};var own=metadata.ConfigTables.ToDictionary(table=>table.TableName,_=>new HashSet<long>());WebApplication? app=null;var created=false;string? fingerprint=null;var failure=new Failure(prefix+"_company");
        try
        {
            Assert.Empty((await schema.InspectAsync(metadata)).Tables);var plan=await schema.PreviewAsync(metadata);Assert.Equal(5,plan.Changes.Count);await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(metadata));Assert.Empty((await schema.PreviewAsync(files)).Changes);
            foreach(var key in fileIds)await fileRows.InsertAsync("yz_file_library",new Dictionary<string,object?>{{"id",key},{"name",prefix+".txt"},{"content_type","text/plain"},{"size",1L},{"storage_key",Guid.NewGuid().ToString("N")+".blob"},{"access_mode","PROTECTED"},{"status","ready"},{"owner_id",Actor},{"yz_project_id",Project}});
            await Seed("company",Company,new(){{"name","公司甲"},{"title","初始标题"}});await Seed("company",Company+1,new(){{"name","公司乙"},{"title","标题乙"}});await Seed("provider",Provider,new(){{"company_id",Company}});await Seed("provider",Provider+1,new(){{"company_id",Company+1}});
            new CSharpGenerator().Generate(metadata,Path.Combine(directory,"generated"),"Example.MixedPropagation");
            foreach(var suffix in new[]{"source","downstream","multi"})
            {
                var type=GenerationModel.Pascal(prefix+"_"+suffix);await File.WriteAllTextAsync(Path.Combine(directory,"generated/Services/"+type+".Probe.cs"),$"using Metarill.Core.Security; using Example.MixedPropagation.Generated.Entities; namespace Example.MixedPropagation.Generated.Services; public partial class {type}Service {{ public static bool RejectBusinessHook{{get;set;}} partial void BeforeSave({type} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields) {{if(RejectBusinessHook)throw new InvalidOperationException(\"传播不能调用消费者业务钩子\");}} }}");
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(database);builder.Services.AddSingleton<IIdGenerator>(ids);builder.Services.AddScoped<IRuntimeAuthorization,Authorization>();builder.Services.AddScoped<IRecordMutationObserver>(provider=>new FileReferenceMutationObserver(fileRows,new([metadata]),provider.GetRequiredService<IRuntimeAuthorization>(),ids));builder.Services.AddSingleton<IRecordMutationObserver>(failure);assembly.GetType("Example.MixedPropagation.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration=>configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error){await File.AppendAllTextAsync(Path.Combine(evidence,"request-errors.txt"),error+"\n");context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(R<object>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};
            string Route(string table,string page)=>"/api/admin/auto/"+prefix+"_"+table+"/"+page;
            async Task<JsonElement> Post(string table,string page,string action,object body){using var response=await client.PostAsJsonAsync(Route(table,page)+action,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            var source=Data(await Post("source","101","/add",new{name="来源业务值",providerId=Provider.ToString(),alternateId=(Provider+1).ToString()}));var a=Id(source);own[prefix+"_source"].Add(a);Assert.Equal("公司甲",source.GetProperty("providerName").GetString());Assert.Equal("公司乙",source.GetProperty("alternateName").GetString());
            var downstream=Data(await Post("downstream","401","/add",new{name="下游业务值",orderId=a.ToString()}));var d=Id(downstream);own[prefix+"_downstream"].Add(d);Assert.Equal("公司甲",downstream.GetProperty("orderName").GetString());
            var multi=Data(await Post("multi","501","/add",new{name="多选业务值",companyIds=new[]{Company.ToString(),(Company+1).ToString()}}));var m=Id(multi);own[prefix+"_multi"].Add(m);Assert.Equal(new[]{"公司甲","公司乙"},multi.GetProperty("companyNames").EnumerateArray().Select(item=>item.GetString()));
            // 根对象的自关联必须把当前名称补回写回执，不读到旧RR缓存。
            source=Data(await Post("source","101","/update",new{id=a.ToString(),parentId=a.ToString(),name="来源当前名"}));Assert.Equal("来源当前名",source.GetProperty("parentName").GetString());
            if(logical){Data(await Post("company","301","/update",new{id=Company.ToString(),attachments=new[]{fileIds[0].ToString()}}));Assert.Single(await fileRows.ReadAsync("yz_file_ref",new Dictionary<string,object?>{{"record_id",Company}}));}
            var other=ids.NextId();await Seed("source",other,new(){{"name","外项目保持"},{"provider_id",Provider},{"provider_name","外项目旧缓存"},{"yz_project_id",Project+1}});long? deleted=null;if(logical){deleted=ids.NextId();await Seed("source",deleted.Value,new(){{"name","软删来源保持"},{"provider_id",Provider},{"provider_name","软删旧缓存"},{"delete_no",new DateTime(2026,1,1)}});}
            var batch=new List<long>();for(var index=0;index<130;index++){var key=ids.NextId();await Seed("source",key,new(){{"name","跨批来源"},{"provider_id",Provider},{"provider_name","历史缓存"}});batch.Add(key);}
            await rows.UpdateAsync(prefix+"_source",a,new Dictionary<string,object?>{{"alternate_name","未触及的历史缓存"}});var before=await Snapshot();Data(await Post("company","301","/update",new{id=Company.ToString(),title="只改无关字段"}));Assert.Equal(before,await Snapshot(includeCompany:false));
            foreach(var suffix in new[]{"source","downstream","multi"})assembly.GetType("Example.MixedPropagation.Generated.Services."+GenerationModel.Pascal(prefix+"_"+suffix)+"Service",true)!.GetProperty("RejectBusinessHook")!.SetValue(null,true);client.DefaultRequestHeaders.Add("denyConsumers","1");
            try
            {
                Data(await Post("company","301","/update",new{id=Company.ToString(),name="公司甲改名"}));await Cache("公司甲改名",new string?[]{"公司甲改名","公司乙"});Assert.Equal("未触及的历史缓存",(await Row("source",a))["alternate_name"]);foreach(var key in batch)Assert.Equal("公司甲改名",(await Row("source",key))["provider_name"]);
                await VerifyRepeatableRead();await VerifyConflict();
                // 原维护回填实际变化继续进入同一混合队列，不能只修根缓存。
                await rows.UpdateAsync(prefix+"_source",a,new Dictionary<string,object?>{{"provider_name","待回填"}});await rows.UpdateAsync(prefix+"_downstream",d,new Dictionary<string,object?>{{"order_name","待下游回填"}});var maintenance=(RelationMaintenanceCatalog)assembly.GetType("Example.MixedPropagation.Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;var filled=await new RelationMaintenanceExecutor(database).BackfillAsync(maintenance,"1");Assert.True(filled.AffectedRows>0);await Cache("公司甲改名",new string?[]{"公司甲改名","公司乙"});
                var state=await Snapshot(true);failure.Fail=true;Assert.Equal(500,(await Post("company","301","/update",logical?(object)new{id=Company.ToString(),name="不能留下的改名",attachments=new[]{fileIds[1].ToString()}}:new{id=Company.ToString(),name="不能留下的改名"})).GetProperty("code").GetInt32());Assert.Equal(state,await Snapshot(true));failure.Fail=false;
                var changed=Data(await Post("provider","201","/update",new{id=Provider.ToString(),companyId=(Company+1).ToString()}));Assert.Equal("公司乙",changed.GetProperty("name").GetString());await Cache("公司乙",new string?[]{"公司甲改名","公司乙"});
                using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,Route("company","301")){Content=JsonContent.Create(new{id=(Company+1).ToString()})}))Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));if(!logical)own[prefix+"_company"].Remove(Company+1);await Cache(null,new string?[]{"公司甲改名",null});Assert.Null((await Row("source",a))["alternate_name"]);
                Data(await Post("provider","201","/update",new{id=Provider.ToString(),companyId=Company.ToString()}));await Cache("公司甲改名",new string?[]{"公司甲改名",null});using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,Route("provider","201")){Content=JsonContent.Create(new{id=Provider.ToString()})}))Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));if(!logical)own[prefix+"_provider"].Remove(Provider);await Cache(null,new string?[]{"公司甲改名",null});Assert.Equal(Provider,(await Row("source",a))["provider_id"]);
            }
            finally{failure.Fail=false;client.DefaultRequestHeaders.Remove("denyConsumers");foreach(var suffix in new[]{"source","downstream","multi"})assembly.GetType("Example.MixedPropagation.Generated.Services."+GenerationModel.Pascal(prefix+"_"+suffix)+"Service",true)!.GetProperty("RejectBusinessHook")!.SetValue(null,false);}
            Assert.Equal("外项目旧缓存",(await Row("source",other))["provider_name"]);if(deleted is{} deletedId)Assert.Equal("软删旧缓存",(await rows.ReadIncludingDeletedAsync(prefix+"_source",new Dictionary<string,object?>{{"id",deletedId}})).Single()["provider_name"]);await Receipt("verified",false);
            /// <summary>先建立旧RR快照，再由独立连接提交改名；刷新必须使用当前读，不能复制旧名称。</summary>
            async Task VerifyRepeatableRead()
            {
                var catalog=(RelationMaintenanceCatalog)assembly.GetType("Example.MixedPropagation.Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;
                await database.InTransactionAsync(async()=>{await using(var command=database.CurrentTransaction!.Connection!.CreateCommand()){command.Transaction=database.CurrentTransaction;command.CommandText="SELECT name FROM `"+prefix+"_company` WHERE id=@id";var value=command.CreateParameter();value.ParameterName="id";value.Value=Company;command.Parameters.Add(value);Assert.Equal("公司甲改名",await command.ExecuteScalarAsync());}await Execute("UPDATE `"+prefix+"_company` SET name=@name WHERE id=@id",("name","RR目标当前"),("id",Company));await new ProjectRelationMutationPropagator(database).AfterMutationAsync(catalog,prefix+"_company",Company,Project.ToString(CultureInfo.InvariantCulture),new HashSet<string>{"name"},false,false);return true;});await Cache("RR目标当前",new string?[]{"RR目标当前","公司乙"});Data(await Post("company","301","/update",new{id=Company.ToString(),name="公司甲改名"}));await Cache("公司甲改名",new string?[]{"公司甲改名","公司乙"});await File.WriteAllTextAsync(Path.Combine(evidence,"repeatable-read.json"),"{\"oldSnapshotEstablished\":true,\"independentTargetCommitted\":true,\"currentMixedAndMultiVerified\":true}\n");
            }
            /// <summary>持有真实来源写锁，再与目标HTTP写锁组成锁环；两个可能牺牲者都要求整链全有或全无。</summary>
            async Task VerifyConflict()
            {
                await using var source=new MySqlConnection(options.ConnectionString);await source.OpenAsync();await using(var check=source.CreateCommand()){check.CommandText="SELECT @@innodb_deadlock_detect";Assert.Equal(1,Convert.ToInt32(await check.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await using var transaction=await source.BeginTransactionAsync();var completed=false;Task<JsonElement>? pending=null;var sourceVictim=false;
                try
                {
                    await using(var command=source.CreateCommand()){command.Transaction=transaction;command.CommandText="SELECT id FROM `"+prefix+"_source` WHERE id=@id FOR UPDATE";command.Parameters.AddWithValue("id",a);Assert.Equal(a,Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}
                    pending=Post("company","301","/update",new{id=Company.ToString(),name="并发混合名称"});var observed=false;var watch=System.Diagnostics.Stopwatch.StartNew();await using var probe=new MySqlConnection(options.ConnectionString);await probe.OpenAsync();while(!observed&&!pending.IsCompleted&&watch.Elapsed<TimeSpan.FromSeconds(8)){await using var check=await probe.BeginTransactionAsync();try{await using var command=probe.CreateCommand();command.Transaction=check;command.CommandText="SELECT id FROM `"+prefix+"_company` WHERE id=@id FOR UPDATE NOWAIT";command.Parameters.AddWithValue("id",Company);await command.ExecuteScalarAsync();}catch(MySqlException error)when(error.Number==3572){observed=true;}finally{await check.RollbackAsync();}if(!observed)await Task.Delay(25);}Assert.True(observed,"必须实际观察目标HTTP写锁。");Assert.False(pending.IsCompleted);
                    try{await using var command=source.CreateCommand();command.Transaction=transaction;command.CommandTimeout=10;command.CommandText="SELECT id FROM `"+prefix+"_company` WHERE id=@id FOR UPDATE";command.Parameters.AddWithValue("id",Company);await command.ExecuteScalarAsync();await transaction.CommitAsync();completed=true;}catch(MySqlException error)when(error.Number==1213){sourceVictim=true;await transaction.RollbackAsync();completed=true;}
                    var response=await pending;var success=response.GetProperty("code").GetInt32()==200;if(sourceVictim)Assert.True(success);else{Assert.False(success);Assert.Contains("并发冲突",response.GetProperty("msg").GetString());}var expected=success?"并发混合名称":"公司甲改名";Assert.Equal(expected,(await Row("company",Company))["name"]);await Cache(expected,new string?[]{expected,"公司乙"});foreach(var key in batch)Assert.Equal(expected,(await Row("source",key))["provider_name"]);await File.WriteAllTextAsync(Path.Combine(evidence,"conflict.json"),JsonSerializer.Serialize(new{targetLockObserved=true,sourceVictim,targetSucceeded=success,allOrNothing=true,noGlobalObservationGrants=true})+"\n");
                }
                finally{if(!completed)await transaction.RollbackAsync();if(pending is not null)await pending;}
                Data(await Post("company","301","/update",new{id=Company.ToString(),name="公司甲改名"}));await Cache("公司甲改名",new string?[]{"公司甲改名","公司乙"});
            }
            async Task Cache(string? name,string?[] names){Assert.Equal(name,(await Row("source",a))["provider_name"]);Assert.Equal(name,(await Row("downstream",d))["order_name"]);Assert.Equal(names,JsonSerializer.Deserialize<string?[]>((string)(await Row("multi",m))["company_names"]!));}
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.txt"),error.ToString());throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){await Pin();Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(metadata)));foreach(var table in metadata.ConfigTables)Assert.True((await rows.ReadIncludingDeletedAsync(table.TableName)).Select(row=>SqlRowStore.Id(row)).ToHashSet().SetEquals(own[table.TableName]));foreach(var key in fileIds){var file=(await fileRows.ReadIncludingDeletedAsync("yz_file_library",new Dictionary<string,object?>{{"id",key}})).Single();Assert.Equal(prefix+".txt",file["name"]);await Execute("DELETE FROM yz_file_ref WHERE file_id=@id",("id",key));await Execute("DELETE FROM yz_file_library WHERE id=@id",("id",key));}foreach(var table in metadata.ConfigTables){Assert.StartsWith(prefix+"_",table.TableName);await Execute("DROP TABLE `"+table.TableName+"`");}await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();foreach(var table in metadata.ConfigTables){await using var check=independent.CreateCommand();check.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";check.Parameters.AddWithValue("name",table.TableName);Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        async Task Seed(string table,long id,Dictionary<string,object?> values){values["id"]=id;values.TryAdd("yz_project_id",Project);await rows.InsertAsync(prefix+"_"+table,values);own[prefix+"_"+table].Add(id);}
        async Task<Dictionary<string,object?>> Row(string table,long id)=>(await rows.ReadAsync(prefix+"_"+table,new Dictionary<string,object?>{{"id",id}})).Single();
        async Task<string> Snapshot(bool includeCompany=false)=>JsonSerializer.Serialize(new{sources=await rows.ReadIncludingDeletedAsync(prefix+"_source"),downstream=await rows.ReadIncludingDeletedAsync(prefix+"_downstream"),multi=await rows.ReadIncludingDeletedAsync(prefix+"_multi"),companies=includeCompany?await rows.ReadIncludingDeletedAsync(prefix+"_company"):null,refs=(await fileRows.ReadIncludingDeletedAsync("yz_file_ref")).Where(row=>fileIds.Contains(SqlRowStore.Id(row,"file_id"))).OrderBy(row=>SqlRowStore.Id(row)).ToArray()});
        async Task Execute(string sql,params(string Name,object Value)[] parameters){await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var value in parameters)command.Parameters.AddWithValue(value.Name,value.Value);await command.ExecuteNonQueryAsync();}
        async Task Pin(){await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString());}
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,prefix,logical,tables=metadata.ConfigTables.Select(table=>table.TableName),own,fileIds,fingerprint,cleaned,instanceVerified=true,fixedAuthorization=true,browserExecuted=false})+"\n");
    }
    /// <summary>源R1的两个角色均指向R0中间表，R1后续副本取已物化值；普通多选并行依赖同一最终目标。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool logical)
    {
        var metadata=new MetadataCatalog{YzProjectId="54",YzAdminTypes=[new(){Id="5",YzProjectId="54"}]};foreach(var (id,name,mode,page) in new[]{("1","source",1,"101"),("2","provider",0,"201"),("3","company",1,"301"),("4","downstream",1,"401"),("5","multi",1,"501")})
        {
            metadata.ConfigTables.Add(new(){Id=id,TableName=prefix+"_"+name,IsRongyu=mode});metadata.ConfigColumns.AddRange([Column(id+"_id",id,"id","int64",true),Column(id+"_project",id,"yz_project_id","int64")]);if(logical)metadata.ConfigColumns.Add(Column(id+"_deleted",id,"delete_no","datetime"));metadata.ConfigPages.Add(new(){Id=page,ConfigTableId=id,YzProjectId="54",YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id"});
        }
        metadata.ConfigColumns.AddRange([Column("source_name","1","name"),Foreign("provider_id","1","provider_id","2"),Join("provider_name","1","provider_name","provider.name"),Foreign("alternate_id","1","alternate_id","2"),Join("alternate_name","1","alternate_name","alternate.name"),Foreign("parent_id","1","parent_id","1"),Join("parent_name","1","parent_name","parent.name"),Foreign("company_id","2","company_id","3"),Join("provider_display","2","name","company.name"),Column("company_name","3","name"),Column("company_title","3","title"),new(){Id="attachments",ConfigTableId="3",ColumnName="attachments",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"},Column("downstream_name","4","name"),Foreign("order_id","4","order_id","1"),Join("order_name","4","order_name","order.provider_name"),Column("multi_name","5","name"),new(){Id="company_ids",ConfigTableId="5",ColumnName="company_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="3",ControlDetail="{\"pageId\":\"302\",\"nameField\":\"name\"}"}]);metadata.ConfigPages.Add(new(){Id="302",ConfigTableId="3",YzProjectId="54",YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id",Extra=new(){{"type",JsonSerializer.SerializeToElement(2)}}});
        if(!logical)metadata.ConfigColumns.RemoveAll(column=>column.Id=="attachments"); // 物理删除含文件的既有观察器缺口单独登记，本测试不把其拒绝冒充关系成功。
        foreach(var page in metadata.ConfigPages)foreach(var column in metadata.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId&&column.ColumnName is not("id" or "yz_project_id" or "delete_no")))metadata.ConfigPageColumns.Add(new(){Id=page.Id+"_"+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsShow=1,IsEdit=column.Extra.ContainsKey("isJoinColumn")?0:1});return metadata;
    }
    private static ColumnMetadata Column(string id,string table,string name,string type="string",bool required=false)=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,ColumnLength="100",IsDbNotNull=required?1:0};
    private static ColumnMetadata Foreign(string id,string table,string name,string target){var column=Column(id,table,name,"int64");column.LinkConfigTableId=target;return column;}
    private static ColumnMetadata Join(string id,string table,string name,string path){var column=Column(id,table,name);column.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);column.Extra["columnFullName"]=JsonSerializer.SerializeToElement(path);return column;}
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.GetRawText());return response.GetProperty("data");}
    private static long Id(JsonElement data)=>long.Parse(data.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);
    private sealed class Failure(string table):IRecordMutationObserver{public bool Fail{get;set;}public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){if(Fail&&context.TableName==table)throw new InvalidOperationException("混合传播尾部故障");return Task.CompletedTask;}}
    private sealed class Authorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(Actor,"混合传播夹具",false,Project.ToString(CultureInfo.InvariantCulture),"5");
        public void RequirePagePermission(string pageId,string operation){if(accessor.HttpContext!.Request.Headers.ContainsKey("denyConsumers")&&pageId is "101" or "401" or "501")throw new UnauthorizedAccessException("消费者页面不授予操作权限");}
    }
}
