using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
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

/// <summary>单表自关联实际HTTP验证自身回执、反向缓存和物理删除，全部样本限于Server-200隔离库。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SelfNamePropagationMySqlTests
{
    /// <summary>新增／自引用更新／下游改名／尾部失败／物理删除使用同一生成服务及真实事务。</summary>
    [StandaloneExportMySqlFact]
    public Task Declared_self_reference_keeps_response_current_and_physical_delete_clears_names()=>Verify(false);
    [StandaloneExportMySqlFact]
    public Task Automatic_self_reference_keeps_response_current_and_physical_delete_clears_names()=>Verify(true);
    private static async Task Verify(bool automatic)
    {
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=config.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||new MySqlConnectionStringBuilder(options.ConnectionString).Database!=options.ExpectedDatabase)throw new InvalidOperationException("只允许隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));
        await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString());}
        var nonce=Guid.NewGuid().ToString("N");var name="mt_selfprop_"+nonce[..12];var catalog=Catalog(name,automatic);var boundary=Path.Combine(Path.GetTempPath(),"metarill-self-propagation");var directory=Path.Combine(boundary,nonce);var evidence=Path.Combine(Root(),".local/relation-propagation/self-"+nonce);Directory.CreateDirectory(evidence);using var session=new DatabaseSession(options);var schema=new SchemaService(session);var rows=new SqlRowStore(session,catalog);var own=new HashSet<long>();WebApplication? app=null;var created=false;string? fingerprint=null;var failure=new Failure();
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Single(plan.Changes);await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SelfPropagation");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(887));builder.Services.AddSingleton<IRuntimeAuthorization>(new Authorization());builder.Services.AddSingleton<IRecordMutationObserver>(failure);assembly.GetType("Example.SelfPropagation.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(settings=>settings.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"request-error.txt"),error.ToString());context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(R<string>.Error("隔离夹具错误已保留"));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(20)};var endpoint="/api/admin/auto/"+name+"/101";
            async Task<JsonElement> Post(string route,object data){using var response=await client.PostAsJsonAsync(endpoint+route,data);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            var first=Data(await Post("/add",new{name="自节点甲",targetIds=(object?)null}));var a=long.Parse(first.GetProperty("id").GetString()!);own.Add(a);var second=Data(await Post("/add",new{name="自节点乙",targetIds=new[]{a.ToString()}}));var b=long.Parse(second.GetProperty("id").GetString()!);own.Add(b);Assert.Equal(new[]{"自节点甲"},second.GetProperty("targetNames").EnumerateArray().Select(item=>item.GetString()));
            var changed=Data(await Post("/update",new{id=a,name="自节点甲改名",targetIds=new[]{a.ToString(),b.ToString()}}));Assert.Equal(new[]{"自节点甲改名","自节点乙"},changed.GetProperty("targetNames").EnumerateArray().Select(item=>item.GetString()));var child=(await rows.ReadAsync(name,new Dictionary<string,object?>{{"id",b}})).Single();Assert.Equal(new[]{"自节点甲改名"},JsonSerializer.Deserialize<string[]>((string)child["target_names"]!));
            var before=JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(name));failure.Fail=true;Assert.Equal(500,(await Post("/update",new{id=a,name="必须回滚的改名"})).GetProperty("code").GetInt32());Assert.Equal(before,JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(name)));failure.Fail=false;
            using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,endpoint){Content=JsonContent.Create(new{id=a})}))Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));Assert.Empty(await rows.ReadAsync(name,new Dictionary<string,object?>{{"id",a}}));child=(await rows.ReadAsync(name,new Dictionary<string,object?>{{"id",b}})).Single();Assert.Equal(new[]{a.ToString()},JsonSerializer.Deserialize<string[]>((string)child["target_ids"]!));Assert.Equal(new string?[]{null},JsonSerializer.Deserialize<string?[]>((string)child["target_names"]!));await Receipt("verified",false);
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.txt"),error.ToString());throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog)));var current=await rows.ReadIncludingDeletedAsync(name);Assert.All(current,row=>Assert.Contains(SqlRowStore.Id(row),own));await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using(var command=connection.CreateCommand()){command.CommandText="DROP TABLE "+session.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using var check=independent.CreateCommand();check.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";check.Parameters.AddWithValue("name",name);Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync()));await Receipt("cleaned",true);}
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,name,own,fingerprint,cleaned,instanceVerified=true,browserExecuted=false,fixedAuthorization=true})+"\n");
    }
    /// <summary>原普通页面与type=2选择页共用一表，未声明delete_no因此执行物理删除。</summary>
    internal static MetadataCatalog Catalog(string name,bool automatic)
    {
        var catalog=new MetadataCatalog{YzProjectId="54",ConfigTables=[new(){Id="1",TableName=name}],ConfigColumns=[new(){Id="11",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="12",ConfigTableId="1",ColumnName="name",LogicalType="string",ColumnLength="100"},new(){Id="13",ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="14",ConfigTableId="1",ColumnName="target_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="1",ControlDetail="{\"pageId\":\"201\",\"nameField\":\"name\"}"}],ConfigPages=[new(){Id="101",ConfigTableId="1",WhereSql="yz_project_id=@current_yz_project_id"},new(){Id="201",ConfigTableId="1",WhereSql="yz_project_id=@current_yz_project_id",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}}],ConfigPageColumns=[new(){Id="101_name",ConfigPageId="101",ConfigColumnId="12"},new(){Id="101_ids",ConfigPageId="101",ConfigColumnId="14"},new(){Id="201_name",ConfigPageId="201",ConfigColumnId="12"}]};if(!automatic){catalog.ConfigColumns.Add(new(){Id="15",ConfigTableId="1",ColumnName="target_names",LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("target.name")}});catalog.ConfigPageColumns.Add(new(){Id="101_names",ConfigPageId="101",ConfigColumnId="15"});}return catalog;
    }
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.GetRawText());return response.GetProperty("data");}
    private sealed class Failure:IRecordMutationObserver{public bool Fail{get;set;}public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){if(Fail)throw new InvalidOperationException("自关联传播尾部失败");return Task.CompletedTask;}}
    private sealed class Authorization:IRuntimeAuthorization{public RuntimeActor RequireActor()=>new(887,"自关联传播",false,"9007199254740993","5");public void RequirePagePermission(string pageId,string operation){}}
    private static string Root()=>NonRedundantApplicationFixture.Root();
}
