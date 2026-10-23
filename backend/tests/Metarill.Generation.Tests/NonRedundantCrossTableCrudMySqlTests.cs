using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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

/// <summary>真实生成写接口与Server-200随机四表，覆盖稀疏结构、逻辑部分选择和同一事务守卫。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantCrossTableCrudMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>固定实例及隔离库后执行四种来源与父键组合，不使用本机Docker。</summary>
    [StandaloneExportMySqlFact]
    public async Task Original_cross_protocol_uses_sparse_storage_and_logical_reads()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例pin不符，未建样本。");}
        foreach(var type in new[]{"string","int64"})foreach(var parentType in new[]{"string","int64"})await Exercise(options,type,parentType);
    }
    /// <summary>自有空表经显式预览创建，生成原接口并验证所有写入链路，失败先存证后清理。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,string parentType)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix="mt_nrcg_"+nonce[..12];var catalog=NonRedundantCrossTableGenerationTests.Catalog(prefix,type,parentType);var secondary=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(catalog.ConfigColumns.Single(column=>column.Id=="25")))!;secondary.Id="80";secondary.ColumnName="secondary_place_id";secondary.ControlDetail=secondary.ControlDetail.Replace("\"columnPrefix\":\"","\"columnPrefix\":\"secondary_");catalog.ConfigColumns.Add(secondary);catalog.ConfigPageColumns.Add(new(){Id="20180",ConfigPageId="201",ConfigColumnId="80",Extra=new(){{"isSearch",JsonSerializer.SerializeToElement(1)}}});
        var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,".local/nonredundant-cross-table-crud/mysql-"+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-crud-mysql");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;var observer=new ProbeObserver();var snapshot=new SnapshotGuard(options,prefix+"_country");using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();
        await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,parentType,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var initial=await schema.PreviewAsync(catalog);Assert.Equal(4,initial.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(initial)).Succeeded);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);var source=prefix+"_source";var physical=(await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==source).Columns;Assert.DoesNotContain(physical,column=>column.Name.EndsWith("_name",StringComparison.Ordinal));Assert.Equal(6,physical.Count(column=>column.Name.EndsWith("_id",StringComparison.Ordinal)&&column.Name!="yz_project_id"));
            var targets=new[]{"country","area","place"};
            // 固定种子只写本nonce四表，类型差异由同一逻辑类型映射决定。
            async Task Seed(int level,long id,string name,long? parent=null,long project=Project,int status=1){var parentColumn=level==0?"":",`"+prefix+"_"+targets[level-1]+"_id`";await Execute(session,"INSERT INTO `"+prefix+"_"+targets[level]+"` (id,name,yz_project_id,status"+parentColumn+") VALUES (@id,@name,@project,@status"+(level==0?"":",@parent")+")",("id",id),("name",name),("project",project),("status",status),("parent",parentType=="string"?(object?)parent?.ToString(CultureInfo.InvariantCulture):parent));}
            await Seed(0,First,"国甲");await Seed(0,First+10,"国乙");await Seed(0,First+20,"隐藏国",status:0);await Seed(0,First+30,"外项目",project:Project+1);await Seed(1,First+1,"区甲",First);await Seed(1,First+11,"区乙",First+10);await Seed(1,First+21,"隐藏父",First+20);await Seed(1,First+31,"外父",First+30);await Seed(2,First+2,"城甲",First+1);await Seed(2,First+12,"城乙",First+11);await Seed(2,First+22,"隐藏祖先叶",First+21);await Seed(2,First+32,"外祖先叶",First+31);
            var entityName=GenerationModel.Pascal(source);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossWriteProbe");await File.WriteAllTextAsync(Path.Combine(directory,"Hook.cs"),$$"""
