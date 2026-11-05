using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>无专属配置只跳过；配置存在仍须先验证新用途与直接批准状态。</summary>
public sealed class GlobalCachedCrossExcelNativeFactAttribute:FactAttribute
{
    public GlobalCachedCrossExcelNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable(GlobalCachedCrossExcelNativeLease.EnvironmentKey)))Skip="缺少六模型正式身份单库专属授权，未运行真实Web或MySQL。";}
}

/// <summary>六个原全局缓存级联模型在同一个独占新库、实际Production/Web宿主中验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed partial class GlobalCachedCrossExcelNativeMySqlTests
{
    private const int Worker=959;
    private const long Project=100,Country=9007199254742011,Area=9007199254742021,Place=9007199254742031,Warehouse=9007199254742041,Lookup=9007199254742051;
    /// <summary>只运行固定不可发布准备包；登录、权限、文件及生成Service均使用真实实现。</summary>
    [GlobalCachedCrossExcelNativeFact]
    public Task Production_web_six_models_roundtrip_original_excel_and_rollback_files()=>ExecuteAsync(false);

    /// <summary>公开Full使用独立用途和固定包；禁止用内部包通过记录替代本轮真实身份链。</summary>
    [PublicR0CrossExcelNativeFact]
    public Task Production_public_Full_six_models_roundtrip_original_excel_and_rollback_files()=>ExecuteAsync(true);

    /// <summary>覆盖模式走正规公开包和独占租约，不能借用追加模式的设置。</summary>
    [PublicR0CrossExcelNativeFact(overwrite:true)]
    public Task Production_public_Full_six_models_overwrite_original_excel_and_rollback_files()=>ExecuteAsync(true,overwrite:true);

    /// <summary>档案由固定测试入口选择，设置不能改用其它包或租约。</summary>
    private async Task ExecuteAsync(bool published,bool overwrite=false)
    {
        var root=overwrite?PublicR0CrossExcelNativePreparation.OverwriteRoot
            :published?PublicR0CrossExcelNativePreparation.Root:GlobalCachedCrossExcelNativePreparation.Root;
        var environment=overwrite?PublicR0CrossExcelNativeLease.OverwriteEnvironmentKey
            :published?PublicR0CrossExcelNativeLease.EnvironmentKey:GlobalCachedCrossExcelNativeLease.EnvironmentKey;
        var settingsPath=Environment.GetEnvironmentVariable(environment)!;
        Assert.Equal(Path.GetFullPath(settingsPath),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));var settings=document.RootElement;var source=NonRedundantApplicationFixture.Root();
        var preparedRoot=CSharpGenerator.SafePath(source,root+"/prepared");
        var run=published?PublicR0CrossExcelNativeLease.Validate(settings,preparedRoot,overwrite):GlobalCachedCrossExcelNativeLease.Validate(settings,preparedRoot);
        var proof=settings.GetProperty(published?"PublicCrossExcelPreparation":"GlobalCrossExcelPreparation").Deserialize<CrossTableExcelPackageProof>()!;
        var catalog=Verify(proof);var options=FreshInstallConfiguration.Read("AgentDatabase","agent");
        var loginRedis=overwrite?FreshInstallConfiguration.ReadLoginRedis():null;
        var evidence=CSharpGenerator.SafePath(source,root+"/"+run+"/evidence");if(Directory.Exists(evidence))throw new InvalidOperationException("六模型本轮证据目录已存在，不自动重跑。");Directory.CreateDirectory(evidence);
        var copied=Copy(proof,CSharpGenerator.SafePath(source,root+"/"+run+"/execution/application"));
        var hostDirectory=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host");var storageBoundary=Path.Combine(evidence,"storage");var storage=Path.Combine(storageBoundary,Guid.NewGuid().ToString("N"));var hooks=Path.Combine(evidence,"transaction-hooks.jsonl");
        using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));
        var securityCatalog=await Model("security");var fileCatalog=await Model("files");var security=new SqlRowStore(session,securityCatalog);var files=new SqlRowStore(session,fileCatalog);var store=new SqlRowStore(session,catalog);
        var physical=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(catalog,securityCatalog),fileCatalog),AgentFoundationCatalog.ForApplication(catalog));
        var ownerName="global-cross-excel-owner";var ownerPassword=Guid.NewGuid().ToString("N");var readerPassword=Guid.NewGuid().ToString("N");var permissions=new Dictionary<string,long>(StringComparer.Ordinal);var completed=new List<string>();
        FreshHost? host=null;HttpClient? reader=null;using var competitor=overwrite?new HttpClient{Timeout=TimeSpan.FromSeconds(90)}:null;Exception? primary=null;var passed=false;var stage="空库核对";string? token=null;Probe? active=null;
        /// <summary>秘密仅经环境传入包内实际宿主，禁用Development身份与自动初始化。</summary>
        ProcessStartInfo Info(string? command=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));if(command is not null)info.ArgumentList.Add(command);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Logging__LogLevel__Default"]="Warning";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            if(command is null&&loginRedis is not null)info.Environment["Authentication__LoginFailures__ConnectionString"]=loginRedis;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=ownerName;info.Environment["Authentication__Bootstrap__Password"]=ownerPassword;
            info.Environment["Ids__WorkerId"]=Worker.ToString(CultureInfo.InvariantCulture);info.Environment["Files__RootDirectory"]=Path.Combine(storage,"uploads");info.Environment["Files__LegacyPublicBaseUrl"]="";info.Environment["Files__PublicContentProjectId"]=catalog.YzProjectId;info.Environment["Exports__RootDirectory"]=Path.Combine(storage,"exports");
            info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;info.Environment["METARILL_GLOBAL_CROSS_EXCEL_HOOK_RECEIPT"]=hooks;return info;
        }
        try
        {
            Assert.Equal(0,await TableCount(session));host=await FreshHost.StartAsync(Info(),"/health");Assert.Equal(0,await TableCount(session));await host.DisposeAsync();host=null;
            stage="显式预览／重复结构安装";await Run("--schema-preview");Assert.Equal(0,await TableCount(session));await Run("--schema-apply");await Run("--schema-apply");Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await TableCount(session));Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);
            stage="真实管理员初始化";await Run("--initialize-security");var hash=SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager",Row("name",ownerName))),"password");Assert.True(PasswordHasher.Verify(ownerPassword,hash));await Run("--initialize-security");Assert.Equal(hash,SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager",Row("name",ownerName))),"password"));
            host=await FreshHost.StartAsync(Info(),"/health");await LoginOriginal(host.Client,ownerName,ownerPassword);
            stage="普通账号与页面许可";var ids=new SnowflakeIdGenerator(Worker+1);var role=ids.NextId();var manager=ids.NextId();const string readerName="global-cross-excel-reader";
            await session.InTransactionAsync(async()=>
            {
                await security.ReadForUpdateAsync("yz_project",Row("id",Project));
                await security.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",9L,"name",readerName,"is_super",0,"is_built_in",0));
                await security.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",9L,"name",readerName,"password",PasswordHasher.Hash(readerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Id(role)})));
                foreach(var page in catalog.ConfigPages){var id=ids.NextId();permissions.Add(page.Id,id);await security.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",Project,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));}
                foreach(var grant in GlobalCachedCrossExcelNativePreparation.NavigationGrants(catalog,role,()=>ids.NextId()))await security.InsertAsync("yz_role_permission",grant);
                return true;
            });
            reader=new(){BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(90)};token=await LoginOriginal(reader,readerName,readerPassword);
            Assert.False((await FreshWebHttp.SendAsync(reader,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            if(competitor is not null)
            {
                competitor.BaseAddress=host.Client.BaseAddress;
                var competingToken=await LoginOriginal(competitor,readerName,readerPassword);
                Assert.True(token!=competingToken,"并发请求必须使用不同的正式会话。");
                var identity=await FreshWebHttp.SendAsync(competitor,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");
                Assert.False(identity.GetProperty("data").GetProperty("isSuper").GetBoolean());
            }
            var menu=await FreshWebHttp.SendAsync(reader,HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu");
            await Receipt("ordinary-navigation",new{run,menu=menu.GetProperty("menu"),permissions=menu.GetProperty("permissions")});
            var probes=GlobalCachedCrossExcelMySqlLease.Profiles.Select(p=>
            {
                var profile=GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter);
                return(profile,Probe:new Probe(profile,p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter,catalog,options,session,store,files,fileCatalog,host.Client,reader,security,permissions,hooks));
            }).ToArray();
            foreach(var item in probes){stage=item.profile+"种子";active=item.Probe;await item.Probe.Seed();}
            foreach(var item in probes)
            {
                var profile=item.profile;var probe=item.Probe;active=probe;stage=profile+"正式业务";
                Assert.True(HasPage(menu.GetProperty("menu"),probe.SourcePage));
                if(competitor is not null)await probe.VerifyOverwrite(evidence,source,competitor);else await probe.Verify();
                completed.Add(profile);
                await Receipt(profile,new{run,profile,passed=true,productionWeb=true,ordinaryAccount=true,originalFormThreeFields=true,originalXlsx=true,sparseStorage=true,overwriteMatchedOriginal=overwrite,concurrentSourceWaiters=probe.ConcurrentSourceWaiters,concurrentCandidateWaiters=probe.ConcurrentCandidateWaiters,renamePropagation=!overwrite,originalMaintenance=!overwrite,realPartialDmlAndFileRollback=true,permissionRevocation=true,browserExecuted=false,performanceExecuted=false});
            }
            stage="同地址重启保持真实会话";var address=host.Client.BaseAddress;await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health",address);host.Client.DefaultRequestHeaders.Add("loginStateId",token);
            var first=catalog.ConfigTables.First(value=>value.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal));var firstPage=catalog.ConfigPages.Single(value=>value.ConfigTableId==first.Id);var route="/api/admin/auto/"+first.TableName+"/"+firstPage.Id+"/page";
            await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,route,new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,route,new{},HttpStatusCode.Unauthorized);
            await host.DisposeAsync();host=null;Verify(copied,true);Verify(proof);Assert.Equal(6,completed.Count);
            if(published)foreach(var table in catalog.ConfigTables)Assert.InRange((await store.ReadIncludingDeletedAsync(table.TableName)).Count,0,PublicR0CrossExcelNativeLease.MaximumBusinessRows);Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);passed=true;
            await Receipt("verified",new{run,passed,completed,productionWeb=true,preparedNotPublishedFull=!published,publicFullApplication=published,fixedAuthorizationInjected=false,ordinaryAccount=true,originalFormThreeFields=true,persistentSession=true,tableCount=await TableCount(session),browserExecuted=false,performanceExecuted=false,databaseCleanupByLease=true});
        }
        catch(Exception error){primary=error;await Receipt("failure",new{run,stage,completed,passed=false,lastOperation=active?.Stage,errorType=error.GetType().Name,error.StackTrace,databaseCleanupByLease=true});throw;}
        finally
        {
            reader?.Dispose();var stopped=host is null;
            try{if(host is not null){await host.DisposeAsync();stopped=true;}if(!stopped)throw new InvalidOperationException("六模型宿主尚未确认停止，保留存储现场。");if(Directory.Exists(storage))await TestDirectoryCleanup.DeleteAsync(storage,storageBoundary);await Receipt("local-cleanup",new{run,stopped,storageRemoved=!Directory.Exists(storage),databaseCleanupByLease=true});}
            catch(Exception cleanup){await Receipt("local-cleanup-failure",new{run,errorType=cleanup.GetType().Name});if(primary is not null)throw new AggregateException("六模型正式验收及本地清理均失败。",primary,cleanup);throw;}
        }
        /// <summary>公开标记与内部准备标记严格分开验证，不能靠业务流程共享替换包。</summary>
        MetadataCatalog Verify(CrossTableExcelPackageProof value,bool runtime=false)=>published?PublicR0CrossExcelNativePreparation.Verify(value,runtime,overwrite):GlobalCachedCrossExcelNativePreparation.Verify(value,runtime);
        /// <summary>两档案均先核验固定文件，执行目录只允许首次创建。</summary>
        CrossTableExcelPackageProof Copy(CrossTableExcelPackageProof value,string destination)=>published?PublicR0CrossExcelNativePreparation.CopyForExecution(value,destination,overwrite):GlobalCachedCrossExcelNativePreparation.CopyForExecution(value,destination);
        /// <summary>回执只保留本轮业务阶段，不保存口令、令牌或连接。</summary>
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
        /// <summary>只读取固定包内已核摘要的基础模型。</summary>
        async Task<MetadataCatalog> Model(string name)=>JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(copied.PackageRoot,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        /// <summary>六个原命令单次执行，日志脱敏，不因结果未知自动重试。</summary>
        Task Run(string command)=>RunOwned(Info(command),evidence,command.TrimStart('-')+"-"+Guid.NewGuid().ToString("N"),options.ConnectionString,ownerPassword,readerPassword);
    }
    /// <summary>原form明确提交三个原字段；登录算法仍由真实宿主处理。</summary>
    private static async Task<string> LoginOriginal(HttpClient client,string name,string password)
    {
        using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"username",name},{"password",password},{"captchaVerification",""}});
        var response=await FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/sys/yz_manager/login",form);var token=response.GetProperty("map").GetProperty("loginStateId").GetString();
        Assert.True(token is{Length:64}&&token.All(value=>value is >= '0' and <= '9' or >= 'a' and <= 'f'));client.DefaultRequestHeaders.Remove("loginStateId");client.DefaultRequestHeaders.Add("loginStateId",token);return token!;
    }
    /// <summary>首次DDL前独立连接核对库名、Server-200实例与UUID。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var row=await command.ExecuteReaderAsync();Assert.True(await row.ReadAsync()&&row.GetString(0)==session.Options.ExpectedDatabase&&row.GetString(1)==pin.GetProperty("hostname").GetString()&&row.GetString(2)==pin.GetProperty("serverUuid").GetString());
    }
    /// <summary>仅从information_schema读取当前唯一租约库的表数。</summary>
    private static async Task<int> TableCount(DatabaseSession session){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()";return Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>只停止本用例启动的进程，消化双管道并剔除临时秘密后保存日志。</summary>
    private static async Task RunOwned(ProcessStartInfo info,string evidence,string name,params string[] secrets)
    {
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var expired=false;
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){expired=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}
        var output=await stdout;var error=await stderr;var builder=new MySqlConnectionStringBuilder(info.Environment["Database__ConnectionString"]!);
        foreach(var secret in secrets.Append(builder.Password).Where(value=>!string.IsNullOrEmpty(value)).OrderByDescending(value=>value.Length)){output=output.Replace(secret,"[已脱敏]",StringComparison.Ordinal);error=error.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}
        await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),output+"\n"+error);if(expired)throw new TimeoutException("六模型原生命令超时，脱敏证据保留。");Assert.True(process.ExitCode==0,"六模型原生命令失败，详见私有脱敏证据。");
    }
    /// <summary>固定后台／项目的真实菜单递归找页，不能借其它后台菜单计通过。</summary>
    private static bool HasPage(JsonElement rows,string page)=>rows.EnumerateArray().Any(value=>value.GetProperty("projectId").GetString()=="100"&&value.GetProperty("adminTypeId").GetString()=="9"&&(value.TryGetProperty("target",out var target)&&target.ValueKind==JsonValueKind.Object&&target.GetProperty("pageId").GetString()==page||HasPage(value.GetProperty("children"),page)));
    /// <summary>参数化种子只进入本轮已核对的新库。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<values.Length;index+=2)row.Add((string)values[index]!,values[index+1]);return row;}
    /// <summary>HTTP与JSON边界始终使用精确的字符串ID。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
