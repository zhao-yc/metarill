using System.Collections.Concurrent;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Core.Persistence;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成控制器与Server-200独占随机表；固定测试身份不代表正式登录或浏览器验收。</summary>
public sealed class CascadeTreeExcelMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;

    /// <summary>固定两种键类型执行真实导入导出、权限、整批回滚及提交前名称锁验收。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_full_path_excel_roundtrip_import_only_permissions_rollback_and_name_locks()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var connectionOptions=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||connectionOptions.Database!=options.ExpectedDatabase||connectionOptions.Server!="deve-server.yinzon.com"||connectionOptions.Port!=3306)throw new InvalidOperationException("级联验收仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));
        await using(var connection=new MySqlConnection(options.ConnectionString))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未创建样本。");
        }
        foreach(var parentType in new[]{"int64","string"})foreach(var automaticName in new[]{false,true})await Exercise(options,parentType,automaticName);
    }

    /// <summary>两个物理父键模式各自使用新的随机两表，精确清理并另起连接核验。</summary>
    private static async Task Exercise(DatabaseOptions options,string parentType,bool automaticName)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_cex_"+nonce[..12];var catalog=CascadeTreeExcelTests.Catalog(prefix,parentType,parentType);catalog.ConfigColumns.Single(column=>column.Id=="25").DefaultValue=First.ToString(CultureInfo.InvariantCulture);
        if(automaticName){catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="26");catalog.ConfigColumns.RemoveAll(column=>column.Id=="26");}
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少仓库路径。");var evidence=Path.Combine(root,".local/cascade-excel/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-excel-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;
        var observer=new ImportObserver();
        using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,parentType,automaticName,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(2,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            var tree=prefix+"_tree";var parentColumn="parent_"+prefix+"_tree_id";
            async Task Seed(long id,string name,long? parent,long project=Project,int status=1,DateTime? deleted=null)
            {
                await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(tree)+" (id,name,yz_project_id,"+session.Provider.QuoteIdentifier(parentColumn)+",status,delete_no,secret) VALUES (@id,@name,@project,@parent,@status,@deleted,@secret)",("id",id),("name",name),("project",project),("parent",parentType=="string"?parent?.ToString(CultureInfo.InvariantCulture):parent),("status",status),("deleted",deleted),("secret","不应进入响应"));
            }
            await Seed(First,"根",null);await Seed(First+1,"子",First);await Seed(First+2,"孙",First+1);await Seed(First+3,"同层",First);
            await Seed(First+10,"外项目",null,Project+1);await Seed(First+11,"外父子",First+10);await Seed(First+20,"已删除",null,deleted:new DateTime(2026,1,1));await Seed(First+21,"删父子",First+20);
            await Seed(First+30,"条件隐藏",null,status:0);await Seed(First+31,"条件隐藏子",First+30);await Seed(First+40,"循环甲",First+41);await Seed(First+41,"循环乙",First+40);
            await Seed(First+60,"另一根",null);await Seed(First+61,"子",First+60);await Seed(First+63,"重复根",null);await Seed(First+64,"重复根",null);await Seed(First+65,"重子",First);await Seed(First+66,"重子",First);await Seed(First+70,"分/隔",First);await Seed(First+71,"1234",null);
            object Key(long id)=>parentType=="string"?id.ToString(CultureInfo.InvariantCulture):id;
            await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(prefix+"_source")+" (id,name,yz_project_id,tree_id,tree_name,amount,delete_no) VALUES (@a,'现有',@project,@tree,'旧孙',10,NULL),(@b,'现有',@foreign,@tree,'外项目',99,NULL),(@c,'现有',@project,@tree,'删除源',99,@deleted)",("a",First+10000),("b",First+10001),("c",First+10002),("project",Project),("foreign",Project+1),("tree",Key(First+2)),("deleted",new DateTime(2026,1,1)));
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeExcelProbe");
            var sourceType=string.Concat((prefix+"_source").Split('_').Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
            await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.CascadeExcelProbe.Generated.Services;
public partial class {{sourceType}}Service
{
    /// <summary>手写钩子不能借导入动作绕过最终项目与路径校验。</summary>
    partial void BeforeSave(Example.CascadeExcelProbe.Generated.Entities.{{sourceType}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {if(entity.Name=="手写改坏"){entity.TreeId={{(parentType=="string"?"\""+(First+10).ToString()+"\"":(First+10).ToString()+"L")}};submittedFields.Add("tree_id");}
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=1000});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.CascadeExcelProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();
            app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or PageImportException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet("/test-export/{ticket}",async(string ticket)=>{var content=await exports.OpenDownloadAsync(ticket);return Results.Stream(content.Stream,content.ContentType,content.FileName);});await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+prefix+"_source/201/";
            var commands=new ConcurrentQueue<(string Sql,int Parameters)>();session.Orm.Aop.CommandBefore+=(_,args)=>{if(args.Command.CommandText.Contains(prefix+"_tree",StringComparison.Ordinal))commands.Enqueue((args.Command.CommandText,args.Command.Parameters.Count));};
            async Task<JsonElement> Post(string route,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(route,body)).Content.ReadAsStringAsync());
            Dictionary<string,object?> Row(string name,object? path,long amount=1)=>new(){["记录名"]=name,["树路径"]=path,["金额"]=amount};
            async Task<JsonElement> Import(string page,params object[] rows)=>await Post(endpoint.Replace("/201/","/"+page+"/")+"importDatas"+page,new{requestJson=rows});
            async Task<string> State(){using var independent=new DatabaseSession(options);var rows=await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(prefix+"_source");return JsonSerializer.Serialize(rows.OrderBy(row=>SqlRowStore.Id(row)));}
            client.DefaultRequestHeaders.Add("importOnly","1");Assert.Equal(200,(await Import("201",Row("追加甲","根/子/孙"),Row("追加乙","另一根/子"),Row("数字名称",1234))).GetProperty("code").GetInt32());
            var page=Data(await Post(endpoint+"page",new{pageSize=1000})).GetProperty("records").EnumerateArray().ToArray();Assert.Equal((First+2).ToString(),page.Single(row=>row.GetProperty("name").GetString()=="追加甲").GetProperty("treeId").ToString());Assert.Equal((First+61).ToString(),page.Single(row=>row.GetProperty("name").GetString()=="追加乙").GetProperty("treeId").ToString());Assert.Equal((First+71).ToString(),page.Single(row=>row.GetProperty("name").GetString()=="数字名称").GetProperty("treeId").ToString());
            // 实际下载完整路径XLSX，只按原导入模板列回传，保留严格只读列边界。
            var ticket=Data(await Post(endpoint+"initExport",new{})).GetString()!;var roundtrip=new List<object>();
            using(var workbook=new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync("/test-export/"+ticket))))
            {
                var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.Equal("根/子/孙",sheet.RowsUsed().Skip(1).Single(row=>row.Cell(headers["记录名"]).GetString()=="现有").Cell(headers["树路径"]).GetString());
                foreach(var row in sheet.RowsUsed().Skip(1))roundtrip.Add(Row("回传_"+row.Cell(headers["记录名"]).GetString(),row.Cell(headers["树路径"]).GetString()));
            }
            Assert.Equal(200,(await Import("201",roundtrip.ToArray())).GetProperty("code").GetInt32());
            Assert.Equal(200,(await Import("201",new Dictionary<string,object?>{{"记录名","使用默认"}})).GetProperty("code").GetInt32());
            Assert.Equal(200,(await Import("201",Row("明确清空",""))).GetProperty("code").GetInt32());
            var defaults=Data(await Post(endpoint+"page",new{pageSize=1000})).GetProperty("records").EnumerateArray().ToArray();Assert.Equal(First.ToString(),defaults.Single(row=>row.GetProperty("name").GetString()=="使用默认").GetProperty("treeId").ToString());Assert.Equal(JsonValueKind.Null,defaults.Single(row=>row.GetProperty("name").GetString()=="明确清空").GetProperty("treeId").ValueKind);
            using(var invalidOperation=await client.PostAsJsonAsync(endpoint+"importDatas201",new{requestJson=Array.Empty<object>(),authorizationOperation="Create"}))Assert.Equal(System.Net.HttpStatusCode.BadRequest,invalidOperation.StatusCode);
            var before=await State();foreach(var path in new[]{"孙","不存在/子","重复根/子","根/重子","外项目/外父子","已删除","条件隐藏/条件隐藏子","根/' OR 1=1 --"})
            {
                var rejected=await Import("201",Row("前行须回滚","根/子/孙"),Row("失败行",path));Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Contains("第2行",rejected.GetProperty("msg").GetString());Assert.Contains("树路径",rejected.GetProperty("msg").GetString());Assert.Equal(before,await State());
            }
            Assert.Equal(500,(await Import("201",Row("先成功","根/子/孙"),Row("手写改坏","根/子/孙"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var failed=await Import("201",Row("先成功","根/子/孙"),Row("后段故障","另一根/子"));Assert.Equal(500,failed.GetProperty("code").GetInt32());Assert.DoesNotContain("敏感",failed.GetProperty("msg").GetString());Assert.Equal(before,await State());
            before=await State();Assert.Equal(500,(await Import("202",Row("现有","另一根/子",66),Row("覆盖失败","不存在/子"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            Assert.Equal(200,(await Import("202",Row("现有","另一根/子",2))).GetProperty("code").GetInt32());Assert.Equal(200,(await Import("202",new Dictionary<string,object?>{{"记录名","现有"},{"金额",3}})).GetProperty("code").GetInt32());
            var current=Data(await Post(endpoint+"page",new{pageSize=1000})).GetProperty("records").EnumerateArray().Single(row=>row.GetProperty("name").GetString()=="现有");Assert.Equal((First+61).ToString(),current.GetProperty("treeId").ToString());
            Assert.Equal(200,(await Import("202",Row("现有",null,4))).GetProperty("code").GetInt32());current=Data(await Post(endpoint+"page",new{pageSize=1000})).GetProperty("records").EnumerateArray().Single(row=>row.GetProperty("name").GetString()=="现有");Assert.Equal(JsonValueKind.Null,current.GetProperty("treeId").ValueKind);Assert.Equal(JsonValueKind.Null,current.GetProperty("treeName").ValueKind);
            await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(prefix+"_source")+" (id,name,yz_project_id,tree_id) VALUES (@id,'分隔名',@project,@tree)",("id",First+11000),("project",Project),("tree",Key(First+70)));Assert.Equal(500,(await Post(endpoint+"initExport",new{})).GetProperty("code").GetInt32());await Execute(session,"DELETE FROM "+session.Provider.QuoteIdentifier(prefix+"_source")+" WHERE id=@id",("id",First+11000));
            client.DefaultRequestHeaders.Add("denyImport","1");before=await State();Assert.Equal(500,(await Import("201",Row("不应写入","根/子/孙"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());client.DefaultRequestHeaders.Remove("denyImport");
            var pending=Import("201",Row("锁持有","根/子/孙"));try
            {
                await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}
                await using var transaction=await independent.BeginTransactionAsync();await using var rename=independent.CreateCommand();rename.Transaction=transaction;rename.CommandText="UPDATE "+session.Provider.QuoteIdentifier(tree)+" SET name='竞争改名' WHERE id=@id";rename.Parameters.AddWithValue("id",First+2);var error=await Assert.ThrowsAsync<MySqlException>(()=>rename.ExecuteNonQueryAsync());Assert.Equal(1205,error.Number);await transaction.RollbackAsync();
            }
            finally{observer.Release.TrySetResult();await pending;}Assert.Equal(200,(await pending).GetProperty("code").GetInt32());await Execute(session,"UPDATE "+session.Provider.QuoteIdentifier(tree)+" SET name='提交后新名' WHERE id=@id",("id",First+2));
            verified=true;
            await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,parentType,automaticName,verified,originalProtocol=true,realMySql=true,realGeneratedHttp=true,fixedTestActor=true,browser=false,fullControl=false,fullPathExcel=true,importOnlyPermissions=true,roundtripAndRollback=true}));
        }
        finally
        {
            observer.Release.TrySetResult();
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var cleanupSchema=new SchemaService(cleanup);if((await cleanupSchema.PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有级联样本结构漂移，保留现场。");
                foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));
                await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}
                await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0}));
            }
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>在源写入后注入故障或有界暂停，验证整批事务与目标名称锁。</summary>
    private sealed class ImportObserver:IRecordMutationObserver
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>只在指定样本名触发故障或有界等待，保持真实事务持锁直到放行。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
        {
            if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"后段故障"))throw new InvalidOperationException("敏感内部故障");
            if(Equals(name,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),cancellationToken);}
        }
    }
    /// <summary>固定测试身份，独立撤销创建/编辑、目标list或来源导入。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>测试身份固定在样本项目，不从请求读取项目或后台归属。</summary>
        public RuntimeActor RequireActor()=>new(944,"级联读取验收",false,Project.ToString(),"1");
        /// <summary>按测试请求独立拒绝来源或目标动作，验证内部导入授权边界。</summary>
        public void RequirePagePermission(string pageId,string operation){if(accessor.HttpContext!.Request.Headers["denyPage"]==pageId||operation=="import"&&accessor.HttpContext.Request.Headers.ContainsKey("denyImport")||accessor.HttpContext.Request.Headers.ContainsKey("importOnly")&&(operation is "create" or "update"||pageId=="301"&&operation=="list"))throw new UnauthorizedAccessException("没有页面读取权限。");}
    }
    /// <summary>先确认原R协议成功，再读取响应数据，防止将错误正文作为业务结果。</summary>
    private static JsonElement Data(JsonElement response){Assert.Equal(200,response.GetProperty("code").GetInt32());return response.GetProperty("data");}
    /// <summary>自有随机表的固定SQL通过参数绑定准备数据，不接受HTTP输入。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;
        foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();
    }
}
