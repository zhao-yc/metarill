using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>仅专属配置能够启用真实验收；普通CI明确跳过，配置错误不得静默跳过。</summary>
public sealed class ArrayTargetNativeTheoryAttribute:TheoryAttribute
{
    /// <summary>只查看是否配置，不读取凭据、创建目录或打开连接。</summary>
    public ArrayTargetNativeTheoryAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_GLOBAL_ARRAY_NATIVE_SETTINGS")))Skip="未配置本轮全局数组目标的专属批准租约，真实MySQL未执行。";}
}

/// <summary>固定八个原完整应用执行真实Production/Web；种子/核验/故障注入之外不直写业务。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed partial class FreshGlobalArrayTargetMySqlTests
{
    private const long Project=100,First=9007199254748501,ForeignId=9007199254749501;
    private const string Source="/api/admin/auto/cached_consumer/30/",Middle="/api/admin/auto/global_source/10/",Target="/api/admin/auto/global_target/20/",Files="/api/admin/sys/file-library/";
    /// <summary>缓存／实时目标、两种来源存储及内层两种物理键分别使用八个独占库。</summary>
    [ArrayTargetNativeTheory]
    [InlineData(0,0,"int64")][InlineData(0,0,"string")][InlineData(0,1,"int64")][InlineData(0,1,"string")]
    [InlineData(1,0,"int64")][InlineData(1,0,"string")][InlineData(1,1,"int64")][InlineData(1,1,"string")]
    public async Task Original_arrays_run_real_web_mysql_maintenance_and_rollback(int targetMode,int sourceMode,string inner)
    {
        var model=new GlobalArrayTargetNativeModel(targetMode,sourceMode,inner);model.Validate();var suffix=model.Suffix;var worker=model.Worker;
        var path=Environment.GetEnvironmentVariable("METARILL_GLOBAL_ARRAY_NATIVE_SETTINGS")!;
        Assert.Equal(Path.GetFullPath(path),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(path));var settings=document.RootElement;var root=NonRedundantApplicationFixture.Root();
        var run=GlobalArrayTargetNativeLease.Validate(settings,CSharpGenerator.SafePath(root,".local/global-array-target-native/prepared"));
        var proof=settings.GetProperty("ArrayTargetPreparation").GetProperty(suffix).Deserialize<GlobalCachedPackageProof>()!;
        var catalog=GlobalArrayTargetNativePreparation.Verify(proof,model);var options=FreshInstallConfiguration.Read(suffix,suffix);
        var evidence=CSharpGenerator.SafePath(root,".local/global-array-target-native/"+run+"/"+suffix);Directory.CreateDirectory(evidence);
        var copied=GlobalArrayTargetNativePreparation.CopyForExecution(proof,Path.Combine(evidence,"execution/application"),model);
        var package=copied.PackageRoot;var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");
        var username="array-owner-"+suffix;var password=Guid.NewGuid().ToString("N");FreshHost? host=null;var stage="实例/零表";var passed=false;
        using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
        /// <summary>仅子进程环境传递秘密，正式身份和文件/导出根固定在本轮包外。</summary>
        ProcessStartInfo Info(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Logging__LogLevel__Default"]="Warning";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;info.Environment["Ids__WorkerId"]=worker.ToString(CultureInfo.InvariantCulture);
            info.Environment["Files__LegacyPublicBaseUrl"]="";info.Environment["Files__PublicContentProjectId"]="100";info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"uploads");info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
        }
        try
        {
            stage="普通启动及显式预览不改空库";host=await FreshHost.StartAsync(Info(),"/health");Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));await host.DisposeAsync();host=null;
            await NonRedundantApplicationFixture.RunAsync(Info("--schema-preview"));Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
            stage="显式安装和重复初始化";await NonRedundantApplicationFixture.RunAsync(Info("--schema-apply"));await NonRedundantApplicationFixture.RunAsync(Info("--schema-apply"));Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            await NonRedundantApplicationFixture.RunAsync(Info("--initialize-security"));var security=new SqlRowStore(session,Model(package,"security"));var digest=SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager",Row("name",username))),"password");Assert.True(PasswordHasher.Verify(password,digest));await NonRedundantApplicationFixture.RunAsync(Info("--initialize-security"));Assert.Equal(digest,SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager",Row("name",username))),"password"));
            var store=new SqlRowStore(session,catalog);await store.InsertAsync("global_target",Row("id",First,"name","甲_%!名称'","status",1));await store.InsertAsync("global_target",Row("id",First+1,"name","乙名称","status",1));
            var ids=new SnowflakeIdGenerator(worker);var role=ids.NextId();var manager=ids.NextId();var reader="array-reader-"+suffix;var secret=Guid.NewGuid().ToString("N");var grants=new Dictionary<string,long>(StringComparer.Ordinal);
            await session.InTransactionAsync(async()=>{await security.ReadForUpdateAsync("yz_project",Row("id",Project));await security.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",9L,"name",reader,"is_super",0,"is_built_in",0));await security.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",9L,"name",reader,"password",PasswordHasher.Hash(secret),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Id(role)})));foreach(var page in catalog.ConfigPages){var id=ids.NextId();grants.Add(page.Id,id);await security.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",Project,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));}return true;});
            host=await FreshHost.StartAsync(Info(),"/health");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,Source+"page",new{},HttpStatusCode.Unauthorized);await GlobalDerivedTargetNativeHttp.LoginAsync(host.Client,username,password);
            using var client=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(60)};var token=await GlobalDerivedTargetNativeHttp.LoginAsync(client,reader,secret);Assert.False((await FreshWebHttp.SendAsync(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="正式候选/保存/传播/回填/回滚";var probe=new Probe(client,host.Client,session,store,new SqlRowStore(session,Model(package,"files")),security,grants,model,value=>stage=value);await probe.Exercise();
            stage="重启后原会话及注销";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Source+"detail/"+probe.RecordId);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Source+"detail/"+probe.RecordId,expected:HttpStatusCode.Unauthorized);
            GlobalArrayTargetNativePreparation.Verify(proof,model);GlobalArrayTargetNativePreparation.Verify(copied,model,true);Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            passed=true;await Receipt("receipt",new{run,suffix,passed,productCommit=GlobalArrayTargetNativePreparation.ProductCommit,targetMode,sourceMode,inner,automaticNames=model.Automatic,formalWeb=true,ordinaryAccount=true,developmentAuthentication=false,authorizationInjected=false,originalArrayControls=true,physicalStorageChecked=true,independentArrayRoles=true,middleForeignKeySwitch=true,softDelete=true,sourceProjectPhysicalIsolation=true,renamePropagation=true,maintenanceBackfill=sourceMode==1||targetMode==1,crossBatchConsumers=sourceMode==1?130:0,lateMaintenanceRollback=sourceMode==1,latePropagationRollback=sourceMode==1,originalImportExport=true,checkboxExcelUsesIds=sourceMode==1,sourceRevocation=true,type2QueryInheritance=true,lateFileRollback=true,largeCheckboxScenario=targetMode==0&&sourceMode==1&&inner=="string",persistentSession=true,browserExecuted=false,deviceExecuted=false,concurrencyExecuted=false,performanceExecuted=false,cleanupByFreshLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,suffix,stage,errorType=error.GetType().Name,passed=false,cleanupByFreshLease=true});throw;}
        finally{if(host is not null)await host.DisposeAsync();if(!passed)await Receipt("failed",new{run,suffix,passed=false,cleanupByFreshLease=true});}
        /// <summary>回执不保存请求、凭据或连接，失败位置供冻结重验使用。</summary>
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>DDL之前同时核对随机库、Server-200实例主机及UUID。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var row=await command.ExecuteReaderAsync();Assert.True(await row.ReadAsync()&&row.GetString(0)==session.Options.ExpectedDatabase&&row.GetString(1)==pin.GetProperty("hostname").GetString()&&row.GetString(2)==pin.GetProperty("serverUuid").GetString(),"实例pin不符，未执行DDL。");}
    /// <summary>仅使用核准会话只读表数量。</summary>
    private static async Task<long> Scalar(DatabaseSession session,string sql){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>基础设施元数据只读本完整包，不接受外部目录。</summary>
    private static MetadataCatalog Model(string package,string name)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(package,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>种子及篡改使用既有存储；不手写实体、Mapper或CRUD。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<pairs.Length;i+=2)row.Add((string)pairs[i]!,pairs[i+1]);return row;}
    /// <summary>JSON边界ID始终使用十进制字符串。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
