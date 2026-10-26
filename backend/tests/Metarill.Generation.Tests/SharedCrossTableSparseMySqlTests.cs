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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享R0实际生成接口与隔离MySQL；固定授权器不替代正式账号、完整包和浏览器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossTableSparseMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    [StandaloneExportMySqlFact]public Task String_string()=>Run("string","string");
    [StandaloneExportMySqlFact]public Task String_long()=>Run("string","int64");
    [StandaloneExportMySqlFact]public Task Long_string()=>Run("int64","string");
    [StandaloneExportMySqlFact]public Task Long_long()=>Run("int64","int64");
    [StandaloneExportMySqlFact]public Task Short_string()=>Run("string","int64",true);
    [StandaloneExportMySqlFact]public Task Short_long()=>Run("int64","string",true);
    [StandaloneExportMySqlFact]public Task Derived_targets()=>Run("string","int64",false,true);
    /// <summary>固定实例pin、数据库与账号配置核对后才建立本轮随机表。</summary>
    private static async Task Run(string type,string parentType,bool shorter=false,bool derived=false)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("跨表缓存实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建缓存样本。");}await Exercise(options,type,parentType,shorter,derived);
    }
    /// <summary>元数据生成R0来源、全部目标和依赖国家的普通List，测试不手补Entity或Service基类。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool shorter,bool derived)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_ssg_"+nonce[..12];var c=SharedCrossTableSparseGenerationTests.Catalog(type,parentType,shorter,derived,prefix);var source=prefix+"_source";var company=prefix+"_company";var item=prefix+"_item";var roles=new[]{"country","area","place","warehouse"};var leaf=shorter?"areaId":"placeId";var leafField=shorter?"area_id":"place_id";
        c.ConfigTables.Add(new(){Id="10",TableName=item,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="101",ConfigTableId="10",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="102",ConfigTableId="10",ColumnName="name",LogicalType="string"},new(){Id="103",ConfigTableId="10",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="104",ConfigTableId="10",ColumnName="delete_no",LogicalType="datetime"},new(){Id="106",ConfigTableId="10",ColumnName="country_id",LogicalType=type},new(){Id="105",ConfigTableId="2",ColumnName="a_item_id",LogicalType="int64",LinkConfigTableId="10",ControlKind="List",ControlDetail="{\"tableId\":\"10\",\"nameField\":\"name\",\"whereSql\":\"country_id=@formParameters.countryId\"}",Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("1001")}}]);c.ConfigPages.Add(new(){Id="1001",ConfigTableId="10",WhereSql="yz_project_id=@current_yz_project_id",IsSave=1});foreach(var id in new[]{"102","106"})c.ConfigPageColumns.Add(new(){Id="1001"+id,ConfigPageId="1001",ConfigColumnId=id});c.ConfigPageColumns.Add(new(){Id="201105",ConfigPageId="201",ConfigColumnId="105"});
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/shared-cross-sparse-crud/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-sparse-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var auth=new Auth();var observer=new Observer(source,leafField,store);WebApplication? app=null;var created=false;var passed=false;
        object Key(long id)=>type=="string"?Id(id):id;object Parent(long id)=>parentType=="string"?Id(id):id;long Node(int role,int batch=0)=>First+batch*10+role;long Company(int role,int batch=0)=>First+500+batch*10+role;long Item(int batch)=>First+700+batch;
        await Receipt("intent",new{nonce,type,parentType,shorter,derived,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);Assert.DoesNotContain((await schema.InspectAsync(c)).Tables.Single(t=>t.Name==source).Columns,col=>col.Name.EndsWith("_name",StringComparison.Ordinal));
            for(var batch=0;batch<3;batch++)
            {
                for(var role=0;role<4;role++){var row=D("id",Node(role,batch),"yz_project_id",Project,"status",1);if(derived&&role<3){await store.InsertAsync(company,D("id",Company(role,batch),"name",new[]{"国","区","城"}[role]+batch,"yz_project_id",Project));row["company_id"]=Company(role,batch);}else row["name"]=new[]{"国","区","城","仓"}[role]+batch;if(role>0)row[prefix+"_"+(role==1?"country":"area")+"_id"]=Parent(Node(role==1?0:1,batch));await store.InsertAsync(prefix+"_"+roles[role],row);}await store.InsertAsync(item,D("id",Item(batch),"name","候选"+batch,"yz_project_id",Project,"country_id",Key(Node(0,batch))));
            }
            var model=GenerationModel.Create(c,"Example.SharedSparseProbe");var generator=new CSharpGenerator();generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);var sourceType=model.Tables.Single(t=>t.Source.Id=="2").Name;var hand=Path.Combine(directory,"generated/Services/"+sourceType+"Service.cs");var property=shorter?"AreaId":"PlaceId";var newId=Id(Node(shorter?1:2,2));var value=type=="string"?"\""+newId+"\"":newId+"L";var hook="using Metarill.Core.Security;\nusing "+model.Namespace+".Generated.Entities;\nnamespace "+model.Namespace+".Generated.Services;\npublic sealed partial class "+sourceType+"Service\n{\n    /// <summary>原手写钩子可以改变末级，包含未显式登记字段的变化。</summary>\n    partial void BeforeSave("+sourceType+" entity,RuntimeActor actor,bool creating,ISet<string> submittedFields){if(entity.Name is \"hook-new-leaf\" or \"hook-without-submitted\"){entity."+property+"="+value+";if(entity.Name==\"hook-new-leaf\")submittedFields.Add(\""+leafField+"\");}}\n}\n";await File.WriteAllTextAsync(hand,hook);generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);Assert.Equal(hook,await File.ReadAllTextAsync(hand));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(997));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddScoped<IRuntimeAuthorization>(_=>auth);assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> Call(string table,string page,string action,object body){await Receipt("last-request",new{table,page,action,fields=JsonSerializer.SerializeToElement(body).EnumerateObject().Select(p=>p.Name).ToArray()});var url="/api/admin/auto/"+table+"/"+page+"/"+action;using var response=action.StartsWith("detail/",StringComparison.Ordinal)?await client.GetAsync(url):await client.PostAsJsonAsync(url,body);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());}
            async Task<string> Snapshot(){var data=new List<object>();foreach(var table in c.ConfigTables.OrderBy(t=>t.TableName,StringComparer.Ordinal))data.Add(new{table.TableName,rows=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x))});return JsonSerializer.Serialize(data);}
            Dictionary<string,object?> Selection(int a,int b)=>D("name","普通保存",leaf,Id(Node(shorter?1:2,a)),"warehouseId",Id(Node(3,b)),"aItemId",null);
            var request=Selection(0,0);request["aItemId"]=Id(Item(0));var added=Data(await Call(source,"201","add",request));var addedId=added.GetProperty("id").GetString()!;var id=long.Parse(addedId,CultureInfo.InvariantCulture);
            async Task<JsonElement> Update(Dictionary<string,object?> body){body["id"]=addedId;return await Call(source,"201","update",body);}
            async Task<Dictionary<string,object?>> Row()=>Assert.Single(await store.ReadAsync(source,D("id",id)));
            async Task<JsonElement> Detail()=>Data(await Call(source,"201","detail/"+addedId,new{}));
            async Task Verify(int batch,bool a,bool b)
            {
                var read=await Detail();Assert.Equal("国"+batch,read.GetProperty("countryName").GetString());Assert.Equal("区"+batch,read.GetProperty("areaName").GetString());var raw=await Row();Assert.Null(raw["country_id"]);if(b||a&&!shorter)Assert.Null(raw["area_id"]);else Assert.Equal(Id(Node(1,batch)),Convert.ToString(raw["area_id"],CultureInfo.InvariantCulture));if(!shorter)Assert.Equal(a?Id(Node(2,batch)):null,Convert.ToString(raw["place_id"],CultureInfo.InvariantCulture) is {Length:>0} text?text:null);Assert.Equal(b?Id(Node(3,batch)):null,Convert.ToString(raw["warehouse_id"],CultureInfo.InvariantCulture) is {Length:>0} ware?ware:null);
            }
            await Verify(0,true,true);Data(await Update(JsonSerializer.Deserialize<Dictionary<string,object?>>(added.GetRawText())!));var copied=Data(await Call(source,"201","copy/"+addedId,new{}));Assert.Equal("国0",copied.GetProperty("countryName").GetString());Assert.NotEqual(addedId,copied.GetProperty("id").GetString());var before=await Snapshot();Assert.Equal(500,(await Update(Selection(1,2))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());Assert.Equal(500,(await Update(D(leaf,Id(Node(shorter?1:2,1))))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
            auth.DenyWarehouse=true;Data(await Update(D(leaf,Id(Node(shorter?1:2,1)),"aItemId",null)));auth.DenyWarehouse=false;await Verify(1,true,false);Data(await Update(D("aItemId",Id(Item(1)))));await Verify(1,true,false);
            foreach(var marker in new[]{"hook-new-leaf","hook-without-submitted"}){Data(await Update(Selection(0,0)));Data(await Update(D("name",marker,"aItemId",null)));await Verify(2,true,false);}
            Data(await Update(Selection(0,0)));var rawBefore=await Row();Data(await Update(D("name","只改业务字段")));Assert.Equal(JsonSerializer.Serialize(rawBefore.Where(x=>x.Key.EndsWith("_id",StringComparison.Ordinal))),JsonSerializer.Serialize((await Row()).Where(x=>x.Key.EndsWith("_id",StringComparison.Ordinal))));await Verify(0,true,true);
            if(!shorter){Data(await Update(D("placeId",null)));await Verify(0,false,true);}var clear=new[]{"countryId","areaId",leaf,"warehouseId"}.Distinct().ToDictionary(k=>k,k=>(object?)null);Data(await Update(clear));Assert.All((await Row()).Where(x=>x.Key is "country_id" or "area_id" or "place_id" or "warehouse_id").Select(x=>x.Value),Assert.Null);if(type=="string"){Data(await Update(Selection(0,0)));var empty=new[]{"countryId","areaId",leaf,"warehouseId"}.Distinct().ToDictionary(k=>k,k=>(object?)"");Data(await Update(empty));Assert.All((await Row()).Where(x=>x.Key is "country_id" or "area_id" or "place_id" or "warehouse_id").Select(x=>x.Value),Assert.Null);}
            Data(await Update(Selection(0,0)));before=await Snapshot();Assert.Equal(500,(await Update(D(leaf,Id(Node(shorter?1:2,2)),"name","source-tail-failure","aItemId",null))).GetProperty("code").GetInt32());Assert.True(observer.SawCoordinated);Assert.Equal(before,await Snapshot());
            // 当前测试账号不能创建触发器；在真实DML结束、生成核验开始前使用同一连接／事务定点改写。
            // 该探针证明实际数据库核验与回滚，不宣称完成数据库触发器验收。
            var tamperMode="";var tamperCount=0;
            session.Orm.Aop.CommandAfter+=(_,args)=>
            {
                if(tamperMode.Length==0||!args.Command.CommandText.TrimStart().StartsWith("UPDATE",StringComparison.OrdinalIgnoreCase)||!args.Command.CommandText.Contains(session.Provider.QuoteIdentifier(source),StringComparison.Ordinal))return;
                var mode=tamperMode;tamperMode="";Assert.NotNull(args.Command.Transaction);using var command=args.Command.Connection!.CreateCommand();command.Transaction=args.Command.Transaction;
                command.CommandText=mode=="raw"?"UPDATE "+session.Provider.QuoteIdentifier(source)+" SET warehouse_id=@value WHERE id=@id":"UPDATE "+session.Provider.QuoteIdentifier(prefix+"_area")+" SET "+session.Provider.QuoteIdentifier(prefix+"_country_id")+"=@value WHERE id=@id";
                var valueParameter=command.CreateParameter();valueParameter.ParameterName="value";valueParameter.Value=Key(mode=="raw"?Node(3,1):Node(0,1));command.Parameters.Add(valueParameter);var idParameter=command.CreateParameter();idParameter.ParameterName="id";idParameter.Value=mode=="raw"?id:Node(1);command.Parameters.Add(idParameter);Assert.Equal(1,command.ExecuteNonQuery());tamperCount++;
            };
            before=await Snapshot();tamperMode="raw";var rawFailure=await Update(D("name","raw-rewrite"));Assert.Equal(500,rawFailure.GetProperty("code").GetInt32());Assert.Contains("共享稀疏ID",ErrorText(rawFailure));Assert.Equal(1,tamperCount);Assert.Equal(before,await Snapshot());tamperMode="path";var pathFailure=await Update(D("name","path-rewrite",leaf,Id(Node(shorter?1:2))));Assert.Equal(500,pathFailure.GetProperty("code").GetInt32());Assert.Contains("共享实际路径",ErrorText(pathFailure));Assert.Equal(2,tamperCount);Assert.Equal(before,await Snapshot());
            // 改名及重挂在R0中实时读取，不把祖先或名称回填到来源。
            auth.DenyConsumerWrites=true;Data(await Call(derived?company:prefix+"_country",derived?"601":"301","update",D("id",Id(derived?Company(0):Node(0)),"name","目标实时名称")));Assert.Equal("目标实时名称",(await Detail()).GetProperty("countryName").GetString());Assert.Null((await Row())["country_id"]);auth.DenyConsumerWrites=false;
            passed=true;await Receipt("verified",new{nonce,type,parentType,shorter,derived,passed,realGeneratedHttp=true,realMySql=true,twoFrontiers=!shorter,sharedPrimary=shorter,oneGroupPacking=true,partialUpdate=true,wholeEntityEcho=true,copy=true,hookWithAndWithoutSubmitted=true,dependentListAfterCoordination=true,nullAndEmpty=true,lateObserverRollback=true,sameTransactionRawRewriteRejected=true,sameTransactionActualPathRewriteRejected=true,databaseTriggersVerified=false,realTimeNames=true,generatedCrud=true,formalWeb=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,shorter,derived,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>文件／业务观察者在同事务存储核验后运行，失败须回滚原始独立末端。</summary>
    private sealed class Observer(string source,string leaf,SqlRowStore store):IRecordMutationObserver
    {
        internal bool SawCoordinated;
        /// <summary>故障标记只用于本轮随机样本。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext c,CancellationToken token=default){if(c.TableName==source&&c.SubmittedFields.Values.Any(v=>Equals(v,"source-tail-failure"))){var row=Assert.Single(await store.ReadAsync(source,D("id",c.RecordId)));SawCoordinated=row["country_id"] is null&&row["warehouse_id"] is null&&Convert.ToString(row[leaf],CultureInfo.InvariantCulture)==Id(First+20+(leaf=="area_id"?1:2));throw new InvalidOperationException("共享R0尾部回滚探针");}}
    }
    /// <summary>当前身份只由宿主建立；目标改名不借来源页面写权限。</summary>
    private sealed class Auth:IRuntimeAuthorization
    {
        internal bool DenyWarehouse,DenyConsumerWrites;
        public RuntimeActor RequireActor()=>new(997,"共享R0实库",false,Id(Project),"5");
        public void RequirePagePermission(string page,string operation){if(DenyWarehouse&&page=="801"&&operation=="list")throw new UnauthorizedAccessException("无仓库候选权限");if(DenyConsumerWrites&&page=="201"&&operation is "create" or "update" or "delete")throw new UnauthorizedAccessException("无来源写权限");}
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
