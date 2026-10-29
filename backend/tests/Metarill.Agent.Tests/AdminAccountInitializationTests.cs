using System.Data.Common;
using System.Text.Json;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>真实SqlRowStore、认证与协调器的离线事务合同；内存传输不冒充MySQL并发锁验收。</summary>
public sealed partial class AdminAccountInitializationTests
{
    /// <summary>只有原按钮权也能初始化，不额外要求页面编辑权。</summary>
    [Fact]
    public async Task Original_button_grant_without_page_edit_can_create_one_super_account()
    {
        using var f=new Fixture();var business=await f.Business();f.Memory.Provider.Commands.Clear();
        var result=await business.InitializeAsync("91");
        Assert.True(result.Applied);var account=Assert.Single(f.TargetAccounts);
        Assert.Equal(result.AccountId,SqlRowStore.Text(account,"id"));
        Assert.True(PasswordHasher.Verify("原初始化密码",SqlRowStore.Text(account,"password")));
        Assert.StartsWith("pbkdf2-sha256$",SqlRowStore.Text(account,"password"));
        var role=Assert.Single(f.TargetRoles);Assert.Equal("超级管理员",role["name"]);Assert.Equal("1",SqlRowStore.Text(role,"is_super"));
        Assert.Equal("99",SqlRowStore.Text(role,"sort_no"));Assert.Equal("[\""+SqlRowStore.Text(role,"id")+"\"]",account["yz_role_ids"]);
        Assert.DoesNotContain(f.Memory.Provider.Commands,sql=>sql.StartsWith("DELETE ",StringComparison.Ordinal));
    }

    /// <summary>保留同后台账号ID与档案，只撤销目标账号的既有Web会话。</summary>
    [Fact]
    public async Task Existing_same_admin_account_keeps_id_profile_references_and_revokes_old_web_sessions()
    {
        using var f=new Fixture();f.ExistingAccount();f.Memory.Add("sys_session",("id",50L),("account_id",30L),("kind","web"),("expires_at",DateTime.UtcNow.AddHours(1)));
        f.Memory.Add("sys_session",("id",51L),("account_id",8L),("kind","web"),("expires_at",DateTime.UtcNow.AddHours(1)));
        f.Memory.Add("yz_role",("id",40L),("yz_admin_type_id",91L),("is_super",1));
        var result=await(await f.Business()).InitializeAsync("91");
        Assert.Equal("30",result.AccountId);Assert.Single(f.TargetAccounts);Assert.Single(f.TargetRoles);
        Assert.Equal("保留业务档案",f.Memory.Row("yz_manager",30)["notes"]);Assert.Equal("保留姓名",f.Memory.Row("yz_manager",30)["real_name"]);
        Assert.Null(f.Memory.Row("yz_manager",30)["salt"]);Assert.Null(f.Memory.Row("yz_manager",30)["old_password"]);
        Assert.NotNull(f.Memory.Row("sys_session",50)["delete_no"]);Assert.Null(f.Memory.Row("sys_session",51).GetValueOrDefault("delete_no"));
        Assert.Null(f.Memory.WebSession.GetValueOrDefault("delete_no"));
    }

    /// <summary>已保存用户名或密码空白时保持原无操作行为。</summary>
    [Theory]
    [InlineData("user_name",null)][InlineData("password",null)][InlineData("user_name"," ")][InlineData("password"," ")]
    public async Task Empty_saved_configuration_is_original_noop(string key,string? value)
    {
        using var f=new Fixture();f.Admin[key]=value;var business=await f.Business();f.Memory.Provider.Commands.Clear();
        var result=await business.InitializeAsync("91");Assert.False(result.Applied);Assert.Null(result.AccountId);NoWrites(f);
    }

