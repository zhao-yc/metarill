using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>专属规则租约才启用正式用例；通用MySQL或首次安装配置不能误启用本轮场景。</summary>
public sealed class RuleRecordNativeFactAttribute:FactAttribute
{
    public RuleRecordNativeFactAttribute()
    {if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_RULE_RECORD_NATIVE_SETTINGS")))Skip="未提供本轮规则原记录独占新库租约，正式Web/OAuth/CLI尚未执行。";}
}

/// <summary>正式宿主、真实数据库角色与真实CLI一起验证规则，不注入认证或SQL传输替身。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RuleRecordNativeMySqlTests
{
    private const long First=9007199254760001,Blocked=9007199254770001,Allowed=9007199254770002,Foreign=9007199254770003,Deleted=9007199254770004;
    /// <summary>独占新库安装后先批准设备，再撤去测试账号临时超级角色；无查询权写入和撤权均走正式入口。</summary>
    [RuleRecordNativeFact]
    public async Task Fixed_shared_rule_application_uses_real_web_oauth_cli_before_tasks()
    {
        var file=Environment.GetEnvironmentVariable("METARILL_RULE_RECORD_NATIVE_SETTINGS")!;Assert.Equal(Path.GetFullPath(file),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));using var document=JsonDocument.Parse(await File.ReadAllTextAsync(file));var settings=document.RootElement;
        Assert.Equal("rule-record-v1",settings.GetProperty("Purpose").GetString());Assert.Equal("approved",settings.GetProperty("AuthorizationStatus").GetString());Assert.Equal("running",settings.GetProperty("LeaseStatus").GetString());Assert.Equal(RuleRecordNativePreparation.ProductCommit,settings.GetProperty("ProductCommit").GetString());
        var root=NonRedundantApplicationFixture.Root();var run=settings.GetProperty("RunId").GetString()!;if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("规则运行标识不规范。");var proof=settings.GetProperty("RuleRecordPreparation").Deserialize<GlobalCachedPackageProof>()!;Assert.Equal(CSharpGenerator.SafePath(root,".local/rule-record-native/prepared"),proof.PackageRoot);var catalog=RuleRecordNativePreparation.Verify(proof);
        var copied=RuleRecordNativePreparation.CopyForExecution(proof,CSharpGenerator.SafePath(root,".local/rule-record-native/"+run+"/execution/application"));var evidence=CSharpGenerator.SafePath(root,".local/rule-record-native/"+run+"/evidence");Directory.CreateDirectory(evidence);
        var options=FreshInstallConfiguration.Read("AgentDatabase","agent");using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));Assert.Equal(0,await Count(session));
        var hostDirectory=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host");var assembly=CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll");
        var infrastructure=MetadataCatalogComposition.MergePhysical(catalog,AgentFoundationCatalog.ForApplication(catalog));var security=await ReadCatalog("security");foreach(var name in new[]{"files","security"})infrastructure=MetadataCatalogComposition.MergePhysical(infrastructure,await ReadCatalog(name));var store=new SqlRowStore(session,catalog);var securityStore=new SqlRowStore(session,security);var tasks=new SqlRowStore(session,AgentFoundationCatalog.ForApplication(catalog));
        string ownerName="rule-native-owner",ownerPassword=Guid.NewGuid().ToString("N"),workerName="rule-native-worker",workerPassword=Guid.NewGuid().ToString("N");var project=long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture);var ids=new SnowflakeIdGenerator(1013);var approvalRole=ids.NextId();var workerRole=ids.NextId();var manager=ids.NextId();var permissions=new Dictionary<string,long>(StringComparer.Ordinal);var cliRunId=Guid.NewGuid().ToString("N");var stage="零表启动";FreshHost? host=null;var passed=false;string ownerToken="",workerToken="";
        ProcessStartInfo HostInfo(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(assembly);if(operation is not null)info.ArgumentList.Add(operation);info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=ownerName;info.Environment["Authentication__Bootstrap__Password"]=ownerPassword;info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";info.Environment["Ids__WorkerId"]="1012";info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"files");info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
        }
        try
        {
            host=await FreshHost.StartAsync(HostInfo(),"/health");Assert.Equal(0,await Count(session));await host.DisposeAsync();host=null;
            stage="显式预览和安装";var preview=await Run(HostInfo("--schema-preview"),"schema-preview");using(var json=JsonDocument.Parse(preview))Assert.Equal(infrastructure.ConfigTables.Count,json.RootElement.GetProperty("changes").EnumerateArray().Count(change=>change.GetProperty("kind").GetString()=="createTable"));Assert.Equal(0,await Count(session));await Run(HostInfo("--schema-apply"),"schema-apply");Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await Count(session));Assert.Empty((await new SchemaService(session).PreviewAsync(infrastructure)).Changes);
            stage="真实安全初始化";await Run(HostInfo("--initialize-security"),"initialize");await Seed(store,project);
            await session.InTransactionAsync(async()=>
            {
                await securityStore.ReadForUpdateAsync("yz_project",Row("id",project));await securityStore.InsertAsync("yz_role",Row("id",approvalRole,"yz_project_id",project,"yz_admin_type_id",5L,"name","rule-approval-only","is_super",1,"is_built_in",0));await securityStore.InsertAsync("yz_role",Row("id",workerRole,"yz_project_id",project,"yz_admin_type_id",5L,"name","rule-worker","is_super",0,"is_built_in",0));
                await securityStore.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",project,"yz_admin_type_id",5L,"name",workerName,"password",PasswordHasher.Hash(workerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{approvalRole.ToString(CultureInfo.InvariantCulture),workerRole.ToString(CultureInfo.InvariantCulture)})));
                foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2")){var id=ids.NextId();permissions.Add(page.Id,id);await securityStore.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",project,"yz_role_id",workerRole,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_update\",\"is_delete\"]"));}return true;
            });
            host=await FreshHost.StartAsync(HostInfo(),"/health");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,"/api/admin/sys/agent/grants",expected:HttpStatusCode.Unauthorized);ownerToken=await FreshWebHttp.LoginAsync(host.Client,ownerName,ownerPassword,form:true);
            using var worker=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(90)};workerToken=await FreshWebHttp.LoginAsync(worker,workerName,workerPassword);Assert.True((await FreshWebHttp.SendAsync(worker,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="正式CLI设备授权";var authorized=await Probe("authorize");Assert.True(authorized.GetProperty("deviceApproved").GetBoolean());Assert.True(authorized.GetProperty("realCliAuth").GetBoolean());
            stage="移除临时超级角色";await securityStore.UpdateAsync("yz_manager",manager,Row("yz_role_ids",JsonSerializer.Serialize(new[]{workerRole.ToString(CultureInfo.InvariantCulture)})));Assert.False((await FreshWebHttp.SendAsync(worker,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());await FreshWebHttp.SendAsync(worker,HttpMethod.Get,Route("201")+"detail/"+Allowed,expected:HttpStatusCode.Forbidden);
            stage="六类逻辑父页面与任务前拒绝";var exercised=await Probe("exercise");foreach(var flag in new[]{"ordinaryWithoutQuery","readonlyDenied","logicalParents","realCliWrites","taskResults"})Assert.True(exercised.GetProperty(flag).GetBoolean());var applied=exercised.GetProperty("allowedRequestIds").EnumerateArray().Select(item=>item.GetString()!).ToArray();var denied=exercised.GetProperty("deniedRequestIds").EnumerateArray().Select(item=>item.GetString()!).ToList();Assert.Equal(6,applied.Length);Assert.Equal(19,denied.Count);
            var ledger=await tasks.ReadAsync("sys_agent_task");Assert.Equal(6,ledger.Count);foreach(var row in ledger){Assert.Contains(SqlRowStore.Text(row,"request_id"),applied);Assert.Equal(manager,SqlRowStore.Id(row,"account_id"));Assert.Equal("applied",SqlRowStore.Text(row,"outcome"));Assert.Equal(Allowed,SqlRowStore.Id(row,"result_id"));}foreach(var request in denied)Assert.Empty(await tasks.ReadAsync("sys_agent_task",Row("request_id",request)));
            stage="物理稀疏位与逻辑详情";var raw=await store.ReadIncludingDeletedAsync(RuleRecordNativePreparation.Prefix+"_source");Assert.Equal(4,raw.Count);foreach(var row in raw){Assert.Null(row["country_id"]);Assert.Null(row["area_id"]);Assert.Equal(Id(Node(2)),SqlRowStore.Text(row,"place_id"));Assert.Equal(Id(Node(3)),SqlRowStore.Text(row,"warehouse_id"));Assert.False(row.ContainsKey("country_name"));}Assert.Equal("禁止",SqlRowStore.Text(raw.Single(row=>SqlRowStore.Id(row)==Blocked),"name"));Assert.Equal("允许-206",SqlRowStore.Text(raw.Single(row=>SqlRowStore.Id(row)==Allowed),"name"));Assert.Equal("允许",SqlRowStore.Text(raw.Single(row=>SqlRowStore.Id(row)==Foreign),"name"));
            foreach(var page in Pages()){var detail=await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Route(page.Id)+"detail/"+Allowed+(page.ParentId is null?"":"?parentId="+page.ParentId));Assert.Equal(Id(Node(0)),detail.GetProperty("data").GetProperty("countryId").GetString());Assert.Equal("国0",detail.GetProperty("data").GetProperty("countryName").GetString());}
            stage="当前撤权不能建任务";await securityStore.UpdateAsync("yz_role_permission",permissions["201"],Row("permission_values","[]"));var revoked=await Probe("revoked");foreach(var flag in new[]{"revokedDirectory","revokedBeforeTask","logout"})Assert.True(revoked.GetProperty(flag).GetBoolean());foreach(var id in revoked.GetProperty("deniedRequestIds").EnumerateArray())Assert.Empty(await tasks.ReadAsync("sys_agent_task",Row("request_id",id.GetString())));Assert.Equal(6,(await tasks.ReadAsync("sys_agent_task")).Count);Assert.Equal("允许-206",SqlRowStore.Text(Assert.Single(await store.ReadAsync(RuleRecordNativePreparation.Prefix+"_source",Row("id",Allowed))),"name"));
            await host.DisposeAsync();host=null;RuleRecordNativePreparation.Verify(copied,true);RuleRecordNativePreparation.Verify(proof);passed=true;await Receipt("verified",new{run,passed,realWeb=true,realOAuth=true,realCli=true,ordinaryWithoutQuery=true,pages=6,readonlyDeniedBeforeTasks=12,wrongParentsDenied=5,scopeDenials=2,currentRevokeBeforeTask=true,appliedTasks=6,sparseRootPreserved=true,logicalReadback=true,browserExecuted=false,concurrentLocksMeasured=false,performanceMeasured=false,cleanupByLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,stage,passed=false,errorType=error.GetType().Name,cleanupByLease=true});throw;}
        finally
        {
            if(host is not null)await host.DisposeAsync();var cliRoot=Path.Combine(evidence,"rule-cli");var cliDirectory=Path.Combine(cliRoot,cliRunId);
            try{if(Directory.Exists(cliDirectory))await TestDirectoryCleanup.DeleteAsync(cliDirectory,cliRoot);}
            catch(Exception cleanupError){await Receipt("credential-cleanup-failure",new{run,errorType=cleanupError.GetType().Name,cleanupByLease=true});if(passed)throw;}
            if(!passed)await Receipt("failed",new{run,passed=false,cleanupByLease=true});
        }
        // 所有过程日志脱敏后留私有证据，标准输出只返回结构结果，不回显账号、令牌、连接或业务正文。
        async Task<string> Run(ProcessStartInfo start,string name)=>await RunOwned(start,name,evidence,ownerPassword,workerPassword,options.ConnectionString,ownerToken,workerToken);
        async Task<JsonElement> Probe(string mode)
        {
            var info=NonRedundantApplicationFixture.Start("node",root);var scenario=new{cliRunId,pages=Pages().Select(page=>new{id=page.Id,parentId=page.ParentId,wrongParentId=page.ParentId is null?null:Id(Node(page.Id switch{"203"=>1,"204"=>2,"205"=>3,_=>0},1))}),blockedId=Id(Blocked),allowedId=Id(Allowed),foreignId=Id(Foreign),deletedId=Id(Deleted),wrongParentId=Id(Node(0,1)),updatePath=Route("201")[4..]+"update"};
            foreach(var argument in new[]{Path.Combine(root,"backend/tests/Metarill.Generation.Tests/RuleRecordCliLiveProbe.mjs"),mode,new Uri(host!.Client.BaseAddress!,"/api").ToString(),catalog.YzProjectId,root,evidence,JsonSerializer.Serialize(scenario)})info.ArgumentList.Add(argument);if(mode=="authorize")info.Environment["METARILL_AGENT_WEB_TOKEN"]=workerToken;using var json=JsonDocument.Parse(await Run(info,"cli-"+mode));return json.RootElement.Clone();
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
        async Task<MetadataCatalog> ReadCatalog(string name)=>JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(copied.PackageRoot,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    /// <summary>父参数保持原字符串ID；混合页同时约束逻辑国家和物理所属国家。</summary>
    private static (string Id,string? ParentId)[] Pages()=>[("201",null),("202",Id(Node(0))),("203",Id(Node(1))),("204",Id(Node(2))),("205",Id(Node(3))),("206",Id(Node(0)))];
    private static long Node(int role,int group=0)=>First+group*10+role;
    private static long Company(int role,int group)=>First+500+group*10+role;
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    private static string Route(string page)=>"/api/admin/auto/"+RuleRecordNativePreparation.Prefix+"_source/"+page+"/";
    /// <summary>合成种子只写本轮独占库：正常/异父路径及受限、可写、异项目和墓碑原记录。</summary>
    private static async Task Seed(SqlRowStore store,long project)
    {
        var prefix=RuleRecordNativePreparation.Prefix;for(var group=0;group<2;group++)for(var role=0;role<4;role++)
        {var row=Row("id",Node(role,group),"yz_project_id",project,"status",1);if(role<3){await store.InsertAsync(prefix+"_company",Row("id",Company(role,group),"name",new[]{"国","区","城"}[role]+group,"yz_project_id",project));row["company_id"]=Company(role,group);}else row["name"]="仓"+group;if(role>0)row[prefix+"_"+(role==1?"country":"area")+"_id"]=Node(role==1?0:1,group);await store.InsertAsync(prefix+"_"+new[]{"country","area","place","warehouse"}[role],row);}
        foreach(var(id,name,other,deleted)in new[]{(Blocked,"禁止",false,false),(Allowed,"允许",false,false),(Foreign,"允许",true,false),(Deleted,"允许",false,true)})
        {var row=Row("id",id,"name",name,"yz_project_id",other?project+1:project,"place_id",Id(Node(2)),"warehouse_id",Id(Node(3)),"owner_country_id",Id(Node(0)));if(deleted)row["delete_no"]=DateTime.UtcNow;await store.InsertAsync(prefix+"_source",row);}
    }
    /// <summary>正式连接先核实例与当前库；不查询其它库业务记录。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin)
    {await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(session.Options.ExpectedDatabase,reader.GetString(0));Assert.Equal(pin.GetProperty("hostname").GetString(),reader.GetString(1));Assert.Equal(pin.GetProperty("serverUuid").GetString(),reader.GetString(2));}
    private static async Task<long> Count(DatabaseSession session)
    {await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()";return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    private static Dictionary<string,object?> Row(params object?[] pairs){var result=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<pairs.Length;index+=2)result.Add((string)pairs[index]!,pairs[index+1]);return result;}
    /// <summary>只拥有自身子进程，超时终止整棵进程树；所有输入秘密在日志落盘前替换。</summary>
    private static async Task<string> RunOwned(ProcessStartInfo start,string name,string evidence,params string[] secrets)
    {
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));var timedOut=false;try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){timedOut=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}var text=await stdout;var error=await stderr;foreach(var secret in secrets.Where(secret=>!string.IsNullOrEmpty(secret)).OrderByDescending(secret=>secret.Length)){text=text.Replace(secret,"[已脱敏]",StringComparison.Ordinal);error=error.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),text+"\n"+error);if(timedOut)throw new TimeoutException("规则正式子进程超时，脱敏证据已保留，未自动重试。");Assert.True(process.ExitCode==0,"规则正式子进程失败，详情仅留私有脱敏日志。");return text;
    }
}
