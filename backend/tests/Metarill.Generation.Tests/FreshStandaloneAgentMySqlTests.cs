using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Agent;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>专属单库租约与固定完整包的正式OAuth/CLI验收；没有租约时明确跳过，不以开发身份替代。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshStandaloneAgentMySqlTests
{
    /// <summary>从0表显式安装，真实Web批准与CLI业务/任务/轮转，重启后核验同一持久Web身份。</summary>
    [FreshInstallMySqlFact]
    public async Task Prepared_full_application_uses_real_web_oauth_cli_and_owned_database()
    {
        var options=FreshInstallConfiguration.Read("AgentDatabase","agent");
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var run=settings.RootElement.GetProperty("RunId").GetString()!;
        var preparation=settings.RootElement.GetProperty("AgentPreparation");
        // 正式执行器可指向已逐字固定的独立检出，不依赖继续开发中的源码。
        var root=Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_AGENT_NATIVE_SOURCE_ROOT")??NonRedundantApplicationFixture.Root());var package=CSharpGenerator.SafePath(root,".local/standalone-agent-native/package");
        Assert.Equal(package,Path.GetFullPath(preparation.GetProperty("PackageRoot").GetString()!));
        var evidence=CSharpGenerator.SafePath(root,".local/standalone-agent-native/"+run);Directory.CreateDirectory(evidence);
        var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");
        var assembly=CSharpGenerator.SafePath(hostDirectory,"bin/Debug/net10.0/Application.Host.dll");
        await VerifyPreparedPackage(package,preparation,assembly);
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(package,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("2200000000000100000",catalog.YzProjectId);Assert.Single(catalog.ConfigTables);Assert.Equal("demo_customer",catalog.ConfigTables[0].TableName);
        var infrastructure=MetadataCatalogComposition.MergePhysical(catalog,AgentFoundationCatalog.ForApplication(catalog));
        foreach(var name in new[]{"files","security"})infrastructure=MetadataCatalogComposition.MergePhysical(infrastructure,JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(package,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        using var session=new DatabaseSession(options);await VerifyInstance(session,settings.RootElement.GetProperty("InstancePin"));Assert.Equal(0,await Scalar(session,"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()"));
        var username="agent-native-owner";var password=Guid.NewGuid().ToString("N");var stage="普通启动保持空库";FreshHost? host=null;var passed=false;
        // 仅本轮合成账号和配置进入环境；HTTPS示例用于设备协议，不宣称浏览器页面真实可达。
        ProcessStartInfo HostInfo(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(assembly);if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;
            info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";info.Environment["Ids__WorkerId"]="1011";
            info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"files");info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            return info;
        }
        try
        {
            host=await FreshHost.StartAsync(HostInfo(),"/health");Assert.Equal(0,await Scalar(session,"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()"));await host.DisposeAsync();host=null;
            stage="显式结构预览及安装";var preview=await RunOwnedProcess(HostInfo("--schema-preview"),"preview",evidence,password,options.ConnectionString);using(var plan=JsonDocument.Parse(preview))Assert.Equal(infrastructure.ConfigTables.Count,plan.RootElement.GetProperty("changes").EnumerateArray().Count(change=>change.GetProperty("kind").GetString()=="createTable"));
            Assert.Equal(0,await Scalar(session,"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()"));await RunOwnedProcess(HostInfo("--schema-apply"),"apply",evidence,password,options.ConnectionString);
            Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await Scalar(session,"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()"));Assert.Empty((await new SchemaService(session).PreviewAsync(infrastructure)).Changes);
            stage="正式安全初始化";await RunOwnedProcess(HostInfo("--initialize-security"),"initialize",evidence,password,options.ConnectionString);
            host=await FreshHost.StartAsync(HostInfo(),"/health");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,"/api/admin/sys/agent/grants",expected:HttpStatusCode.Unauthorized);
            var token=await FreshWebHttp.LoginAsync(host.Client,username,password,form:true);Assert.True((await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="原CLI设备拒绝批准及真实业务";var node=NonRedundantApplicationFixture.Start("node",root);foreach(var argument in new[]{Path.Combine(root,"backend/tests/Metarill.Generation.Tests/StandaloneAgentCliLiveProbe.mjs"),new Uri(host.Client.BaseAddress!,"/api").ToString(),catalog.ConfigPages.Single().Id,catalog.YzProjectId,root,evidence})node.ArgumentList.Add(argument);node.Environment["METARILL_AGENT_WEB_TOKEN"]=token;
            var cli=await RunOwnedProcess(node,"cli",evidence,password,options.ConnectionString,token);using(var proof=JsonDocument.Parse(cli))foreach(var flag in new[]{"deviceDenied","deviceApproved","realCli","strictCatalog","preview","create","detail","explicitNull","stringId","task","duplicateUuid","delete","revoke","refreshReplay"})Assert.True(proof.RootElement.GetProperty(flag).GetBoolean());
            stage="独立库内核验";Assert.Equal(1,await Scalar(session,"SELECT COUNT(*) FROM demo_customer"));Assert.Equal(0,await Scalar(session,"SELECT COUNT(*) FROM demo_customer WHERE delete_no IS NULL"));Assert.Equal(2,await Scalar(session,"SELECT COUNT(*) FROM sys_agent_task WHERE outcome='applied'"));Assert.Equal(2,await Scalar(session,"SELECT COUNT(*) FROM sys_agent_grant WHERE status='revoked'"));Assert.Equal(1,await Scalar(session,"SELECT COUNT(*) FROM sys_agent_grant WHERE status='denied'"));
            stage="重启与同一Web会话";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(HostInfo(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,"/api/admin/sys/agent/grants");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,"/api/admin/sys/agent/grants",expected:HttpStatusCode.Unauthorized);
            Assert.Empty((await new SchemaService(session).PreviewAsync(infrastructure)).Changes);passed=true;
            await Receipt("receipt",new{run,passed,formalWeb=true,realCli=true,productionEnvironment=true,developmentAuthentication=false,authorizationInjected=false,tableCount=infrastructure.ConfigTables.Count,deviceApi=true,browserApprovalExecuted=false,agentRestartExecuted=false,webSessionRestart=true,nativeDeviceExecuted=false,performanceExecuted=false,cleanupByLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,stage,passed=false,errorType=error.GetType().Name,cleanupByLease=true});throw;}
        finally{if(host is not null)await host.DisposeAsync();if(!passed)await Receipt("failed",new{run,passed=false,cleanupByLease=true});}
        // 回执没有令牌、连接或业务正文，实际租约执行器负责销库、销账号及独立核零。
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }

    /// <summary>逐文件验证完整包清单和实际宿主程序集，拒绝替换、链接及未固定的路径。</summary>
    private static async Task VerifyPreparedPackage(string package,JsonElement preparation,string assembly)
    {
        var manifest=CSharpGenerator.SafePath(package,".metarill-package.json");Assert.Equal(preparation.GetProperty("PackageManifestSha256").GetString(),Hash(manifest));Assert.Equal(preparation.GetProperty("AssemblySha256").GetString(),Hash(assembly));
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(manifest));Assert.Equal("FullApplication",json.RootElement.GetProperty("packageMode").GetString());Assert.Equal("web-database-v1",json.RootElement.GetProperty("authenticationProfile").GetString());
        foreach(var file in json.RootElement.GetProperty("files").EnumerateArray())Assert.Equal(file.GetProperty("sha256").GetString(),Hash(CSharpGenerator.SafePath(package,file.GetProperty("path").GetString()!)));
    }
    /// <summary>散列只返回摘要，不在异常中打印文件内容。</summary>
    private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    /// <summary>每次正式验收在DDL之前匹配数据库及已审批实例指纹。</summary>
    private static async Task VerifyInstance(DatabaseSession session,JsonElement pin)
    {
        await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var row=await command.ExecuteReaderAsync();Assert.True(await row.ReadAsync()&&row.GetString(0)==session.Options.ExpectedDatabase&&row.GetString(1)==pin.GetProperty("hostname").GetString()&&row.GetString(2)==pin.GetProperty("serverUuid").GetString(),"Agent验收实例pin不符，未执行DDL。");
    }
    /// <summary>固定验收SQL只读本轮库，不读取凭据或其他库业务数据。</summary>
    private static async Task<long> Scalar(DatabaseSession session,string sql){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>只拥有自身进程，有界等待并消费两条管道；秘密脱敏后写私有日志，不自动重试。</summary>
    private static async Task<string> RunOwnedProcess(ProcessStartInfo start,string name,string evidence,params string[] secrets)
    {
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var timedOut=false;try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){timedOut=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}
        var text=await stdout;var errors=await stderr;foreach(var secret in secrets.Where(value=>value.Length>0).OrderByDescending(value=>value.Length)){text=text.Replace(secret,"[已脱敏]",StringComparison.Ordinal);errors=errors.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}
        await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),text+"\n"+errors);if(timedOut)throw new TimeoutException("本轮Agent验收进程超时，脱敏日志已保留，未自动重试。");Assert.True(process.ExitCode==0,"本轮Agent验收子进程失败，详见私有脱敏日志；未自动重试。");return text;
    }
}
