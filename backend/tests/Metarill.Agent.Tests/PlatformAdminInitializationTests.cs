using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Api.Controllers;
using Metarill.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text;
using Metarill.Runtime.Metadata;
using Metarill.Runtime;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;
using Metarill.Runtime.Agent;
namespace Metarill.Agent.Tests;

/// <summary>两套独立SqlRowStore和真实认证/锁后协调；内存传输只验证事务和归属，不冒充实库。</summary>
public sealed class PlatformAdminInitializationTests
{
    /// <summary>源平台配置只读，目标保留账号ID/档案，重置角色密码并撤旧Web会话。</summary>
    [Fact]
    public async Task Cross_database_initialization_updates_only_target_and_preserves_profile()
    {
        using var f=new Pair();await f.Prepare();
        f.Target.Account["name"]="initial-account";f.Target.Account["real_name"]="保留姓名";f.Target.Account["notes"]="保留档案";
        var source=f.SourceSnapshot();var result=await f.Execute();
        Assert.True(result.Applied);Assert.Equal("8",result.AccountId);Assert.Equal(source,f.SourceSnapshot());
        var account=f.Target.Account;Assert.Equal("保留姓名",account["real_name"]);Assert.Equal("保留档案",account["notes"]);
        Assert.True(PasswordHasher.Verify("平台已保存密码",SqlRowStore.Text(account,"password")));Assert.Null(account["salt"]);Assert.Null(account["old_password"]);
        Assert.NotNull(f.Target.WebSession["delete_no"]);Assert.DoesNotContain(f.Source.Provider.Commands,sql=>sql.StartsWith("UPDATE ")||sql.StartsWith("INSERT "));
    }
    /// <summary>目标缺归属行时仅补本范围项目/后台，与账号角色一起提交，不复制配置密码到业务后台行。</summary>
    [Fact]
    public async Task Missing_target_scope_is_seeded_in_target_transaction_without_copying_plaintext_configuration()
    {
        using var f=new Pair();await f.Prepare();
        f.Target.Provider.Rows["yz_project"].RemoveAll(row=>SqlRowStore.Text(row,"id")==f.Source.DemoProject);
        f.Target.Provider.Rows["yz_admin_type"].RemoveAll(row=>SqlRowStore.Text(row,"id")==f.Source.DemoAdmin);
        var source=f.SourceSnapshot();var result=await f.Execute();Assert.True(result.Applied);Assert.Equal(source,f.SourceSnapshot());
        var admin=f.Target.Row("yz_admin_type",long.Parse(f.Source.DemoAdmin));
        Assert.Equal(f.Source.DemoProject,SqlRowStore.Text(admin,"yz_project_id"));Assert.Null(admin.GetValueOrDefault("password"));Assert.Null(admin.GetValueOrDefault("user_name"));
        Assert.Single(f.Target.Provider.Rows["yz_manager"],row=>SqlRowStore.Text(row,"name")=="initial-account");
    }
    /// <summary>别名连接的同物理库复用源事务，只改当前业务后台，平台账号和Web会话保持。</summary>
    [Fact]
    public async Task Same_physical_database_uses_source_transaction_without_resetting_platform_account()
    {
        using var f=new Pair();await f.Prepare();
        using var alias=new Metarill.Data.DatabaseSession(f.Source.Session.Options,f.Source.Provider.Orm,f.Source.Provider);
        var target=new SqlRowStore(alias,f.Source.Registry.Platform);
        var principal=JsonSerializer.Serialize(f.Source.Account);var session=JsonSerializer.Serialize(f.Source.WebSession);
        var result=await f.Source.Session.InTransactionAsync(async()=>
        {
            var scope=await f.Guard.PreparePlatformAdminInitializationAsync(f.Source.DemoAdmin);
            var applied=await PlatformAdminAccountTarget.InitializeAsync(target,f.Source.Ids,scope,new AgentAdminInitializationPolicy());Assert.False(scope.TargetCommittedIndependently);return applied;
        });
        Assert.True(result.Applied);Assert.Equal(principal,JsonSerializer.Serialize(f.Source.Account));Assert.Equal(session,JsonSerializer.Serialize(f.Source.WebSession));
        var created=Assert.Single(f.Source.Provider.Rows["yz_manager"],row=>SqlRowStore.Text(row,"name")=="initial-account");Assert.Equal(f.Source.DemoAdmin,SqlRowStore.Text(created,"yz_admin_type_id"));
    }
    /// <summary>共库没有独立提交证明；源提交异常必须失败并回滚全部业务写入。</summary>
    [Fact]
    public async Task Shared_database_source_commit_failure_rolls_back_and_is_not_suppressed()
    {
        using var f=new Pair();await f.Prepare();var before=f.SourceSnapshot();var warnings=0;
        f.Source.Provider.BeforeCommit=()=>throw new InvalidOperationException("共库提交故障");
        var business=new PlatformAdminAccountInitializationBusiness(f.Source.Store,f.Guard,_=>f.Source.Store,f.Source.Ids,new Execution(),new AgentAdminInitializationPolicy(),()=>warnings++);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.InitializeAsync(f.Source.DemoAdmin));Assert.Equal(before,f.SourceSnapshot());Assert.Equal(0,warnings);
    }
    /// <summary>目标账号写出后，源会话若已到期，提交前当前读必须拒绝并回滚目标。</summary>
    [Fact]
    public async Task Source_expiration_before_target_commit_rolls_back_target()
    {
        using var f=new Pair();await f.Prepare();var target=f.TargetSnapshot();
        f.Target.Provider.BeforeWrite=sql=>{if(sql.StartsWith("INSERT INTO "+(char)96+"yz_manager"))f.Source.WebSession["expires_at"]=DateTime.UtcNow.AddMinutes(-1);};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Execute());Assert.Equal(target,f.TargetSnapshot());
    }
    /// <summary>创建角色之后的账号失败必须回滚目标全部修改，源平台仍保持。</summary>
    [Fact]
    public async Task Account_failure_rolls_back_new_role_and_scope_rows()
    {
        using var f=new Pair();await f.Prepare();f.Target.Provider.Rows["yz_role"].RemoveAll(row=>SqlRowStore.Text(row,"yz_admin_type_id")==f.Source.DemoAdmin);
        f.Target.Provider.Rows["yz_admin_type"].RemoveAll(row=>SqlRowStore.Text(row,"id")==f.Source.DemoAdmin);
        var source=f.SourceSnapshot();var target=f.TargetSnapshot();var sawRole=false;
        f.Target.Provider.BeforeWrite=sql=>
        {
            if(sql.StartsWith("INSERT INTO "+(char)96+"yz_role"))sawRole=true;
            if(sql.StartsWith("INSERT INTO "+(char)96+"yz_manager"))throw new InvalidOperationException("本轮受控账号失败");
        };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Execute());Assert.True(sawRole);Assert.Equal(source,f.SourceSnapshot());Assert.Equal(target,f.TargetSnapshot());
    }
    /// <summary>缺口令配置沿原按钮无操作；即使目标身份相同也不进行任何目标查询或写入。</summary>
    [Fact]
    public async Task Empty_configuration_does_not_open_target_transaction()
    {
        using var f=new Pair();await f.Prepare();f.Admin["password"]="";f.Target.Provider.PhysicalIdentity=f.Source.Provider.PhysicalIdentity;
        var result=await f.Execute();Assert.False(result.Applied);Assert.Empty(f.Target.Provider.Commands);
    }
    /// <summary>授权范围在源平台事务退出后不能重放，不能拿已提交proof绕过重新认证。</summary>
    [Fact]
    public async Task Source_authority_cannot_be_replayed_after_source_transaction()
    {
        using var f=new Pair();await f.Prepare();
        var scope=await f.Source.Session.InTransactionAsync(()=>f.Guard.PreparePlatformAdminInitializationAsync(f.Source.DemoAdmin));
        Assert.Equal(f.Source.DemoProject,scope.ProjectId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>PlatformAdminAccountTarget.InitializeAsync(f.Target.Store,f.Target.Ids,scope,new AgentAdminInitializationPolicy()));Assert.Empty(f.Target.Provider.Commands);
    }
    /// <summary>原平台项目、非当前业务项目、旧Java模板、保留名称及Agent身份不得取得初始化范围。</summary>
    [Theory][InlineData("platform")][InlineData("workspace")][InlineData("java")][InlineData("reserved")][InlineData("grant")]
    public async Task Invalid_source_scope_rejects_before_target_access(string kind)
    {
        using var f=new Pair();await f.Prepare();var targetId=f.Source.DemoAdmin;
        if(kind=="platform")targetId=f.Source.PlatformAdmin;
        if(kind=="workspace")f.Accessor.Identity=f.Accessor.Identity with{Actor=f.Accessor.Identity.Actor with{ProjectId=f.Source.PlatformProject}};
        if(kind=="java")f.Source.Row("yz_project",long.Parse(f.Source.DemoProject))["yz_yzp_version_id"]="java-v8";
        if(kind=="reserved")f.Source.Row("yz_project",long.Parse(f.Source.DemoProject))["name"]=" YZP ";
        if(kind=="grant")f.Accessor.Identity=f.Accessor.Identity with{GrantId=900};
        if(kind=="java")await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Source.Session.InTransactionAsync(()=>f.Guard.PreparePlatformAdminInitializationAsync(targetId)));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Source.Session.InTransactionAsync(()=>f.Guard.PreparePlatformAdminInitializationAsync(targetId)));
        Assert.Empty(f.Target.Provider.Commands);
    }
    /// <summary>没有活跃agent session仍可能有刷新授权；按Web重置边界拒绝，不能遗留旧CLI重新签发能力。</summary>
    [Fact]
    public async Task Refreshable_agent_grant_without_session_blocks_reset_without_mutation()
    {
        using var f=new Pair();await f.Prepare();f.Target.Account["name"]="initial-account";
        f.Target.Add("sys_agent_grant",("id",71L),("account_id",8L),("status","approved"),("refresh_expires_at",DateTime.UtcNow.AddHours(1)));
        var before=f.TargetSnapshot();await Assert.ThrowsAsync<NotSupportedException>(()=>f.Execute());Assert.Equal(before,f.TargetSnapshot());
    }
    /// <summary>已过期或已撤销的授权不被复活；仅旧Web会话和当前密码按已确认规则处理。</summary>
    [Theory][InlineData("approved",-1)][InlineData("revoked",1)]
    public async Task Inactive_agent_grants_are_not_reactivated_by_initialization(string status,int hours)
    {
        using var f=new Pair();await f.Prepare();f.Target.Account["name"]="initial-account";
        f.Target.Add("sys_agent_grant",("id",71L),("account_id",8L),("status",status),("refresh_expires_at",DateTime.UtcNow.AddHours(hours)));
        var grant=JsonSerializer.Serialize(f.Target.Row("sys_agent_grant",71));Assert.True((await f.Execute()).Applied);Assert.Equal(grant,JsonSerializer.Serialize(f.Target.Row("sys_agent_grant",71)));
    }
    /// <summary>目标墓碑绝不被当作空记录补入；整个目标事务无变化。</summary>
    [Theory][InlineData("project")][InlineData("admin")][InlineData("account")]
    public async Task Deleted_target_rows_are_rejected_without_restoration(string kind)
    {
        using var f=new Pair();await f.Prepare();
        if(kind=="project")f.Target.Row("yz_project",long.Parse(f.Source.DemoProject))["delete_no"]=DateTime.UtcNow;
        if(kind=="admin")f.Target.Row("yz_admin_type",long.Parse(f.Source.DemoAdmin))["delete_no"]=DateTime.UtcNow;
        if(kind=="account"){f.Target.Account["name"]="initial-account";f.Target.Account["delete_no"]=DateTime.UtcNow;}
        var before=f.TargetSnapshot();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Execute());Assert.Equal(before,f.TargetSnapshot());
    }
    /// <summary>重复执行只复用已有账号与超级角色；角色设置及会话按同一原子规则重新应用。</summary>
    [Fact]
    public async Task Repeated_configuration_keeps_account_and_role_ids()
    {
        using var f=new Pair();await f.Prepare();var first=await f.Execute();var roles=f.Target.Provider.Rows["yz_role"].Count;
        var next=await f.Execute();Assert.Equal(first.AccountId,next.AccountId);Assert.Equal(roles,f.Target.Provider.Rows["yz_role"].Count);
        Assert.Single(f.Target.Provider.Rows["yz_manager"],row=>SqlRowStore.Text(row,"name")=="initial-account");
    }
    /// <summary>完整跨库编排在目标提交后只释放源授权锁；源清理异常不能误报目标回滚。</summary>
    [Fact]
    public async Task Source_cleanup_failure_preserves_proven_target_commit_and_emits_fixed_signal()
    {
        using var f=new Pair();await f.Prepare();var warnings=0;var lease=new Execution();
        var business=new PlatformAdminAccountInitializationBusiness(f.Source.Store,f.Guard,project=>{Assert.Equal(f.Source.DemoProject,project);return f.Target.Store;},f.Target.Ids,lease,new AgentAdminInitializationPolicy(),()=>warnings++);
        f.Source.Provider.BeforeCommit=()=>throw new InvalidOperationException("受控源清理异常");
        var result=await business.InitializeAsync(f.Source.DemoAdmin);Assert.True(result.Applied);Assert.Equal(1,warnings);
        Assert.Equal((f.Source.PlatformProject,AdminAccountInitializationBusiness.ButtonId),Assert.Single(lease.Scopes));
        Assert.Single(f.Target.Provider.Rows["yz_manager"],row=>SqlRowStore.Text(row,"name")=="initial-account");
    }
    /// <summary>目标提交故障不产生成功证明，不被源清理处理器吞掉，账号与角色一起回滚。</summary>
    [Fact]
    public async Task Target_commit_failure_is_not_converted_to_success()
    {
        using var f=new Pair();await f.Prepare();var target=f.TargetSnapshot();var warnings=0;
        f.Target.Provider.BeforeCommit=()=>throw new InvalidOperationException("受控目标提交异常");
        var business=new PlatformAdminAccountInitializationBusiness(f.Source.Store,f.Guard,_=>f.Target.Store,f.Target.Ids,new Execution(),new AgentAdminInitializationPolicy(),()=>warnings++);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.InitializeAsync(f.Source.DemoAdmin));Assert.Equal(target,f.TargetSnapshot());Assert.Equal(0,warnings);
    }
    /// <summary>无配置和授权失败不解析目标连接，不通过租约为无权请求制造业务动作。</summary>
    [Fact]
    public async Task Empty_configuration_skips_target_resolution_and_button_lease()
    {
        using var f=new Pair();await f.Prepare();f.Admin["password"]="";var lease=new Execution();
        var business=new PlatformAdminAccountInitializationBusiness(f.Source.Store,f.Guard,_=>throw new InvalidOperationException("不应解析目标"),f.Target.Ids,lease,new AgentAdminInitializationPolicy());
        Assert.False((await business.InitializeAsync(f.Source.DemoAdmin)).Applied);Assert.Empty(lease.Scopes);Assert.Empty(f.Target.Provider.Commands);
    }
    /// <summary>实际原MVC中央/别名分发到真实跨库服务；HTTP身份由离线真实认证建立，不冒充正式网络登录。</summary>
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Original_mvc_entries_reach_cross_database_service_and_ignore_client_password(bool central)
    {
        using var f=new Pair();await f.Prepare();
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ISecurityIdentityAccessor>(provider=>new HttpRuntimeAuthorization(provider.GetRequiredService<IHttpContextAccessor>(),f.Source.Registry));
        builder.Services.AddSingleton<IAdminAccountInitializer>(new PlatformAdminAccountInitializationBusiness(f.Source.Store,f.Guard,_=>f.Target.Store,f.Target.Ids,new Execution(),new AgentAdminInitializationPolicy()));
        builder.Services.AddControllers().AddApplicationPart(typeof(CompatibilityButtonController).Assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));
        builder.Services.AddSingleton<IControllerActivator,InitializationActivator>();
        await using var app=builder.Build();
        app.Use(async(context,next)=>
        {
            Assert.Equal("Bearer "+f.Source.Token,context.Request.Headers.Authorization.ToString());
            var identity=await f.Source.Identity(f.Source.DemoProject);f.Accessor.Identity=identity;context.Items[typeof(AuthenticatedIdentity)]=identity;await next(context);
        });
        app.MapControllers();await app.StartAsync();using var client=app.GetTestClient();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",f.Source.Token);
        var body=central?JsonSerializer.Serialize(new{buttonId=AdminAccountInitializationBusiness.ButtonId,entityId=f.Source.DemoAdmin,entity=new{id=f.Source.DemoAdmin,password="客户端不可信密码",userName="不可信账号"}}):JsonSerializer.Serialize(new{id=f.Source.DemoAdmin});
        using var response=await client.PostAsync(central?"/api/admin/sys/config_button/execute":"/api/admin/custom/index/init_admin",new StringContent(body,Encoding.UTF8,"application/json"));
        response.EnsureSuccessStatusCode();var text=await response.Content.ReadAsStringAsync();using var json=JsonDocument.Parse(text);Assert.Equal(200,json.RootElement.GetProperty("code").GetInt32());Assert.DoesNotContain("平台已保存密码",text);Assert.DoesNotContain("客户端不可信密码",text);
        var account=Assert.Single(f.Target.Provider.Rows["yz_manager"],row=>SqlRowStore.Text(row,"name")=="initial-account");Assert.True(PasswordHasher.Verify("平台已保存密码",SqlRowStore.Text(account,"password")));
    }
    /// <summary>只激活本测试涉及的原Controller；其未用的其它按钮依赖不作为HTTP/事务验收替身。</summary>
    private sealed class InitializationActivator:IControllerActivator
    {
        public object Create(Microsoft.AspNetCore.Mvc.ControllerContext context)
        {Assert.Equal(typeof(CompatibilityButtonController),context.ActionDescriptor.ControllerTypeInfo.AsType());return new CompatibilityButtonController(null!,null!);}
        public void Release(Microsoft.AspNetCore.Mvc.ControllerContext context,object controller){}
    }
    private sealed class Execution:IBusinessButtonExecutionGuard
    {
        internal List<(string,string)> Scopes {get;}=[];
        /// <summary>仅替代租约传输；真实平台授权、目标事务及数据写入仍全部执行。</summary>
        public async Task<T> ExecuteAsync<T>(string project,string button,Func<Task<T>> action,CancellationToken token=default)
        {token.ThrowIfCancellationRequested();Scopes.Add((project,button));return await action();}
    }
    /// <summary>只向真实协调器提供已认证身份，不能从请求体生成身份。</summary>
    private sealed class Accessor:ISecurityIdentityAccessor
    {
        internal AuthenticatedIdentity Identity {get;set;}=null!;
        public AuthenticatedIdentity RequireIdentity()=>Identity;
    }
    private sealed class Pair:IDisposable
    {
        internal PrincipalRealmFixture Source {get;}=new();internal PrincipalRealmFixture Target {get;}=new(business:true);
        internal Accessor Accessor {get;}=new();internal SecurityRealmMutationGuard Guard {get;private set;}=null!;
        internal Dictionary<string,object?> Admin=>Source.Row("yz_admin_type",long.Parse(Source.DemoAdmin));
        /// <summary>源平台实际元数据入内存传输，再通过真实认证及平台当前资源适配取得授权。</summary>
        internal async Task Prepare()
        {
            await new MetadataRepository(Source.Store,Source.Registry).SeedAsync(Source.Registry.Platform);
            Admin["user_name"]="initial-account";Admin["password"]="平台已保存密码";Admin["name"]="业务后台";
            Source.Row("yz_project",long.Parse(Source.DemoProject))["yz_yzp_version_id"]=TemplateVersionCatalog.CurrentValue;
            Accessor.Identity=await Source.Identity(Source.DemoProject);
            Guard=new(Source.Store,Source.Registry,Accessor,Source.CurrentReader());
            Source.Provider.Commands.Clear();Target.Provider.Commands.Clear();
        }
        internal Task<AdminAccountInitializationResult> Execute()=>Source.Session.InTransactionAsync(async()=>
        {
            var scope=await Guard.PreparePlatformAdminInitializationAsync(Source.DemoAdmin);
            return await PlatformAdminAccountTarget.InitializeAsync(Target.Store,Target.Ids,scope,new AgentAdminInitializationPolicy());
        });
        internal string SourceSnapshot()=>JsonSerializer.Serialize(Source.Provider.Rows);
        internal string TargetSnapshot()=>JsonSerializer.Serialize(Target.Provider.Rows);
        public void Dispose(){Source.Dispose();Target.Dispose();}
    }
}
