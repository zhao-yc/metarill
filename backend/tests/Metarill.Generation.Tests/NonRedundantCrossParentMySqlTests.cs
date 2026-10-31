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

/// <summary>真实生成HTTP与MySQL验证逻辑父范围、守卫、稀疏值、导入及当前路径锁。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantCrossParentMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>隐式各级ID不增加表头，四种来源／父键组合复用原空值和标量导出协议。</summary>
    [StandaloneExportMySqlFact]
    public async Task Logical_parent_mutations_keep_sparse_storage_and_current_scopes()
    {var options=await Options();foreach(var type in new[]{"string","int64"})foreach(var parent in new[]{"string","int64"})await Exercise(options,type,parent,false);}
    /// <summary>显式祖先按原模板保留，字符串祖先与int64末级亦不依赖JSON属性顺序。</summary>
    [StandaloneExportMySqlFact]
    public async Task Mixed_slots_and_physical_delete_keep_logical_parent_proofs()
    {var options=await Options();await Exercise(options,"string","int64",true);await Exercise(options,"int64","string",true);await Exercise(options,"int64","int64",false,true);}
    /// <summary>任何DDL之前独立核对实例、端口、主机及隔离库，不使用本机Docker。</summary>
    private static async Task<DatabaseOptions> Options()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("只允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");return options;
    }
    /// <summary>先编译真实产物，再显式建四张随机表；任何一行失败均比较全部物理数据。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType,bool explicitAncestor,bool physicalDelete=false)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrcp_"+nonce[..12];var catalog=NonRedundantCrossParentTests.Catalog(prefix,type,parentType,explicitAncestor,physicalDelete);var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/nonredundant-cross-table-parent-generation/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-parent-mysql");var directory=Path.Combine(boundary,nonce);var source=prefix+"_source";var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var observer=new Observer();WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var guard=new Guard(options,prefix);await Receipt("intent",new{nonce,type,parentType,explicitAncestor,physicalDelete,names,database=options.ExpectedDatabase,verified=false});
        try
        {
            var entity=GenerationModel.Pascal(source);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossParentProbe");var countryType=explicitAncestor?"string":type;await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.CrossParentProbe.Generated.Services;
public partial class {{entity}}Service
{
    /// <summary>真实手写规则仍在关系归一化之前，import不能跳过数据归属校验。</summary>
    partial void BeforeSave(Example.CrossParentProbe.Generated.Entities.{{entity}} value,RuntimeActor actor,bool creating,ISet<string> submitted)
    {
        if(value.Name=="钩子越界"){value.PlaceId={{(type=="string"?"\""+(First+12)+"\"":(First+12)+"L")}};submitted.Add("place_id");}
        if(value.Name=="静默改位")value.PlaceId={{(type=="string"?"\""+(First+12)+"\"":(First+12)+"L")}};
    }
    /// <summary>删除钩子不能把临时关系值带给通知，或改变当前父归属。</summary>
    partial void BeforeDelete(Example.CrossParentProbe.Generated.Entities.{{entity}} value,RuntimeActor actor)
    {
        if(value.Name=="删除改位")value.PlaceId={{(type=="string"?"\""+(First+12)+"\"":(First+12)+"L")}};
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var initial=await schema.PreviewAsync(catalog);Assert.Equal(4,initial.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(initial)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==source).Columns,column=>column.Name.EndsWith("_name",StringComparison.Ordinal));
            object? Key(string field,long? value)=>value is null?null:(explicitAncestor&&field=="country_id"||type=="string")?value.Value.ToString(CultureInfo.InvariantCulture):value;
            // 只向本nonce样本写固定种子，不借UI权限或生产配置制造验收数据。
            async Task Target(int level,long id,string name,long? parent=null,long project=Project){var levels=new[]{"country","area","place"};var parentField=level==0?"":",`"+prefix+"_"+levels[level-1]+"_id`";await Execute(session,"INSERT INTO `"+prefix+"_"+levels[level]+"` (id,name,yz_project_id,status"+parentField+") VALUES (@id,@name,@project,1"+(level==0?"":",@parent")+")",("id",id),("name",name),("project",project),("parent",parentType=="string"?(object?)parent?.ToString(CultureInfo.InvariantCulture):parent));}
            foreach(var branch in new[]{0,10}){await Target(0,First+branch,branch==0?"国甲":"国乙");await Target(1,First+branch+1,branch==0?"区甲":"区乙",First+branch);await Target(2,First+branch+2,branch==0?"城甲":"城乙",First+branch+1);}await Target(2,First+32,"外项目叶",First+11,Project+1);
            async Task Seed(int index,string name,int depth,long? selected,int secondDepth,long? second,long project=Project,int status=1,DateTime? deleted=null){var fields=new[]{"country_id","area_id","place_id","secondary_country_id","secondary_area_id","secondary_place_id"};var values=fields.Select((field,position)=>(field,Key(field,position<3?position==depth?selected:null:position-3==secondDepth?second:null))).ToArray();await Execute(session,"INSERT INTO `"+source+"` (id,name,yz_project_id,status,amount"+(physicalDelete?"":",delete_no")+","+string.Join(",",fields.Select(field=>"`"+field+"`"))+") VALUES (@id,@name,@project,@status,1"+(physicalDelete?"":",@deleted")+","+string.Join(",",fields.Select(field=>"@"+field))+")",new[]{("id",(object?)(First+100+index)),("name",name),("project",project),("status",status),("deleted",deleted)}.Concat(values).ToArray());}
            await Seed(1,"甲",2,First+2,0,First+10);await Seed(2,"乙",1,First+1,2,First+12);await Seed(3,"根",0,First,1,First+11);await Seed(4,"乙组",2,First+12,-1,null);await Seed(5,"外项目",2,First+2,-1,null,project:Project+1);await Seed(6,"条件隐藏",2,First+2,-1,null,status:0);if(!physicalDelete)await Seed(7,"逻辑删除",2,First+2,-1,null,deleted:new DateTime(2026,1,1));
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);var exportRoot=Path.Combine(directory,"exports");var exports=new ExcelPageExportService(new(){RootDirectory=exportRoot,MaxRows=100});builder.Services.AddSingleton<IPageExportService>(exports);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(guard);builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(954));builder.Services.AddScoped<IRuntimeAuthorization,Authorization>();assembly.GetType("Example.CrossParentProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(settings=>settings.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or PageImportException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();app.MapGet(ExportDownloadAccess.DownloadPath,async(HttpContext context)=>{if(!ExportDownloadAccess.CanUseGrant(context.Request))return Results.Unauthorized();var file=await exports.OpenDownloadAsync(context.Request.Query["exportId"]!);return Results.Stream(file.Stream,file.ContentType,file.FileName);});await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/201/";
            // 原主子页面在SQL分页之前按逻辑祖先筛选，公开VO仍恢复完整路径。
            var original=(First+101).ToString();var rootId=First.ToString();var midId=(First+1).ToString();var leafId=(First+2).ToString();
            Assert.Equal("3",Data(await Post("202","page",new{parentId=rootId,pageSize=100})).GetProperty("total").GetRawText().Trim('"'));
            Assert.Equal("2",Data(await Post("203","page",new{parentId=midId,pageSize=100})).GetProperty("total").GetRawText().Trim('"'));
            Assert.Equal("1",Data(await Post("204","page",new{parentId=leafId,pageSize=100})).GetProperty("total").GetRawText().Trim('"'));
            var selected=Data(await Post("202","selection/selected",new{parentId=rootId,selectedIds=new[]{original}}));Assert.Equal("国甲/区甲/城甲",Assert.Single(selected.EnumerateArray()).GetProperty("placeIdDisplayText").GetString());
            var before=await State();Assert.Equal(500,(await Post("202","selection/selected",new{parentId=(First+10).ToString(),selectedIds=new[]{original}})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            // 整项省略不填物理祖先，不借目标list；只改标量也必须持当前父路径锁。
            client.DefaultRequestHeaders.Add("denyTargets","1");Data(await Post("202","update",new{id=original,name="只改名称"},rootId));await Slots("只改名称",2,First+2,0,First+10);client.DefaultRequestHeaders.Remove("denyTargets");
            // 父上下文由页面提供，新建时覆盖同级输入；向另一分支的末层不能越过原父范围。
            foreach(var (page,parent,depth,node) in new[]{("202",rootId,0,First),("203",midId,1,First+1),("204",leafId,2,First+2)})
            {
                var createdRow=Data(await Post(page,"add",new{name="创建"+page},parent));await Slots("创建"+page,depth,node,-1,null);
                var copied=Data(await Post(page,"copy/"+original,new{},parent));Assert.Equal("国甲/区甲/城甲",copied.GetProperty("placeIdDisplayText").GetString());var key=copied.GetProperty("id").GetString()!;
                await Delete(page,key,parent,true);if(physicalDelete){using var independent=new DatabaseSession(options);Assert.DoesNotContain(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source),row=>SqlRowStore.Id(row)==long.Parse(key,CultureInfo.InvariantCulture));}
            }
            var dual=Data(await Post("205","add",new{name="双父同范围",placeId=leafId,secondaryPlaceId=leafId},rootId));await Slots("双父同范围",2,First+2,2,First+2);Assert.Equal("1",Data(await Post("205","page",new{parentId=rootId,pageSize=100})).GetProperty("total").GetRawText().Trim('"'));
            foreach(var payload in new object[]{new{id=original,placeId=(First+12).ToString()},new{id=original,name="钩子越界"},new{id=original,name="静默改位"},new{id=original,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null}})
            {before=await State();Assert.Equal(500,(await Post("202","update",payload,rootId)).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            before=await State();Assert.Equal(500,(await Post("205","update",new{id=dual.GetProperty("id").GetString(),secondaryPlaceId=(First+12).ToString()},rootId)).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            foreach(var bad in new[]{"0","0"+First,(First+10).ToString(),(First+9999).ToString()})
            {before=await State();Assert.Equal(500,(await Post("202","update",new{id=original,name="错误父"},bad)).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("202","copy/"+original,new{},bad)).GetProperty("code").GetInt32());await Delete("202",original,bad,false);Assert.Equal(before,await State());}
            // 原Excel空批次仍检真实父，首列覆盖在逻辑父范围内找原对象；普通动作与目标list均撤销。
            client.DefaultRequestHeaders.Add("denyOrdinary","1");client.DefaultRequestHeaders.Add("denyTargets","1");Data(await Import("202",rootId,Row("名称","只改名称","金额",42)));await Slots("只改名称",2,First+2,0,First+10);Data(await Import("202",rootId,Row("名称","导入新根","级联",null)));await Slots("导入新根",0,First,-1,null);
            Data(await Import("203",midId,Row("名称","导入新中","级联","")));await Slots("导入新中",1,First+1,-1,null);Data(await Import("204",leafId,Row("名称","导入新叶")));await Slots("导入新叶",2,First+2,-1,null);Data(await Import("202",rootId));
            before=await State();Assert.Equal(500,(await Import("202",(First+9999).ToString())).GetProperty("code").GetInt32());Assert.Equal(500,(await Import("202",rootId,Row("名称","前行","级联",null),Row("名称","通知故障","级联",null))).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Import("202",rootId,Row("名称","非空路径","级联","国甲/区甲/城甲"))).GetProperty("code").GetInt32());Assert.Equal(before,await State());client.DefaultRequestHeaders.Remove("denyTargets");client.DefaultRequestHeaders.Remove("denyOrdinary");
            // 删除前的物理值与逻辑范围都复核，实际DELETE和软删使用同一入口。
            var badDelete=Data(await Post("202","add",new{name="删除改位",placeId=leafId},rootId));before=await State();await Delete("202",badDelete.GetProperty("id").GetString()!,rootId,false);Assert.Equal(before,await State());
            // 旧RR快照外部换父，当前写范围拒绝原父；守卫内同事务换父也必须在写后拒绝并回滚。
            guard.ChangeSnapshot=true;before=await State();try{Assert.Equal(500,(await Post("202","update",new{id=original,name="RR旧父拒绝"},rootId)).GetProperty("code").GetInt32());}finally{await Move(First+1);}Assert.True(guard.SnapshotChecked);Assert.Equal(before,await State());
            guard.MoveBeforeWrite=true;before=await State();await Delete("202",original,rootId,false);Assert.Equal(before,await State());
            guard.MoveBeforeWrite=true;before=await State();Assert.Equal(500,(await Post("202","update",new{id=original,name="守卫换父拒绝"},rootId)).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            // 模拟数据库在普通标量DML后改写稀疏位；没有提交关系也不能绕过写后复核。
            var rewrite=true;session.Orm.Aop.CommandAfter+=(_,args)=>{if(rewrite&&args.Command.CommandText.TrimStart().StartsWith("UPDATE",StringComparison.OrdinalIgnoreCase)&&args.Command.CommandText.Contains(source,StringComparison.Ordinal)){rewrite=false;using var command=args.Command.Connection!.CreateCommand();command.Transaction=args.Command.Transaction;command.CommandText="UPDATE `"+source+"` SET country_id=@root WHERE id=@id";var rootParam=command.CreateParameter();rootParam.ParameterName="root";rootParam.Value=Key("country_id",First)!;command.Parameters.Add(rootParam);var key=command.CreateParameter();key.ParameterName="id";key.Value=long.Parse(original,CultureInfo.InvariantCulture);command.Parameters.Add(key);command.ExecuteNonQuery();}};
            before=await State();Assert.Equal(500,(await Post("202","update",new{id=original,name="数据库静默改位"},rootId)).GetProperty("code").GetInt32());Assert.False(rewrite);Assert.Equal(before,await State());
            // 来源及选中三层实际持锁；无关分支仍可改，观察者只见真实稀疏值。
            var pending=Post("202","update",new{id=original,name="锁持有"},rootId);
            try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}foreach(var (table,id) in new[]{(source,long.Parse(original,CultureInfo.InvariantCulture)),(prefix+"_country",First),(prefix+"_area",First+1),(prefix+"_place",First+2)}){await using var command=independent.CreateCommand();command.CommandText="UPDATE `"+table+"` SET name=@name WHERE id=@id";command.Parameters.AddWithValue("name","竞争修改");command.Parameters.AddWithValue("id",id);Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>command.ExecuteNonQueryAsync())).Number);}
                await using var unrelated=await independent.BeginTransactionAsync();foreach(var (table,id) in new[]{(prefix+"_country",First+10),(prefix+"_area",First+11),(prefix+"_place",First+12)}){await using var command=independent.CreateCommand();command.Transaction=unrelated;command.CommandText="UPDATE `"+table+"` SET name=@name WHERE id=@id";command.Parameters.AddWithValue("name","无关分支仍可改");command.Parameters.AddWithValue("id",id);Assert.Equal(1,await command.ExecuteNonQueryAsync());}await unrelated.RollbackAsync();}
            finally{observer.Release.TrySetResult();await pending;}Data(await pending);await Slots("锁持有",2,First+2,0,First+10);Assert.True(observer.CheckedSparse);Assert.True(guard.CheckedHints);Assert.True(guard.CheckedValidation);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            client.DefaultRequestHeaders.Add("denySource","1");before=await State();Assert.Equal(500,(await Post("202","update",new{id=original,name="撤权"},rootId)).GetProperty("code").GetInt32());Assert.Equal(before,await State());client.DefaultRequestHeaders.Remove("denySource");
            verified=true;await Receipt("verified",new{nonce,type,parentType,explicitAncestor,physicalDelete,names,verified,realGeneratedHttp=true,realMySql=true,logicalParentCrud=true,logicalParentImport=true,type2Selected=true,sparsePhysicalStorage=true,omission=true,privatePhysicalGuards=true,oldRrParentMove=true,guardParentMoveRollback=true,databaseRewriteRollback=true,deleteHookRejected=true,actualSourceAndThreeTargetLocks=true,unrelatedBranchWritable=true,batchRollback=true,nonemptyPolicyStillGated=true,formalAuthentication=false,browser=false});
            // 所有比较均用独立来源连接，不能用VO冒充物理层证明。
            async Task Slots(string name,int depth,long? id,int secondDepth,long? second){using var independent=new DatabaseSession(options);var row=Assert.Single(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source),row=>Equals(row["name"],name)&&Convert.ToInt64(row["yz_project_id"],CultureInfo.InvariantCulture)==Project&&(!row.TryGetValue("delete_no",out var deleted)||deleted is null));foreach(var (fields,selectedNode,value) in new[]{(new[]{"country_id","area_id","place_id"},depth,id),(new[]{"secondary_country_id","secondary_area_id","secondary_place_id"},secondDepth,second)})for(var level=0;level<fields.Length;level++){if(level==selectedNode)Assert.Equal(value!.Value.ToString(CultureInfo.InvariantCulture),Convert.ToString(row[fields[level]],CultureInfo.InvariantCulture));else Assert.Null(row[fields[level]]);}Assert.DoesNotContain(row.Keys,key=>key.EndsWith("_name",StringComparison.Ordinal));}
            async Task<JsonElement> Post(string page,string route,object body,string? parent=null){var uri="/api/admin/auto/"+source+"/"+page+"/"+route+(parent is null?"":"?parentId="+Uri.EscapeDataString(parent));return JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(uri,body)).Content.ReadAsStringAsync());}
            Task<JsonElement> Import(string page,string parent,params Dictionary<string,object?>[] rows)=>Post(page,"importDatas"+page,new{parentId=parent,requestJson=rows});
            async Task Delete(string page,string id,string parent,bool success){using var request=new HttpRequestMessage(HttpMethod.Delete,"/api/admin/auto/"+source+"/"+page+"?parentId="+Uri.EscapeDataString(parent)){Content=JsonContent.Create(new{id})};using var response=await client.SendAsync(request);var value=JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());Assert.Equal(success?200:500,value.GetProperty("code").GetInt32());}
            async Task<string> State(){using var independent=new DatabaseSession(options);var rows=new List<object>();foreach(var table in names)rows.Add((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(table)).OrderBy(row=>SqlRowStore.Id(row)).ToArray());return JsonSerializer.Serialize(rows);}
            Task Move(long parent)=>Execute(session,"UPDATE `"+prefix+"_place` SET `"+prefix+"_area_id`=@parent WHERE id=@id",("parent",parentType=="string"?parent.ToString(CultureInfo.InvariantCulture):(object)parent),("id",First+2));

        }
        catch(Exception error){await Receipt("failure",new{nonce,type,parentType,explicitAncestor,errorType=error.GetType().FullName,error.Message,created,verified=false});throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var inspection=new SchemaService(cleanup);var actual=await inspection.InspectAsync(catalog);if((await inspection.PreviewAsync(catalog)).Changes.Count!=0||!names.SequenceEqual(new[]{prefix+"_source",prefix+"_country",prefix+"_area",prefix+"_place"}))throw new InvalidOperationException("自有Excel样本结构／表名不符，保留现场。");var state=new Dictionary<string,string>();foreach(var name in names)state[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)));var fingerprint=MySqlSchemaPlanner.Fingerprint(actual);await Receipt("cleanup-before",new{nonce,names,fingerprint,rowsSha256=state.ToDictionary(pair=>pair.Key,pair=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value))))});using var readback=new DatabaseSession(options);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await new SchemaService(readback).InspectAsync(catalog)));foreach(var name in names)Assert.Equal(state[name],JsonSerializer.Serialize((await new SqlRowStore(readback,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row))));foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}await Receipt("cleanup",new{nonce,type,parentType,explicitAncestor,physicalDelete,names,verified,independentTableAbsenceChecked=true,remaining=0});}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
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
        public RuntimeActor RequireActor()=>new(954,"非冗余跨表Excel",false,Project.ToString(),"1");
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
