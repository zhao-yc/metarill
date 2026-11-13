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

/// <summary>共享R0逻辑父范围的实际生成接口与隔离MySQL；固定授权器不替代正式账号、完整包和浏览器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossParentMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    [StandaloneExportMySqlFact]public Task String_string()=>Run("string","string");
    [StandaloneExportMySqlFact]public Task String_long()=>Run("string","int64");
    [StandaloneExportMySqlFact]public Task Long_string()=>Run("int64","string");
    [StandaloneExportMySqlFact]public Task Long_long()=>Run("int64","int64");
    [StandaloneExportMySqlFact]public Task Short_string()=>Run("string","int64",true);
    [StandaloneExportMySqlFact]public Task Short_long()=>Run("int64","string",true);
    [StandaloneExportMySqlFact]public Task Derived_targets()=>Run("string","int64",false,true);
    /// <summary>共享全局根／中／两个末端／全链及短控件在全部五个父页执行相同真实生成流程。</summary>
    [StandaloneExportMySqlFact]public Task Global_root()=>Run("string","string",globalMask:1);
    [StandaloneExportMySqlFact]public Task Global_middle()=>Run("string","int64",globalMask:2,noProject:true);
    [StandaloneExportMySqlFact]public Task Global_place()=>Run("int64","string",globalMask:4);
    [StandaloneExportMySqlFact]public Task Global_warehouse()=>Run("int64","int64",globalMask:8,noProject:true);
    [StandaloneExportMySqlFact]public Task Global_all()=>Run("int64","int64",globalMask:15,noProject:true);
    [StandaloneExportMySqlFact]public Task Global_short_root()=>Run("string","int64",true,globalMask:1);
    [StandaloneExportMySqlFact]public Task Global_short_all()=>Run("int64","string",true,globalMask:11,noProject:true);
    /// <summary>固定实例pin、数据库与账号配置核对后才建立本轮随机表。</summary>
    private static async Task Run(string type,string parentType,bool shorter=false,bool derived=false,int globalMask=0,bool noProject=false)
    {
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=doc.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("跨表缓存实库只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==options.ExpectedDatabase&&rows.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&rows.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建缓存样本。");}await Exercise(options,type,parentType,shorter,derived,globalMask,noProject);
    }
    /// <summary>元数据生成共享R0、固定目标及物理父外键，五个父页面不手补生成基类。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool shorter,bool derived,int globalMask,bool noProject)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_spr_"+nonce[..12];var c=SharedCrossParentGenerationTests.Catalog(prefix,type,parentType,shorter,derived);var source=prefix+"_source";var company=prefix+"_company";var roles=new[]{"country","area","place","warehouse"};var leaf=shorter?"areaId":"placeId";var leafField=shorter?"area_id":"place_id";
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";
        GlobalSharedCrossGenerationTests.ApplyGlobals(c,globalMask,noProject);
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/shared-cross-parent/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-parent-http");var directory=Path.Combine(boundary,nonce);var names=c.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).ToArray();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,c);var auth=new Auth();var observer=new Observer(source);WebApplication? app=null;var created=false;var passed=false;
        object Parent(long id)=>parentType=="string"?Id(id):id;long Node(int role,int batch=0)=>First+batch*10+role;long Company(int role,int batch=0)=>First+500+batch*10+role;
        await Receipt("intent",new{nonce,type,parentType,shorter,derived,globalMask,noProject,names,database=options.ExpectedDatabase,instanceVerified=true});
        try
        {
            Assert.Empty((await schema.InspectAsync(c)).Tables);var ddl=await schema.PreviewAsync(c);Assert.Equal(names.Length,ddl.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(ddl)).Succeeded);Assert.Empty((await schema.PreviewAsync(c)).Changes);Assert.DoesNotContain((await schema.InspectAsync(c)).Tables.Single(t=>t.Name==source).Columns,col=>col.Name.EndsWith("_name",StringComparison.Ordinal));
            for(var batch=0;batch<4;batch++)
            {
                for(var role=0;role<4;role++){var row=D("id",Node(role,batch),"yz_project_id",batch==3?Project+1:Project,"status",1);if(derived&&role<3){await store.InsertAsync(company,D("id",Company(role,batch),"name",new[]{"国","区","城"}[role]+batch,"yz_project_id",batch==3?Project+1:Project));row["company_id"]=Company(role,batch);}else row["name"]=new[]{"国","区","城","仓"}[role]+batch;if(role>0)row[prefix+"_"+(role==1?"country":"area")+"_id"]=Parent(Node(role==1?0:1,batch));if((globalMask&(1<<role))!=0){if(noProject)row.Remove("yz_project_id");else row["yz_project_id"]=Project+20;}await store.InsertAsync(prefix+"_"+roles[role],row);}
            }
            var model=GenerationModel.Create(c,"Example.SharedSparseProbe");var generator=new CSharpGenerator();generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);var sourceType=model.Tables.Single(t=>t.Source.Id=="2").Name;var hand=Path.Combine(directory,"generated/Services/"+sourceType+"Service.cs");var property=shorter?"AreaId":"PlaceId";var newId=Id(Node(shorter?1:2,2));var value=type=="string"?"\""+newId+"\"":newId+"L";var hook="using Metarill.Core.Security;\nusing "+model.Namespace+".Generated.Entities;\nnamespace "+model.Namespace+".Generated.Services;\npublic sealed partial class "+sourceType+"Service\n{\n    /// <summary>手写钩子改变父路径，登记或静默变化都不能绕过页面父归属。</summary>\n    partial void BeforeSave("+sourceType+" entity,RuntimeActor actor,bool creating,ISet<string> submittedFields){if(entity.Name is \"钩子越界\" or \"静默越界\"){entity."+property+"="+value+";if(entity.Name==\"钩子越界\")submittedFields.Add(\""+leafField+"\");}}\n    /// <summary>删除钩子也不得伪造稀疏末端。</summary>\n    partial void BeforeDelete("+sourceType+" entity,RuntimeActor actor){if(entity.Name==\"删除钩子越界\")entity.WarehouseId="+(type=="string"?"\""+Id(Node(3,1))+"\"":Id(Node(3,1))+"L")+";}\n}\n";await File.WriteAllTextAsync(hand,hook);generator.Generate(c,Path.Combine(directory,"generated"),model.Namespace);Assert.Equal(hook,await File.ReadAllTextAsync(hand));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(997));builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddScoped<IRuntimeAuthorization>(_=>auth);assembly.GetType(model.Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(x=>x.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(ctx,next)=>{try{await next();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or UnauthorizedAccessException or PageImportException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(40)};
            async Task<JsonElement> Call(string page,string action,object body,string? parent=null)
            {
                await Receipt("last-request",new{page,action,parent});var url="/api/admin/auto/"+source+"/"+page+"/"+(action=="remove"?"":action)+(parent is null?"":"?parentId="+Uri.EscapeDataString(parent));using var request=new HttpRequestMessage(action=="remove"?HttpMethod.Delete:action.StartsWith("detail/",StringComparison.Ordinal)?HttpMethod.Get:HttpMethod.Post,url);if(request.Method!=HttpMethod.Get)request.Content=JsonContent.Create(body);using var response=await client.SendAsync(request);return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            }
            async Task<string> Snapshot(){var rows=new Dictionary<string,object>();foreach(var table in c.ConfigTables)rows[table.TableName]=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x)).ToArray();return JsonSerializer.Serialize(rows);}
            Dictionary<string,object?> Select(int group)=>D("name","共享父范围",leaf,Id(Node(shorter?1:2,group)),"warehouseId",Id(Node(3,group)),"ownerCountryId",Id(Node(0,group)));
            foreach(var (page,role) in new[]{("202",0),("203",1),("204",shorter?1:2),("205",3),("206",0)})
            {
                var parent=Id(Node(role));var wrong=Id(Node(role,1));var added=Data(await Call(page,"add",Select(0),parent));var id=added.GetProperty("id").GetString()!;var number=long.Parse(id,CultureInfo.InvariantCulture);
                async Task<JsonElement> Update(Dictionary<string,object?> values,string? context=null){values["id"]=id;return await Call(page,"update",values,context??parent);}
                async Task<Dictionary<string,object?>> Raw()=>Assert.Single(await store.ReadAsync(source,D("id",number)));
                async Task Verify(bool a=true,bool b=true){var detail=Data(await Call(page,"detail/"+id,new{},parent));Assert.Equal("国0",detail.GetProperty("countryName").GetString());Assert.Equal("区0",detail.GetProperty("areaName").GetString());var raw=await Raw();Assert.Null(raw["country_id"]);Assert.Equal(shorter&&a&&!b?Id(Node(1)):null,raw["area_id"] is{} area?Convert.ToString(area,CultureInfo.InvariantCulture):null);if(!shorter)Assert.Equal(a?Id(Node(2)):null,raw["place_id"] is{} place?Convert.ToString(place,CultureInfo.InvariantCulture):null);Assert.Equal(b?Id(Node(3)):null,raw["warehouse_id"] is{} ware?Convert.ToString(ware,CultureInfo.InvariantCulture):null);Assert.DoesNotContain(raw.Keys,k=>k is "country_name" or "area_name" or "place_name" or "warehouse_name");}
                await Verify();var before=await Snapshot();Assert.Equal(500,(await Call(page,"detail/"+id,new{},wrong)).GetProperty("code").GetInt32());Assert.Equal(500,(await Update(D("name","错误父上下文"),wrong)).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
                auth.DenyWarehouse=true;Data(await Update(D("name","仅改业务字段")));auth.DenyWarehouse=false;await Verify();Data(await Update(JsonSerializer.Deserialize<Dictionary<string,object?>>(added.GetRawText())!));await Verify();
                before=await Snapshot();Assert.Equal(500,(await Update(Select(1))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());Assert.Equal(500,(await Update(new[]{"countryId","areaId",leaf,"warehouseId"}.Distinct().ToDictionary(k=>k,_=>(object?)null))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
                foreach(var marker in new[]{"钩子越界","静默越界"}){Assert.Equal(500,(await Update(D("name",marker))).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());}
                if(role!=3){Data(await Update(D("warehouseId",null)));await Verify(true,false);Data(await Update(Select(0)));await Verify();}else if(!shorter){Data(await Update(D(leaf,null)));await Verify(false,true);Data(await Update(Select(0)));await Verify();}
                var copy=Data(await Call(page,"copy/"+id,new{},parent)).GetProperty("id").GetString()!;Data(await Call(page,"remove",new{id=copy},parent));before=await Snapshot();observer.SawScoped=false;var lateFailure=await Update(D("name","晚期回滚"));Assert.Equal(500,lateFailure.GetProperty("code").GetInt32());Assert.Contains("共享父范围尾部回滚探针",ErrorText(lateFailure));Assert.True(observer.SawScoped);Assert.Equal(before,await Snapshot());
                Data(await Update(D("name","删除钩子越界")));before=await Snapshot();Assert.Equal(500,(await Call(page,"remove",new{id},parent)).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());Data(await Update(D("name","恢复删除")));Data(await Call(page,"remove",new{id},parent));Assert.Equal(500,(await Call(page,"detail/"+id,new{},parent)).GetProperty("code").GetInt32());
                before=await Snapshot();var foreign=await Call(page,"add",Select(3),Id(Node(role,3)));if((globalMask&(shorter?11:15))==(shorter?11:15)){var foreignId=Data(foreign).GetProperty("id").GetString()!;var own=Assert.Single(await store.ReadAsync(source,D("id",long.Parse(foreignId,CultureInfo.InvariantCulture))));Assert.Equal(Id(Project),SqlRowStore.Text(own,"yz_project_id"));Data(await Call(page,"remove",new{id=foreignId},Id(Node(role,3))));before=await Snapshot();}else{Assert.Equal(500,foreign.GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());}Assert.Equal(500,(await Call(page,"add",Select(0),"0"+parent)).GetProperty("code").GetInt32());Assert.Equal(before,await Snapshot());
            }
            // 同事务把共同地区改挂到另一有效国家，最终核验必须发现实际父范围已经越界。
            var finalId=Data(await Call("202","add",Select(0),Id(Node(0)))).GetProperty("id").GetString()!;var tamper=false;var observed=false;session.Orm.Aop.CommandAfter+=(_,args)=>{if(!tamper||!args.Command.CommandText.TrimStart().StartsWith("UPDATE",StringComparison.OrdinalIgnoreCase)||!args.Command.CommandText.Contains(session.Provider.QuoteIdentifier(source),StringComparison.Ordinal))return;tamper=false;using var command=args.Command.Connection!.CreateCommand();command.Transaction=args.Command.Transaction;command.CommandText="UPDATE "+session.Provider.QuoteIdentifier(prefix+"_area")+" SET "+session.Provider.QuoteIdentifier(prefix+"_country_id")+"=@parent WHERE id=@id";var p=command.CreateParameter();p.ParameterName="parent";p.Value=Parent(Node(0,1));command.Parameters.Add(p);var q=command.CreateParameter();q.ParameterName="id";q.Value=Node(1);command.Parameters.Add(q);Assert.Equal(1,command.ExecuteNonQuery());observed=true;};var unchanged=await Snapshot();tamper=true;Assert.Equal(500,(await Call("202","update",D("id",finalId,"name","数据库重挂"),Id(Node(0)))).GetProperty("code").GetInt32());Assert.True(observed);Assert.Equal(unchanged,await Snapshot());
            passed=true;await Receipt("verified",new{nonce,type,parentType,shorter,derived,globalMask,noProject,passed,realGeneratedHttp=true,realMySql=true,parentPages=5,groupPacking=true,omittedGroup=true,wholeEntityEcho=true,sourceScopeEscapeRejected=true,ordinaryPhysicalAndLogicalParent=true,partialClear=true,hookWithAndWithoutSubmitted=true,copyAndDelete=true,deleteHookRejected=true,lateObserverRollback=true,actualParentRewriteRejected=true,foreignProjectAndCanonicalParent=true,formalWeb=false,browser=false});
        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,shorter,derived,globalMask,noProject,error=error.GetType().Name,error.Message,error.StackTrace});throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var actual=await new SchemaService(cleanup).InspectAsync(c);if(actual.Tables.Count>0){Assert.Empty((await new SchemaService(cleanup).PreviewAsync(c)).Changes);var rows=new Dictionary<string,string>();foreach(var name in names)rows[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x)));await Receipt("cleanup-before",new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=rows.ToDictionary(x=>x.Key,x=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(x.Value))))});using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(c)));foreach(var name in names)Assert.Equal(rows[name],JsonSerializer.Serialize((await new SqlRowStore(check,c).ReadIncludingDeletedAsync(name)).OrderBy(x=>SqlRowStore.Id(x))));foreach(var name in names)await Sql(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var connection=cleanup.Provider.CreateConnection(options);await connection.OpenAsync();foreach(var name in names){await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=cmd.CreateParameter();p.ParameterName="name";p.Value=name;cmd.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,names,passed,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
        /// <summary>意图、首因和清理记录只在私有目录保存。</summary>
        Task Receipt(string name,object payload)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(payload)+"\n");
    }
    /// <summary>文件／业务观察者在同事务存储核验后运行，失败须回滚原始独立末端。</summary>
    private sealed class Observer(string source):IRecordMutationObserver
    {
        internal bool SawScoped;
        /// <summary>只在生成器真实DML及父归属核验之后抛出，确认原事务整笔回滚。</summary>
        public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken token=default){if(context.TableName==source&&context.SubmittedFields.Values.Any(v=>Equals(v,"晚期回滚"))){SawScoped=true;throw new InvalidOperationException("共享父范围尾部回滚探针");}return Task.CompletedTask;}
    }
    /// <summary>当前身份只由宿主建立；候选动作权限和已存父归属分别校验。</summary>
    private sealed class Auth:IRuntimeAuthorization
    {
        internal bool DenyWarehouse;
        public RuntimeActor RequireActor()=>new(997,"共享R0实库",false,Id(Project),"5");
        public void RequirePagePermission(string page,string operation){if(DenyWarehouse&&page=="801"&&operation=="list")throw new UnauthorizedAccessException("无仓库候选权限");}
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
