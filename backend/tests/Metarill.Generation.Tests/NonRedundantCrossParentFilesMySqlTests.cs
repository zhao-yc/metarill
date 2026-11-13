using System.Text;
using System.Security.Cryptography;
using Metarill.Runtime.Files;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实生成HTTP与MySQL验证逻辑父范围下的原文件控制器、稀疏CRUD、导入与引用事务。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantCrossParentFilesMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>两种存储型分别实际走软删与物理删，使用原文件控制器和真实引用观察器。</summary>
    [StandaloneExportMySqlFact]
    public async Task Generated_scoped_file_crud_import_copy_delete_share_real_file_transaction()
    {var options=await Options();await Exercise(options,"string","string",false);await Exercise(options,"int64","int64",false,true);}
    /// <summary>带真实文件列的逻辑父模型随BackendOnly下发完整运行时并实际编译，Full仍保持原门禁。</summary>
    [Fact]
    public async Task Backend_package_includes_logical_file_parent_runtime_and_builds()
    {
        var catalog=NonRedundantCrossParentTests.Catalog("nr_parent_files_pkg","int64","string",true);
        catalog.ConfigColumns.Add(new(){Id="9001",ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",SortNo=10000,ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))catalog.ConfigPageColumns.Add(new(){Id=page.Id+"9001",ConfigPageId=page.Id,ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=40});
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-parent-file-package");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,output,"Example.CrossParentFilePackage",NonRedundantApplicationFixture.Root(),ApplicationPackageMode.BackendOnly);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Files/FileCrossParentScope.cs")));
            foreach(var action in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,action);
            Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output+"-full","Example.CrossParentFull",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output+"-full"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>任何DDL之前独立核对实例、端口、主机及隔离库，不使用本机Docker。</summary>
    private static async Task<DatabaseOptions> Options()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");return options;
    }
    /// <summary>先编译真实产物，再显式建四张随机表；任何一行失败均比较全部物理数据。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool explicitAncestor,bool physicalDelete=false)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrcpf_"+nonce[..12];var catalog=NonRedundantCrossParentTests.Catalog(prefix,type,parentType,explicitAncestor,physicalDelete);foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))catalog.ConfigPageColumns.Add(new(){Id=page.Id+"9001",ConfigPageId=page.Id,ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=40});catalog.ConfigColumns.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",SortNo=10000,ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/nonredundant-cross-table-parent-files/generated-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-parent-files-mysql");var directory=Path.Combine(boundary,nonce);var source=prefix+"_source";var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var observer=new Observer();WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var guard=new Guard(options,prefix);var fileIds=new List<long>();await Receipt("intent",new{nonce,type,parentType,explicitAncestor,physicalDelete,names,database=options.ExpectedDatabase,verified=false});
        try
        {
            var files=JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(root,"backend/metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var fileSchema=await schema.PreviewAsync(files);Assert.All(fileSchema.Changes,change=>Assert.Equal("createTable",change.Kind));if(fileSchema.Changes.Count>0)Assert.True((await schema.ApplyAsync(fileSchema)).Succeeded);
            var infrastructure=Metarill.Core.Metadata.MetadataCatalogComposition.MergePhysical(catalog,files);var fileStore=new SqlRowStore(session,infrastructure);var fileContext=new FileMetadataContext([catalog]);var fileOptions=new FileLibraryOptions{RootDirectory=Path.Combine(directory,"objects"),MaxFileBytes=4096};
            var entity=GenerationModel.Pascal(source);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossParentFilesProbe");await File.WriteAllTextAsync(Path.Combine(evidence,"generated-service.cs"),await File.ReadAllTextAsync(Path.Combine(directory,"generated","Services",entity+"Service.g.cs")));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var initial=await schema.PreviewAsync(catalog);Assert.Equal(4,initial.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(initial)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==source).Columns,column=>column.Name.EndsWith("_name",StringComparison.Ordinal));
            object? Key(string field,long? value)=>value is null?null:(explicitAncestor&&field=="country_id"||type=="string")?value.Value.ToString(CultureInfo.InvariantCulture):value;
            // 只向本nonce样本写固定种子，不借UI权限或生产配置制造验收数据。
            async Task Target(int level,long id,string name,long? parent=null,long project=Project){var levels=new[]{"country","area","place"};var parentField=level==0?"":",`"+prefix+"_"+levels[level-1]+"_id`";await Execute(session,"INSERT INTO `"+prefix+"_"+levels[level]+"` (id,name,yz_project_id,status"+parentField+") VALUES (@id,@name,@project,1"+(level==0?"":",@parent")+")",("id",id),("name",name),("project",project),("parent",parentType=="string"?(object?)parent?.ToString(CultureInfo.InvariantCulture):parent));}
            foreach(var branch in new[]{0,10}){await Target(0,First+branch,branch==0?"国甲":"国乙");await Target(1,First+branch+1,branch==0?"区甲":"区乙",First+branch);await Target(2,First+branch+2,branch==0?"城甲":"城乙",First+branch+1);}await Target(2,First+32,"外项目叶",First+11,Project+1);
            async Task Seed(int index,string name,int depth,long? selected,int secondDepth,long? second,long project=Project,int status=1,DateTime? deleted=null){var fields=new[]{"country_id","area_id","place_id","secondary_country_id","secondary_area_id","secondary_place_id"};var values=fields.Select((field,position)=>(field,Key(field,position<3?position==depth?selected:null:position-3==secondDepth?second:null))).ToArray();await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id,status,amount"+(physicalDelete?"":",delete_no")+","+string.Join(",",fields.Select(field=>"`"+field+"`"))+") VALUES (@id,@name,@project,@status,1"+(physicalDelete?"":",@deleted")+","+string.Join(",",fields.Select(field=>"@"+field))+")",new[]{("id",(object?)(First+100+index)),("name",name),("project",project),("status",status),("deleted",deleted)}.Concat(values).ToArray());}
            await Seed(1,"甲",2,First+2,0,First+10);await Seed(2,"乙",1,First+1,2,First+12);await Seed(3,"根",0,First,1,First+11);await Seed(4,"乙组",2,First+12,-1,null);await Seed(5,"外项目",2,First+2,-1,null,project:Project+1);await Seed(6,"条件隐藏",2,First+2,-1,null,status:0);if(!physicalDelete)await Seed(7,"逻辑删除",2,First+2,-1,null,deleted:new DateTime(2026,1,1));
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var exportRoot=Path.Combine(directory,"exports");var exports=new ExcelPageExportService(new(){RootDirectory=exportRoot,MaxRows=100});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(guard);builder.Services.AddSingleton(fileContext);builder.Services.AddSingleton(fileOptions);builder.Services.AddScoped<FileLibraryService>(provider=>new(fileStore,fileContext,new SnowflakeIdGenerator(957),provider.GetRequiredService<IRuntimeAuthorization>(),fileOptions));builder.Services.AddScoped<IFileLibraryResolver,SingleDatabaseFileLibraryResolver>();builder.Services.AddScoped<IRecordMutationObserver>(provider=>new FileReferenceMutationObserver(fileStore,fileContext,provider.GetRequiredService<IRuntimeAuthorization>(),new SnowflakeIdGenerator(958),fileOptions));builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(954));builder.Services.AddScoped<IRuntimeAuthorization,Authorization>();assembly.GetType("Example.CrossParentFilesProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddApplicationPart(typeof(Metarill.Api.Controllers.FileLibraryController).Assembly).AddJsonOptions(settings=>settings.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or PageImportException){await Receipt("http-error",new{path=context.Request.Path.Value,error=error.ToString()});context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/201/";
            var bytes=Encoding.UTF8.GetBytes("原文件控制器及逻辑父范围");var fileRoute="/api/admin/sys/file-library";var rootId=First.ToString();var leafId=(First+2).ToString();var route="/api/admin/auto/"+source+"/202";
            var plan=Data(await Post(fileRoute+"/upload-plan",new{originalName="scoped.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="9001"}));var file=plan.GetProperty("fileId").GetString()!;fileIds.Add(long.Parse(file,CultureInfo.InvariantCulture));
            using(var form=new MultipartFormDataContent()){form.Add(new ByteArrayContent(bytes),"file","scoped.txt");using var response=await client.PostAsync(fileRoute+"/local-upload/"+file,form);Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));}Data(await Post(fileRoute+"/complete",new{fileId=file}));
            var added=Data(await Post(route+"/add?parentId="+rootId,new{name="逻辑父文件",placeId=leafId,attachments=new[]{file}}));var id=added.GetProperty("id").GetString()!;Assert.Equal(file,Assert.Single(added.GetProperty("attachments").EnumerateArray()).GetString());await References(1);await Sparse(id);
            client.DefaultRequestHeaders.Add("reader","1");using(var download=await client.GetAsync(fileRoute+"/protected-content/"+file)){Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(bytes,await download.Content.ReadAsByteArrayAsync());}client.DefaultRequestHeaders.Remove("reader");
            var copy=Data(await Post(route+"/copy/"+id+"?parentId="+rootId,new{})).GetProperty("id").GetString()!;await References(2);await Sparse(copy);Data(await Post(route+"/update?parentId="+rootId,new{id=copy,name="复制文件记录"}));
            var before=await State();Assert.Equal(500,(await Post(route+"/update?parentId="+(First+10),new{id,attachments=(string[]?)null})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            Assert.Equal(500,(await Post(route+"/update?parentId="+rootId,new{id,name="必须回滚",attachments=new[]{long.MaxValue.ToString()}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            Data(await Post(route+"/importDatas202",new{parentId=rootId,requestJson=new[]{new Dictionary<string,object?>{{"名称","逻辑父文件"},{"附件",Array.Empty<string>()}}}}));await References(1);await Sparse(id);
            before=await State();Assert.Equal(500,(await Post(route+"/importDatas202",new{parentId=rootId,requestJson=new[]{new Dictionary<string,object?>{{"名称","逻辑父文件"},{"附件",new[]{file}}},new Dictionary<string,object?>{{"名称","坏后行"},{"级联","未确认路径"}}}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());await References(1);
            await Delete(id);await Delete(copy);await References(0);if(physicalDelete)Assert.Empty(await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source,new Dictionary<string,object?>{{"id",long.Parse(id,CultureInfo.InvariantCulture)}}));
            verified=true;await Receipt("verified",new{nonce,type,parentType,explicitAncestor,physicalDelete,names,fileIds,verified,generatedHttp=true,originalFileController=true,realUploadAndDownload=true,realFileObserver=true,parentCrud=true,copyAndImport=true,rollback=true,physicalAndSoftDelete=true,sparseStorage=true,formalAuthentication=false,browser=false});
            async Task<JsonElement> Post(string uri,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(uri,body)).Content.ReadAsStringAsync());
            async Task Delete(string record){using var request=new HttpRequestMessage(HttpMethod.Delete,route+"?parentId="+rootId){Content=JsonContent.Create(new{id=record})};using var response=await client.SendAsync(request);Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));}
            async Task References(int count){var refs=await fileStore.ReadAsync("yz_file_ref",new Dictionary<string,object?>{{"file_id",long.Parse(file,CultureInfo.InvariantCulture)}});Assert.Equal(count,refs.Count);Assert.All(refs,row=>{Assert.Equal("202",row["page_id"]!.ToString());Assert.Equal("attachments",row["field_name"]);Assert.Equal(Project.ToString(),row["yz_project_id"]!.ToString());});}
            async Task Sparse(string key){var row=Assert.Single(await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source,new Dictionary<string,object?>{{"id",long.Parse(key,CultureInfo.InvariantCulture)}}));Assert.Null(row["country_id"]);Assert.Null(row["area_id"]);Assert.Equal(leafId,row["place_id"]!.ToString());}
            async Task<string> State()=>JsonSerializer.Serialize(new{source=(await new SqlRowStore(session,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)).ToArray(),refs=(await fileStore.ReadIncludingDeletedAsync("yz_file_ref",new Dictionary<string,object?>{{"file_id",long.Parse(file,CultureInfo.InvariantCulture)}})).OrderBy(row=>SqlRowStore.Id(row)).ToArray()});

        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,explicitAncestor,errorType=error.GetType().FullName,error.Message,created,verified=false});throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}foreach(var file in fileIds){await Execute(session,"DELETE FROM yz_file_ref WHERE file_id=@id",("id",file));await Execute(session,"DELETE FROM yz_file_library WHERE id=@id",("id",file));}if(created){using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);if((await inspection.PreviewAsync(catalog)).Changes.Count!=0||!names.SequenceEqual(new[]{prefix+"_source",prefix+"_country",prefix+"_area",prefix+"_place"}))throw new InvalidOperationException("自有逻辑父文件样本结构／表名不符，保留现场。");var state=new Dictionary<string,string>();foreach(var name in names)state[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)));var fingerprint=MySqlSchemaPlanner.Fingerprint(actual);await Receipt("cleanup-before",new{nonce,names,fileIds,fingerprint,rowsSha256=state.ToDictionary(pair=>pair.Key,pair=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value))))});using var readback=new DatabaseSession(options);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await new SchemaService(readback).InspectAsync(catalog)));foreach(var name in names)Assert.Equal(state[name],JsonSerializer.Serialize((await new SqlRowStore(readback,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row))));foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,type,parentType,explicitAncestor,physicalDelete,names,fileIds,verified,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string stage,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>保留原节点对象协议，名称只回显，不能作为SQL或层级推断。</summary>
    private static Dictionary<string,object?> Filter(string key,long id)=>new(){{"placeId",new Dictionary<string,object?>{{key,id.ToString(CultureInfo.InvariantCulture)}}}};
    /// <summary>原Excel解析后的中文标题JSON，传入顺序由调用方明确给出。</summary>
    private static Dictionary<string,object?> Row(params object?[] fields){var row=new Dictionary<string,object?>();for(var i=0;i<fields.Length;i+=2)row.Add((string)fields[i]!,fields[i+1]);return row;}
    /// <summary>错误正文不能当作下载凭证或业务成功。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.GetRawText());return value.GetProperty("data");}
    /// <summary>固定测试身份可撤销来源普通动作、全部来源动作或目标list；不替代正式登录。</summary>
    private sealed class Authorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>身份固定来自测试宿主，不能从请求指定项目或账号。</summary>
        public RuntimeActor RequireActor()=>new(accessor.HttpContext!.Request.Headers.ContainsKey("reader")?955:954,"非冗余跨表Excel",false,Project.ToString(),"1");
        /// <summary>只撤销既定动作，证明导入与整项省略不借普通写或目标列表权限。</summary>
        public void RequirePagePermission(string page,string operation){var headers=accessor.HttpContext!.Request.Headers;var source=page is "201" or "202" or "203" or "204" or "205";if(source&&(headers.ContainsKey("denySource")||headers.ContainsKey("denyOrdinary")&&operation is "create" or "update" or "delete" or "copy")||!source&&headers.ContainsKey("denyTargets"))throw new UnauthorizedAccessException("固定来源或目标撤权。");}
    }
    /// <summary>守卫不接收伪造物理祖先，故障注入只访问本nonce样本并加入当前事务。</summary>
    private sealed class Guard(DatabaseOptions options,string prefix):IRecordMutationGuard
    {
        internal bool ChangeSnapshot{get;set;}internal bool MoveBeforeWrite{get;set;}internal bool SnapshotChecked{get;private set;}internal bool CheckedHints{get;private set;}internal bool CheckedValidation{get;private set;}
        /// <summary>首次记录锁前建立真实RR快照，再通过独立连接换父，不能信旧快照归属。</summary>
        public async Task BeforeMutationAsync(RecordMutationPreparation context,CancellationToken token=default)
        {
            Assert.DoesNotContain(context.ScopeValues.Keys,key=>key is "country_id" or "area_id" or "place_id" or "secondary_country_id");CheckedHints=true;
            if(!ChangeSnapshot||context.Operation!=RecordMutationOperation.Update)return;ChangeSnapshot=false;await using(var command=context.Transaction.Connection!.CreateCommand()){command.Transaction=context.Transaction;command.CommandText="SELECT name FROM `"+prefix+"_country` WHERE id=@id";var key=command.CreateParameter();key.ParameterName="id";key.Value=First;command.Parameters.Add(key);await command.ExecuteScalarAsync(token);}await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync(token);await using var move=independent.CreateCommand();move.CommandText="UPDATE `"+prefix+"_place` SET `"+prefix+"_area_id`=@parent WHERE id=@id";move.Parameters.AddWithValue("parent",(First+11).ToString(CultureInfo.InvariantCulture));move.Parameters.AddWithValue("id",First+2);Assert.Equal(1,await move.ExecuteNonQueryAsync(token));SnapshotChecked=true;
        }
        /// <summary>逻辑祖先不进入物理快照；守卫尾部改父必须由写后当前范围复核整体回滚。</summary>
        public async Task BeforeWriteAsync(RecordMutationValidation context,CancellationToken token=default)
        {
            Assert.DoesNotContain(context.OriginalScopeValues.Keys,key=>key is "country_id" or "area_id" or "place_id" or "secondary_country_id");Assert.DoesNotContain(context.ScopeValues.Keys,key=>key is "country_id" or "area_id" or "place_id" or "secondary_country_id");CheckedValidation=true;if(!MoveBeforeWrite)return;MoveBeforeWrite=false;await using var command=context.Transaction.Connection!.CreateCommand();command.Transaction=context.Transaction;command.CommandText="UPDATE `"+prefix+"_place` SET `"+prefix+"_area_id`=@parent WHERE id=@id";var parent=command.CreateParameter();parent.ParameterName="parent";parent.Value=First+11;command.Parameters.Add(parent);var id=command.CreateParameter();id.ParameterName="id";id.Value=First+2;command.Parameters.Add(id);Assert.Equal(1,await command.ExecuteNonQueryAsync(token));
        }
    }
    /// <summary>观察器核对真实稀疏值，并在后段注入回滚及有界锁等待。</summary>
    private sealed class Observer:IRecordMutationObserver
    {
        internal bool CheckedSparse{get;private set;}internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>真实DML后、提交前核验稀疏字段，并注入通知故障及有界锁持有。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){foreach(var fields in new[]{new[]{"country_id","area_id","place_id"},new[]{"secondary_country_id","secondary_area_id","secondary_place_id"}})Assert.InRange(fields.Count(field=>context.SubmittedFields.TryGetValue(field,out var value)&&value is not null),0,1);CheckedSparse=true;if(context.SubmittedFields.TryGetValue("name",out var name)){if(Equals(name,"通知故障"))throw new InvalidOperationException("固定通知故障。");if(Equals(name,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),token);}}}
    }
    /// <summary>只对本nonce已声明表执行固定SQL，所有值均参数绑定。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] values){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in values){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
