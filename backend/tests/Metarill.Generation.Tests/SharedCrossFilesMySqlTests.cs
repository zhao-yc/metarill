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
using Metarill.Runtime.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享父范围的原文件控制器、真实引用观察器及隔离MySQL；固定授权器不替代正式账号、完整包和浏览器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossFilesMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    [StandaloneExportMySqlFact]public Task String_string()=>Run("string","string");
    [StandaloneExportMySqlFact]public Task String_long()=>Run("string","int64",physicalDelete:true);
    [StandaloneExportMySqlFact]public Task Long_string()=>Run("int64","string");
    [StandaloneExportMySqlFact]public Task Long_long()=>Run("int64","int64",physicalDelete:true);
    [StandaloneExportMySqlFact]public Task Short_string()=>Run("string","int64",true);
    [StandaloneExportMySqlFact]public Task Short_long()=>Run("int64","string",true,physicalDelete:true);
    [StandaloneExportMySqlFact]public Task Derived_targets()=>Run("string","int64",false,true);
    /// <summary>HTTP并发与同源基线在独立审计目录运行，不混入B79历史样本。</summary>
    internal static Task RunHttp(string type,string parentType,bool shorter=false,bool derived=false,bool physicalDelete=false,bool concurrency=true)=>Run(type,parentType,shorter,derived,physicalDelete,concurrency,true);
    /// <summary>固定实例pin、数据库与账号配置核对后才建立本轮随机表。</summary>
    private static async Task Run(string type,string parentType,bool shorter=false,bool derived=false,bool physicalDelete=false,bool concurrency=false,bool auditHttp=false)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("跨表缓存实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建缓存样本。");}await Exercise(options,type,parentType,shorter,derived,physicalDelete,concurrency,auditHttp);
    }
    /// <summary>元数据生成共享R0、固定目标及物理父外键，五个父页面不手补生成基类。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool shorter,bool derived,bool physicalDelete,bool concurrency,bool auditHttp)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix=(auditHttp?"mt_sfh_":"mt_sfl_")+nonce[..12];var c=SharedCrossFilesGenerationTests.Catalog(prefix,type,parentType,shorter,derived,physicalDelete);var source=prefix+"_source";var company=prefix+"_company";var roles=new[]{"country","area","place","warehouse"};var leaf=shorter?"areaId":"placeId";var leafField=shorter?"area_id":"place_id";
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";
        var root=NonRedundantApplicationFixture.Root();var evidence=auditHttp?Path.Combine(root,".local/shared-cross-file-http",concurrency?"concurrent":"baseline","mysql-"+nonce):Path.Combine(root,".local/shared-cross-files/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-files-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var observer=new Observer(source);var fileIds=new List<long>();SqlRowStore? library=null;WebApplication? app=null;var created=false;var passed=false;
        object Parent(long id)=>parentType=="string"?Id(id):id;long Node(int role,int batch=0)=>First+batch*10+role;long Company(int role,int batch=0)=>First+500+batch*10+role;
        await Receipt("intent",new{nonce,type,parentType,shorter,derived,physicalDelete,httpConcurrency=concurrency,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            var fileMetadata=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(root,"backend/metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var fileSchema=await schema.PreviewAsync(fileMetadata);Assert.All(fileSchema.Changes,x=>Assert.Equal("createTable",x.Kind));if(fileSchema.Changes.Count>0)Assert.True((await schema.ApplyAsync(fileSchema)).Succeeded);library=new SqlRowStore(session,MetadataCatalogComposition.MergePhysical(c,fileMetadata));var fileContext=new FileMetadataContext([c]);Assert.Single(fileContext.Catalogs);var fileOptions=new FileLibraryOptions{RootDirectory=Path.Combine(directory,"objects"),MaxFileBytes=4096};
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);Assert.DoesNotContain((await schema.InspectAsync(c)).Tables.Single(t=>t.Name==source).Columns,col=>col.Name.EndsWith("_name",StringComparison.Ordinal));
            for(var batch=0;batch<4;batch++)
            {
                for(var role=0;role<4;role++){var row=D("id",Node(role,batch),"yz_project_id",batch==3?Project+1:Project,"status",1);if(derived&&role<3){await store.InsertAsync(company,D("id",Company(role,batch),"name",new[]{"国","区","城"}[role]+batch,"yz_project_id",batch==3?Project+1:Project));row["company_id"]=Company(role,batch);}else row["name"]=new[]{"国","区","城","仓"}[role]+batch;if(role>0)row[prefix+"_"+(role==1?"country":"area")+"_id"]=Parent(Node(role==1?0:1,batch));await store.InsertAsync(prefix+"_"+roles[role],row);}
            }
            var model=GenerationModel.Create(c,"Example.SharedSparseProbe");var generator=new CSharpGenerator();generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);var sourceType=model.Tables.Single(t=>t.Source.Id=="2").Name;var hand=Path.Combine(directory,"generated/Services/"+sourceType+"Service.cs");var property=shorter?"AreaId":"PlaceId";var newId=Id(Node(shorter?1:2,2));var value=type=="string"?"\""+newId+"\"":newId+"L";var hook="using Metarill.Core.Security;\nusing "+model.Namespace+".Generated.Entities;\nnamespace "+model.Namespace+".Generated.Services;\npublic sealed partial class "+sourceType+"Service\n{\n    /// <summary>手写钩子改变父路径，登记或静默变化都不能绕过页面父归属。</summary>\n    partial void BeforeSave("+sourceType+" entity,RuntimeActor actor,bool creating,ISet<string> submittedFields){if(entity.Name is \"钩子越界\" or \"静默越界\"){entity."+property+"="+value+";if(entity.Name==\"钩子越界\")submittedFields.Add(\""+leafField+"\");}}\n    /// <summary>删除钩子也不得伪造稀疏末端。</summary>\n    partial void BeforeDelete("+sourceType+" entity,RuntimeActor actor){if(entity.Name==\"删除钩子越界\")entity.WarehouseId="+(type=="string"?"\""+Id(Node(3,1))+"\"":Id(Node(3,1))+"L")+";}\n}\n";await File.WriteAllTextAsync(hand,hook);generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);Assert.Equal(hook,await File.ReadAllTextAsync(hand));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(997));builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<IRuntimeAuthorization,Auth>();builder.Services.AddSingleton(fileContext);builder.Services.AddSingleton(fileOptions);builder.Services.AddScoped<FileLibraryService>(provider=>new(library!,fileContext,new SnowflakeIdGenerator(1013),provider.GetRequiredService<IRuntimeAuthorization>(),fileOptions));builder.Services.AddScoped<IFileLibraryResolver,SingleDatabaseFileLibraryResolver>();builder.Services.AddScoped<IRecordMutationObserver>(provider=>new FileReferenceMutationObserver(library!,fileContext,provider.GetRequiredService<IRuntimeAuthorization>(),new SnowflakeIdGenerator(1014),fileOptions));builder.Services.AddSingleton<IRecordMutationObserver>(observer);assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddApplicationPart(typeof(Metarill.Api.Controllers.FileLibraryController).Assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or PageImportException or MySqlException){ctx.Response.StatusCode=error is UnauthorizedAccessException?401:400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message,error is UnauthorizedAccessException?401:500));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> Post(string uri,object body){using var response=await client.PostAsJsonAsync(uri,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<JsonElement> Call(string page,string action,object body,string parent){await Receipt("last-request",new{page,action,parent});var url="/api/admin/auto/"+source+"/"+page+"/"+(action=="remove"?"":action)+"?parentId="+parent;using var request=new HttpRequestMessage(action=="remove"?HttpMethod.Delete:HttpMethod.Post,url){Content=JsonContent.Create(body)};using var response=await client.SendAsync(request);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<long> Upload(string label){var bytes=Encoding.UTF8.GetBytes("共享父范围文件"+nonce+label);var planned=Data(await Post("/api/admin/sys/file-library/upload-plan",new{originalName=nonce+"-"+label+".txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="9001"}));var id=long.Parse(planned.GetProperty("fileId").GetString()!,CultureInfo.InvariantCulture);fileIds.Add(id);using(var form=new MultipartFormDataContent()){form.Add(new ByteArrayContent(bytes),"file",label+".txt");using var response=await client.PostAsync("/api/admin/sys/file-library/local-upload/"+Id(id),form);Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));}Data(await Post("/api/admin/sys/file-library/complete",new{fileId=Id(id)}));return id;}
            async Task Download(long file,bool allowed){client.DefaultRequestHeaders.Add("reader","1");try{using var response=await client.GetAsync("/api/admin/sys/file-library/protected-content/"+Id(file));Assert.Equal(allowed?HttpStatusCode.OK:HttpStatusCode.Unauthorized,response.StatusCode);if(allowed)Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());}finally{client.DefaultRequestHeaders.Remove("reader");}}
            async Task Refs(long file,int count,string page){var rows=await library!.ReadAsync("yz_file_ref",D("file_id",file));Assert.Equal(count,rows.Count);Assert.All(rows,r=>{Assert.Equal(page,SqlRowStore.Text(r,"page_id"));Assert.Equal("attachments",r["field_name"]);Assert.Equal(Id(Project),SqlRowStore.Text(r,"yz_project_id"));});}
            async Task<string> Snapshot(){var rows=new Dictionary<string,object>();foreach(var table in c.ConfigTables)rows[table.TableName]=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x)).ToArray();rows["ownFiles"]=(await library!.ReadIncludingDeletedAsync("yz_file_library")).Where(r=>fileIds.Contains(SqlRowStore.Id(r))).OrderBy(r=>SqlRowStore.Id(r)).ToArray();rows["ownReferences"]=(await library.ReadIncludingDeletedAsync("yz_file_ref")).Where(r=>fileIds.Contains(SqlRowStore.Id(r,"file_id"))).OrderBy(r=>SqlRowStore.Id(r)).ToArray();return JsonSerializer.Serialize(rows);}
            Dictionary<string,object?> Selection()=>D("name","共享文件",leaf,Id(Node(shorter?1:2)),"warehouseId",Id(Node(3)),"ownerCountryId",Id(Node(0)));
            foreach(var (page,role) in new[]{("202",0),("203",1),("204",shorter?1:2),("205",3),("206",0)})
            {
                var parent=Id(Node(role));var file=await Upload(page);var createdRow=Selection();createdRow["attachments"]=new[]{Id(file)};var added=Data(await Call(page,"add",createdRow,parent));var id=added.GetProperty("id").GetString()!;var number=long.Parse(id,CultureInfo.InvariantCulture);await Refs(file,1,page);await Download(file,true);
                async Task<JsonElement> Update(Dictionary<string,object?> body,string? context=null){body["id"]=id;return await Call(page,"update",body,context??parent);}
                var original=Assert.Single(await store.ReadAsync(source,D("id",number)));Assert.Null(original["country_id"]);Assert.Null(original["area_id"]);Assert.Equal(Id(Node(3)),SqlRowStore.Text(original,"warehouse_id"));Data(await Update(D("name","省略附件")));await Refs(file,1,page);Data(await Update(JsonSerializer.Deserialize<Dictionary<string,object?>>(added.GetRawText())!));await Refs(file,1,page);
                var copy=Data(await Call(page,"copy/"+id,new{},parent)).GetProperty("id").GetString()!;await Refs(file,2,page);
                if(concurrency)await VerifyConcurrentHttp();
                Data(await Call(page,"remove",new{id=copy},parent));await Refs(file,1,page);
                /// <summary>生成DML和真实文件观察器完成后暂停原HTTP事务，验证另一个HTTP请求的提交可见性及真实SQL竞争。</summary>
                async Task VerifyConcurrentHttp()
                {
                    using var reader=new HttpClient{BaseAddress=client.BaseAddress,Timeout=TimeSpan.FromSeconds(10)};reader.DefaultRequestHeaders.Add("reader","1");
                    foreach(var commit in new[]{true,false})
                    {
                        var trial=await Upload(page+(commit?"-http-commit":"-http-rollback"));var oldName=SqlRowStore.Text(Assert.Single(await store.ReadAsync(source,D("id",number))),"name");var beforeConcurrent=await Snapshot();
                        var marker=commit?"HTTP并发提交":"HTTP并发回滚";var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        observer.Checkpoint=async(context,token)=>
                        {
                            if(context.TableName!=source||context.RecordId!=number||!context.SubmittedFields.Values.Any(v=>Equals(v,marker)))return;
                            Assert.NotNull(context.Transaction);entered.TrySetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(30),token);
                            if(!commit)throw new InvalidOperationException("共享HTTP文件并发回滚探针");
                        };
                        var pending=Update(D("name",marker,"attachments",new[]{Id(trial)}));Exception? primary=null;JsonElement result=default;
                        try
                        {
                            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));Assert.Null(session.CurrentTransaction);
                            using(var detail=await reader.GetAsync("/api/admin/auto/"+source+"/"+page+"/detail/"+id+"?parentId="+parent))
                            {
                                Assert.Equal(HttpStatusCode.OK,detail.StatusCode);var data=Data(JsonSerializer.Deserialize<JsonElement>(await detail.Content.ReadAsStringAsync()));
                                Assert.Equal(oldName,data.GetProperty("name").GetString());Assert.Equal(new[]{Id(file)},data.GetProperty("attachments").EnumerateArray().Select(v=>v.GetString()).ToArray());
                            }
                            await Protected(file,true);await Protected(trial,false);await Refs(file,2,page);await Refs(trial,0,page);
                            await RowLock(source,number,true);foreach(var index in Enumerable.Range(0,4).Where(i=>!shorter||i!=2))await RowLock(prefix+"_"+roles[index],Node(index),true);
                            await RowLock("yz_file_library",trial,true);await RowLock(prefix+"_warehouse",Node(3,2),false);await WaitingWriter(true);
                        }
                        catch(Exception error){primary=error;}
                        finally{observer.Checkpoint=null;release.TrySetResult();}
                        try{result=await pending.WaitAsync(TimeSpan.FromSeconds(20));}
                        catch(Exception error)when(primary is not null){throw new AggregateException("并发检查与原HTTP收尾均失败，首因保留。",primary,error);}
                        if(primary is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
                        await RowLock(source,number,false);foreach(var index in Enumerable.Range(0,4).Where(i=>!shorter||i!=2))await RowLock(prefix+"_"+roles[index],Node(index),false);
                        await RowLock("yz_file_library",trial,false);await WaitingWriter(false);
                        if(commit)
                        {
                            Data(result);await Refs(file,1,page);await Refs(trial,1,page);await Protected(trial,true);
                            Assert.Equal(marker,SqlRowStore.Text(Assert.Single(await store.ReadAsync(source,D("id",number))),"name"));
                            // 已提交副本保留旧文件的可读引用，按原规则恢复；不能借上传者身份重绑历史孤儿。
                            Data(await Update(D("name",oldName,"attachments",new[]{Id(file)})));await Refs(file,2,page);await Refs(trial,0,page);await Protected(trial,false);
                        }
                        else
                        {
                            Assert.Equal(500,result.GetProperty("code").GetInt32());Assert.Contains("共享HTTP文件并发回滚探针",ErrorText(result));
                            Assert.Equal(beforeConcurrent,await Snapshot());await Refs(file,2,page);await Refs(trial,0,page);await Protected(file,true);await Protected(trial,false);
                        }
                        await Receipt("http-"+page+(commit?"-commit":"-rollback"),new{page,commit,sourceTransactionIsolated=true,committedHttpDetailOnly=true,uncommittedProtectedFileDenied=true,allStoredFrontierLocks=true,unselectedWarehouseFree=true,waitingWriterError=1205,releasedWriterActuallyChangedAndRolledBack=true,rollbackSnapshotEqual=!commit,fixedAuthorizationInjected=true,formalIdentity=false});
                    }
                    /// <summary>保护内容走真实HTTP控制器及字节流，读取账号不具上传者身份。</summary>
                    async Task Protected(long fid,bool allowed){using var response=await reader.GetAsync("/api/admin/sys/file-library/protected-content/"+Id(fid));Assert.Equal(allowed?HttpStatusCode.OK:HttpStatusCode.Unauthorized,response.StatusCode);if(allowed)Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());}
                    /// <summary>逐行竞争和未选对照使用独立事务，精确3572才算当前HTTP持有锁。</summary>
                    async Task RowLock(string table,long key,bool blocked)
                    {
                        await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();
                        try{await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT id FROM "+session.Provider.QuoteIdentifier(table)+" WHERE id=@id FOR UPDATE NOWAIT";command.Parameters.AddWithValue("id",key);if(blocked)Assert.Equal(3572,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteScalarAsync())).Number);else Assert.Equal(key,Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}finally{await transaction.RollbackAsync();}
                    }
                    /// <summary>独立非池化会话实际切换目标状态；持锁时1205，释放后可写并观察到变化，再回滚。</summary>
                    async Task WaitingWriter(bool blocked)
                    {
                        var cs=new MySqlConnectionStringBuilder(options.ConnectionString){Pooling=false};await using var connection=new MySqlConnection(cs.ConnectionString);await connection.OpenAsync();await using(var setting=connection.CreateCommand()){setting.CommandText="SET SESSION innodb_lock_wait_timeout=1";await setting.ExecuteNonQueryAsync();}await using var transaction=await connection.BeginTransactionAsync();
                        try{await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandTimeout=10;command.CommandText="UPDATE "+session.Provider.QuoteIdentifier(prefix+"_warehouse")+" SET status=1-status WHERE id=@id";command.Parameters.AddWithValue("id",Node(3));if(blocked)Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteNonQueryAsync())).Number);else{Assert.Equal(1,await command.ExecuteNonQueryAsync());command.CommandText="SELECT status FROM "+session.Provider.QuoteIdentifier(prefix+"_warehouse")+" WHERE id=@id";Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}}finally{await transaction.RollbackAsync();}
                    }
                }
                // 保护下载依据完整共享组，不能只检查恰好提供父字段的一个好分支。
                // 短主叶压缩后只有仓库末端；整组合法迁移仍可读，独立末端冲突／混合物理父归属则拒绝。
                await store.UpdateAsync(source,number,D("warehouse_id",type=="string"?(object)Id(Node(3,1)):Node(3,1)));await Download(file,shorter&&page!="206");await store.UpdateAsync(source,number,D("warehouse_id",original["warehouse_id"]));await Download(file,true);
                await store.UpdateAsync(source,number,D("country_id",type=="string"?(object)Id(Node(0)):Node(0)));await Download(file,false);await store.UpdateAsync(source,number,D("country_id",null));await Download(file,true);
                await store.UpdateAsync(prefix+"_warehouse",Node(3),D("status",0));await Download(file,false);await store.UpdateAsync(prefix+"_warehouse",Node(3),D("status",1));await Download(file,true);
                if(parentType=="string"){await store.UpdateAsync(prefix+"_warehouse",Node(3),D(prefix+"_area_id","0"+Id(Node(1))));await Download(file,false);await store.UpdateAsync(prefix+"_warehouse",Node(3),D(prefix+"_area_id",Parent(Node(1))));await Download(file,true);}
                await store.UpdateAsync(source,number,D("yz_project_id",Project+1));await Download(file,false);await store.UpdateAsync(source,number,D("yz_project_id",Project));if(page=="206"){await store.UpdateAsync(source,number,D("owner_country_id",type=="string"?(object)Id(Node(0,1)):Node(0,1)));await Download(file,false);await store.UpdateAsync(source,number,D("owner_country_id",original["owner_country_id"]));}await Download(file,true);
                client.DefaultRequestHeaders.Add("denySource","1");await Download(file,false);client.DefaultRequestHeaders.Remove("denySource");client.DefaultRequestHeaders.Add("denyTargets","1");await Download(file,true);Data(await Update(D("name","目标候选撤权时附件省略")));client.DefaultRequestHeaders.Remove("denyTargets");
                var before=await Snapshot();Assert.Equal(500,(await Update(D("attachments",null),Id(Node(role,1)))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());Assert.Equal(401,(await Update(D("attachments",new[]{Id(long.MaxValue)}))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
                var second=await Upload(page+"-late");var constraint="mt_sfl_ref_"+nonce[..8]+page;var installed=false;var late=D("name","晚期文件替换","attachments",new[]{Id(second)});if(role!=3)late["warehouseId"]=null;else if(!shorter)late[leaf]=null;
                try{await Sql(session,"ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+Id(second)+")");installed=true;before=await Snapshot();var failed=await Update(late);Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.Contains(constraint,ErrorText(failed));Assert.Equal(before,await Snapshot());await Refs(file,1,page);await Refs(second,0,page);await Sql(session,"ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");installed=false;Data(await Update(late));await Refs(file,0,page);await Refs(second,1,page);await Download(file,false);await Download(second,true);}finally{if(installed)await Sql(session,"ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");}
                Data(await Update(D("attachments",null)));await Refs(second,0,page);await Download(second,false);Data(await Update(D("attachments",Array.Empty<string>())));await Refs(second,0,page);// 已解绑的历史文件不能复用首次上传例外；使用新上传文件验证重绑和晚期观察器事务回滚。
                var third=await Upload(page+"-reset");var reset=Selection();reset["attachments"]=new[]{Id(third)};Data(await Update(reset));await Refs(third,1,page);var fourth=await Upload(page+"-rollback");before=await Snapshot();observer.SawScoped=false;var lateFailure=await Update(D("name","晚期回滚","attachments",new[]{Id(fourth)}));Assert.Equal(500,lateFailure.GetProperty("code").GetInt32());Assert.Contains("共享父范围尾部回滚探针",ErrorText(lateFailure));Assert.True(observer.SawScoped);Assert.Equal(before,await Snapshot());await Refs(third,1,page);await Refs(fourth,0,page);
                Data(await Call(page,"remove",new{id},parent));await Refs(third,0,page);await Download(third,false);var deleted=await store.ReadIncludingDeletedAsync(source,D("id",number));if(physicalDelete)Assert.Empty(deleted);else Assert.NotNull(Assert.Single(deleted)["delete_no"]);
            }
            passed=true;await Receipt("verified",new{nonce,type,parentType,shorter,derived,physicalDelete,httpConcurrency=concurrency,httpConcurrentCases=concurrency?10:0,fixedAuthorizationInjected=true,passed,parentPages=5,realGeneratedHttp=true,realFileController=true,realFileObserver=true,uploadAndProtectedDownload=true,allStoredFrontiersValidated=true,shortWholeGroupMoveAllowed=shorter,badAncestorDenied=true,targetRangeAndCanonicalIds=true,projectAndPhysicalParentScope=true,sourceAndTargetPermissionSeparation=true,omissionNullAndEmpty=true,copyAndDelete=true,lateReferenceConstraintRollback=true,lateObserverRollback=true,fileIds,formalWeb=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,shorter,derived,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(library is not null){foreach(var file in fileIds){var rows=await library.ReadIncludingDeletedAsync("yz_file_library",D("id",file));if(rows.Count>0){Assert.Equal(997L,SqlRowStore.Id(rows[0],"insert_manager_id"));Assert.Contains(nonce,SqlRowStore.Text(rows[0],"name"));}await using var txConnection=session.Provider.CreateConnection(options);await txConnection.OpenAsync();await using var command=txConnection.CreateCommand();command.CommandText="DELETE FROM yz_file_ref WHERE file_id=@id; DELETE FROM yz_file_library WHERE id=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=file;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();}using var verifyFiles=new DatabaseSession(options);var fileCheck=new SqlRowStore(verifyFiles,JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(root,"backend/metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!);foreach(var file in fileIds){Assert.Empty(await fileCheck.ReadIncludingDeletedAsync("yz_file_library",D("id",file)));Assert.Empty(await fileCheck.ReadIncludingDeletedAsync("yz_file_ref",D("file_id",file)));}await Receipt("file-cleanup",new{fileIds,independentAbsenceVerified=true,remaining=0});}if(created){using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>文件／业务观察者在同事务存储核验后运行，失败须回滚原始独立末端。</summary>
    private sealed class Observer(string source):IRecordMutationObserver
    {
        internal bool SawScoped;
        internal Func<RecordMutationContext,CancellationToken,Task>? Checkpoint;
        /// <summary>只在生成器真实DML及父归属核验之后抛出，确认原事务整笔回滚。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){if(context.TableName==source&&context.SubmittedFields.Values.Any(v=>Equals(v,"晚期回滚"))){SawScoped=true;throw new InvalidOperationException("共享父范围尾部回滚探针");}if(Checkpoint is{} checkpoint)await checkpoint(context,token);}
    }
    /// <summary>当前身份只由宿主建立；候选动作权限和已存父归属分别校验。</summary>
    private sealed class Auth(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>项目固定由宿主建立，reader仅切换为不同的测试账号。</summary>
        public RuntimeActor RequireActor()=>new(accessor.HttpContext!.Request.Headers.ContainsKey("reader")?998:997,"共享文件实库",false,Id(Project),"5");
        /// <summary>来源撤权禁止保护读取；目标仅撤销list，不能借候选动作扩大或缩小已有文件归属。</summary>
        public void RequirePagePermission(string page,string operation){var headers=accessor.HttpContext!.Request.Headers;var source=page is "201" or "202" or "203" or "204" or "205" or "206";if(source&&headers.ContainsKey("denySource")||!source&&headers.ContainsKey("denyTargets")&&operation=="list")throw new UnauthorizedAccessException("固定测试页面撤权。");}
    }
    /// <summary>解码原R错误文本，避免只凭code=500把权限或请求错误当作存储核验通过。</summary>
    private static string ErrorText(JsonElement value)=>string.Join(" ",value.EnumerateObject().Where(p=>p.Value.ValueKind==JsonValueKind.String).Select(p=>p.Value.GetString()));
    /// <summary>原R成功码是读取data的前提。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>所有业务数据参数化，ID不经过浮点数。</summary>
    private static Dictionary<string,object?> D(params object?[] values){var d=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)d.Add((string)values[i]!,values[i+1]);return d;}
    /// <summary>长ID按原接口以字符串返回。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>只有本轮已核对所有权、结构和行摘要的随机表可清理。</summary>
    private static async Task Sql(DatabaseSession session,string sql){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=sql;await cmd.ExecuteNonQueryAsync();}
}
