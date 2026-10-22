using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
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

/// <summary>固定测试身份的真实生成HTTP/MySQL，覆盖非冗余自树；不冒充正式登录或浏览器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantSelfTreeMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    /// <summary>两种ID／父键及显隐名称各用独占两表，清理后独立连接确认不存在。</summary>
    [StandaloneExportMySqlFact]
    public async Task Only_selected_id_is_stored_while_paths_filters_excel_and_current_writes_share_graph()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("自树实库仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少实例pin。")));
        await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不匹配，未创建样本。");}
        foreach(var type in new[]{"string","int64"})foreach(var automatic in new[]{false,true})await Exercise(options,type,automatic);
    }
    /// <summary>元数据先行显式建表；父及候选范围沿原页面，名称不进入物理记录。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,bool automatic)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrself_"+nonce[..12];var catalog=NonRedundantSelfTreeTests.Catalog(prefix,type,type,automatic);
        foreach(var column in catalog.ConfigColumns)column.Name=column.ColumnName;
        catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=1;catalog.ConfigColumns.Single(column=>column.Id=="25").SortNo=2;
        catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="amount",Name="amount",LogicalType="int64",SortNo=3});catalog.ConfigPageColumns.Add(new(){Id="2008",ConfigPageId="201",ConfigColumnId="28",Extra=new(){["sumKind"]=JsonSerializer.SerializeToElement(1)}});
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");sourcePage.Extra["isSum"]=JsonSerializer.SerializeToElement(1);sourcePage.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        var selection=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;selection.Id="202";selection.Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigPages.Add(selection);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="202"+field.Id;copy.ConfigPageId="202";if(copy.ConfigColumnId=="25")copy.Extra["controlDetail"]=JsonSerializer.SerializeToElement(catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail.Replace("selfAndDescendants","exact",StringComparison.Ordinal));catalog.ConfigPageColumns.Add(copy);}
        if(!automatic)
        {
            // 名称可单独授权展示／导出，所选ID仅作私有依赖，不能因此进入VO或Excel表头。
            var nameOnly=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;nameOnly.Id="203";nameOnly.IsImport=0;nameOnly.IsCopy=0;nameOnly.Extra.Remove("importMethod");catalog.ConfigPages.Add(nameOnly);
            catalog.ConfigPageColumns.Add(new(){Id="20326",ConfigPageId="203",ConfigColumnId="26",SortNo=1});
        }
        var root=Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少仓库路径。"));var evidence=Path.Combine(root,".local/nonredundant-self-tree/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self-http");var directory=Path.Combine(boundary,nonce);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var source=prefix+"_source";var target=prefix+"_tree";var parent="parent_"+prefix+"_tree_id";
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var store=new SqlRowStore(session,catalog);var ids=new SnowflakeIdGenerator(873);var owned=new HashSet<long>();var observer=new FailureObserver();var guard=new SnapshotGuard(options,target);WebApplication? app=null;bool created=false;string? fingerprint=null;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(2,plan.Changes.Count);await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            var longName=new string('长',80);
            for(var index=0;index<11;index++)
            {
                long? parentId=index switch{1=>First,2=>First+1,7=>First+6,8=>First+999,9=>First+10,10=>First+9,_=>null};
                await store.InsertAsync(target,new Dictionary<string,object?>{{"id",First+index},{"name",index switch{0=>"根",1=>"中",2=>longName,_=>"节点"+index}},{parent,type=="string"?parentId?.ToString(CultureInfo.InvariantCulture):parentId},{"yz_project_id",index==4?Project+1:Project},{"status",index==6?0:1},{"delete_no",index==5?new DateTime(2026,1,1):null}});
            }
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NonRedTreeHttp");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(ids);builder.Services.AddScoped<IRuntimeAuthorization,Authorization>();builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(guard);
            var exports=new ExcelPageExportService(new ExportOptions{RootDirectory=Path.Combine(directory,"exports"),MaxRows=30});builder.Services.AddSingleton<IPageExportService>(exports);assembly.GetType("Example.NonRedTreeHttp.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(settings=>settings.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or PageImportException){await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(25)};var endpoint="/api/admin/auto/"+source+"/201/";var selectedEndpoint="/api/admin/auto/"+source+"/202/";
            var added=Success(await Post(endpoint+"add",new{name="第一条",treeId=(First+2).ToString(),amount=3}));var record=added.GetProperty("id").GetString()!;owned.Add(long.Parse(record,CultureInfo.InvariantCulture));Assert.Equal(longName,added.GetProperty("treeName").GetString());Assert.Equal("根/中/"+longName,added.GetProperty("treeIdDisplayText").GetString());Assert.DoesNotContain("tree_name",(await store.ReadAsync(source)).Single().Keys);
            if(!automatic)
            {
                var nameRow=Success(await Post(endpoint.Replace("/201/","/203/")+"page",new PageRequest())).GetProperty("records")[0];Assert.Equal(longName,nameRow.GetProperty("treeName").GetString());Assert.False(nameRow.TryGetProperty("treeId",out _));Assert.False(nameRow.TryGetProperty("treeIdDisplayText",out _));
            }
            var whole=Success(await Post(endpoint+"update",new{id=record,name="第一条",treeId=(First+2).ToString(),treeName="伪造回显",treeIdDisplayText="伪造/路径"}));Assert.Equal(longName,whole.GetProperty("treeName").GetString());Assert.Equal("根/中/"+longName,whole.GetProperty("treeIdDisplayText").GetString());Assert.DoesNotContain("tree_name",(await store.ReadAsync(source)).Single().Keys);
            var second=Success(await Post(endpoint+"add",new{name="第二条",treeId=(First+1).ToString(),amount=2}));owned.Add(long.Parse(second.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));
            var filtered=await Post(endpoint+"page",new{pageSize=1,filters=Filter(First)});Assert.Equal("2",Success(filtered).GetProperty("total").GetString());Assert.Equal(5,filtered.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").GetDecimal());
            Assert.Equal("2",Success(await Post(endpoint+"count",new{filters=Filter(First)})).ToString());Assert.Equal("1",Success(await Post(selectedEndpoint+"page",new{filters=Filter(First+1)})).GetProperty("total").GetString());
            var selected=Success(await Post(selectedEndpoint+"selection/selected",new{selectedIds=new[]{record}}));Assert.Equal("根/中/"+longName,selected[0].GetProperty("treeIdDisplayText").GetString());
            var initial=await Hash();foreach(var bad in new[]{First+4,First+5,First+6,First+7,First+8,First+9,First+999}){Assert.Equal(500,(await Post(endpoint+"update",new{id=record,treeId=bad.ToString()})).GetProperty("code").GetInt32());Assert.Equal(initial,await Hash());}
            client.DefaultRequestHeaders.Add("denyTargetList","1");Assert.Equal("2",Success(await Post(endpoint+"page",new PageRequest())).GetProperty("total").GetString());Assert.Equal(500,(await Post(endpoint+"cascadeTreeOptions25",new{})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post(endpoint+"update",new{id=record,treeId=(First+2).ToString()})).GetProperty("code").GetInt32());Success(await Post(endpoint+"update",new{id=record,name="省略树字段"}));client.DefaultRequestHeaders.Remove("denyTargetList");
            guard.Change=true;var current=Success(await Post(endpoint+"update",new{id=record,treeId=(First+2).ToString()}));Assert.Equal("RR新叶",current.GetProperty("treeName").GetString());Assert.Equal("RR新根/中/RR新叶",current.GetProperty("treeIdDisplayText").GetString());
            var copied=Success(await Post(endpoint+"copy/"+record,new{}));owned.Add(long.Parse(copied.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));Assert.Equal("RR新根/中/RR新叶",copied.GetProperty("treeIdDisplayText").GetString());
            initial=await Hash();observer.Fail=true;Assert.Equal(500,(await Post(endpoint+"update",new{id=record,treeId=(object?)null,name="不可提交"})).GetProperty("code").GetInt32());observer.Fail=false;Assert.Equal(initial,await Hash());
            client.DefaultRequestHeaders.Add("denyTargetList","1");Assert.Equal(200,(await Post(endpoint+"importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"name","导入路径"},{"tree_id","RR新根/中/RR新叶"},{"amount",7}}}})).GetProperty("code").GetInt32());var imported=(await store.ReadAsync(source)).Single(row=>Equals(row["name"],"导入路径"));owned.Add(SqlRowStore.Id(imported));initial=await Hash();Assert.Equal(500,(await Post(endpoint+"importDatas201",new{requestJson=new[]{new Dictionary<string,object?>{{"name","整批第一行"},{"tree_id","RR新根/中"}},new Dictionary<string,object?>{{"name","整批第二行"},{"tree_id","不存在/路径"}}}})).GetProperty("code").GetInt32());Assert.Equal(initial,await Hash());
            var ticket=Success(await Post(endpoint+"initExport",new PageRequest{SelectedIds=[record]}));using(var download=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket.GetString()!))){Assert.Equal(HttpStatusCode.OK,download.StatusCode);using var book=new XLWorkbook(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.Equal("RR新根/中/RR新叶",sheet.Cell(2,headers["tree_id"]).GetString());}client.DefaultRequestHeaders.Remove("denyTargetList");
            if(!automatic)
            {
                var ticketName=Success(await Post(endpoint.Replace("/201/","/203/")+"initExport",new PageRequest{SelectedIds=[record]}));using var downloadName=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticketName.GetString()!));Assert.Equal(HttpStatusCode.OK,downloadName.StatusCode);using var bookName=new XLWorkbook(new MemoryStream(await downloadName.Content.ReadAsByteArrayAsync()));Assert.Equal("tree_name",bookName.Worksheet(1).Cell(1,1).GetString());Assert.Equal("RR新叶",bookName.Worksheet(1).Cell(2,1).GetString());Assert.Single(bookName.Worksheet(1).Row(1).CellsUsed());
            }
            var cleared=Success(await Post(endpoint+"update",new{id=record,treeId=(object?)null}));Assert.Equal(JsonValueKind.Null,cleared.GetProperty("treeId").ValueKind);Assert.Equal(JsonValueKind.Null,cleared.GetProperty("treeName").ValueKind);Assert.Equal("",cleared.GetProperty("treeIdDisplayText").GetString());Success(await Post(endpoint+"update",new{id=record,treeId=(First+2).ToString()}));
            await store.UpdateAsync(target,First,new Dictionary<string,object?>{{"status",0}});using(var detail=await client.GetAsync(endpoint+"detail/"+record)){var value=Success(JsonSerializer.Deserialize<JsonElement>(await detail.Content.ReadAsStringAsync()));Assert.Equal("",value.GetProperty("treeIdDisplayText").GetString());Assert.Equal(JsonValueKind.Null,value.GetProperty("treeName").ValueKind);Assert.Equal((First+2).ToString(),value.GetProperty("treeId").ToString());}
            if(!automatic)
            {
                using var detailName=await client.GetAsync(endpoint.Replace("/201/","/203/")+"detail/"+record);var nameValue=Success(JsonSerializer.Deserialize<JsonElement>(await detailName.Content.ReadAsStringAsync()));Assert.Equal(JsonValueKind.Null,nameValue.GetProperty("treeName").ValueKind);Assert.False(nameValue.TryGetProperty("treeId",out _));
                var badExport=await Post(endpoint+"initExport",new PageRequest{SelectedIds=[record]});Assert.Equal(500,badExport.GetProperty("code").GetInt32());
                var hiddenNameTicket=Success(await Post(endpoint.Replace("/201/","/203/")+"initExport",new PageRequest{SelectedIds=[record]}));using var hiddenDownload=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(hiddenNameTicket.GetString()!));Assert.Equal(HttpStatusCode.OK,hiddenDownload.StatusCode);using var hiddenWorkbook=new XLWorkbook(new MemoryStream(await hiddenDownload.Content.ReadAsByteArrayAsync()));Assert.Equal("",hiddenWorkbook.Worksheet(1).Cell(2,1).GetString());
            }
            await store.UpdateAsync(target,First,new Dictionary<string,object?>{{"status",1}});
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);await Receipt("verified",false);
            async Task<JsonElement> Post(string url,object value){using var response=await client.PostAsJsonAsync(url,value);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Hash()=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes((await store.ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)).ToArray())));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.txt"),error.ToString());throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);Assert.NotNull(fingerprint);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await new SchemaService(cleanup).InspectAsync(catalog)));Assert.Equal(owned.Order(),(await store.ReadIncludingDeletedAsync(source)).Select(row=>SqlRowStore.Id(row)).Order());Assert.Equal(Enumerable.Range(0,11).Select(index=>First+index),(await store.ReadIncludingDeletedAsync(target)).Select(row=>SqlRowStore.Id(row)).Order());
                await using(var connection=cleanup.Provider.CreateConnection(options)){await connection.OpenAsync();foreach(var name in names){Assert.StartsWith(prefix+"_",name);await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}}
                await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";command.Parameters.AddWithValue("name",name);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);
            }
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,type,automatic,names,fingerprint,owned,instanceVerified=true,cleaned,fixedTestAuthorization=true,browser=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>原组件的节点对象只发送固定叶子ID，名称不会作为SQL或匹配依据。</summary>
    private static Dictionary<string,object?> Filter(long id)=>new(){{"treeId",new{treeId=id.ToString(CultureInfo.InvariantCulture),treeName="不参与匹配"}}};
    private static JsonElement Success(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.GetRawText());return response.GetProperty("data");}
    /// <summary>既有源事务建立旧快照后独立改名，验证私有写后当前读取完整新路径。</summary>
    private sealed class SnapshotGuard(DatabaseOptions options,string target):IRecordMutationGuard
    {
        internal bool Change{get;set;}
        public async Task BeforeMutationAsync(RecordMutationPreparation context,CancellationToken token=default)
        {
            if(!Change||context.Operation!=RecordMutationOperation.Update)return;Change=false;await using(var command=context.Transaction.Connection!.CreateCommand()){command.Transaction=context.Transaction;command.CommandText="SELECT name FROM `"+target+"` WHERE id=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=First;command.Parameters.Add(parameter);await command.ExecuteScalarAsync(token);}
            await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync(token);foreach(var pair in new[]{(First,"RR新根"),(First+2,"RR新叶")}){await using var command=independent.CreateCommand();command.CommandText="UPDATE `"+target+"` SET name=@name WHERE id=@id";command.Parameters.AddWithValue("name",pair.Item2);command.Parameters.AddWithValue("id",pair.Item1);Assert.Equal(1,await command.ExecuteNonQueryAsync(token));}
        }
    }
    /// <summary>只控制测试撤权，不由请求指定账号、项目或目标表。</summary>
    private sealed class Authorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(873,"非冗余自树实库",false,Project.ToString(),"1");
        public void RequirePagePermission(string pageId,string operation){if(pageId=="301"&&operation=="list"&&accessor.HttpContext!.Request.Headers.ContainsKey("denyTargetList"))throw new UnauthorizedAccessException("测试撤销目标list。");}
    }
    private sealed class FailureObserver:IRecordMutationObserver
    {
        internal bool Fail{get;set;}
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){if(Fail)throw new InvalidOperationException("测试提交前故障。");return Task.CompletedTask;}
    }
}
