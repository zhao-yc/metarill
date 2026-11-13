using ClosedXML.Excel;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>用原生成的 HTTP 路由验证真实行归属和已发布候选条件。</summary>
public sealed partial class GlobalScopedCandidateTests
{
    private const long Own=9007199254740993;
    private const long Published=Own+1;
    private const long Hidden=Own+2;
    private const long Deleted=Own+3;

    [StandaloneExportMySqlFact]
    public async Task Scoped_global_candidates_and_owned_crud_use_real_mysql()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase")
            .Deserialize<DatabaseOptions>(new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test")throw new InvalidOperationException("仅使用隔离 MySQL 测试库。");

        // 两个模型分别验证原单选／List 和原多选／复选；均用派生名称和相邻高位ID。
        await Verify(false,0,"int64");
        await Verify(true,0,"string");
        await Verify(false,1,"string");
        await Verify(true,1,"int64");

        async Task Verify(bool multiple,int storage,string projectType)
        {
            var catalog=Catalog(multiple,storage,projectType);
            var prefix="mt_gscp_"+Guid.NewGuid().ToString("N")[..12]+"_";
            var renames=catalog.ConfigTables.ToDictionary(table=>table.TableName,table=>prefix+table.TableName);
            foreach(var table in catalog.ConfigTables)table.TableName=renames[table.TableName];
            foreach(var column in catalog.ConfigColumns)
            {
                foreach(var (oldName,newName) in renames)
                    column.ControlDetail=column.ControlDetail.Replace("\"table\":\""+oldName+"\"","\"table\":\""+newName+"\"",StringComparison.Ordinal);
            }
            var targetName=renames["global_source"];var terminalName=renames["global_target"];var consumerName=renames["cached_consumer"];
            var target="api/admin/auto/"+targetName+"/";var consumer="api/admin/auto/"+consumerName+"/30/";
            var model=GenerationModel.Create(catalog,ApplicationNamespace);
            var consumerTable=model.Tables.Single(table=>table.Source.Id=="3");
            var page=Assert.Single(consumerTable.Pages);
            using var session=new DatabaseSession(options);
            var schema=new SchemaService(session);
            var boundary=Path.Combine(Path.GetTempPath(),"metarill-scoped-global-mysql");
            var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
            var installed=false;
            try
            {
                Assert.Empty((await schema.InspectAsync(catalog)).Tables);
                var plan=await schema.PreviewAsync(catalog);
                Assert.Equal(3,plan.Changes.Count);
                Assert.All(plan.Changes,change=>Assert.Equal("createTable",change.Kind));
                installed=true;
                Assert.True((await schema.ApplyAsync(plan)).Succeeded);
                await Execute("INSERT INTO "+Quote(terminalName)+" (id,name,status) VALUES (8,'已发布名称',1)");
                await Execute("INSERT INTO "+Quote(targetName)+
                    $" (id,yz_project_id,global_target_id,status,delete_no) VALUES ({Own},100,8,1,NULL),({Published},101,8,1,NULL),({Hidden},101,8,0,NULL),({Deleted},101,8,1,NOW())");
                var selectedField=multiple?"global_source_ids":"global_source_id";
                var selectedValues=multiple?"'"+JsonSerializer.Serialize(new[]{Text(Published),Text(Hidden)})+"'":Text(Published);
                var cacheField=multiple?"global_source_global_target_names":"global_source_global_target_name";
                if(storage==0)
                    await Execute("INSERT INTO "+Quote(consumerName)+$" (id,yz_project_id,{selectedField}) VALUES (5,100,{selectedValues})");
                else
                {
                    var ownedValue=multiple?"'"+JsonSerializer.Serialize(new[]{Text(Own)})+"'":Text(Own);
                    var ownedName=multiple?"'[\"已发布名称\"]'":"'已发布名称'";
                    await Execute("INSERT INTO "+Quote(consumerName)+$" (id,yz_project_id,{selectedField},{cacheField}) VALUES (5,100,{ownedValue},{ownedName}),(6,101,{ownedValue},{ownedName})");
                }
                new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ApplicationNamespace);
                var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
                using var listener=new TcpListener(IPAddress.Loopback,0);
                listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
                builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
                var exports=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"exports"),MaxRows=20});
                builder.Services.AddSingleton<IPageExportService>(exports);
                builder.Services.AddHttpContextAccessor();
                builder.Services.AddSingleton(session);
                builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(857));
                builder.Services.AddScoped<IRuntimeAuthorization,ScopedCandidateAuthorization>();
                assembly.GetType(ApplicationNamespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
                builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(json=>json.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
                await using var app=builder.Build();
                app.Use(async(context,next)=>
                {
                    try{await next();}
                    catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        context.Response.StatusCode=500;
                        await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));
                    }
                });
                app.MapControllers();
                app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>
                {
                    if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();
                    var download=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);
                    return Results.Stream(download.Stream,download.ContentType,download.FileName);
                });
                await app.StartAsync();
                using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(20)};
                var request=new{pageNo=1,pageSize=20};
                Assert.Equal(new[]{Text(Published),Text(Own)},Ids(Data(await Post(target+"10/page",request)).GetProperty("records")));
                Assert.Equal("2",Data(await Post(target+"10/count",request)).ToString());
                Assert.Equal(new[]{Text(Own)},Ids(Data(await Post(target+"19/page",request)).GetProperty("records")));
                Assert.Equal("1",Data(await Post(target+"19/count",request)).ToString());
                Assert.Equal(500,Code(await Get(target+"10/detail/"+Published)));
                Assert.Equal(500,Code(await Get(target+"19/detail/"+Published)));
                Assert.Equal(200,Code(await Get(target+"19/detail/"+Own)));
                Assert.Equal(500,Code(await Post(target+"19/update",new{id=Text(Published),status=0})));

                var consumerRow=Data(await Post(consumer+"page",request)).GetProperty("records")[0];
                if(multiple)
                {
                    var labels=consumerRow.GetProperty("globalSourceGlobalTargetNames");
                    Assert.Equal("已发布名称",labels[0].GetString());
                    if(storage==0)Assert.Equal(JsonValueKind.Null,labels[1].ValueKind);

                    var endpoint=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,consumerTable,page)).Endpoint;
                    Assert.Equal(new[]{Text(Own),Text(Published)},Ids(Data(await Post(consumer+endpoint,new{}))).Order(StringComparer.Ordinal));
                    Assert.Equal(new[]{Text(Published),Text(Own)},Ids(Data(await Post(target+"10/selection/selected",new{selectedIds=new[]{Text(Published),Text(Own)}}))));
                    Assert.Equal(500,Code(await Post(target+"10/selection/selected",new{selectedIds=new[]{Text(Published),Text(Hidden)}})));
                }
                else
                {
                    var endpoint=RelationCompilation.ForPage(model,page).Single(relation=>relation.SourceColumn.Source.Id=="34").Endpoint;
                    var optionsPage=Data(await Post(consumer+endpoint+"/page",new{pageNo=1,pageSize=20}));
                    Assert.Equal("2",optionsPage.GetProperty("total").ToString());
                    var selected=Data(await Post(consumer+endpoint+"/selected",new{selectedIds=new[]{Text(Published)}}));
                    Assert.Equal(new[]{Text(Published)},Ids(selected));
                    Assert.Equal("已发布名称",selected[0].GetProperty("name").GetString());
                    Assert.Equal(500,Code(await Post(consumer+endpoint+"/selected",new{selectedIds=new[]{Text(Published),Text(Hidden)}})));
                }

                foreach(var pageId in new[]{"10","19"})
                {
                    var grant=Data(await Post(target+pageId+"/initExport",request)).GetString()!;
                    using var file=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(grant));
                    Assert.Equal(HttpStatusCode.OK,file.StatusCode);
                    using var workbook=new XLWorkbook(new MemoryStream(await file.Content.ReadAsByteArrayAsync()));
                    Assert.Single(workbook.Worksheets.Single().RowsUsed().Skip(1));
                }

                client.DefaultRequestHeaders.Add("denyPage","10");
                Assert.Equal(500,Code(await Post(target+"10/page",request)));
                client.DefaultRequestHeaders.Remove("denyPage");
                client.DefaultRequestHeaders.Add("foreignActor","1");
                Assert.Equal(500,Code(await Post(target+"10/page",request)));
                client.DefaultRequestHeaders.Remove("foreignActor");
                Assert.Equal(200,Code(await Post(target+"19/update",new{id=Text(Own),status=0})));
                Assert.Equal(new[]{Text(Published)},Ids(Data(await Post(target+"10/page",request)).GetProperty("records")));
                Assert.Equal(new[]{Text(Own)},Ids(Data(await Post(target+"19/page",request)).GetProperty("records")));
                if(storage==1)
                {
                    var column=multiple?"globalSourceGlobalTargetNames":"globalSourceGlobalTargetName";
                    var row=Data(await Get(consumer+"detail/5"));
                    Assert.Equal(JsonValueKind.Null,multiple?row.GetProperty(column)[0].ValueKind:row.GetProperty(column).ValueKind);
                    Assert.Equal(0L,await CountMissingOtherCache(cacheField));
                }
                await app.StopAsync();

                async Task<JsonElement> Post(string path,object value)
                {
                    using var response=await client.PostAsJsonAsync(path,value);
                    return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
                }
                async Task<JsonElement> Get(string path)
                {
                    using var response=await client.GetAsync(path);
                    return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
                }
            }
            finally
            {
                try
                {
                    if(installed)
                    {
                        foreach(var table in catalog.ConfigTables)await Execute("DROP TABLE IF EXISTS "+Quote(table.TableName));
                        using var readback=new DatabaseSession(options);
                        Assert.Empty((await new SchemaService(readback).InspectAsync(catalog)).Tables);
                    }
                }
                finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
            }

            // 分开读取外项目消费者物理缓存，确认传播不会越过本目录行项目。
            async Task<long> CountMissingOtherCache(string field)
            {
                await using var connection=session.Provider.CreateConnection(options);
                await connection.OpenAsync();
                await using var command=connection.CreateCommand();
                command.CommandText="SELECT COUNT(*) FROM "+Quote(consumerName)+" WHERE id=6 AND ("+Quote(field)+" IS NULL OR "+Quote(field)+"='[null]')";
                return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);
            }

            string Quote(string name)=>session.Provider.QuoteIdentifier(name);
            async Task Execute(string sql)
            {
                await using var connection=session.Provider.CreateConnection(options);
                await connection.OpenAsync();
                await using var command=connection.CreateCommand();
                command.CommandText=sql;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    /// <summary>固定账号只经原控制器的授权接口建立身份。</summary>
    private sealed class ScopedCandidateAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(857,"候选验收",false,accessor.HttpContext!.Request.Headers.ContainsKey("foreignActor")?"101":"100","9");
        public void RequirePagePermission(string pageId,string operation)
        {
            if(accessor.HttpContext!.Request.Headers["denyPage"]==pageId)throw new UnauthorizedAccessException("该页面未授权。");
        }
    }

    private static string Text(long value)=>value.ToString(CultureInfo.InvariantCulture);
    private static int Code(JsonElement response)=>response.GetProperty("code").GetInt32();
    private static JsonElement Data(JsonElement response)
    {
        Assert.True(Code(response)==200,response.GetRawText());
        return response.GetProperty("data");
    }
    private static string[] Ids(JsonElement rows)=>rows.EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).ToArray();
}