using Metarill.Core.Security;
namespace Example.CrossWriteProbe.Generated.Services;
public partial class {{entityName}}Service
{
    /// <summary>手写钩子仍在最终路径及作用域检查之前，不作为跨项目授权。</summary>
    partial void BeforeSave(Example.CrossWriteProbe.Generated.Entities.{{entityName}} entity,RuntimeActor actor,bool creating,ISet<string> submitted)
    {
        if(entity.Name=="钩子选中"){entity.PlaceId={{(type=="string"?"\""+(First+12).ToString()+"\"":(First+12).ToString()+"L")}};submitted.Add("place_id");}
        if(entity.Name=="钩子越界"){entity.PlaceId={{(type=="string"?"\""+(First+32).ToString()+"\"":(First+32).ToString()+"L")}};submitted.Add("place_id");}
        if(entity.Name=="钩子归属"){entity.YzProjectId={{(Project+1).ToString()}}L;submitted.Add("yz_project_id");}
    }
}
""");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddSingleton<IRecordMutationObserver>(observer);builder.Services.AddSingleton<IRecordMutationGuard>(snapshot);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(944));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.CrossWriteProbe.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or MySqlException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error is MySqlException?"数据库拒绝写入":error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/201/";
            // 请求沿原实体JSON；类型为long的字段仍从原字符串输入，响应ID仍为字符串。
            async Task<JsonElement> Post(string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+action,body)).Content.ReadAsStringAsync());
            async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}
            // 原节点负载、伪造祖先／名称、部分选择均由生成Service校验后返回逻辑VO。
            var saved=Data(await Post("add",new{name="完整选择",secondaryAreaId=(First+11).ToString(),placeId=(First+2).ToString(),countryId=(First+10).ToString(),countryName="伪造",placeName="伪造"}));var id=saved.GetProperty("id").GetString()!;Assert.Equal(First.ToString(),saved.GetProperty("countryId").GetString());Assert.Equal((First+1).ToString(),saved.GetProperty("areaId").GetString());Assert.Equal("国甲/区甲/城甲",saved.GetProperty("placeIdDisplayText").GetString());await Sparse(id,2,First+2);Assert.Equal("国乙/区乙",saved.GetProperty("secondaryPlaceIdDisplayText").GetString());await SecondarySparse(id);
            var onlyRoot=Data(await Post("add",new{name="部分根",countryId=First.ToString()}));Assert.Equal("国甲",onlyRoot.GetProperty("placeIdDisplayText").GetString());Assert.Equal(JsonValueKind.Null,onlyRoot.GetProperty("areaId").ValueKind);await Sparse(onlyRoot.GetProperty("id").GetString()!,0,First);
            var onlyMid=Data(await Post("add",new{name="部分中",areaId=(First+1).ToString()}));Assert.Equal("国甲/区甲",onlyMid.GetProperty("placeIdDisplayText").GetString());Assert.Equal(JsonValueKind.Null,onlyMid.GetProperty("placeId").ValueKind);await Sparse(onlyMid.GetProperty("id").GetString()!,1,First+1);
            await Execute(session,"UPDATE `"+prefix+"_country` SET name='新国甲' WHERE id=@id",("id",First));client.DefaultRequestHeaders.Add("denyTargets","1");var unchanged=Data(await Post("update",new{id,name="只改名称"}));Assert.Equal("新国甲/区甲/城甲",unchanged.GetProperty("placeIdDisplayText").GetString());await Sparse(id,2,First+2);client.DefaultRequestHeaders.Remove("denyTargets");
            var fromFull=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(unchanged.GetRawText())!;fromFull["countryId"]=JsonSerializer.SerializeToElement((First+10).ToString());fromFull["countryName"]=JsonSerializer.SerializeToElement("伪造");fromFull["placeIdDisplayText"]=JsonSerializer.SerializeToElement("伪造路径");var echoed=Data(await Post("update",fromFull));Assert.Equal("新国甲/区甲/城甲",echoed.GetProperty("placeIdDisplayText").GetString());await Sparse(id,2,First+2);
            var copy=Data(await Post("copy/"+id,new{}));await Sparse(copy.GetProperty("id").GetString()!,2,First+2);Assert.Equal("新国甲/区甲/城甲",copy.GetProperty("placeIdDisplayText").GetString());
            var partial=Data(await Post("update",new{id,placeId=(string?)null}));Assert.Equal((First+1).ToString(),partial.GetProperty("areaId").GetString());Assert.Equal("新国甲/区甲",partial.GetProperty("placeIdDisplayText").GetString());await Sparse(id,1,First+1);await SecondarySparse(id);
            partial=Data(await Post("update",new{id,areaId=(string?)null}));Assert.Equal(First.ToString(),partial.GetProperty("countryId").GetString());await Sparse(id,0,First);
            // 已存来源读取无需目标list，但固定目标条件仍决定可见路径，筛选发生在SQL分页前。
            client.DefaultRequestHeaders.Add("denyTargets","1");var list=Data(await Post("page",new{pageSize=100})).GetProperty("records").EnumerateArray().ToArray();Assert.Equal("新国甲",list.Single(row=>row.GetProperty("id").GetString()==id).GetProperty("placeIdDisplayText").GetString());var selected=Data(await Post("selection/selected",new{selectedIds=new[]{onlyMid.GetProperty("id").GetString(),onlyRoot.GetProperty("id").GetString()}})).EnumerateArray().ToArray();Assert.Equal(new[]{"新国甲/区甲","新国甲"},selected.Select(row=>row.GetProperty("placeIdDisplayText").GetString()));using(var response=await client.GetAsync(endpoint+"detail/"+id)){var detail=Data(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()));Assert.Equal("新国甲",detail.GetProperty("placeIdDisplayText").GetString());}
            var secondFilter=Data(await Post("page",new{pageSize=100,filters=new{secondaryPlaceId=new{secondaryCountryId=(First+10).ToString()}}}));Assert.Equal(2,Convert.ToInt64(secondFilter.GetProperty("total").ToString(),CultureInfo.InvariantCulture));var filtered=Data(await Post("page",new{pageNo=2,pageSize=1,filters=new{placeId=new{countryId=First.ToString(),countryName="不参与匹配"}}}));Assert.Equal(4,Convert.ToInt64(filtered.GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Single(filtered.GetProperty("records").EnumerateArray());var empty=Data(await Post("page",new{pageNo=99,pageSize=1,filters=new{placeId=new{countryId=First.ToString()}}}));Assert.Equal(4,Convert.ToInt64(empty.GetProperty("total").ToString(),CultureInfo.InvariantCulture));Assert.Empty(empty.GetProperty("records").EnumerateArray());
            var cleared=Data(await Post("update",new{id,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null}));Assert.Equal(JsonValueKind.Null,cleared.GetProperty("countryId").ValueKind);await Sparse(id,-1,0);await SecondarySparse(id);var before=await State();Assert.Equal(500,(await Post("add",new{name="拒绝目标",countryId=First.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());client.DefaultRequestHeaders.Remove("denyTargets");
            client.DefaultRequestHeaders.Add("denyLeaf","1");Data(await Post("add",new{name="仅根授权",countryId=First.ToString()}));client.DefaultRequestHeaders.Remove("denyLeaf");
            foreach(var invalid in new[]{First+22,First+32,First+9999}){before=await State();Assert.Equal(500,(await Post("add",new{name="拒绝祖先",placeId=invalid.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            var hook=Data(await Post("add",new{name="钩子选中"}));Assert.Equal((First+10).ToString(),hook.GetProperty("countryId").GetString());await Sparse(hook.GetProperty("id").GetString()!,2,First+12);foreach(var name in new[]{"钩子越界","钩子归属","通知故障"}){before=await State();Assert.Equal(500,(await Post("add",new{name,placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());}
            // 原存储歧义必须在复制前拒绝；明确原始值按本样本自行恢复，不能静默挑最深位。
            var copyId=long.Parse(copy.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);await Execute(session,"UPDATE `"+source+"` SET country_id=@country WHERE id=@id",("country",type=="string"?First.ToString():(object)First),("id",copyId));before=await State();Assert.Equal(500,(await Post("copy/"+copyId,new{})).GetProperty("code").GetInt32());Assert.Equal(before,await State());await Execute(session,"UPDATE `"+source+"` SET country_id=NULL WHERE id=@id",("id",copyId));
            // 模拟数据库在本次正常UPDATE后改写物理ID；后核验必须早于观察者并整笔回滚。
            var rewrite=true;session.Orm.Aop.CommandAfter+=(_,args)=>{if(rewrite&&args.Command.CommandText.TrimStart().StartsWith("UPDATE",StringComparison.OrdinalIgnoreCase)&&args.Command.CommandText.Contains(source,StringComparison.Ordinal)){rewrite=false;using var command=args.Command.Connection!.CreateCommand();command.Transaction=args.Command.Transaction;command.CommandText="UPDATE `"+source+"` SET country_id=@country WHERE id=@id";var country=command.CreateParameter();country.ParameterName="country";country.Value=type=="string"?First.ToString():(object)First;command.Parameters.Add(country);var key=command.CreateParameter();key.ParameterName="id";key.Value=copyId;command.Parameters.Add(key);command.ExecuteNonQuery();}};before=await State();var changed=await Post("update",new{id=copyId.ToString(),placeId=(First+2).ToString()});Assert.False(rewrite);Assert.Equal(500,changed.GetProperty("code").GetInt32());Assert.Contains("存储后发生变化",changed.GetProperty("msg").GetString());Assert.Equal(before,await State());
            // 目标固定条件不能因静态字段初始化次序丢失，坏目标只隐藏路径，来源ID仍保留。
            await Execute(session,"UPDATE `"+prefix+"_country` SET status=0 WHERE id=@id",("id",First));var hidden=Data(await Post("page",new{pageSize=100})).GetProperty("records").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==copy.GetProperty("id").GetString());Assert.Equal((First+2).ToString(),hidden.GetProperty("placeId").GetString());Assert.Equal(JsonValueKind.Null,hidden.GetProperty("countryId").ValueKind);Assert.Equal("",hidden.GetProperty("placeIdDisplayText").GetString());before=await State();Assert.Equal(500,(await Post("add",new{name="条件祖先",placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());await Execute(session,"UPDATE `"+prefix+"_country` SET status=1 WHERE id=@id",("id",First));
            // 显式层号使不同表中的相同数字ID无损保留。
            await Seed(1,First+10,"同号区",First+10);await Seed(2,First+10,"同号城",First+10);var same=Data(await Post("add",new{name="同号末级",placeId=(First+10).ToString()}));Assert.Equal("国乙/同号区/同号城",same.GetProperty("placeIdDisplayText").GetString());await Sparse(same.GetProperty("id").GetString()!,2,First+10);
            snapshot.Change=true;var current=Data(await Post("update",new{id=copy.GetProperty("id").GetString(),name="旧快照更新"}));Assert.Equal("RR新国甲/区甲/城甲",current.GetProperty("placeIdDisplayText").GetString());await Sparse(copy.GetProperty("id").GetString()!,2,First+2);
            var pending=Post("add",new{name="锁持有",placeId=(First+2).ToString()});try{await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync();await using(var timeout=independent.CreateCommand()){timeout.CommandText="SET SESSION innodb_lock_wait_timeout=1";await timeout.ExecuteNonQueryAsync();}await using var transaction=await independent.BeginTransactionAsync();await using var update=independent.CreateCommand();update.Transaction=transaction;update.CommandText="UPDATE `"+prefix+"_area` SET name='竞争中层' WHERE id=@id";update.Parameters.AddWithValue("id",First+1);Assert.Equal(1205,(await Assert.ThrowsAsync<MySqlException>(()=>update.ExecuteNonQueryAsync())).Number);await transaction.RollbackAsync();}finally{observer.Release.TrySetResult();await pending;}Data(await pending);
            // 观察者看到真实稀疏值而非临时逻辑祖先；后置故障必须整体回滚。
            Assert.True(observer.CheckedSparse);Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            async Task SecondarySparse(string key){using var independent=new DatabaseSession(options);var row=Assert.Single(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source),row=>SqlRowStore.Id(row)==long.Parse(key,CultureInfo.InvariantCulture));Assert.Null(row["secondary_country_id"]);Assert.Equal((First+11).ToString(),Convert.ToString(row["secondary_area_id"],CultureInfo.InvariantCulture));Assert.Null(row["secondary_place_id"]);}
            async Task Sparse(string key,int selected,long value){using var independent=new DatabaseSession(options);var row=Assert.Single(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source),row=>SqlRowStore.Id(row)==long.Parse(key,CultureInfo.InvariantCulture));var fields=new[]{"country_id","area_id","place_id"};for(var index=0;index<fields.Length;index++){if(index==selected)Assert.Equal(value.ToString(CultureInfo.InvariantCulture),Convert.ToString(row[fields[index]],CultureInfo.InvariantCulture));else Assert.Null(row[fields[index]]);}Assert.DoesNotContain(row.Keys,name=>name.EndsWith("_name",StringComparison.Ordinal));}
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,parentType,verified,realGeneratedHttp=true,realMySql=true,sparseStorage=true,partialSelections=true,originalProtocol=true,omissionAndNull=true,twoIndependentControls=true,sameIdAcrossTargetTables=true,oldSnapshotCurrentRead=true,logicalFilterBeforePage=true,currentNames=true,currentLocks=true,storedIdPostCheckRollback=true,ambiguousCopyRejected=true,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{error=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,structureLockRejected=error is SchemaApplyException schemaError&&schemaError.InnerException?.Message=="其他结构任务正在执行，未取得数据库结构锁。",failedTestPreserved=true}));throw;}
        finally
        {
            observer.Release.TrySetResult();if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("自有跨表写四表结构漂移，保留现场。");if(existing.Tables.Count>0){if(!names.SequenceEqual(new[]{prefix+"_source",prefix+"_country",prefix+"_area",prefix+"_place"}))throw new InvalidOperationException("自有样本表名不符，未清理。");var state=new Dictionary<string,string>();foreach(var name in names)state[name]=JsonSerializer.Serialize((await new SqlRowStore(cleanup,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)));await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup-before.json"),JsonSerializer.Serialize(new{nonce,names,fingerprint=MySqlSchemaPlanner.Fingerprint(existing),rowsSha256=state.ToDictionary(pair=>pair.Key,pair=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.Value))))}));using var check=new DatabaseSession(options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(existing),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(catalog)));foreach(var name in names)Assert.Equal(state[name],JsonSerializer.Serialize((await new SqlRowStore(check,catalog).ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row))));foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));}await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>创建旧RR快照后独立改名，原写回显必须读取已提交的新名称。</summary>
    private sealed class SnapshotGuard(DatabaseOptions options,string table):IRecordMutationGuard
    {
        internal bool Change{get;set;}
        /// <summary>只在一次指定Update准备阶段建立旧快照，使用已核验业务库和本nonce目标。</summary>
        public async Task BeforeMutationAsync(RecordMutationPreparation context,CancellationToken token=default)
        {
            if(!Change||context.Operation!=RecordMutationOperation.Update)return;Change=false;await using(var command=context.Transaction.Connection!.CreateCommand()){command.Transaction=context.Transaction;command.CommandText="SELECT name FROM `"+table+"` WHERE id=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=First;command.Parameters.Add(parameter);await command.ExecuteScalarAsync(token);}
            await using var independent=new MySqlConnection(options.ConnectionString);await independent.OpenAsync(token);await using var update=independent.CreateCommand();update.CommandText="UPDATE `"+table+"` SET name='RR新国甲' WHERE id=@id";update.Parameters.AddWithValue("id",First);Assert.Equal(1,await update.ExecuteNonQueryAsync(token));
        }
    }
    /// <summary>固定身份不代表正式登录，请求只可撤销动作。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>项目与后台由宿主确定。</summary>
        public RuntimeActor RequireActor()=>new(944,"跨表保存验收",false,Project.ToString(),"1");
        /// <summary>验证根级不附加未来叶list，清空无需目标list。</summary>
        public void RequirePagePermission(string page,string operation){var headers=accessor.HttpContext!.Request.Headers;if(headers.ContainsKey("denyTargets")&&page is "301" or "401" or "501"||headers.ContainsKey("denyLeaf")&&page=="501")throw new UnauthorizedAccessException("固定目标撤权。");}
    }
    /// <summary>后段故障与有界暂停均在原事务中，验证回滚及锁保持。</summary>
    private sealed class ProbeObserver:IRecordMutationObserver
    {
        public bool CheckedSparse{get;private set;}
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>仅固定标记触发，不改变普通写入语义。</summary>
        public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default){var populated=new[]{"country_id","area_id","place_id"}.Count(field=>context.SubmittedFields.TryGetValue(field,out var value)&&value is not null);Assert.InRange(populated,0,1);CheckedSparse=true;if(!context.SubmittedFields.TryGetValue("name",out var name))return;if(Equals(name,"通知故障"))throw new InvalidOperationException("固定通知故障");if(Equals(name,"锁持有")){Entered.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(25),cancellationToken);}}
    }
    /// <summary>错误正文不能当作业务数据。</summary>
    private static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.ToString());return response.GetProperty("data");}
    /// <summary>自有随机表固定SQL均参数化，连接字符串不写入回执。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] parameters){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in parameters){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
