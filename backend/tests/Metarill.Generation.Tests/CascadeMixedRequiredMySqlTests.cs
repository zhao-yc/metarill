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

/// <summary>同页混合级联与页面必填/物理非空在真实MySQL和生成HTTP中的一致性。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CascadeMixedRequiredMySqlTests
{
    private const long Project=9007199254740993;
    private const long First=9007199254742001;
    /// <summary>固定Server-200实例及隔离库后，两种来源键分别运行独立五表样本。</summary>
    [StandaloneExportMySqlFact]
    public async Task Mixed_controls_keep_required_readonly_nullable_and_projection_contracts()
    {
        var options=await Options();foreach(var type in new[]{"string","int64"})await Exercise(options,type);
    }
    /// <summary>四种R0来源／父键在独立随机五表执行实际生成HTTP，不以Full构建替代事务验证。</summary>
    [StandaloneExportMySqlFact]
    public async Task Nonredundant_mixed_cascades_keep_sparse_storage_current_names_and_atomic_mutations()
    {
        var options=await Options();
        foreach(var type in new[]{"string","int64"})foreach(var parentType in new[]{"string","int64"})await Exercise(options,type,true,parentType);
    }
    /// <summary>每次实际建表前核对Server-200与既定隔离库，实例pin不符不写入。</summary>
    private static async Task<DatabaseOptions> Options()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("仅允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不符，未建样本。");}return options;
    }
    /// <summary>从元数据生成所有实体、页面与校验，初始错误响应先保存，不以测试改预期掩盖差异。</summary>
    private static async Task Exercise(DatabaseOptions options,string type,bool nonRedundant=false,string? parentType=null)
    {
        var nonce=Guid.NewGuid().ToString("N");var prefix=(nonRedundant?"mt_nrmix_":"mt_mrq_")+nonce[..12];var catalog=nonRedundant?NonRedundantMixedCascadeTests.Catalog(prefix,type,parentType??type):CascadeMixedRequiredTests.Catalog(prefix,type,type);var root=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!;var evidence=Path.Combine(root,(nonRedundant?".local/nonredundant-mixed-cascade/mysql-":".local/cascade-mixed-required/mysql-")+nonce);Directory.CreateDirectory(evidence);var boundary=Path.Combine(Path.GetTempPath(),"metarill-mixed-required");var directory=Path.Combine(boundary,nonce);WebApplication? app=null;var created=false;var verified=false;using var session=new DatabaseSession(options);var schema=new SchemaService(session);var names=catalog.ConfigTables.Select(table=>table.TableName).ToArray();var source=prefix+"_source";await File.WriteAllTextAsync(Path.Combine(evidence,"intent.json"),JsonSerializer.Serialize(new{nonce,type,parentType,nonRedundant,names,database=options.ExpectedDatabase,verified=false}));
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal(5,plan.Changes.Count);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);object Key(long id)=>(parentType??type)=="string"?id.ToString(CultureInfo.InvariantCulture):id;
            await Execute(session,"INSERT INTO `"+prefix+"_country` (id,name,yz_project_id,status) VALUES (@id,'国甲',@project,1)",("id",First),("project",Project));await Execute(session,"INSERT INTO `"+prefix+"_area` (id,name,yz_project_id,status,`"+prefix+"_country_id`) VALUES (@id,'区甲',@project,1,@parent)",("id",First+1),("project",Project),("parent",Key(First)));await Execute(session,"INSERT INTO `"+prefix+"_place` (id,name,yz_project_id,status,`"+prefix+"_area_id`) VALUES (@id,'城甲',@project,1,@parent)",("id",First+2),("project",Project),("parent",Key(First+1)));await Execute(session,"INSERT INTO `"+prefix+"_tree` (id,name,yz_project_id,status,`parent_"+prefix+"_tree_id`) VALUES (@root,'树根',@project,1,NULL),(@child,'树叶',@project,1,@rootKey)",("root",First+50),("child",First+51),("project",Project),("rootKey",Key(First+50)));
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.MixedRequired");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(950));builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));assembly.GetType("Example.MixedRequired.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(config=>config.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error)when(error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or JsonException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));}});app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(35)};var endpoint="/api/admin/auto/"+source+"/";
            async Task<JsonElement> Post(string page,string action,object body)=>JsonSerializer.Deserialize<JsonElement>(await(await client.PostAsJsonAsync(endpoint+page+"/"+action,body)).Content.ReadAsStringAsync());async Task<string> State(){using var independent=new DatabaseSession(options);return JsonSerializer.Serialize((await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(source)).OrderBy(row=>SqlRowStore.Id(row)));}
            // 三个独立错误先全部落盘：数据库非空不应等于NotBlank，空多选不能绕过isMust，只读必填不应再开放。
            var dbBlank=await Post("201","add",new{name="数据库空白",notes=""});var emptyArray=await Post("203","add",new{name="错误空数组",choiceIds=Array.Empty<string>()});var readonlyBase=Data(await Post("201","add",new{name="只读基线"}));var readonlyUpdate=await Post("205","update",new{id=readonlyBase.GetProperty("id").GetString(),name="只读祖先仍空"});await File.WriteAllTextAsync(Path.Combine(evidence,"boundary-observations.json"),JsonSerializer.Serialize(new{dbBlank,emptyArray,readonlyUpdate}));Assert.Equal(200,dbBlank.GetProperty("code").GetInt32());Assert.Equal(500,emptyArray.GetProperty("code").GetInt32());Assert.Equal(200,readonlyUpdate.GetProperty("code").GetInt32());
            var before=await State();Assert.Equal(500,(await Post("201","add",new{name="非法NULL",notes=(string?)null})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Post("202","add",new{name="页面必填空白",notes=" "})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Data(await Post("202","add",new{name="默认满足必填"}));
            var chosen=Data(await Post("203","add",new{name="非空多选",choiceIds=new[]{(First+50).ToString()}}));before=await State();Assert.Equal(500,(await Post("203","update",new{id=chosen.GetProperty("id").GetString(),choiceIds=Array.Empty<string>()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Data(await Post("203","update",new{id=chosen.GetProperty("id").GetString(),name="省略保留多选"}));
            before=await State();Assert.Equal(500,(await Post("204","add",new{name="原末级必填",countryId=First.ToString(),treeId=(First+51).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());Assert.Equal(500,(await Post("204","add",new{name="原自树必填",placeId=(First+2).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
            var mixed=Data(await Post("201","add",new{name="混合",notes="",placeId=(First+2).ToString(),treeId=(First+51).ToString(),choiceIds=new[]{(First+50).ToString(),(First+51).ToString()}}));var id=mixed.GetProperty("id").GetString();Assert.Equal("国甲/区甲/城甲",mixed.GetProperty("placeIdDisplayText").GetString());Assert.Equal("树根/树叶",mixed.GetProperty("treeIdDisplayText").GetString());Assert.Equal(new[]{"树根","树叶"},mixed.GetProperty("choiceNames").EnumerateArray().Select(value=>value.GetString()));Data(await Post("204","update",new{id,name="混合必填"}));
            await Execute(session,"UPDATE `"+prefix+"_country` SET name='新国' WHERE id=@id",("id",First));await Execute(session,"UPDATE `"+prefix+"_tree` SET name='新叶' WHERE id=@id",("id",First+51));var changed=Data(await Post("201","update",new{id,name="混合刷新差异"}));Assert.Equal(nonRedundant?"新国/区甲/城甲":"国甲/区甲/城甲",changed.GetProperty("placeIdDisplayText").GetString());Assert.Equal("树根/新叶",changed.GetProperty("treeIdDisplayText").GetString());
            var filtered=Data(await Post("201","page",new{pageSize=100,filters=new{placeId=new{countryId=First.ToString()},treeId=new{treeId=(First+50).ToString()}}}));Assert.Equal(id,Assert.Single(filtered.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());var selected=Data(await Post("201","selection/selected",new{selectedIds=new[]{id}}));Assert.Equal("树根/新叶",Assert.Single(selected.EnumerateArray()).GetProperty("treeIdDisplayText").GetString());var copied=Data(await Post("201","copy/"+id,new{}));Assert.Equal("新国/区甲/城甲",copied.GetProperty("placeIdDisplayText").GetString());Assert.Equal("树根/新叶",copied.GetProperty("treeIdDisplayText").GetString());
            if(nonRedundant)
            {
                // 实际物理稀疏值与两个级联整体回滚共同证明，不能只看回显。
                await Sparse(id!,null,null,(First+2).ToString(),(First+51).ToString());
                Assert.Equal(new[]{"树根","新叶"},changed.GetProperty("choiceNames").EnumerateArray().Select(value=>value.GetString()));
                before=await State();
                Assert.Equal(500,(await Post("201","update",new{id,placeId=(First+999).ToString(),treeId=(First+50).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
                Assert.Equal(500,(await Post("201","update",new{id,placeId=(First+2).ToString(),treeId=(First+999).ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await State());
                var partial=Data(await Post("201","update",new{id,countryId=First.ToString(),areaId=(string?)null,placeId=(string?)null,treeId=(First+50).ToString()}));
                Assert.Equal("新国",partial.GetProperty("placeIdDisplayText").GetString());Assert.Equal("树根",partial.GetProperty("treeIdDisplayText").GetString());
                await Sparse(id!,First.ToString(),null,null,(First+50).ToString());
                Data(await Post("201","update",new{id,name="省略保持两个级联"}));await Sparse(id!,First.ToString(),null,null,(First+50).ToString());
                Data(await Post("201","update",new{id,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null,treeId=(string?)null,choiceIds=Array.Empty<string>()}));await Sparse(id!,null,null,null,null);
                await Sparse(copied.GetProperty("id").GetString()!,null,null,(First+2).ToString(),(First+51).ToString());
                client.DefaultRequestHeaders.Add("denySource","1");try{Assert.Equal(500,(await Post("201","selection/selected",new{selectedIds=new[]{id}})).GetProperty("code").GetInt32());}finally{client.DefaultRequestHeaders.Remove("denySource");}
            }
            /// <summary>直接读取本轮物理表；名称列不落库，ID类型依然使用原字符串HTTP合同。</summary>
            async Task Sparse(string record,string? country,string? area,string? place,string? tree)
            {
                var row=Assert.Single(await new SqlRowStore(session,catalog).ReadAsync(source,new Dictionary<string,object?>{{"id",long.Parse(record,CultureInfo.InvariantCulture)}}));
                foreach(var item in new[]{("country_id",country),("area_id",area),("place_id",place),("tree_id",tree)})
                    Assert.Equal(item.Item2,row[item.Item1] is null or DBNull?null:Convert.ToString(row[item.Item1],CultureInfo.InvariantCulture));
                Assert.DoesNotContain((await schema.InspectAsync(catalog)).Tables.Single(table=>table.Name==source).Columns,column=>column.Name.EndsWith("_name",StringComparison.Ordinal)||column.Name=="choice_names");
            }
            verified=true;await File.WriteAllTextAsync(Path.Combine(evidence,"verified.json"),JsonSerializer.Serialize(new{nonce,type,parentType,nonRedundant,verified,realGeneratedHttp=true,realMySql=true,notNullSeparate=true,requiredArray=true,readonlyRequired=true,mixedProjection=true,mixedFilter=true,selection=true,copy=true,browser=false}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().Name,error.Message,structureResult=(error as SchemaApplyException)?.Result,failedTestPreserved=true}));throw;}
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}if(created){using var cleanup=new DatabaseSession(options);var existing=await new SchemaService(cleanup).InspectAsync(catalog);if(existing.Tables.Count>0&&(await new SchemaService(cleanup).PreviewAsync(catalog)).Changes.Count!=0)throw new InvalidOperationException("混合五表结构漂移，保留现场。");if(existing.Tables.Count>0)foreach(var name in names)await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(name));await using var independent=cleanup.Provider.CreateConnection(options);await independent.OpenAsync();foreach(var name in names){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var parameter=command.CreateParameter();parameter.ParameterName="name";parameter.Value=name;command.Parameters.Add(parameter);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}await File.WriteAllTextAsync(Path.Combine(evidence,"cleanup.json"),JsonSerializer.Serialize(new{nonce,names,verified,independentTableAbsenceChecked=true,remaining=0,dropExecuted=existing.Tables.Count>0}));}await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
    /// <summary>固定真实测试身份，头只可撤销权限。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        /// <summary>身份、项目和后台由宿主决定。</summary>
        public RuntimeActor RequireActor()=>new(950,"混合必填验收",false,Project.ToString(),"1");
        /// <summary>保留明确来源和目标页面权限，可由测试模拟撤销。</summary>
        public void RequirePagePermission(string page,string operation){if(accessor.HttpContext!.Request.Headers.ContainsKey("denySource")&&page=="201")throw new UnauthorizedAccessException("固定撤权。");}
    }
    /// <summary>错误正文不能当业务数据。</summary>
    private static JsonElement Data(JsonElement value){Assert.True(value.GetProperty("code").GetInt32()==200,value.ToString());return value.GetProperty("data");}
    /// <summary>只对自有随机表固定SQL并绑定数据。</summary>
    private static async Task Execute(DatabaseSession session,string sql,params(string Name,object? Value)[] values){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;foreach(var(name,value)in values){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}await command.ExecuteNonQueryAsync();}
}