    /// <summary>跨后台和墓碑同名均在任何写入前拒绝，不删除或复活记录。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Other_admin_or_deleted_name_collision_is_refused_without_deleting_or_writing(bool deleted)
    {
        using var f=new Fixture();f.ExistingAccount();var account=f.Memory.Row("yz_manager",30);
        if(deleted)account["delete_no"]=DateTime.UtcNow;else account["yz_admin_type_id"]=long.Parse(f.Memory.DemoAdmin);
        var business=await f.Business();f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.InitializeAsync("91"));Assert.Single(f.TargetAccounts);
        Assert.Empty(f.TargetRoles);NoWrites(f);Assert.Equal("旧摘要",f.Memory.Row("yz_manager",30)["password"]);
    }

    /// <summary>沿用原50字符限制，历史超限配置不能产生部分写入。</summary>
    [Theory]
    [InlineData("user_name")][InlineData("password")]
    public async Task Saved_configuration_length_violation_is_refused_before_writes(string key)
    {
        using var f=new Fixture();f.Admin[key]=new string('x',51);var business=await f.Business();f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAsync<ArgumentException>(()=>business.InitializeAsync("91"));NoWrites(f);
    }

    /// <summary>锁后重新核对按钮、身份和发布归属，旧认证快照不能维持已撤权限。</summary>
    [Theory]
    [InlineData("grant")][InlineData("directory")][InlineData("page-edit-only")][InlineData("platform")][InlineData("unpublished-target")][InlineData("other-project")][InlineData("agent")]
    public async Task Current_button_identity_and_target_boundaries_cannot_be_replaced_by_saved_identity(string invalid)
    {
        using var f=new Fixture();var business=await f.Business();
        if(invalid=="grant")f.Memory.Row("yz_role_permission",70)["delete_no"]=DateTime.UtcNow;
        if(invalid=="directory")f.Resources.Catalog=f.Registry.Catalog with{Buttons=[]};
        if(invalid=="page-edit-only")
        {
            f.Memory.Row("yz_role_permission",70)["config_button_id"]=null;
            f.Memory.Row("yz_role_permission",70)["config_page_id"]=long.Parse(Page);
            f.Memory.Row("yz_role_permission",70)["permission_values"]="[\"is_update\"]";
        }
        if(invalid=="platform")f.Registry.Platform=true;
        if(invalid=="unpublished-target")f.Registry.Catalog=f.Registry.Catalog with{Pages=f.Registry.Catalog.Pages.Where(page=>page.AdminTypeId!="91").ToArray()};
        if(invalid=="other-project")f.Admin["yz_project_id"]=long.Parse(f.Memory.PlatformProject);
        if(invalid=="agent")f.Accessor.Identity=f.Accessor.Identity with{GrantId=80L};
        f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(()=>business.InitializeAsync("91"));NoWrites(f);Assert.Empty(f.TargetRoles);
    }

    /// <summary>同后台超级角色仍沿原按钮规则，不新增编辑权限门槛。</summary>
    [Fact]
    public async Task Realm_super_keeps_original_button_rule_without_separate_grant_or_edit_permission()
    {
        using var f=new Fixture();f.Memory.Role["is_super"]=1;f.Memory.Row("yz_role_permission",70)["delete_no"]=DateTime.UtcNow;
        Assert.True((await(await f.Business()).InitializeAsync("91")).Applied);
    }

    /// <summary>超级角色也不能执行当前目录已经移除的固定按钮。</summary>
    [Fact]
    public async Task Missing_current_button_is_refused_even_for_realm_super()
    {
        using var f=new Fixture();f.Memory.Role["is_super"]=1;var business=await f.Business();
        f.Resources.Catalog=f.Registry.Catalog with{Buttons=[]};f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.InitializeAsync("91"));NoWrites(f);
    }

    /// <summary>缺少Agent撤销适配时先拒绝，不部分重置账号。</summary>
    [Fact]
    public async Task Non_web_target_session_is_refused_before_password_role_or_session_writes()
    {
        using var f=new Fixture();f.ExistingAccount();f.Memory.Add("sys_session",("id",50L),("account_id",30L),("kind","agent"));
        var business=await f.Business();f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.InitializeAsync("91"));NoWrites(f);
        Assert.Equal("旧摘要",f.Memory.Row("yz_manager",30)["password"]);Assert.Null(f.Memory.Row("sys_session",50).GetValueOrDefault("delete_no"));
    }

    /// <summary>嵌套业务失败时账号、角色和会话全部回滚。</summary>
    [Fact]
    public async Task Reset_rollback_keeps_all_rows_when_outer_business_fails()
    {
        using var f=new Fixture();f.ExistingAccount();var business=await f.Business();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Memory.Session.InTransactionAsync(async()=>
        {
            Assert.True((await business.InitializeAsync("91")).Applied);throw new InvalidOperationException("离线模拟外层失败");
#pragma warning disable CS0162
            return true;
#pragma warning restore CS0162
        }));
        Assert.Empty(f.TargetRoles);Assert.Equal("旧摘要",f.Memory.Row("yz_manager",30)["password"]);
    }

    /// <summary>重复初始化复用主键与角色，旧密码失效，新密码仍能经真实认证登录。</summary>
    [Fact]
    public async Task Repeated_initialization_reuses_account_and_super_role_and_old_password_no_longer_logs_in()
    {
        using var f=new Fixture();var business=await f.Business();var first=await business.InitializeAsync("91");
        var account=Assert.Single(f.TargetAccounts);var firstDigest=SqlRowStore.Text(account,"password");
        f.Admin["password"]="修改后的初始化密码";
        var second=await business.InitializeAsync("91");Assert.Equal(first.AccountId,second.AccountId);Assert.Single(f.TargetRoles);
        Assert.NotEqual(firstDigest,SqlRowStore.Text(account,"password"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Authentication.LoginAsync("initial-account","原初始化密码"));
        var login=await f.Authentication.LoginAsync("initial-account","修改后的初始化密码");
        Assert.NotNull(await f.Authentication.AuthenticateAsync(login.Token,null));
        Assert.Contains(f.Memory.Provider.Commands,sql=>sql.StartsWith("SELECT ",StringComparison.Ordinal)&&sql.Contains("FROM `yz_manager`",StringComparison.Ordinal)&&sql.EndsWith("FOR UPDATE",StringComparison.Ordinal));
    }

    /// <summary>包含墓碑的账号名查询必须加入实际事务。</summary>
    [Fact]
    public async Task Including_deleted_lock_read_requires_transaction()
    {
        using var f=new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await f.Memory.Store.ReadAccountNameForInitializationAsync("initial-account"));
    }

    /// <summary>原实现取同后台ID最大的有效超级角色，不能因为枚举顺序不同而换成另一个角色。</summary>
    [Fact]
    public async Task Existing_highest_super_role_is_reused_with_original_order()
    {
        using var f=new Fixture();
        f.Memory.Add("yz_role",("id",41L),("yz_admin_type_id",91L),("is_super",1));
        f.Memory.Add("yz_role",("id",40L),("yz_admin_type_id",91L),("is_super",1));
        Assert.True((await(await f.Business()).InitializeAsync("91")).Applied);
        Assert.Equal("[\"41\"]",Assert.Single(f.TargetAccounts)["yz_role_ids"]);Assert.Equal(2,f.TargetRoles.Count());
    }
    private const string Page="1696799309006348290";
    private const string Button="1696805746482847746";
    /// <summary>只检查真实存储收到的写命令，不以最终行数掩盖已发生的部分写入。</summary>
    private static void NoWrites(Fixture f)=>Assert.DoesNotContain(f.Memory.Provider.Commands,sql=>sql.StartsWith("UPDATE ",StringComparison.Ordinal)||sql.StartsWith("INSERT ",StringComparison.Ordinal)||sql.StartsWith("DELETE ",StringComparison.Ordinal));

    /// <summary>目录适配只替换传输，真实Reader仍逐项锁读会话、账号、角色、授权及后台归属。</summary>
    private sealed class Fixture:IDisposable
    {
        internal PrincipalRealmFixture Memory {get;}=new(business:true,super:false);
        internal ResourceRegistry Registry {get;}
        internal Resources Resources {get;}
        internal Accessor Accessor {get;}=new();
        internal AuthenticationService Authentication {get;}
        internal Dictionary<string,object?> Admin=>Memory.Row("yz_admin_type",91);
        internal IEnumerable<Dictionary<string,object?>> TargetAccounts=>Memory.Provider.Rows["yz_manager"].Where(row=>SqlRowStore.Text(row,"name")=="initial-account");
        internal IEnumerable<Dictionary<string,object?>> TargetRoles=>(Memory.Provider.Rows.GetValueOrDefault("yz_role")??[]).Where(row=>SqlRowStore.Text(row,"yz_admin_type_id")=="91");
        internal Fixture()
        {
            var catalog=new SecurityResourceCatalog(Memory.DemoProject,[new("1696799308939239425","yz_admin_type"),new("6031906809708544","yz_manager")],
                [new(Page,"1696799308939239425",Memory.DemoProject,Memory.DemoAdmin,true,true,true,false),
                 new("300","6031906809708544",Memory.DemoProject,"91",true,true,true,false)],[],[new(Button,Page)]);
            Registry=new(catalog);Resources=new(catalog,Memory.Session);
            Authentication=new(Memory.Store,Memory.Ids,Registry,new PlatformSecuritySessionPolicy(Memory.Store));
            Memory.Add("yz_admin_type",("id",91L),("yz_project_id",long.Parse(Memory.DemoProject)),("user_name","initial-account"),("password","原初始化密码"));
            Memory.Add("yz_role_permission",("id",70L),("yz_role_id",10L),("config_button_id",long.Parse(Button)),("permission_values","[]"));
        }
        /// <summary>构造带既有档案和历史摘要的目标账号。</summary>
        internal void ExistingAccount()=>Memory.Add("yz_manager",("id",30L),("name","initial-account"),("password","旧摘要"),("salt","旧盐"),("old_password","旧口令"),("notes","保留业务档案"),("real_name","保留姓名"),("yz_admin_type_id",91L),("yz_project_id",long.Parse(Memory.DemoProject)),("yz_role_ids","[]"));
        /// <summary>先通过真实认证建立身份，再使用同一Session构造协调器与业务。</summary>
        internal async Task<AdminAccountInitializationBusiness> Business()
        {
            Accessor.Identity=await Authentication.AuthenticateAsync(Memory.Token,null)??throw new InvalidOperationException("离线身份未建立");
            return new(Memory.Store,new(Memory.Store,Registry,Accessor,new(Memory.Store,Registry,Resources,new PlatformSecurityCurrentSessionPolicy(Memory.Store))),Memory.Ids);
        }
        public void Dispose()=>Memory.Dispose();
    }
    private sealed class Accessor:ISecurityIdentityAccessor
    {
        internal AuthenticatedIdentity Identity {get;set;}=null!;
        public AuthenticatedIdentity RequireIdentity()=>Identity;
    }
    private sealed class ResourceRegistry(SecurityResourceCatalog catalog):ISecurityResourceRegistry
    {
        internal SecurityResourceCatalog Catalog {get;set;}=catalog;
        internal bool Platform {get;set;}
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[Catalog];
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>Platform;
    }
    private sealed class Resources(SecurityResourceCatalog catalog,DatabaseSession expected):ISecurityCurrentResources
    {
        internal SecurityResourceCatalog Catalog {get;set;}=catalog;
        public Task<SecurityResourceCatalog> ReadForUpdateAsync(DatabaseSession session,DbTransaction transaction,long projectId,long adminTypeId,CancellationToken token)
        {
            Assert.Same(expected,session);Assert.Same(session.CurrentTransaction,transaction);return Task.FromResult(Catalog);
        }
        public Task<bool> HasAdminMenuReferencesForUpdateAsync(DatabaseSession session,DbTransaction transaction,long adminTypeId,CancellationToken token)=>throw new InvalidOperationException("初始化不删除后台或菜单。");
    }
}
