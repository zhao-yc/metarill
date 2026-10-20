using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>实际SqlRowStore/认证/策略接固定离线传输；只证明当前读命令和决策，不冒称RR等待验收。</summary>
public sealed class SecurityRealmCurrentReaderTests
{
    /// <summary>请求旧权限不能替代锁后的当前授权，所有安全链查询都使用同一事务FOR UPDATE。</summary>
    [Fact]
    public async Task Current_grants_replace_request_snapshot_without_regular_read_fallback()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);var page=RolePage(f);Grant(f,page,"is_query","is_update");var identity=await f.Identity();
        Assert.Contains(page+":is_update",identity.Permissions);f.Row("yz_role_permission",71)["permission_values"]="[\"is_query\"]";f.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>
        {
            var current=await Read(f,identity);Assert.DoesNotContain(page+":is_update",current.Permissions);Assert.Contains(page+":is_query",current.Permissions);return true;
        });
        Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));
        AssertOrder(f,"sys_session","yz_manager","yz_role","yz_role_permission","config_table","config_page","config_menu","config_button");
    }

    /// <summary>平台超级角色切工作区只多锁工作区项目，真实身份后台和平台权限保持。</summary>
    [Fact]
    public async Task Platform_super_keeps_identity_realm_while_workspace_is_only_in_lock_set()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var identity=await f.Identity(f.DemoProject);
        await f.Session.InTransactionAsync(async()=>
        {
            var current=await Read(f,identity);Assert.True(current.Actor.IsAdministrator);Assert.Equal(f.DemoProject,current.Actor.ProjectId);Assert.Equal(f.PlatformProject,current.PrincipalProjectId);Assert.Equal(f.PlatformAdmin,current.PrincipalAdminTypeId);return true;
        });
    }

    /// <summary>Agent撤销与刷新先持grant再持session，当前读沿用该次序且不把已撤授权继续使用。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Agent_grant_precedes_session_and_revocation_is_current(bool revoked)
    {
        using var f=new PrincipalRealmFixture();await Seed(f);
        f.Add("sys_agent_grant",("id",70L),("account_id",8L),("project_id",long.Parse(f.PlatformProject)),("client_id",AgentOAuthService.ClientId),("status","approved"),("refresh_expires_at",DateTime.UtcNow.AddHours(1)));
        f.WebSession["kind"]="agent";f.WebSession["grant_id"]=70L;var identity=await f.Identity();if(revoked)f.Row("sys_agent_grant",70)["status"]="revoked";f.Provider.Commands.Clear();
        if(revoked)await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>{await Read(f,identity);return true;}));
        else await f.Session.InTransactionAsync(async()=>{Assert.Equal(70, (await Read(f,identity)).GrantId);return true;});
        AssertOrder(f,"sys_agent_grant","sys_session");Assert.All(f.Provider.Commands,sql=>Assert.EndsWith(" FOR UPDATE",sql));
    }

    /// <summary>会话/账号/后台/超级语义变化都拒绝，不能回退为普通Load重用旧快照。</summary>
    [Theory]
    [InlineData("logout")][InlineData("expired")][InlineData("account")][InlineData("admin")][InlineData("super")][InlineData("admin-project")]
    public async Task Changed_identity_or_session_is_rejected(string kind)
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var identity=await f.Identity();
        if(kind=="logout")f.WebSession["delete_no"]=DateTime.UtcNow;
        if(kind=="expired")f.WebSession["expires_at"]=DateTime.UtcNow.AddMinutes(-1);
        if(kind=="account")f.WebSession["account_id"]=99L;
        if(kind=="admin")f.Account["yz_admin_type_id"]=long.Parse(f.DemoAdmin);
        if(kind=="super")f.Role["is_super"]=0;
        if(kind=="admin-project")f.Row("yz_admin_type",long.Parse(f.PlatformAdmin))["yz_project_id"]=long.Parse(f.DemoProject);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>{await Read(f,identity);return true;}));
    }

    /// <summary>已软删页面、跨后台脏授权和未发布页不进入当前身份权限。</summary>
    [Theory]
    [InlineData("deleted")][InlineData("foreign")][InlineData("unpublished")]
    public async Task Only_current_and_published_same_realm_resources_are_aggregated(string kind)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);var page=RolePage(f);Grant(f,page,"is_query","is_update");var identity=await f.Identity();
        var key=page;
        if(kind=="deleted")f.Row("config_page",long.Parse(page))["delete_no"]=DateTime.UtcNow;
        if(kind=="foreign") {key=f.DemoPage;f.Row("yz_role_permission",71)["config_page_id"]=long.Parse(key);}
        if(kind=="unpublished")
        {
            key="909";f.Add("config_page",("id",909L),("name","未发布页"),("config_table_id",long.Parse(f.Registry.Platform.ConfigPages.Single(value=>value.Id==page).ConfigTableId)),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("is_edit",1),("is_update",1));f.Row("yz_role_permission",71)["config_page_id"]=909L;
        }
        await f.Session.InTransactionAsync(async()=>{var current=await Read(f,identity);Assert.DoesNotContain(key+":is_query",current.Permissions);Assert.DoesNotContain(key+":is_update",current.Permissions);return true;});
    }

    /// <summary>公共调用仍只能由协调器提供真实同事务锁集；测试使用反射避免开放内部锁集构造入口。</summary>
    private static async Task<AuthenticatedIdentity> Read(PrincipalRealmFixture f,AuthenticatedIdentity identity)
    {
        var projects=new Dictionary<long,Dictionary<string,object?>>();var admins=new Dictionary<long,Dictionary<string,object?>>();
        foreach(var id in new[]{identity.Actor.ProjectId,identity.PrincipalProjectId}.Select(value=>long.Parse(value,CultureInfo.InvariantCulture)).Distinct().Order())
            projects[id]=(await f.Store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",id}})).Single();
        var adminId=long.Parse(identity.PrincipalAdminTypeId,CultureInfo.InvariantCulture);admins[adminId]=(await f.Store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",adminId}})).Single();
        var type=typeof(SecurityRealmCurrentReader).Assembly.GetType("Metarill.Runtime.Security.SecurityRealmLocks",true)!;
        var locks=Activator.CreateInstance(type,f.Session.CurrentTransaction!,projects,admins)!;
        var reader=new SecurityRealmCurrentReader(f.Store,f.Registry,new(f.Store,f.Registry));
        var task=(Task)typeof(SecurityRealmCurrentReader).GetMethod("ReadAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,[identity,locks,CancellationToken.None])!;await task;
        var result=task.GetType().GetProperty("Result")!.GetValue(task)!;return (AuthenticatedIdentity)result.GetType().GetProperty("Identity")!.GetValue(result)!;
    }
    private static string RolePage(PrincipalRealmFixture f)=>f.Registry.Platform.ConfigPages.Single(page=>f.Registry.Platform.ConfigTables.Single(table=>table.Id==page.ConfigTableId).TableName=="yz_role").Id;
    private static void Grant(PrincipalRealmFixture f,string page,params string[] actions)=>f.Add("yz_role_permission",("id",71L),("yz_role_id",10L),("config_page_id",long.Parse(page)),("permission_values",JsonSerializer.Serialize(actions)));
    private static async Task Seed(PrincipalRealmFixture f){var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);await metadata.SeedAsync(f.Registry.Demo);}
    private static void AssertOrder(PrincipalRealmFixture f,params string[] tables)
    {
        var last=-1;foreach(var table in tables){var current=f.Provider.Commands.FindIndex(sql=>sql.Contains(" FROM `"+table+"`",StringComparison.Ordinal));Assert.True(current>last,"安全当前读顺序不一致："+table);last=current;}
    }
}
