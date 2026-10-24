using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>原import／initExport及真实XLSX，在固定Server-200自有四表验证双控件稀疏存储。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantCrossTableExcelMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>隐式各级ID不增加表头，四种来源／父键组合复用原空值和标量导出协议。</summary>
    [StandaloneExportMySqlFact]
    public async Task Implicit_slots_keep_empty_import_and_scalar_export_with_logical_filters()
    {var options=await Options();foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"})await Exercise(options,type,parent,false);}
    /// <summary>显式祖先按原模板保留，字符串祖先与int64末级亦不依赖JSON属性顺序。</summary>
    [StandaloneExportMySqlFact]
    public async Task Explicit_ancestor_keeps_original_header_order_independence_and_mixed_slot_types()
    {var options=await Options();await Exercise(options,"string","int64",true);await Exercise(options,"int64","string",true);}
    /// <summary>任何DDL之前独立核对实例、端口、主机及隔离库，不使用本机Docker。</summary>
    private static async Task<DatabaseOptions> Options()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");return options;
    }
    /// <summary>先编译真实产物，再显式建四张随机表；任何一行失败均比较全部物理数据。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool explicitAncestor)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrce_"+nonce[..12];var catalog=NonRedundantCrossTableExcelTests.Catalog(prefix,type,parentType,explicitAncestor);var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/nonredundant-cross-table-excel/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-excel-mysql");var directory=Path.Combine(boundary,nonce);var source=prefix+"_source";var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var observer=new Observer();WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);await Receipt("intent",new{nonce,type,parentType,explicitAncestor,names,database=options.ExpectedDatabase,verified=false});
        try
        {
            var entity=GenerationModel.Pascal(source);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossExcelProbe");var countryType=explicitAncestor?"string":type;await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.CrossExcelProbe.Generated.Services;
public partial class {{entity}}Service
{
    /// <summary>真实手写规则仍在关系归一化之前，import不能跳过数据归属校验。</summary>
    partial void BeforeSave(Example.CrossExcelProbe.Generated.Entities.{{entity}} value,RuntimeActor actor,bool creating,ISet<string> submitted)
    {
        if(value.Name is "钩子根" or "锁持有"){value.CountryId={{(countryType=="string"?"\""+First+"\"":First+"L")}};submitted.Add("country_id");}
        if(value.Name=="钩子越界"){value.PlaceId={{(type=="string"?"\""+(First+32)+"\"":(First+32)+"L")}};submitted.Add("place_id");}
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var initial=await schema.PreviewAsync(catalog);Assert.Equal(4,initial.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(initial)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==source).Columns,column=>column.Name.EndsWith("_name",StringComparison.Ordinal));
            object? Key(string field,long? value)=>value is null?null:(explicitAncestor&&field=="country_id"||type=="string")?value.Value.ToString(CultureInfo.InvariantCulture):value;
            // 只向本nonce样本写固定种子，不借UI权限或生产配置制造验收数据。
            async Task Target(int level,long id,string name,long? parent=null,long project=Project){var levels=new[]{"country","area","place"};var parentField=level==0?"":",`"+prefix+"_"+levels[level-1]+"_id`";await Execute(session,"INSERT INTO `"+prefix+"_"+levels[level]+"` (id,name,yz_project_id,status"+parentField+") VALUES (@id,@name,@project,1"+(level==0?"":",@parent")+")",("id",id),("name",name),("project",project),("parent",parentType=="string"?(object?)parent?.ToString(CultureInfo.InvariantCulture):parent));}
            foreach(var branch in new[]{0,10}){await Target(0,First+branch,branch==0?"国甲":"国乙");await Target(1,First+branch+1,branch==0?"区甲":"区乙",First+branch);await Target(2,First+branch+2,branch==0?"城甲":"城乙",First+branch+1);}await Target(2,First+32,"外项目叶",First+11,Project+1);
            async Task Seed(int index,string name,int depth,long? selected,int secondDepth,long? second,long project=Project,int status=1,DateTime? deleted=null){var fields=new[]{"country_id","area_id","place_id","secondary_country_id","secondary_area_id","secondary_place_id"};var values=fields.Select((field,position)=>(field,Key(field,position<3?position==depth?selected:null:position-3==secondDepth?second:null))).ToArray();await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id,status,amount,delete_no,"+string.Join(",",fields.Select(field=>"`"+field+"`"))+") VALUES (@id,@name,@project,@status,1,@deleted,"+string.Join(",",fields.Select(field=>"@"+field))+")",new[]{("id",(object?)(First+100+index)),("name",name),("project",project),("status",status),("deleted",deleted)}.Concat(values).ToArray());}
            await Seed(1,"甲",2,First+2,0,First+10);await Seed(2,"乙",1,First+1,2,First+12);await Seed(3,"根",0,First,1,First+11);await Seed(4,"乙组",2,First+12,-1,null);await Seed(5,"外项目",2,First+2,-1,null,project:Project+1);await Seed(6,"条件隐藏",2,First+2,-1,null,status:0);await Seed(7,"逻辑删除",2,First+2,-1,null,deleted:new DateTime(2026,1,1));
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var exportRoot=Path.Combine(directory,"exports");var exports=new ExcelPageExportService(new(){RootDirectory=exportRoot,MaxRows=3});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(954));builder.Services.AddScoped<IRuntimeAuthorization,Authorization>();assembly.GetType("Example.CrossExcelProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(settings=>settings.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or PageImportException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/201/";
            // 真实XLSX只包含原标量列表，源export已授权，目标list和源普通CRUD从未授权。
            Assert.Equal(new[]{"乙","根","甲"}.Order(),(await Export(Filter("countryId",First))).Order());Assert.Equal(new[]{"乙","甲"}.Order(),(await Export(Filter("areaId",First+1))).Order());Assert.Equal(new[]{"甲"},await Export(Filter("placeId",First+2)));Assert.Equal(new[]{"乙"},await Export(new Dictionary<string,object?>{{"placeId",new{countryId=First.ToString()}},{"secondaryPlaceId",new{secondaryPlaceId=(First+12).ToString()}}}));Assert.Equal(new[]{"甲"},await Export(new Dictionary<string,object?>(),[(First+101).ToString()]));
            var files=FileCount();var tooMany=await Post("initExport",new{});Assert.Equal(500,tooMany.GetProperty("code").GetInt32());Assert.Equal(files,FileCount());client.DefaultRequestHeaders.Add("denyExport","1");Assert.Equal(500,(await Post("initExport",new{filters=Filter("placeId",First+2)})).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("denyExport");Assert.Equal(files,FileCount());
            await Execute(session,"UPDATE `"+prefix+"_country` SET status=0 WHERE id=@id",("id",First));Assert.Equal(new[]{"根"},await Export(Filter("countryId",First)));await Execute(session,"UPDATE `"+prefix+"_country` SET status=1 WHERE id=@id",("id",First));
            await Execute(session,"UPDATE `"+source+"` SET country_id=@root WHERE id=@id",("root",Key("country_id",First)),("id",First+101));files=FileCount();Assert.Equal(500,(await Post("initExport",new{selectedIds=new[]{(First+101).ToString()}})).GetProperty("code").GetInt32());Assert.Equal(files,FileCount());await Execute(session,"UPDATE `"+source+"` SET country_id=NULL WHERE id=@id",("id",First+101));
            Data(await Import(Row("名称","甲","级联","")));await Slots("甲",-1,null,0,First+10);Data(await Import(Row("名称","乙","金额",42)));await Slots("乙",1,First+1,2,First+12);Data(await Import(Row("名称","甲","另一层级","  ")));await Slots("甲",-1,null,-1,null);Data(await Import(Row("名称","新建空","级联",null,"另一层级",null)));await Slots("新建空",-1,null,-1,null);
            var before=await State();Assert.Equal(500,(await Import(Row("名称","乙","级联",null),Row("名称","非空未确认","级联","国甲/区甲/城甲"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Import(Row("名称","乙","另一层级","国乙/区乙"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());foreach(var hidden in new[]{"countryId","country_name","yz_project_id"}){Assert.Equal(500,(await Import(Row("名称","乙",hidden,First.ToString()))).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            Data(await Import(Row("名称","钩子根","级联",null)));await Slots("钩子根",0,First,-1,null);before=await State();Assert.Equal(500,(await Import(Row("名称","钩子越界","级联",null))).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Import(Row("名称","前行","级联",null),Row("名称","通知故障","级联",null))).GetProperty("code").GetInt32());Assert.Equal(before,await State());client.DefaultRequestHeaders.Add("denyImport","1");Assert.Equal(500,(await Import(Row("名称","拒绝","级联",null))).GetProperty("code").GetInt32());client.DefaultRequestHeaders.Remove("denyImport");Assert.Equal(before,await State());
            if(explicitAncestor){Data(await Import(Row("名称","显式一","级联",null,"国家ID",First.ToString()),Row("国家ID",First.ToString(),"级联",null,"名称","显式二")));await Slots("显式一",0,First,-1,null);await Slots("显式二",0,First,-1,null);}
            var pending=Import(Row("名称","锁持有","级联",null));try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var transaction=await independent.BeginTransactionAsync();await using var change=independent.CreateCommand();change.Transaction=transaction;change.CommandText="UPDATE `"+prefix+"_country` SET name='竞争国' WHERE id=@id";change.Parameters.AddWithValue("id",First);Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>change.ExecuteNonQueryAsync())).Number);await transaction.RollbackAsync();}finally{observer.Release.TrySetResult();await pending;}Data(await pending);await Slots("锁持有",0,First,-1,null);Assert.True(observer.CheckedSparse);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);verified=true;await Receipt("verified",new{nonce,type,parentType,explicitAncestor,names,verified,realGeneratedHttp=true,realMySql=true,realXlsx=true,originalColumns=true,twoControls=true,emptyAndOmission=true,logicalExportFilters=true,hiddenSlotsNotExported=true,batchRollback=true,currentTargetLocks=true,nonemptyPolicyStillGated=true,formalAuthentication=false,browser=false});
            // 下载并读取实际XLSX，记录ID必须保留文本类型，隐式依赖不增加任何表头。
            async Task<string[]> Export(Dictionary<string,object?> filters,string[]? selected=null){var ticket=Data(await Post("initExport",new{filters,selectedIds=selected??[]})).GetString()!;using var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket));Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.Equal(new[]{"id","名称","金额"}.Order(),headers.Keys.Order());foreach(var row in sheet.RowsUsed().Skip(1)){Assert.Equal(XLDataType.Text,row.Cell(headers["id"]).DataType);Assert.True(long.Parse(row.Cell(headers["id"]).GetString(),CultureInfo.InvariantCulture)>9007199254740991);}return sheet.RowsUsed().Skip(1).Select(row=>row.Cell(headers["名称"]).GetString()).ToArray();}
            Task<JsonElement> Import(params Dictionary<string,object?>[] rows)=>Post("importDatas201",new{requestJson=rows});async Task<JsonElement> Post(string route,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+route,body)).Content.ReadAsStringAsync());async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}int FileCount()=>Directory.Exists(exportRoot)?Directory.GetFiles(exportRoot,"*.xlsx").Length:0;
            // 两棵树分别检查真实物理位，不能用逻辑VO冒充存储验收。
            async Task Slots(string name,int depth,long? id,int secondDepth,long? second){using var independent=new DatabaseSession(options);var row=Assert.Single(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source),row=>Equals(row["name"],name)&&Convert.ToInt64(row["yz_project_id"],CultureInfo.InvariantCulture)==Project);foreach(var (fields,selected,value) in new[]{(new[]{"country_id","area_id","place_id"},depth,id),(new[]{"secondary_country_id","secondary_area_id","secondary_place_id"},secondDepth,second)})for(var level=0;level<fields.Length;level++){if(level==selected)Assert.Equal(value!.Value.ToString(CultureInfo.InvariantCulture),Convert.ToString(row[fields[level]],CultureInfo.InvariantCulture));else Assert.Null(row[fields[level]]);}Assert.DoesNotContain(row.Keys,key=>key.EndsWith("_name",StringComparison.Ordinal));}
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,explicitAncestor,errorType=error.GetType().FullName,error.Message,created,verified=false});throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);if((await inspection.PreviewAsync(catalog)).Changes.Count!=0||!names.SequenceEqual(new[]{prefix+"_source",prefix+"_country",prefix+"_area",prefix+"_place"}))throw new InvalidOperationException("自有Excel样本结构／表名不符，保留现场。");var state=new Dictionary<string,string>();foreach(var name in names)state[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)));var fingerprint=MySqlSchemaPlanner.Fingerprint(actual);await Receipt("cleanup-before",new{nonce,names,fingerprint,rowsSha256=state.ToDictionary(pair=>pair.Key,pair=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value))))});using var readback=new DatabaseSession(options);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await new SchemaService(readback).InspectAsync(catalog)));foreach(var name in names)Assert.Equal(state[name],JsonSerializer.Serialize((await new SqlRowStore(readback,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row))));foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,type,parentType,explicitAncestor,names,verified,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string stage,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>保留原节点对象协议，名称只回显，不能作为SQL或层级推断。</summary>
    private static Dictionary<string,object?> Filter(string key,long id)=>new(){{"placeId",new Dictionary<string,object?>{{key,id.ToString(CultureInfo.InvariantCulture)}}}};
    /// <summary>原Excel解析后的中文标题JSON，传入顺序由调用方明确给出。</summary>
    private static Dictionary<string,object?> Row(params object?[] fields){var row=new Dictionary<string,object?>();for(var i=0;i<fields.Length;i+=2)row.Add((string)fields[i]!,fields[i+1]);return row;}
    /// <summary>错误正文不能当作下载凭证或业务成功。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.GetRawText());return value.GetProperty("data");}
    /// <summary>测试仅授予来源import／export，其余动作及所有目标list固定撤销。</summary>
    private sealed class Authorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>身份固定来自测试宿主，不能从请求指定项目或账号。</summary>
        public RuntimeActor RequireActor()=>new(954,"非冗余跨表Excel",false,Project.ToString(),"1");
        /// <summary>只撤销既定动作，证明导入／导出不能借普通写或目标列表权限。</summary>
        public void RequirePagePermission(string page,string operation){var headers=accessor.HttpContext!.Request.Headers;if(page!="201"||operation is not("import" or "export")||operation=="import"&&headers.ContainsKey("denyImport")||operation=="export"&&headers.ContainsKey("denyExport"))throw new UnauthorizedAccessException("来源动作或目标列表未授权。");}
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
