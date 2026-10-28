using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Security;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>只使用本轮双库租约及固定完整包，正式Production/Web身份，不注入授权替身。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshGlobalCachedSourceMySqlTests
{
    private const long Project=810000000000000001,First=9007199254748201;
    private const string Customer="/api/admin/auto/sample_customer/81001/",Contract="/api/admin/auto/sample_contract/81002/",Line="/api/admin/auto/sample_contract_line/81003/",FileRoute="/api/admin/sys/file-library/";
    /// <summary>原三表和额外文件父变体各用独占新库；普通CI没有本轮租约时明确跳过。</summary>
    [FreshCrossMySqlTheory][InlineData(false,"CachedOriginalDatabase","cached_original",1001)][InlineData(true,"CachedParentDatabase","cached_parent",1002)]
    public async Task Fixed_full_global_source_executes_real_web_mutations_and_owned_database(bool parentFiles,string property,string suffix,int worker)
    {
        var options=FreshInstallConfiguration.Read(property,suffix);using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));var config=settings.RootElement;
        Assert.Equal(GlobalCachedSourceNativePreparation.ProductCommit,config.GetProperty("CachedSourceProductCommit").GetString());var proof=config.GetProperty("CachedSourcePreparation").GetProperty(suffix).Deserialize<GlobalCachedPackageProof>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var catalog=GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);
        var root=NonRedundantApplicationFixture.Root();var run=config.GetProperty("RunId").GetString()!;var evidence=Path.Combine(root,".local/global-cached-source-native",run+"-"+suffix);Directory.CreateDirectory(evidence);
        var runtimeProof=GlobalCachedSourceNativePreparation.CopyForExecution(proof,Path.Combine(evidence,"application"),parentFiles);var package=runtimeProof.PackageRoot;var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");var assembly=CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll");
        using var session=new DatabaseSession(options);await Pin(session,config.GetProperty("InstancePin"));Assert.Equal(0,await Count(session));var owner="global-cached-owner-"+suffix;var ownerPassword=Guid.NewGuid().ToString("N");var stage="普通启动保持空库";FreshHost? host=null;var passed=false;
        /// <summary>秘密只放当前子进程环境，上传/导出落点在固定包外，不能污染包清单。</summary>
        ProcessStartInfo HostInfo(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(assembly);if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=owner;info.Environment["Authentication__Bootstrap__Password"]=ownerPassword;info.Environment["Ids__WorkerId"]=worker.ToString(CultureInfo.InvariantCulture);info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"files");info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
        }
        try
        {
            host=await FreshHost.StartAsync(HostInfo(),"/health");Assert.Equal(0,await Count(session));await host.DisposeAsync();host=null;
            stage="显式预览安装及重复核验";await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-preview"));Assert.Equal(0,await Count(session));await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await Count(session));await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            stage="真实安全初始化及种子";await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));var security=ReadCatalog(package,"security");var files=ReadCatalog(package,"files");var securityStore=new SqlRowStore(session,security);var store=new SqlRowStore(session,catalog);var fileStore=new SqlRowStore(session,files);
            var digest=SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager",Row("name",owner))),"password");Assert.True(PasswordHasher.Verify(ownerPassword,digest));await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));Assert.Equal(digest,SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager",Row("name",owner))),"password"));
            await store.InsertAsync("sample_customer",Row("id",First,"name","客户甲"));await store.InsertAsync("sample_customer",Row("id",First+1,"name","客户乙"));
            var ids=new SnowflakeIdGenerator(worker+10);var role=ids.NextId();var manager=ids.NextId();var reader="global-cached-reader-"+suffix;var readerPassword=Guid.NewGuid().ToString("N");var permissions=new Dictionary<string,long>(StringComparer.Ordinal);
            await session.InTransactionAsync(async()=>{await securityStore.ReadForUpdateAsync("yz_project",Row("id",Project));await securityStore.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",85001L,"name",reader,"is_super",0,"is_built_in",0));await securityStore.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",85001L,"name",reader,"password",PasswordHasher.Hash(readerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Id(role)})));foreach(var page in catalog.ConfigPages){var id=ids.NextId();permissions.Add(page.Id,id);await securityStore.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",Project,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));}return true;});
            host=await FreshHost.StartAsync(HostInfo(),"/health");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,Contract+"page",new PageRequest(),HttpStatusCode.Unauthorized);await FreshWebHttp.LoginAsync(host.Client,owner,ownerPassword,form:true);
            using var client=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(30)};var token=await FreshWebHttp.LoginAsync(client,reader,readerPassword);Assert.False((await FreshWebHttp.SendAsync(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="原候选及三表保存";var probe=new Probe(client,host.Client,session,store,fileStore,securityStore,permissions,parentFiles);await probe.ExerciseAsync();
            stage="重启保持真实Web会话";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(HostInfo(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Contract+"detail/"+probe.CopyId);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Contract+"detail/"+probe.CopyId,expected:HttpStatusCode.Unauthorized);
            Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);GlobalCachedSourceNativePreparation.Verify(proof,parentFiles);GlobalCachedSourceNativePreparation.VerifyRuntime(runtimeProof,parentFiles);passed=true;await Receipt("receipt",new{run,suffix,passed,productCommit=GlobalCachedSourceNativePreparation.ProductCommit,formalWeb=true,ordinaryAccount=true,developmentAuthentication=false,authorizationInjected=false,globalSourceWithoutProjectColumn=true,realCrud=true,physicalRenamePropagation=true,maintenanceBackfill=true,lateFileRollback=true,negativeAmountRollback=true,sourceAndTargetRevocation=true,sourceWriteContractPreserved=true,parentFiles,persistentSession=true,browserExecuted=false,performanceExecuted=false,cleanupByFreshLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,suffix,stage,errorType=error.GetType().Name,passed=false,cleanupByFreshLease=true});throw;}
        finally{if(host is not null)await host.DisposeAsync();if(!passed)await Receipt("failed",new{run,suffix,passed=false,cleanupByFreshLease=true});}
        /// <summary>回执不保存账号、请求正文、令牌或连接。</summary>
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>所有业务通过正式HTTP，独立SQL只负责种子、核验和本轮故障注入。</summary>
    private sealed class Probe(HttpClient client,HttpClient owner,DatabaseSession session,SqlRowStore store,SqlRowStore files,SqlRowStore security,Dictionary<string,long> permissions,bool parentFiles)
    {
        internal string CopyId="";
        /// <summary>省略/null、字符串大ID、缓存、父范围、复制、回填、撤权与失败回滚使用同一原模型。</summary>
        internal async Task ExerciseAsync()
        {
            var options=await Post(Contract,"findNameIdSampleCustomer82004/page",new{pageNo=1,pageSize=1,keyword="客户甲"});Assert.Equal("1",options.GetProperty("total").GetString());Assert.Equal(Id(First),Assert.Single(options.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
            var bytes=Encoding.UTF8.GetBytes("全局来源真实文件");var file=await Upload("82007",bytes);var added=await Post(Contract,"add",new{name="合同甲",sampleCustomerId=Id(First),amount=12.5m,remarks="原备注",attachments=new[]{file}});var record=added.GetProperty("id").GetString()!;Assert.True(long.Parse(record,CultureInfo.InvariantCulture)>9007199254740991);Assert.Equal("客户甲",added.GetProperty("sampleCustomerName").GetString());Assert.Equal(Id(First),added.GetProperty("sampleCustomerId").GetString());await Download(file,bytes);await References(file,1);
            var parentQuery="?parentId="+record;var child=await Post(Line,"add"+parentQuery,new{name="明细甲",amount=3.5m});var childId=child.GetProperty("id").GetString()!;Assert.Equal(record,SqlRowStore.Text(await Physical("sample_contract_line",childId),"sample_contract_id"));Assert.Equal("合同甲",SqlRowStore.Text(await Physical("sample_contract_line",childId),"sample_contract_name"));
            var other=await Post(Contract,"add",new{name="合同乙",sampleCustomerId=Id(First+1)});var otherId=other.GetProperty("id").GetString()!;var empty=await Post(Line,"page",new PageRequest{ParentId=otherId});Assert.Equal("0",empty.GetProperty("total").GetString());await OutsideParentDetail(childId,otherId);
            await Post(Customer,"update",new{id=Id(First),name="客户甲已改名"});Assert.Equal("客户甲已改名",SqlRowStore.Text(await Physical("sample_contract",record),"sample_customer_name"));await Post(Contract,"update",new{id=record,name="合同甲已改名"});Assert.Equal("合同甲已改名",SqlRowStore.Text(await Physical("sample_contract_line",childId),"sample_contract_name"));Assert.Equal("原备注",SqlRowStore.Text(await Physical("sample_contract",record),"remarks"));
            await Post(Contract,"update",new{id=record,sampleCustomerId=(string?)null,remarks=(string?)null});var cleared=await Physical("sample_contract",record);Assert.Null(cleared["sample_customer_id"]);Assert.Null(cleared["sample_customer_name"]);Assert.Null(cleared["remarks"]);await Post(Contract,"update",new{id=record,sampleCustomerId=Id(First)});
            var before=await Snapshot();var negative=await BusinessFailure(Contract+"update",new{id=record,amount=-0.01m});Assert.Equal("合同金额不能小于零",negative.GetProperty("msg").GetString());Assert.Equal(before,await Snapshot());
            await store.UpdateAsync("sample_contract",long.Parse(record,CultureInfo.InvariantCulture),Row("sample_customer_name","旧客户副本"));await store.UpdateAsync("sample_contract_line",long.Parse(childId,CultureInfo.InvariantCulture),Row("sample_contract_name","旧合同副本"));foreach(var table in new[]{"810000000000000012","810000000000000013"}){var backfill=(await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId=table})).GetProperty("data");Assert.Equal(1,backfill.GetProperty("affectedRows").GetInt32());}Assert.Equal("客户甲已改名",SqlRowStore.Text(await Physical("sample_contract",record),"sample_customer_name"));Assert.Equal("合同甲已改名",SqlRowStore.Text(await Physical("sample_contract_line",childId),"sample_contract_name"));
            CopyId=(await Post(Contract,"copy/"+record,new{})).GetProperty("id").GetString()!;await References(file,2);var second=await Upload("82007",Encoding.UTF8.GetBytes("晚期失败文件"));var installed=false;try{await Sql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `mt_gcs_late_failure` CHECK (`file_id` <> "+long.Parse(second,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)+")");installed=true;before=await Snapshot();await FreshWebHttp.SendAsync(client,HttpMethod.Post,Contract+"update",new{id=record,name="不应提交的合同",attachments=new[]{second}},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());}finally{if(installed)await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `mt_gcs_late_failure`");}
            await Grant("81001","[]");before=await Snapshot();await FreshWebHttp.SendAsync(client,HttpMethod.Post,Contract+"findNameIdSampleCustomer82004/selected",new{selectedIds=new[]{Id(First)}},HttpStatusCode.Forbidden);Assert.Equal(before,await Snapshot());
            // 原V8普通编辑不附加目标列表门槛；候选撤权后仍按来源编辑权限验证真实目标。
            var relinked=await Post(Contract,"update",new{id=record,sampleCustomerId=Id(First+1)});Assert.Equal(Id(First+1),relinked.GetProperty("sampleCustomerId").GetString());Assert.Equal("客户乙",SqlRowStore.Text(await Physical("sample_contract",record),"sample_customer_name"));await Grant("81001","[\"is_all\"]");
            await Grant("81002","[]");await FreshWebHttp.SendAsync(client,HttpMethod.Post,Contract+"page",new PageRequest(),HttpStatusCode.Forbidden);await Download(file,bytes,HttpStatusCode.Unauthorized);await Grant("81002","[\"is_all\"]");
            string? childFile=null;var childBytes=Encoding.UTF8.GetBytes("全局父的子表文件");if(parentFiles){childFile=await Upload("82012",childBytes);await Post(Line,"update"+parentQuery,new{id=childId,attachments=new[]{childFile}});await Download(childFile,childBytes);await References(childFile,1);}
            await FreshWebHttp.SendAsync(client,HttpMethod.Delete,Contract,new{id=record});before=await Snapshot();await FreshWebHttp.SendAsync(client,HttpMethod.Post,Line+"add"+parentQuery,new{name="失效父不应新建"},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());if(childFile is not null){await FreshWebHttp.SendAsync(client,HttpMethod.Post,Line+"update"+parentQuery,new{id=childId,name="失效父文件不应保存",attachments=new[]{childFile}},HttpStatusCode.Unauthorized);Assert.Equal(before,await Snapshot());await Download(childFile,childBytes,HttpStatusCode.Unauthorized);}await References(file,1);await Download(file,bytes);
        }
        /// <summary>显式业务规则沿原HTTP200/R500，不与未知异常或输入类型失败混为一类。</summary>
        private async Task<JsonElement> BusinessFailure(string path,object body)
        {
            using var response=await client.PostAsJsonAsync(path,body);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var json=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(500,json.GetProperty("code").GetInt32());return json;
        }
        /// <summary>原详情空范围沿控制器返回HTTP200/R500，不能把它误当作异常中间件的HTTP500。</summary>
        private async Task OutsideParentDetail(string id,string parent)
        {
            using var response=await client.GetAsync(Line+"detail/"+id+"?parentId="+parent);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(500,json.RootElement.GetProperty("code").GetInt32());Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("data").ValueKind);
        }
        /// <summary>请求保持原统一R，只有成功时读取data。</summary>
        private async Task<JsonElement> Post(string route,string action,object body)=>(await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+action,body)).GetProperty("data");
        /// <summary>读真实物理记录验证缓存，不用生成VO代替存储证明。</summary>
        private async Task<IReadOnlyDictionary<string,object?>> Physical(string table,string id)=>Assert.Single(await store.ReadIncludingDeletedAsync(table,Row("id",long.Parse(id,CultureInfo.InvariantCulture))));
        /// <summary>摘要包含全部业务和文件引用墓碑，验证失败没有部分提交。</summary>
        private async Task<string> Snapshot(){var state=new Dictionary<string,object>();foreach(var name in new[]{"sample_customer","sample_contract","sample_contract_line"})state[name]=(await store.ReadIncludingDeletedAsync(name)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();state["refs"]=(await files.ReadIncludingDeletedAsync("yz_file_ref")).OrderBy(row=>SqlRowStore.Id(row)).ToArray();return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))));}
        /// <summary>撤权只修改本轮角色的固定页许可。</summary>
        private Task Grant(string page,string values)=>security.UpdateAsync("yz_role_permission",permissions[page],Row("permission_values",values));
        /// <summary>真实原上传计划、multipart、完成流程；只上传本轮合成文本。</summary>
        private async Task<string> Upload(string column,byte[] bytes){var id=(await FreshWebHttp.SendAsync(client,HttpMethod.Post,FileRoute+"upload-plan",new{originalName="global-source.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId=column})).GetProperty("data").GetProperty("fileId").GetString()!;using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),"file","global-source.txt");await FreshWebHttp.SendAsync(client,HttpMethod.Post,FileRoute+"local-upload/"+id,content);await FreshWebHttp.SendAsync(client,HttpMethod.Post,FileRoute+"complete",new{fileId=id});return id;}
        /// <summary>保护下载读取实际字节，失效父或撤权必须真实拒绝。</summary>
        private async Task Download(string id,byte[] bytes,HttpStatusCode status=HttpStatusCode.OK){using var response=await client.GetAsync(FileRoute+"protected-content/"+id);Assert.Equal(status,response.StatusCode);if(status==HttpStatusCode.OK)Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());}
        /// <summary>只计算本轮文件的活动引用，项目归属必须精确。</summary>
        private async Task References(string id,int count){var rows=await files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(id,CultureInfo.InvariantCulture)));Assert.Equal(count,rows.Count);Assert.All(rows,row=>Assert.Equal(Id(Project),SqlRowStore.Text(row,"yz_project_id")));}
        /// <summary>故障注入只增加/撤销本轮新库内的明确检查约束。</summary>
        private async Task Sql(string sql){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
    /// <summary>DDL前同时核对库名、容器主机与实例UUID，不能只靠连接地址。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var row=await command.ExecuteReaderAsync();Assert.True(await row.ReadAsync()&&row.GetString(0)==session.Options.ExpectedDatabase&&row.GetString(1)==pin.GetProperty("hostname").GetString()&&row.GetString(2)==pin.GetProperty("serverUuid").GetString(),"全局来源实库pin不符，未执行DDL。");}
    /// <summary>只读当前独占库表数。</summary>
    private static async Task<long> Count(DatabaseSession session){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()";return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>基础设施模型只读本完整包，禁止加载旧平台表配置。</summary>
    private static MetadataCatalog ReadCatalog(string package,string name)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(package,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>测试种子与篡改值通过现有参数化存储，不手写实体或CRUD。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs){var result=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<pairs.Length;i+=2)result.Add((string)pairs[i]!,pairs[i+1]);return result;}
    /// <summary>HTTP身份始终用精确十进制字符串。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
