using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Relations;
using Metarill.Data.Schema;
using Metarill.Runtime.Maintenance;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>原控制器真实HTTP与Server-200随机表验收；本夹具固定身份，不冒充正式网页登录验收。</summary>
public sealed class SystemMaintenanceMySqlTests
{
    private const long Project=54,Party=9007199254740993,Mid=9007199254741993,First=9007199254742993;
    private const string Endpoint="/api/admin/sys/system_maintenance";
    /// <summary>混合单值／数组整表重算、真实项目隔离、旧RR当前读和跨批失败整体回滚。</summary>
    [MySqlApiFact]
    public async Task Original_http_backfills_complete_published_table_and_rolls_back_late_failures()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||new MySqlConnectionStringBuilder(options.ConnectionString).Database!=options.ExpectedDatabase)throw new InvalidOperationException("仅允许隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await Pin();
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_maint_"+nonce[..12];var model=Catalog(prefix);var plans=RelationMaintenanceCatalog.Compile(model,Definitions());using var session=new DatabaseSession(options);var rows=new SqlRowStore(session,model);var schema=new SchemaService(session);var evidence=Path.Combine(Repository(),".local/relation-maintenance/http-"+nonce);Directory.CreateDirectory(evidence);var own=model.ConfigTables.ToDictionary(table=>table.TableName,_=>new HashSet<long>());WebApplication? app=null;var created=false;string? fingerprint=null;var selected=JsonSerializer.Serialize(new[]{(Party+1).ToString(),Party.ToString(),(Party+2).ToString(),(Party+3).ToString(),(Party+4).ToString()});
        try
        {
            Assert.Empty((await schema.InspectAsync(model)).Tables);var create=await schema.PreviewAsync(model);Assert.Equal(3,create.Changes.Count);await Receipt("intent",false);created=true;Assert.True((await schema.ApplyAsync(create)).Succeeded);fingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(model));
            await session.InTransactionAsync(async()=>
            {
                for(var index=0;index<4;index++)await Seed(prefix+"_party",Party+index,new(){{"name",index==0?"公司甲":index==1?"公司乙":"不可见公司"},{"title","标题"+index},{"yz_project_id",index==3?Project+1:Project},{"delete_no",index==2?new DateTime(2026,1,1):null}});
                await Seed(prefix+"_mid",Mid,new(){{"company_id",Party},{"score",12.50m}});
                for(var index=0;index<132;index++)await Seed(prefix+"_source",First+index,new(){{"name","业务值"+index},{"target_id",Mid},{"target_name","旧单值"},{"target_score",0m},{"target_company_id",null},{"target_company_name","旧公司显示"},{"supplier_ids",selected},{"supplier_names","旧名称"},{"supplier_titles","旧标题"},{"yz_project_id",index==130?Project+1:Project},{"delete_no",index==131?new DateTime(2026,1,1):null}});
                return true;
            });
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<ISecurityIdentityAccessor,FixtureIdentity>();builder.Services.AddScoped(provider=>new SystemMaintenanceBusiness(session,plans,provider.GetRequiredService<ISecurityIdentityAccessor>()));builder.Services.AddControllers().AddApplicationPart(typeof(SystemMaintenanceController).Assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error){context.Response.StatusCode=200;await context.Response.WriteAsJsonAsync(R<object>.Error(error is UnauthorizedAccessException?"没有维护权限":error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};
            async Task<JsonElement> Post(string action,object? data=null,string mode="granted")
            {using var request=new HttpRequestMessage(HttpMethod.Post,Endpoint+"/"+action);request.Headers.Add("maintenance-test-identity",mode);if(data is not null)request.Content=JsonContent.Create(data);using var response=await client.SendAsync(request);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            foreach(var mode in new[]{"anonymous","denied","agent","outside"}){Assert.Equal(500,(await Post("tables",mode:mode)).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("backfill_join_columns",new{configTableId="1"},mode)).GetProperty("code").GetInt32());}
            var optionsList=Data(await Post("tables")).EnumerateArray().ToArray();Assert.Equal(2,optionsList.Length);var source=optionsList.Single(item=>item.GetProperty("id").GetString()=="1");Assert.Equal("来源（"+prefix+"_source）",source.GetProperty("name").GetString());Assert.Equal(source.GetProperty("name").GetString(),source.GetProperty("text").GetString());Assert.Equal("1",source.GetProperty("value").GetString());Assert.Equal("来源",source.GetProperty("rawName").GetString());
            var before=await Snapshot();Assert.Equal(500,(await Post("backfill_join_columns")).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("backfill_join_columns",new{configTableId="2"})).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
            var result=Data(await Post("backfill_join_columns",new{configTableId="1",projectId="999999",connectionString="ignored"}));Assert.Equal(5,result.GetProperty("joinColumnCount").GetInt32());Assert.Equal(650,result.GetProperty("affectedRows").GetInt64());Assert.Equal(5,result.GetProperty("details").GetArrayLength());var auxiliary=result.GetProperty("details").EnumerateArray().Single(item=>item.GetProperty("columnName").GetString()=="target_company_id");Assert.Equal("target_company_name",auxiliary.GetProperty("displayColumnName").GetString());Assert.Equal("company_name",auxiliary.GetProperty("targetDisplayColumnName").GetString());await AssertCurrent("公司甲");
            Assert.Equal(0,Data(await Post("backfill_join_columns",new{configTableId="1"})).GetProperty("affectedRows").GetInt64());
            // 已建立旧RR快照后外部改名提交，回填必须当前读取得最新目标，不被旧快照覆盖。
            await session.InTransactionAsync(async()=>{Assert.Equal("公司甲",(await rows.ReadAsync(prefix+"_party",new Dictionary<string,object?>{{"id",Party}})).Single()["name"]);await External("UPDATE `"+prefix+"_party` SET name='公司甲当前名' WHERE id="+Party);var refreshed=await new RelationMaintenanceExecutor(session).BackfillAsync(plans,"1");Assert.True(refreshed.AffectedRows>0);return true;});await AssertCurrent("公司甲当前名");
            // 第129条超过200项，第一批128条即便已重算，也必须随整批失败回滚。
            await session.InTransactionAsync(async()=>{for(var index=0;index<130;index++)await rows.UpdateAsync(prefix+"_source",First+index,new Dictionary<string,object?>{{"target_name","整批旧缓存"},{"supplier_names","整批旧名称"}});await rows.UpdateAsync(prefix+"_source",First+129,new Dictionary<string,object?>{{"supplier_ids",JsonSerializer.Serialize(Enumerable.Repeat(Party.ToString(),201))}});return true;});before=await Snapshot();Assert.Equal(500,(await Post("backfill_join_columns",new{configTableId="1"})).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());await rows.UpdateAsync(prefix+"_source",First+129,new Dictionary<string,object?>{{"supplier_ids",selected}});
            // 一列物理类型不匹配时，预检必须在任何缓存更新前拒绝；恢复仅限本样本列。
            await External("ALTER TABLE `"+prefix+"_source` MODIFY COLUMN supplier_titles varchar(30) NULL");before=await Snapshot();Assert.Equal(500,(await Post("backfill_join_columns",new{configTableId="1"})).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());await External("ALTER TABLE `"+prefix+"_source` MODIFY COLUMN supplier_titles longtext NULL");Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(model)));Data(await Post("backfill_join_columns",new{configTableId="1"}));await AssertCurrent("公司甲当前名");await Receipt("verified",false);
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.txt"),error.ToString());throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created){await Pin();Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(model)));foreach(var table in model.ConfigTables){var ids=(await rows.ReadIncludingDeletedAsync(table.TableName)).Select(row=>SqlRowStore.Id(row)).ToHashSet();Assert.True(ids.SetEquals(own[table.TableName]));}foreach(var table in model.ConfigTables){Assert.StartsWith(prefix+"_",table.TableName);await External("DROP TABLE `"+table.TableName+"`");}await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();foreach(var table in model.ConfigTables){await using var check=independent.CreateCommand();check.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table";check.Parameters.AddWithValue("table",table.TableName);Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleaned",true);}
        }
        async Task Seed(string table,long id,Dictionary<string,object?> values){values["id"]=id;values.TryAdd("yz_project_id",Project);await rows.InsertAsync(table,values);own[table].Add(id);}
        async Task<string> Snapshot()=>JsonSerializer.Serialize(await rows.ReadIncludingDeletedAsync(prefix+"_source"));
        async Task AssertCurrent(string company)
        {
            var current=await rows.ReadIncludingDeletedAsync(prefix+"_source");foreach(var row in current){var id=SqlRowStore.Id(row);if(id==First+130||id==First+131){Assert.Equal("旧单值",row["target_name"]);Assert.Equal("旧名称",row["supplier_names"]);continue;}Assert.Equal(company,row["target_name"]);Assert.Equal(12.50m,row["target_score"]);Assert.Equal(Party,row["target_company_id"]);Assert.Equal(company,row["target_company_name"]);Assert.Equal(selected,row["supplier_ids"]);Assert.Equal(new string?[]{"公司乙",company,null,null,null},JsonSerializer.Deserialize<string?[]>((string)row["supplier_names"]!));Assert.Equal("业务值"+(id-First),row["name"]);}
        }
        async Task External(string sql){await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
        async Task Pin(){await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString());}
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,prefix,tables=model.ConfigTables.Select(table=>table.TableName),fingerprint,own,cleaned,instanceVerified=true,realHttpController=true,fixedIdentity=true,realWebLoginExecuted=false,browserExecuted=false})+"\n");
    }
    /// <summary>R1来源同时包含R0中间联接、标量／派生ID和两份物理多值名称。</summary>
    internal static MetadataCatalog Catalog(string prefix)
    {
        var catalog=new MetadataCatalog{YzProjectId="54",YzAdminTypes=[new(){Id="5",YzProjectId="54"}],ConfigTables=[new(){Id="1",TableName=prefix+"_source",Name="来源",IsRongyu=1},new(){Id="2",TableName=prefix+"_mid",IsRongyu=0},new(){Id="3",TableName=prefix+"_party",IsRongyu=1}]};
        foreach(var table in catalog.ConfigTables)catalog.ConfigColumns.AddRange([Column(table.Id+"_id",table.Id,"id","int64",true),Column(table.Id+"_project",table.Id,"yz_project_id","int64"),Column(table.Id+"_delete",table.Id,"delete_no","datetime")]);
        catalog.ConfigColumns.AddRange([Column("source_name","1","name"),new(){Id="foreign",ConfigTableId="1",ColumnName="target_id",LogicalType="int64",LinkConfigTableId="2"},Join("target_name","1","target_name","target.name"),Join("target_score","1","target_score","target.score","decimal"),Join("target_company","1","target_company_id","target.company_id","int64"),Column("target_company_name","1","target_company_name"),new(){Id="multiple",ConfigTableId="1",ColumnName="supplier_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="3",ControlDetail="{\"pageId\":\"301\",\"nameField\":\"name\"}"},Join("names","1","supplier_names","supplier.name",length:"max"),Join("titles","1","supplier_titles","supplier.title",length:"max"),new(){Id="company",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="3"},Join("mid_name","2","name","company.name"),Column("mid_score","2","score","decimal"),Column("party_name","3","name"),Column("party_title","3","title")]);catalog.ConfigPages.Add(new(){Id="301",ConfigTableId="3"});
        foreach(var (action,id) in new[]{("tables","9001"),("backfill_join_columns","9002")})catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id,sql="/admin/sys/system_maintenance/"+action}));return catalog;
    }
    internal static RelationCacheDefinition[] Definitions()=>[new("1","multiple","3","party_name","names",true),new("1","multiple","3","party_title","titles",true)];
    private static ColumnMetadata Column(string id,string table,string name,string type="string",bool required=false)=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,ColumnLength="100",IsDbNotNull=required?1:0};
    private static ColumnMetadata Join(string id,string table,string name,string path,string type="string",string length="100"){var column=Column(id,table,name,type);column.ColumnLength=length;column.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);column.Extra["columnFullName"]=JsonSerializer.SerializeToElement(path);return column;}
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.GetRawText());return response.GetProperty("data");}
    private static string Repository()=>Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??throw new InvalidOperationException("缺少验收仓库根目录。");
    private sealed class FixtureIdentity(IHttpContextAccessor accessor):ISecurityIdentityAccessor
    {
        /// <summary>固定身份只用于本隔离控制器测试，不替代正式登录管线或权限撤销验收。</summary>
        public AuthenticatedIdentity RequireIdentity()
        {
            var mode=accessor.HttpContext!.Request.Headers["maintenance-test-identity"].ToString();if(mode=="anonymous")throw new UnauthorizedAccessException();return new(new RuntimeActor(777,"维护夹具",false,"54","5"),new HashSet<string>(),778,mode=="agent"?779:null){PrincipalProjectId=mode=="outside"?"55":"54",PrincipalAdminTypeId="5",ButtonIds=mode=="denied"?new HashSet<string>():new HashSet<string>{"9001","9002"}};
        }
    }
}
