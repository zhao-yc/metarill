using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
    /// <summary>同一固定完整包覆盖正式身份、CLI 跨重启、MCP、工作台和保留 ID 初始化。</summary>
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
            await Send(host.Client,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(roleId,snapshot.GetProperty("revision").GetString()!,new(ManagementNativePreparation.GrantableMenuIds(model),pagePermissions,[])));
            var editorId=Id(await Send(host.Client,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/add",new{name=editor,realName="必须保留的普通档案",password,isDisabled=1,yzAdminTypeId=ManagementNativePreparation.Admin,yzRoleIds=new[]{roleId}}));
            using var ordinary=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(25)};var editorToken=await FreshWebHttp.LoginAsync(ordinary,editor,password,form:true);Assert.False((await Send(ordinary,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            using(var foreign=new HttpClient{BaseAddress=host.Client.BaseAddress}){await FreshWebHttp.LoginAsync(foreign,foreignOwner,password,form:true);await FreshWebHttp.SendAsync(foreign,HttpMethod.Post,Route("yz_role",RolePage)+"/page",new{},HttpStatusCode.Forbidden);}
            stage="真实 OAuth 授权列表与跨身份撤销隔离";
            using (var otherOwner = new HttpClient { BaseAddress = host.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(25) })
            {
                await FreshWebHttp.LoginAsync(otherOwner, foreignOwner, password, form: true);
                await VerifyGrantIsolationAsync(host.Client, otherOwner, ordinary, store, physical.YzProjectId,
                    SqlRowStore.Text(await Account(owner), "id"), SqlRowStore.Text(await Account(foreignOwner), "id"), evidence);
                await Send(otherOwner, HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login", new { });
            }
            stage="普通编辑者不能审批Agent授权";
            using(var device=await host.Client.PostAsync("/api/agent/oauth/device",new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id","metarill-agent-cli"}})))
            {
                Assert.Equal(HttpStatusCode.OK,device.StatusCode);using var grant=JsonDocument.Parse(await device.Content.ReadAsStringAsync());var userCode=grant.RootElement.GetProperty("user_code").GetString()!;
                var selection=Row("user_code",userCode);var pending=Fingerprint(await store.ReadIncludingDeletedAsync("sys_agent_grant",selection));
                await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/sys/agent/approve",new{userCode,approve=true},HttpStatusCode.Unauthorized);
                Assert.Equal(pending,Fingerprint(await store.ReadIncludingDeletedAsync("sys_agent_grant",selection)));
                await Send(host.Client,HttpMethod.Post,"/api/admin/sys/agent/approve",new{userCode,approve=false});Assert.Equal("denied",SqlRowStore.Text(Assert.Single(await store.ReadAsync("sys_agent_grant",selection)),"status"));
            }
            // 独立应用审批沿原后台super规则；普通Web／MCP身份继续独立验证，不能放宽产品审批门槛。
            stage = "重启前真实 OAuth 和 CLI 写入";
            var agentAddress = host.Client.BaseAddress!;
            var catalog = ManagementNativePreparation.Catalog(root);
            var node = NonRedundantApplicationFixture.Start("node", root);
            foreach (var argument in new[] { Path.Combine(root, "backend/tests/Metarill.Generation.Tests/StandaloneAgentCliLiveProbe.mjs"),
                new Uri(agentAddress, "/api").ToString(), catalog.ConfigPages.Single().Id, physical.YzProjectId, root, evidence })
                node.ArgumentList.Add(argument);
            node.Environment["METARILL_AGENT_WEB_TOKEN"] = rootToken;
            node.Environment["METARILL_AGENT_RESTART_CHECK"] = "1";
            var cliRun = Run(node, "real-cli", rootToken);
            try
            {
                var checkpoint = await ReadAgentRestartCheckpointAsync(evidence, cliRun);
                var requestId = checkpoint.GetProperty("requestId").GetString()!;
                var recordId = checkpoint.GetProperty("recordId").GetString()!;
                var grantKey = Row("id", long.Parse(checkpoint.GetProperty("grantId").GetString()!, CultureInfo.InvariantCulture));
                var taskKey = Row("request_id", requestId);
                var table = catalog.ConfigTables.Single().TableName;
                var grantBefore = Assert.Single(await store.ReadAsync("sys_agent_grant", grantKey));
                var taskBefore = Assert.Single(await store.ReadAsync("sys_agent_task", taskKey));
                var recordBefore = Assert.Single(await store.ReadAsync(table, Row("id", long.Parse(recordId, CultureInfo.InvariantCulture))));
                Assert.Equal("approved", SqlRowStore.Text(grantBefore, "status"));
                Assert.Equal(SqlRowStore.Text(await Account(owner), "id"), SqlRowStore.Text(grantBefore, "account_id"));
                Assert.Equal(SqlRowStore.Text(grantBefore, "account_id"), SqlRowStore.Text(taskBefore, "account_id"));
                Assert.Equal(physical.YzProjectId, SqlRowStore.Text(taskBefore, "project_id"));
                Assert.Equal("applied", SqlRowStore.Text(taskBefore, "outcome"));
                Assert.Equal(recordId, SqlRowStore.Text(taskBefore, "result_id"));

                // 停止原进程后复用地址，让 CLI 沿用此前真实登录的配置。
                stage = "同址重启后读取任务、刷新及去重";
                await host.DisposeAsync();
                host = null;
                host = await FreshHost.StartAsync(Info(), "/health", agentAddress);
                Assert.Equal(agentAddress, host.Client.BaseAddress);
                host.Client.DefaultRequestHeaders.Add("loginStateId", rootToken);
                var resume = Path.Combine(evidence, "agent-restart-resume");
                await File.WriteAllTextAsync(resume + ".tmp", requestId);
                File.Move(resume + ".tmp", resume);
                using var cliProof = JsonDocument.Parse(await cliRun);
                foreach (var flag in new[] { "deviceDenied", "deviceApproved", "realCli", "strictCatalog", "preview", "create", "detail",
                    "explicitNull", "stringId", "task", "duplicateUuid", "delete", "revoke", "refreshReplay", "priorAccessAfterRestart",
                    "taskAfterRestart", "refreshAfterRestart", "priorAccessRejectedAfterRefresh" })
                    Assert.True(cliProof.RootElement.GetProperty(flag).GetBoolean(), flag);

                // 用独立实库读回核对原任务和唯一业务行，避免仅凭重复请求的状态码判定。
                var taskAfter = Assert.Single(await store.ReadAsync("sys_agent_task", taskKey));
                Assert.Equal(Fingerprint(taskBefore), Fingerprint(taskAfter));
                var recordAfter = Assert.Single(await store.ReadIncludingDeletedAsync(table, Row("name", SqlRowStore.Text(recordBefore, "name"))));
                Assert.Equal(recordId, SqlRowStore.Text(recordAfter, "id"));
                Assert.NotNull(recordAfter["delete_no"]);
                var grantAfter = Assert.Single(await store.ReadAsync("sys_agent_grant", grantKey));
                Assert.Equal("revoked", SqlRowStore.Text(grantAfter, "status"));
                Assert.Null(grantAfter["refresh_hash"]);
                Assert.Equal(grantBefore["refresh_expires_at"], grantAfter["refresh_expires_at"]);
                var sessions = await store.ReadIncludingDeletedAsync("sys_session", Row("grant_id", SqlRowStore.Id(grantBefore)));
                Assert.Single(sessions, session => SqlRowStore.Text(session, "kind") == "agent_refresh_used");
                Assert.DoesNotContain(sessions, session => SqlRowStore.Text(session, "kind") == "agent" && session["delete_no"] is null);
                await File.WriteAllTextAsync(Path.Combine(evidence, "agent-restart-proof.json"), JsonSerializer.Serialize(new
                {
                    sameAddress = true, originalProcessExited = true, taskUnchanged = true, uniqueBusinessRow = true,
                    cleanupDeleteVerified = true, refreshDeadlineUnchanged = true, grantRevoked = true, activeAgentSessions = 0
                }));
            }
            finally
            {
                // 即使宿主重启失败，也等待探针退出并清理私有凭据后才允许回收测试库。
                if (!cliRun.IsCompleted)
                    try { await cliRun; } catch (Exception) { /* 保留当前阶段的首个失败。 */ }
            }
            stage="普通编辑者管理CRUD及跨后台回滚";var superRole=Id(await Send(ordinary,HttpMethod.Post,Route("yz_role",RolePage)+"/add",new{name="management-assigned-super",isSuper=1,sortNo=11,yzAdminTypeId=ManagementNativePreparation.Admin}));var child=Id(await Send(ordinary,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/add",new{name="management-child",realName="原表单创建",password,isDisabled=1,yzAdminTypeId=ManagementNativePreparation.Admin,yzRoleIds=new[]{roleId}}));var before=Fingerprint(await Account("management-child"));
            var foreignRole=SqlRowStore.Text(Assert.Single(await store.ReadAsync("yz_role",Row("yz_admin_type_id",long.Parse(ManagementNativePreparation.ForeignAdmin,CultureInfo.InvariantCulture)))),"id");await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/update",new{id=child,realName="失败不得保存",yzRoleIds=new[]{foreignRole}},HttpStatusCode.BadRequest);Assert.Equal(before,Fingerprint(await Account("management-child")));
            await Send(ordinary,HttpMethod.Post,Route("yz_manager",ManagerPage)+"/update",new{id=child,yzRoleIds=new[]{superRole}});using(var childClient=new HttpClient{BaseAddress=host.Client.BaseAddress}){await FreshWebHttp.LoginAsync(childClient,"management-child",password,form:true);Assert.True((await Send(childClient,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());}
            var roleSnapshot=(await Send(ordinary,HttpMethod.Post,Workbench+"/snapshot",new{roleId=superRole})).GetProperty("data");await Send(ordinary,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(superRole,roleSnapshot.GetProperty("revision").GetString()!,new([],new(){{RolePage,["is_query"]}},[])));var permissionBefore=Fingerprint(await store.ReadIncludingDeletedAsync("yz_role_permission",Row("yz_role_id",long.Parse(superRole,CultureInfo.InvariantCulture))));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(superRole,roleSnapshot.GetProperty("revision").GetString()!,new([],new(),[])),HttpStatusCode.BadRequest);Assert.Equal(permissionBefore,Fingerprint(await store.ReadIncludingDeletedAsync("yz_role_permission",Row("yz_role_id",long.Parse(superRole,CultureInfo.InvariantCulture)))));
            stage="同包真实MCP及原工作台撤权";mcp=await TrustedManagementMcpProbe.StartAsync(host.Client.BaseAddress!.ToString().TrimEnd('/'),editorToken,store,long.Parse(physical.YzProjectId,CultureInfo.InvariantCulture),long.Parse(ManagementNativePreparation.Admin,CultureInfo.InvariantCulture),"management-native",evidence);await mcp.VerifyOperationsAsync();var current=(await Send(host.Client,HttpMethod.Post,Workbench+"/snapshot",new{roleId})).GetProperty("data");await Send(host.Client,HttpMethod.Post,Workbench+"/save",new RolePermissionSave(roleId,current.GetProperty("revision").GetString()!,new([],new(),[])));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,Route("yz_role",RolePage)+"/page",new{},HttpStatusCode.Forbidden);await mcp.VerifyRevocationAsync();await Send(ordinary,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await mcp.VerifyLogoutAsync();await mcp.DisposeAsync();mcp=null;
            stage="保留账号ID初始化及异后台同名拒绝";await FreshWebHttp.LoginAsync(ordinary,editor,password,form:true);var editorBefore=await Account(editor);Assert.Equal(editorId,SqlRowStore.Text(editorBefore,"id"));Assert.DoesNotContain(await store.ReadIncludingDeletedAsync("sys_agent_grant",Row("account_id",long.Parse(editorId,CultureInfo.InvariantCulture))),grant=>SqlRowStore.Text(grant,"status")=="approved");
            var foreignBefore=Fingerprint(await Account(foreignOwner));
            await Send(host.Client,HttpMethod.Post,Route("yz_admin_type",AdminPage)+"/update",new{id=ManagementNativePreparation.Admin,userName=foreignOwner,password=newPassword});
            // 原按钮业务拒绝必须是HTTP200/R500和固定可读提示，不能把任意HTTP500当作同名校验成功。
            using(var rejected=await host.Client.PostAsJsonAsync("/api/admin/custom/index/init_admin",new{id=ManagementNativePreparation.Admin}))
            {
                Assert.Equal(HttpStatusCode.OK,rejected.StatusCode);var failure=await rejected.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(500,failure.GetProperty("code").GetInt32());Assert.Equal("初始化账号名已被其他后台或已删除记录占用，请修改已保存的初始化用户名。",failure.GetProperty("msg").GetString());
            }
            Assert.Equal(Fingerprint(editorBefore),Fingerprint(await Account(editor)));Assert.Equal(foreignBefore,Fingerprint(await Account(foreignOwner)));
            await Send(host.Client,HttpMethod.Post,Route("yz_admin_type",AdminPage)+"/update",new{id=ManagementNativePreparation.Admin,userName=editor,password=newPassword});await Send(host.Client,HttpMethod.Post,"/api/admin/custom/index/init_admin",new{id=ManagementNativePreparation.Admin});var after=await Account(editor);foreach(var column in new[]{"id","name","real_name"})Assert.Equal(SqlRowStore.Text(editorBefore,column),SqlRowStore.Text(after,column));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions",expected:HttpStatusCode.Unauthorized);var renewed=await FreshWebHttp.LoginAsync(ordinary,editor,newPassword,form:true);Assert.True((await Send(ordinary,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());await Send(host.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");
            stage="同包重启持久Web会话";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",rootToken);await Send(host.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");using(var restarted=new HttpClient{BaseAddress=host.Client.BaseAddress}){restarted.DefaultRequestHeaders.Add("loginStateId",renewed);await Send(restarted,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");await Send(restarted,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(restarted,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions",expected:HttpStatusCode.Unauthorized);}
            Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);ManagementNativePreparation.Verify(copied,root,runtime:true);ManagementNativePreparation.Verify(proof,root);passed=true;await Receipt("verified");
        }
        catch(Exception error){await Receipt("failure",error.GetType().Name);throw;}
        finally{if(mcp is not null)await mcp.DisposeAsync();if(host is not null)await host.DisposeAsync();}

        // 所有证据只写本轮私有目录，不输出HTTP正文、口令或连接。
        Task Receipt(string name,string? error=null)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(new{run,stage,passed,error,packageMode="FullManagementApplication",samePackage=true,developmentAuthentication=false,identityInjected=false,tableCount=physical.ConfigTables.Count,formalWeb=passed,realCli=passed,realMcp=passed,ordinaryAgentApprovalRejected=passed,foreignNameConflictRejected=passed,initializationPreservedId=passed,activeAgentResetPolicyExecuted=false,browserApprovalExecuted=false,nativeDeviceExecuted=false,performanceExecuted=false,cleanupByLease=true})+"\n");
        async Task<string> Run(ProcessStartInfo info,string name,params string[] additionalSecrets)=>await OwnedProcess(info,name,evidence,new[]{password,newPassword,options.ConnectionString,databasePassword}.Concat(additionalSecrets).ToArray());
        async Task<Dictionary<string,object?>> Account(string name)=>Assert.Single(await store.ReadAsync("yz_manager",Row("name",name)));
    }
    /// <summary>等待已提交写入的 CLI 检查点，探针提前退出时保留真实失败。</summary>
    private static async Task<JsonElement> ReadAgentRestartCheckpointAsync(string evidence, Task<string> cli)
    {
        var path = Path.Combine(evidence, "agent-restart-ready.json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        while (!File.Exists(path) && !cli.IsCompleted)
            await Task.Delay(100, timeout.Token);
        if (cli.IsCompleted)
        {
            await cli;
            throw new InvalidOperationException("CLI 未等待正式宿主重启。");
        }
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, timeout.Token));
        var checkpoint = document.RootElement;
        AgentTaskStore.ValidateRequestId(checkpoint.GetProperty("requestId").GetString()!);
        Assert.True(long.TryParse(checkpoint.GetProperty("recordId").GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var recordId) && recordId > 0);
        Assert.True(long.TryParse(checkpoint.GetProperty("grantId").GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var grantId) && grantId > 0);
        return checkpoint.Clone();
    }

    /// <summary>使用真实设备令牌和普通 Web 会话验证归属。拒绝操作后独立读回授权及活动会话。</summary>
    private static async Task VerifyGrantIsolationAsync(HttpClient owner, HttpClient otherOwner, HttpClient ordinary,
        SqlRowStore store, string projectId, string ownerId, string otherOwnerId, string evidence)
    {
        const string oauth = "/api/agent/oauth/";
        const string management = "/api/admin/sys/agent/";
        using var anonymous = new HttpClient { BaseAddress = owner.BaseAddress, Timeout = TimeSpan.FromSeconds(25) };

        async Task<(string Id, string Access, string Refresh)> Issue(HttpClient web, string name)
        {
            using var device = await anonymous.PostAsync(oauth + "device", new FormUrlEncodedContent(new Dictionary<string,string>
                { ["client_id"] = "metarill-agent-cli", ["client_name"] = name }));
            Assert.Equal(HttpStatusCode.OK, device.StatusCode);
            var offer = await device.Content.ReadFromJsonAsync<JsonElement>();
            var userCode = offer.GetProperty("user_code").GetString()!;
            await Send(web, HttpMethod.Post, management + "approve", new { userCode, approve = true });
            using var exchanged = await anonymous.PostAsync(oauth + "token", new FormUrlEncodedContent(new Dictionary<string,string>
                { ["client_id"] = "metarill-agent-cli", ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                  ["device_code"] = offer.GetProperty("device_code").GetString()! }));
            Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
            var tokens = await exchanged.Content.ReadFromJsonAsync<JsonElement>();
            var row = Assert.Single(await store.ReadAsync("sys_agent_grant", Row("user_code", userCode)));
            Assert.Equal("approved", SqlRowStore.Text(row, "status"));
            Assert.Equal(projectId, SqlRowStore.Text(row, "project_id"));
            return (SqlRowStore.Text(row, "id"), tokens.GetProperty("access_token").GetString()!, tokens.GetProperty("refresh_token").GetString()!);
        }

        async Task<string[]> List(HttpClient web) =>
            (await Send(web, HttpMethod.Get, management + "grants")).GetProperty("data").EnumerateArray()
                .Select(grant => grant.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray();

        Dictionary<string,object?> GrantKey(string id) => Row("id", long.Parse(id, CultureInfo.InvariantCulture));
        Dictionary<string,object?> Sessions(string id) => Row("grant_id", long.Parse(id, CultureInfo.InvariantCulture));
        async Task<string> Snapshot(string id) => Fingerprint(new
        {
            Grant = Assert.Single(await store.ReadIncludingDeletedAsync("sys_agent_grant", GrantKey(id))),
            Sessions = await store.ReadIncludingDeletedAsync("sys_session", Sessions(id))
        });

        using var cli = new HttpClient { BaseAddress = owner.BaseAddress, Timeout = TimeSpan.FromSeconds(25) };
        var first = await Issue(owner, "Metarill V776 连接甲");
        var sibling = await Issue(owner, "Metarill V776 连接乙");
        var foreign = await Issue(otherOwner, "Metarill V776 异后台连接");
        cli.DefaultRequestHeaders.Authorization = new("Bearer", first.Access);
        async Task AssertIdentity(string grant, string account, string access)
        {
            cli.DefaultRequestHeaders.Authorization = new("Bearer", access);
            var data = (await Send(cli, HttpMethod.Get, management + "me")).GetProperty("data");
            Assert.Equal(grant, data.GetProperty("grantId").GetString());
            Assert.Equal(account, data.GetProperty("accountId").GetString());
            Assert.Equal(projectId, data.GetProperty("projectId").GetString());
        }
        await AssertIdentity(first.Id, ownerId, first.Access);
        await AssertIdentity(sibling.Id, ownerId, sibling.Access);
        await AssertIdentity(foreign.Id, otherOwnerId, foreign.Access);
        cli.DefaultRequestHeaders.Authorization = new("Bearer", first.Access);

        Assert.Equal(new[] { first.Id, sibling.Id }.Order(StringComparer.Ordinal), await List(owner));
        Assert.Equal(new[] { foreign.Id }, await List(otherOwner));
        Assert.Empty(await List(ordinary));
        await FreshWebHttp.SendAsync(anonymous, HttpMethod.Get, management + "grants", expected: HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(cli, HttpMethod.Get, management + "grants", expected: HttpStatusCode.Unauthorized);

        var siblingBefore = await Snapshot(sibling.Id);
        var foreignBefore = await Snapshot(foreign.Id);
        await FreshWebHttp.SendAsync(cli, HttpMethod.Post, management + "revoke", new { grantId = sibling.Id }, HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(owner, HttpMethod.Post, management + "revoke", new { grantId = foreign.Id }, HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, management + "revoke", new { grantId = foreign.Id }, HttpStatusCode.Unauthorized);
        Assert.Equal(siblingBefore, await Snapshot(sibling.Id));
        Assert.Equal(foreignBefore, await Snapshot(foreign.Id));

        await Send(cli, HttpMethod.Post, management + "revoke", new { grantId = first.Id });
        await FreshWebHttp.SendAsync(cli, HttpMethod.Get, management + "me", expected: HttpStatusCode.Unauthorized);
        using var deniedRefresh = await anonymous.PostAsync(oauth + "token", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["client_id"] = "metarill-agent-cli", ["grant_type"] = "refresh_token", ["refresh_token"] = first.Refresh }));
        Assert.Equal(HttpStatusCode.BadRequest, deniedRefresh.StatusCode);
        Assert.Equal("invalid_grant", (await deniedRefresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(new[] { sibling.Id }, await List(owner));
        Assert.Equal(new[] { foreign.Id }, await List(otherOwner));
        Assert.Equal(siblingBefore, await Snapshot(sibling.Id));
        Assert.Equal(foreignBefore, await Snapshot(foreign.Id));
        await AssertIdentity(sibling.Id, ownerId, sibling.Access);
        await AssertIdentity(foreign.Id, otherOwnerId, foreign.Access);
        await Send(owner, HttpMethod.Post, management + "revoke", new { grantId = sibling.Id });
        await Send(otherOwner, HttpMethod.Post, management + "revoke", new { grantId = foreign.Id });
        Assert.Empty(await List(owner));
        Assert.Empty(await List(otherOwner));
        await File.WriteAllTextAsync(Path.Combine(evidence, "grant-isolation.json"), JsonSerializer.Serialize(new
        {
            passed = true, realWebAccounts = 3, realDeviceGrants = 3, stringIds = true, scopedLists = true,
            anonymousAndCliListDenied = true, crossGrantAndCrossAccountRevokesDenied = 3,
            independentSnapshotsUnchanged = true, ownCliRevokeAccess = 401, ownCliRevokeRefresh = "invalid_grant",
            otherTokensStayedUsable = true, ownWebRevokes = 2
        }));
    }

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
