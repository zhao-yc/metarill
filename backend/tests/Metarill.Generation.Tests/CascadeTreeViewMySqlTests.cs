using System.Collections.Concurrent;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Core.Persistence;
using Metarill.Runtime.Exports;
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
public sealed class CascadeTreeViewMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;

    [StandaloneExportMySqlFact]
    public async Task Original_filter_path_projection_count_statistics_selection_and_xlsx_share_scoped_graph()
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
        foreach(var parentType in new[]{"int64","string"})await Exercise(options,parentType);
    }

    /// <summary>两个物理父键模式各自使用新的随机两表，精确清理并另起连接核验。</summary>
    private static async Task Exercise(DatabaseOptions options,string parentType)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_cvw_"+nonce[..12];var catalog=CascadeTreeViewCompilationTests.Catalog(prefix,parentType,parentType);
        foreach(var column in catalog.ConfigColumns)column.Name=column.ColumnName;
        catalog.ConfigPageColumns.Add(new(){Id="2000",ConfigPageId="201",ConfigColumnId="21",IsEdit=0});
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");var exactPage=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(sourcePage))!;exactPage.Id="202";catalog.ConfigPages.Add(exactPage);
        foreach(var original in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
        {
            var field=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(original))!;field.Id="202"+original.Id;field.ConfigPageId="202";
            if(field.ConfigColumnId=="25")field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail.Replace("selfAndDescendants","exact"));catalog.ConfigPageColumns.Add(field);
        }
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少仓库路径。");var evidence=Path.Combine(root,".local/cascade-view/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-view-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;
        var snapshot=new SnapshotGuard(options,prefix);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,parentType,names,database=options.ExpectedDatabase,verified=false}));
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
            // 宽树和源行都参数化分批准备；超过5000个后代验证真实SQL不会截断。
            async Task Batch(string table,string[] columns,List<object?[]> values)
            {
                foreach(var rows in values.Chunk(250))
                {
                    var parameters=new List<(string,object?)>();var tuples=new List<string>();
                    for(var i=0;i<rows.Length;i++){var names=new List<string>();for(var j=0;j<columns.Length;j++){var key="v"+i+"_"+j;names.Add("@"+key);parameters.Add((key,rows[i][j]));}tuples.Add("("+string.Join(",",names)+")");}
                    await Execute(session,"INSERT INTO `"+table+"` ("+string.Join(",",columns.Select(column=>"`"+column+"`"))+") VALUES "+string.Join(",",tuples),parameters.ToArray());
                }
            }
            object? Key(long value)=>parentType=="string"?value.ToString(CultureInfo.InvariantCulture):value;
            var targetRows=new List<object?[]>();var sourceRows=new List<object?[]>{new object?[]{First+10000,"源根",Project,Key(First),"历史根",10L,null},new object?[]{First+10001,"源子",Project,Key(First+1),"历史子",20L,null},new object?[]{First+10002,"源孙",Project,Key(First+2),"历史孙",30L,null}};
            for(var i=0;i<5500;i++){targetRows.Add([First+100+i,"叶"+i,Project,Key(First),1]);sourceRows.Add([First+20000+i,"源"+i,Project,Key(First+100+i),"旧叶名",1L,null]);}
            targetRows.Add([First+6000,"加深",Project,Key(First+5599),1]);sourceRows.Add([First+30000,"源加深",Project,Key(First+6000),"旧加深",1L,null]);
            foreach(var invalid in new[]{First+10,First+11,First+20,First+21,First+30,First+31,First+40,First+999999})sourceRows.Add([First+40000+sourceRows.Count,"坏来源",Project,Key(invalid),"不可信缓存",999L,null]);
            sourceRows.Add([First+50000,"外项目源",Project+1,Key(First),"外项目",999L,null]);sourceRows.Add([First+50001,"删除源",Project,Key(First),"已删除",999L,new DateTime(2026,1,1)]);
            await Batch(tree,["id","name","yz_project_id",parentColumn,"status"],targetRows);await Batch(prefix+"_source",["id","name","yz_project_id","tree_id","tree_name","amount","delete_no"],sourceRows);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeViewProbe");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=10000});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.CascadeViewProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();
            app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet("/test-export/{ticket}",async(string ticket)=>{var content=await exports.OpenDownloadAsync(ticket);return Results.Stream(content.Stream,content.ContentType,content.FileName);});await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};var endpoint="/api/admin/auto/"+prefix+"_source/201/";
            var moveDuringPage=false;
            session.Orm.Aop.CommandBefore+=(_,args)=>
            {
                if(!moveDuringPage||!args.Command.CommandText.Contains(prefix+"_source",StringComparison.Ordinal)||!args.Command.CommandText.Contains("count",StringComparison.OrdinalIgnoreCase))return;
                moveDuringPage=false;Assert.NotNull(args.Command.Transaction);Assert.Equal(System.Data.IsolationLevel.RepeatableRead,args.Command.Transaction!.IsolationLevel);
                Execute(session,"UPDATE `"+tree+"` SET `"+parentColumn+"`=NULL WHERE id=@id",("id",First+5599)).GetAwaiter().GetResult();
            };
            var commands=new ConcurrentQueue<(string Sql,int Parameters)>();session.Orm.Aop.CommandBefore+=(_,args)=>{if(args.Command.CommandText.Contains(prefix+"_tree",StringComparison.Ordinal))commands.Enqueue((args.Command.CommandText,args.Command.Parameters.Count));};
            async Task<JsonElement> Post(string route,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(route,body)).Content.ReadAsStringAsync());
            object Filter(long selected)=>new{treeId=new{treeId=selected.ToString(),treeName="仅回显"}};
            commands.Clear();var plain=await Post(endpoint+"page",new{pageSize=1000});Assert.Equal(5512,long.Parse(Data(plain).GetProperty("total").ToString()));Assert.InRange(commands.Count,1,9);
            Assert.All(commands,command=>{Assert.True(command.Parameters>0);Assert.DoesNotContain("secret",command.Sql,StringComparison.OrdinalIgnoreCase);});
            var firstRows=Data(plain).GetProperty("records").EnumerateArray().ToArray();Assert.All(firstRows.Where(row=>row.GetProperty("name").GetString()=="坏来源"),row=>Assert.Equal("",row.GetProperty("treeIdDisplayText").GetString()));
            var page=await Post(endpoint+"page",new{pageSize=1000,filters=Filter(First)});Assert.Equal(5504,long.Parse(Data(page).GetProperty("total").ToString()));Assert.Equal(5561,decimal.Parse(page.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").ToString(),CultureInfo.InvariantCulture));
            Assert.All(Data(page).GetProperty("records").EnumerateArray(),row=>Assert.StartsWith("根",row.GetProperty("treeIdDisplayText").GetString()));
            Assert.Equal(5504,long.Parse(Data(await Post(endpoint+"count",new{filters=Filter(First)})).ToString()));
            var exact=endpoint.Replace("/201/","/202/");Assert.Equal(1,long.Parse(Data(await Post(exact+"count",new{filters=Filter(First)})).ToString()));
            Assert.Equal(5512,long.Parse(Data(await Post(endpoint+"count",new{filters=new{treeId=new{}}})).ToString()));
            var requested=new[]{(First+30000).ToString(),(First+10002).ToString()};var selected=Data(await Post(endpoint+"selection/selected",new{selectedIds=requested})).EnumerateArray().ToArray();Assert.Equal(requested,selected.Select(row=>row.GetProperty("id").GetString()));Assert.Equal("根/叶5499/加深",selected[0].GetProperty("treeIdDisplayText").GetString());Assert.Equal("根/子/孙",selected[1].GetProperty("treeIdDisplayText").GetString());
            client.DefaultRequestHeaders.Add("denyTargetList","1");Assert.Equal(5504,long.Parse(Data(await Post(endpoint+"count",new{filters=Filter(First)})).ToString()));Assert.Equal(200,(await Post(endpoint+"page",new{pageSize=1})).GetProperty("code").GetInt32());
            Assert.Equal(500,(await Post(endpoint+"cascadeTreeOptions25",new{levelKey="selfTree-0",selectedPath=new{parentId=""}})).GetProperty("code").GetInt32());
            var ticket=Data(await Post(endpoint+"initExport",new{filters=Filter(First)})).GetString()!;
            using(var workbook=new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync("/test-export/"+ticket)))){var sheet=workbook.Worksheet(1);Assert.Equal(5505,sheet.LastRowUsed()!.RowNumber());Assert.DoesNotContain(sheet.RowsUsed().Skip(1),row=>row.CellsUsed().Any(cell=>cell.GetString()=="坏来源"));}
            client.DefaultRequestHeaders.Remove("denyTargetList");
            // 图遍历后、来源COUNT前迁父：整份分页/统计/路径应保持旧快照，下一请求才看到新树。
            moveDuringPage=true;var oldPage=await Post(endpoint+"page",new{pageSize=1000,filters=Filter(First)});Assert.False(moveDuringPage);Assert.Equal(5504,long.Parse(Data(oldPage).GetProperty("total").ToString()));Assert.Equal(5561,decimal.Parse(oldPage.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.All(Data(oldPage).GetProperty("records").EnumerateArray(),row=>Assert.StartsWith("根",row.GetProperty("treeIdDisplayText").GetString()));
            Assert.Equal(5502,long.Parse(Data(await Post(endpoint+"count",new{filters=Filter(First)})).ToString()));await Execute(session,"UPDATE `"+tree+"` SET `"+parentColumn+"`=@parent WHERE id=@id",("parent",Key(First)),("id",First+5599));
            await using(var weak=new MySqlConnection(options.ConnectionString)){await weak.OpenAsync();await using var transaction=await weak.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);await Assert.ThrowsAsync<InvalidOperationException>(()=>session.Provider.EnsureReadSnapshotAsync(transaction));await transaction.RollbackAsync();}

            foreach(var invalid in new[]{First+10,First+20,First+30,First+40,First+999999})Assert.Equal(500,(await Post(endpoint+"count",new{filters=Filter(invalid)})).GetProperty("code").GetInt32());
            foreach(var invalid in new object[]{new{treeId="1 OR 1=1"},new{treeId=First.ToString(),projectId="2"}})Assert.Equal(500,(await Post(endpoint+"count",new{filters=new{treeId=invalid}})).GetProperty("code").GetInt32());
            client.DefaultRequestHeaders.Add("denyPage","201");commands.Clear();Assert.Equal(500,(await Post(endpoint+"page",new{pageSize=1})).GetProperty("code").GetInt32());Assert.Empty(commands);client.DefaultRequestHeaders.Remove("denyPage");
            // 写响应的路径必须使用当前读，不能回到准备阶段RR旧快照。
            snapshot.Change=true;var updated=Data(await Post(endpoint+"update",new{id=(First+10000).ToString(),treeId=First.ToString(),treeIdDisplayText="伪造路径"}));Assert.Equal("RR新根",updated.GetProperty("treeIdDisplayText").GetString());Assert.Equal("RR新根",updated.GetProperty("treeName").GetString());
            verified=true;
            await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,parentType,verified,originalProtocol=true,realMySql=true,realGeneratedHttp=true,fixedTestActor=true,browser=false,fullControl=false,pathDisplayAndFilter=true,realXlsx=true}));
        }
        finally
        {
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

    /// <summary>准备阶段建立真实RR旧快照，独立改名后写响应也必须走当前读。</summary>
    private sealed class SnapshotGuard(DatabaseOptions options,string prefix):IRecordMutationGuard
    {
        public bool Change{get;set;}
        public async Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)
        {
            if(!Change||preparation.Operation!=RecordMutationOperation.Update)return;Change=false;
            await using var read=preparation.Transaction.Connection!.CreateCommand();read.Transaction=preparation.Transaction;read.CommandText="SELECT @@transaction_isolation";Assert.Equal("REPEATABLE-READ",Convert.ToString(await read.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture));
            read.CommandText="SELECT name FROM `"+prefix+"_tree` WHERE id=@id";var parameter=read.CreateParameter();parameter.ParameterName="id";parameter.Value=First;read.Parameters.Add(parameter);await read.ExecuteScalarAsync(cancellationToken);
            using var independent=new DatabaseSession(options);await Execute(independent,"UPDATE `"+prefix+"_tree` SET name='RR新根' WHERE id=@id",("id",First));
        }
    }
    /// <summary>固定测试身份仅允许撤销权限，来源读取与候选浏览分别验证。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(944,"级联读取验收",false,Project.ToString(),"1");
        public void RequirePagePermission(string pageId,string operation){if(accessor.HttpContext!.Request.Headers["denyPage"]==pageId || pageId=="301"&&operation=="list"&&accessor.HttpContext.Request.Headers.ContainsKey("denyTargetList"))throw new UnauthorizedAccessException("没有页面读取权限。");}
    }
    private static JsonElement Data(JsonElement response){Assert.Equal(200,response.GetProperty("code").GetInt32());return response.GetProperty("data");}
    /// <summary>自有随机表的固定SQL通过参数绑定准备数据，不接受HTTP输入。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;
        foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();
    }
}
