using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>没有本用途专属配置就明确跳过，不把普通测试配置或旧租约当作本轮授权。</summary>
public sealed class ManagementNativeFactAttribute:FactAttribute
{
    /// <summary>没有专属配置时明确未执行，不回落普通测试连接。</summary>
    public ManagementNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_MANAGEMENT_NATIVE_SETTINGS")))Skip="缺完整管理专属批准租约，同包正式验收未执行。";}
}

/// <summary>公开FullManagementApplication同包运行，生产身份与真实MySQL/CLI/MCP；外层独占租约负责创建及清理。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class ManagementNativeMySqlTests
{
    private const string ManagerPage="6031906822946816",RolePage="1692532858529329154",AdminPage="1696799309006348290",Workbench="/api/admin/sys/yzp_permission";
    /// <summary>原空库/正式Web/管理CRUD/OAuth/CLI/MCP/工作台/保留ID初始化在同一个固定完整包中完成。</summary>
    [ManagementNativeFact]
    public async Task Fixed_public_management_package_uses_real_identity_database_cli_mcp_and_initialization()
    {
        var path=Environment.GetEnvironmentVariable("METARILL_MANAGEMENT_NATIVE_SETTINGS")!;Assert.Equal(Path.GetFullPath(path),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));using var settingsDocument=JsonDocument.Parse(await File.ReadAllTextAsync(path));var settings=settingsDocument.RootElement;
        var root=Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_MANAGEMENT_NATIVE_SOURCE_ROOT")??NonRedundantApplicationFixture.Root());var prepared=CSharpGenerator.SafePath(root,".local/management-full-native/prepared");var run=ManagementNativeLease.Validate(settings,prepared);var options=FreshInstallConfiguration.Read("AgentDatabase","agent");var proof=settings.GetProperty("ManagementPreparation").Deserialize<GlobalCachedPackageProof>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var model=ManagementNativePreparation.Verify(proof,root);var copied=ManagementNativePreparation.CopyForExecution(proof,root,CSharpGenerator.SafePath(root,".local/management-full-native/"+run+"/execution/application"));var evidence=CSharpGenerator.SafePath(root,".local/management-full-native/"+run+"/evidence");Directory.CreateDirectory(evidence);
        var physical=model.Physical;foreach(var name in new[]{"files","security"})physical=MetadataCatalogComposition.MergePhysical(physical,JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(copied.PackageRoot,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!);physical=MetadataCatalogComposition.MergePhysical(physical,AgentFoundationCatalog.ForApplication(physical));
        using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));var store=new SqlRowStore(session,physical);var databasePassword=new MySqlConnectionStringBuilder(options.ConnectionString).Password;var filesRoot=Path.Combine(evidence,"files",Guid.NewGuid().ToString("N"));
        var hostDirectory=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host");var assembly=CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll");const string owner="management-owner",foreignOwner="management-foreign-owner",editor="management-editor";var password=Guid.NewGuid().ToString("N");var newPassword=Guid.NewGuid().ToString("N");var stage="空库普通启动";var passed=false;FreshHost? host=null;TrustedManagementMcpProbe? mcp=null;
        // 凭据只走本轮子进程环境，原生产认证/控制器/Service均不替换。
        ProcessStartInfo Info(string? operation=null,string? admin=null,string? username=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(assembly);if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username??owner;info.Environment["Authentication__Bootstrap__Password"]=password;info.Environment["Authentication__Bootstrap__AdminTypeId"]=admin??ManagementNativePreparation.Admin;
            info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";info.Environment["Ids__WorkerId"]="1012";info.Environment["Files__RootDirectory"]=filesRoot;info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            // 仅供现有fixture日志脱敏器读取，生产宿主不消费此测试变量。
            info.Environment["METARILL_ACCEPTANCE_REDACTION_SECRET"]=newPassword;return info;
        }
        try
        {
            host=await FreshHost.StartAsync(Info(),"/health");foreach(var route in new[]{Route("yz_role",RolePage)+"/page",Route("yz_manager",ManagerPage)+"/page"})await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,route,new{},HttpStatusCode.Unauthorized);Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));await host.DisposeAsync();host=null;
            stage="显式结构安装";var preview=await Run(Info("--schema-preview"),"schema-preview");using(var plan=JsonDocument.Parse(preview))Assert.Equal(physical.ConfigTables.Count,plan.RootElement.GetProperty("changes").EnumerateArray().Count(change=>change.GetProperty("kind").GetString()=="createTable"));Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));await Run(Info("--schema-apply"),"schema-apply");Assert.Equal((long)physical.ConfigTables.Count,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);
            stage="两个已发布后台显式初始化";await Run(Info("--initialize-security"),"initialize-owner");await Run(Info("--initialize-security",ManagementNativePreparation.ForeignAdmin,foreignOwner),"initialize-foreign");var originalOwner=Fingerprint(await Account(owner));await Run(Info("--initialize-security"),"initialize-unchanged");Assert.Equal(originalOwner,Fingerprint(await Account(owner)));
            host=await FreshHost.StartAsync(Info(),"/health");var rootToken=await FreshWebHttp.LoginAsync(host.Client,owner,password,form:true);Assert.True((await Send(host.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="原工作台为普通身份配置动作";var roleId=Id(await Send(host.Client,HttpMethod.Post,Route("yz_role",RolePage)+"/add",new{name="management-editor-role",isSuper=0,sortNo=10,yzAdminTypeId=ManagementNativePreparation.Admin}));var snapshot=(await Send(host.Client,HttpMethod.Post,Workbench+"/snapshot",new{roleId})).GetProperty("data");
            var pagePermissions=snapshot.GetProperty("catalog").GetProperty("pages").EnumerateArray().ToDictionary(page=>page.GetProperty("id").GetString()!,page=>page.GetProperty("actions").EnumerateArray().Select(action=>action.GetString()!).ToArray(),StringComparer.Ordinal);
            await Send(host.Client,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(roleId,snapshot.GetProperty("revision").GetString()!,new(model.Published.ConfigMenus.Where(menu=>menu.YzAdminTypeId==ManagementNativePreparation.Admin).Select(menu=>menu.Id).ToArray(),pagePermissions,[])));
            var editorId=Id(await Send(host.Client,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/add",new{name=editor,realName="必须保留的普通档案",password,isDisabled=1,yzAdminTypeId=ManagementNativePreparation.Admin,yzRoleIds=new[]{roleId}}));
            using var ordinary=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(25)};var editorToken=await FreshWebHttp.LoginAsync(ordinary,editor,password,form:true);Assert.False((await Send(ordinary,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            using(var foreign=new HttpClient{BaseAddress=host.Client.BaseAddress}){await FreshWebHttp.LoginAsync(foreign,foreignOwner,password,form:true);await FreshWebHttp.SendAsync(foreign,HttpMethod.Post,Route("yz_role",RolePage)+"/page",new{},HttpStatusCode.Forbidden);}
            stage="同包真实OAuth和CLI";var node=NonRedundantApplicationFixture.Start("node",root);foreach(var arg in new[]{Path.Combine(root,"backend/tests/Metarill.Generation.Tests/StandaloneAgentCliLiveProbe.mjs"),new Uri(host.Client.BaseAddress!,"/api").ToString(),ManagementNativePreparation.Catalog(root).ConfigPages.Single().Id,physical.YzProjectId,root,evidence})node.ArgumentList.Add(arg);node.Environment["METARILL_AGENT_WEB_TOKEN"]=editorToken;var cli=await Run(node,"real-cli",editorToken);using(var cliProof=JsonDocument.Parse(cli))foreach(var flag in new[]{"deviceDenied","deviceApproved","realCli","strictCatalog","preview","create","detail","explicitNull","stringId","task","duplicateUuid","delete","revoke","refreshReplay"})Assert.True(cliProof.RootElement.GetProperty(flag).GetBoolean());
            stage="普通编辑者管理CRUD及跨后台回滚";var superRole=Id(await Send(ordinary,HttpMethod.Post,Route("yz_role",RolePage)+"/add",new{name="management-assigned-super",isSuper=1,sortNo=11,yzAdminTypeId=ManagementNativePreparation.Admin}));var child=Id(await Send(ordinary,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/add",new{name="management-child",realName="原表单创建",password,isDisabled=1,yzAdminTypeId=ManagementNativePreparation.Admin,yzRoleIds=new[]{roleId}}));var before=Fingerprint(await Account("management-child"));
            var foreignRole=SqlRowStore.Text(Assert.Single(await store.ReadAsync("yz_role",Row("yz_admin_type_id",long.Parse(ManagementNativePreparation.ForeignAdmin,CultureInfo.InvariantCulture)))),"id");await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/update",new{id=child,realName="失败不得保存",yzRoleIds=new[]{foreignRole}},HttpStatusCode.BadRequest);Assert.Equal(before,Fingerprint(await Account("management-child")));
            await Send(ordinary,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/update",new{id=child,yzRoleIds=new[]{superRole}});using(var childClient=new HttpClient{BaseAddress=host.Client.BaseAddress}){await FreshWebHttp.LoginAsync(childClient,"management-child",password,form:true);Assert.True((await Send(childClient,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());}
            var roleSnapshot=(await Send(ordinary,HttpMethod.Post,Workbench+"/snapshot",new{roleId=superRole})).GetProperty("data");await Send(ordinary,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(superRole,roleSnapshot.GetProperty("revision").GetString()!,new([],new(){{RolePage,["is_query"]}},[])));var permissionBefore=Fingerprint(await store.ReadIncludingDeletedAsync("yz_role_permission",Row("yz_role_id",long.Parse(superRole,CultureInfo.InvariantCulture))));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(superRole,roleSnapshot.GetProperty("revision").GetString()!,new([],new(),[])),HttpStatusCode.BadRequest);Assert.Equal(permissionBefore,Fingerprint(await store.ReadIncludingDeletedAsync("yz_role_permission",Row("yz_role_id",long.Parse(superRole,CultureInfo.InvariantCulture)))));
            stage="同包真实MCP及原工作台撤权";mcp=await TrustedManagementMcpProbe.StartAsync(host.Client.BaseAddress!.ToString().TrimEnd('/'),editorToken,store,long.Parse(physical.YzProjectId,CultureInfo.InvariantCulture),long.Parse(ManagementNativePreparation.Admin,CultureInfo.InvariantCulture),"management-native",evidence);await mcp.VerifyOperationsAsync();var current=(await Send(host.Client,HttpMethod.Post,Workbench+"/snapshot",new{roleId})).GetProperty("data");await Send(host.Client,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(roleId,current.GetProperty("revision").GetString()!,new([],new(),[])));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,Route("yz_role",RolePage)+"/page",new{},HttpStatusCode.Forbidden);await mcp.VerifyRevocationAsync();await Send(ordinary,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await mcp.VerifyLogoutAsync();await mcp.DisposeAsync();mcp=null;
            stage="保留账号ID初始化及异后台同名拒绝";await FreshWebHttp.LoginAsync(ordinary,editor,password,form:true);var editorBefore=await Account(editor);Assert.Equal(editorId,SqlRowStore.Text(editorBefore,"id"));Assert.DoesNotContain(await store.ReadIncludingDeletedAsync("sys_agent_grant",Row("account_id",long.Parse(editorId,CultureInfo.InvariantCulture))),grant=>SqlRowStore.Text(grant,"status")=="approved");
            await Send(host.Client,HttpMethod.Post,Route("yz_admin_type",AdminPage)+"/update",new{id=ManagementNativePreparation.Admin,userName=foreignOwner,password=newPassword});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/custom/index/init_admin",new{id=ManagementNativePreparation.Admin},HttpStatusCode.BadRequest);Assert.Equal(Fingerprint(editorBefore),Fingerprint(await Account(editor)));
            await Send(host.Client,HttpMethod.Post,Route("yz_admin_type",AdminPage)+"/update",new{id=ManagementNativePreparation.Admin,userName=editor,password=newPassword});await Send(host.Client,HttpMethod.Post,"/api/admin/custom/index/init_admin",new{id=ManagementNativePreparation.Admin});var after=await Account(editor);foreach(var column in new[]{"id","name","real_name"})Assert.Equal(SqlRowStore.Text(editorBefore,column),SqlRowStore.Text(after,column));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions",expected:HttpStatusCode.Unauthorized);var renewed=await FreshWebHttp.LoginAsync(ordinary,editor,newPassword,form:true);Assert.True((await Send(ordinary,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());await Send(host.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");
            stage="同包重启持久Web会话";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",rootToken);await Send(host.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");using(var restarted=new HttpClient{BaseAddress=host.Client.BaseAddress}){restarted.DefaultRequestHeaders.Add("loginStateId",renewed);await Send(restarted,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");await Send(restarted,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(restarted,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions",expected:HttpStatusCode.Unauthorized);}
            Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);ManagementNativePreparation.Verify(copied,root,runtime:true);ManagementNativePreparation.Verify(proof,root);passed=true;await Receipt("verified");
        }
        catch(Exception error){await Receipt("failure",error.GetType().Name);throw;}
        finally{if(mcp is not null)await mcp.DisposeAsync();if(host is not null)await host.DisposeAsync();}

        // 所有证据只写本轮私有目录，不输出HTTP正文、口令或连接。
        Task Receipt(string name,string? error=null)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(new{run,stage,passed,error,packageMode="FullManagementApplication",samePackage=true,developmentAuthentication=false,identityInjected=false,tableCount=physical.ConfigTables.Count,formalWeb=passed,realCli=passed,realMcp=passed,initializationPreservedId=passed,activeAgentResetPolicyExecuted=false,browserApprovalExecuted=false,nativeDeviceExecuted=false,performanceExecuted=false,cleanupByLease=true})+"\n");
        async Task<string> Run(ProcessStartInfo info,string name,params string[] additionalSecrets)=>await OwnedProcess(info,name,evidence,new[]{password,newPassword,options.ConnectionString,databasePassword}.Concat(additionalSecrets).ToArray());
        async Task<Dictionary<string,object?>> Account(string name)=>Assert.Single(await store.ReadAsync("yz_manager",Row("name",name)));
    }
    /// <summary>只访问原HTTP/R协议，拒绝把未知失败正文拼进断言。</summary>
    private static Task<JsonElement> Send(HttpClient client,HttpMethod method,string route,object? body=null)=>FreshWebHttp.SendAsync(client,method,route,body);
    /// <summary>新增/修改原响应中的ID保持字符串，不走浮点转换。</summary>
    private static string Id(JsonElement response)=>response.GetProperty("data").GetProperty("id").GetString()!;
    /// <summary>原表级路由不替换成测试专用入口。</summary>
    private static string Route(string table,string page)=>"/api/admin/auto/"+table+"/"+page;
    /// <summary>只读本轮随机库并核对数据库、主机与实例，先于任何DDL和业务写入。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement expected)
    {
        await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==session.Options.ExpectedDatabase&&reader.GetString(1)==expected.GetProperty("hostname").GetString()&&reader.GetString(2)==expected.GetProperty("serverUuid").GetString(),"完整管理验收实例不符，未执行DDL。");
    }
    /// <summary>结构核验只使用夹具内固定SQL，不接受用户输入作为查询。</summary>
    private static async Task<long> Scalar(DatabaseSession session,string sql)
    {await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>只构造本轮已知表的精确等值条件。</summary>
    private static Dictionary<string,object?> Row(string key,object? value)=>new(){{key,value}};
    /// <summary>失败前后比较完整行摘要，不泄露账号密码摘要或其他字段。</summary>
    private static string Fingerprint(object value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    /// <summary>拥有且限时等待自身子进程，消费两条管道并脱敏；失败不自动重试业务请求。</summary>
    private static async Task<string> OwnedProcess(ProcessStartInfo info,string name,string evidence,params string[] secrets)
    {
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));var timedOut=false;
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){timedOut=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}
        var text=await stdout;var error=await stderr;foreach(var secret in secrets.Where(value=>value.Length>0).OrderByDescending(value=>value.Length)){text=text.Replace(secret,"[已脱敏]",StringComparison.Ordinal);error=error.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),text+"\n"+error);if(timedOut)throw new TimeoutException("完整管理子进程超时，未自动重试。");Assert.True(process.ExitCode==0,"完整管理子进程失败，正文及凭据不回显；见本轮私有脱敏日志。");return text;
    }
}
