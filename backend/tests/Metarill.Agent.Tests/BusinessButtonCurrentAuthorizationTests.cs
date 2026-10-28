using System.Data.Common;
using System.Security;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Runtime.Files;
namespace Metarill.Agent.Tests;

/// <summary>真实认证/角色策略/SqlRowStore/当前读协调器的离线合同；数据库传输不承担MySQL锁与隔离证明。</summary>
public sealed class BusinessButtonCurrentAuthorizationTests
{
    [Fact]
    public async Task Stable_button_only_scope_uses_ordered_current_reads_and_keeps_request_snapshot()
    {
        using var f = new Fixture(); await f.Authenticate(); var original = f.Accessor.Identity;
        Assert.Empty(original.Permissions); Assert.Contains(Fixture.Button, original.ExactDataManageButtonIds);
        await f.Check(); Assert.Same(original, f.Accessor.Identity); Assert.Equal(1, f.Resources.Calls);
        var locks = f.Memory.Provider.Commands.Where(sql => sql.Contains("FOR UPDATE", StringComparison.Ordinal)).ToList();
        var previous = -1;
        foreach (var table in new[] { "yz_project", "yz_admin_type", "sys_session", "yz_manager", "yz_role", "yz_role_permission" })
        {
            var index = locks.FindIndex(sql => sql.Contains("FROM `" + table + "` ", StringComparison.Ordinal)); Assert.True(index > previous, table); previous = index;
        }
    }
    [Theory]
    [InlineData("grant-deleted")]
    [InlineData("role-deleted")]
    [InlineData("account-disabled")]
    [InlineData("session-expired")]
    [InlineData("session-deleted")]
    [InlineData("super-changed")]
    [InlineData("admin-moved")]
    [InlineData("button-moved")]
    [InlineData("table-changed")]
    public async Task Stale_request_cannot_keep_revoked_or_rebound_button(string change)
    {
        using var f = new Fixture(); await f.Authenticate(); Assert.Contains(Fixture.Button, f.Accessor.Identity.ButtonIds);
        switch (change)
        {
            case "grant-deleted": f.Grant["delete_no"] = DateTime.UtcNow; break;
            case "role-deleted": f.Memory.Role["delete_no"] = DateTime.UtcNow; break;
            case "account-disabled": f.Memory.Account["is_disabled"] = 1; break;
            case "session-expired": f.Memory.WebSession["expires_at"] = DateTime.UtcNow.AddMinutes(-1); break;
            case "session-deleted": f.Memory.WebSession["delete_no"] = DateTime.UtcNow; break;
            case "super-changed": f.Memory.Role["is_super"] = 1; break;
            case "admin-moved": f.Memory.Account["yz_admin_type_id"] = 999L; break;
            case "button-moved": f.Resources.Catalog = f.Resources.Catalog with { Buttons = [new(Fixture.Button, "80022")] }; break;
            case "table-changed": f.Resources.Catalog = f.Resources.Catalog with { Tables = [new("80011", "other_table")] }; break;
        }
        var error = await Record.ExceptionAsync(f.Check); Assert.NotNull(error); Assert.True(error is UnauthorizedAccessException or SecurityException, error.GetType().Name);
        Assert.DoesNotContain("仅甲", error.Message); Assert.Contains(Fixture.Button, f.Accessor.Identity.ButtonIds);
    }
    [Theory]
    [InlineData("value")]
    [InlineData("scope")]
    [InlineData("ordinary-same-button")]
    public async Task Changed_match_value_or_effective_scope_requires_new_request(string change)
    {
        using var f = new Fixture(); await f.Authenticate();
        if (change == "value") f.Memory.Account["real_name"] = "仅乙";
        if (change == "scope") f.Memory.Role["is_scope"] = 0;
        if (change == "ordinary-same-button") f.ExtraRole(sameButton: true);
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(f.Check); Assert.DoesNotContain("仅乙", error.Message);
        Assert.Contains(Fixture.Button, f.Accessor.Identity.ExactDataManageButtonIds);
    }
    [Fact]
    public async Task Unrelated_page_or_other_button_grant_does_not_cancel_this_button_scope()
    {
        using var f = new Fixture(); await f.Authenticate(); f.ExtraRole(sameButton: false); await f.Check();
        Assert.Contains(Fixture.Button, f.Accessor.Identity.ExactDataManageButtonIds); Assert.Empty(f.Accessor.Identity.Permissions);
    }
    [Fact]
    public async Task Second_check_reads_own_transaction_changes_instead_of_cached_identity()
    {
        using var f = new Fixture(); await f.Authenticate();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Memory.Session.InTransactionAsync(async () =>
        {
            await f.Current(); f.Memory.Account["real_name"] = "事务内变化"; await f.Current(); return true;
        }));
        Assert.Equal(2, f.Resources.Calls); Assert.Equal("仅甲", SqlRowStore.Text(f.Memory.Account, "real_name"));
    }
    [Fact]
    public async Task Missing_current_adapter_does_not_fall_back_to_published_catalog()
    {
        using var f = new Fixture(); await f.Authenticate(); f.Resources.Unavailable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(f.Check); Assert.Contains(Fixture.Button, f.Accessor.Identity.ButtonIds);
    }
    [Fact]
    public async Task Foreign_transaction_and_wrong_actor_fail_before_any_current_sql()
    {
        using var f = new Fixture(); using var other = new PrincipalRealmFixture(); await f.Authenticate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.Session.InTransactionAsync(async () =>
        {
            await f.Guard.RequireBusinessButtonAsync(Fixture.Page, Fixture.Button, f.Accessor.Identity.Actor, other.Session.CurrentTransaction!); return true;
        })); Assert.Empty(f.Memory.Provider.Commands);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Memory.Session.InTransactionAsync(async () =>
        {
            await f.Guard.RequireBusinessButtonAsync(Fixture.Page, Fixture.Button, f.Accessor.Identity.Actor with { AccountId = 9 }, f.Memory.Session.CurrentTransaction!); return true;
        })); Assert.Empty(f.Memory.Provider.Commands);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Agent_grant_is_checked_before_session_and_cannot_borrow_web_identity(bool valid)
    {
        using var f = new Fixture(); await f.Authenticate(); f.Accessor.Identity = f.Accessor.Identity with { GrantId = 99 };
        f.Memory.WebSession["kind"] = "agent"; f.Memory.WebSession["grant_id"] = 99L; f.Policy.Valid = valid;
        if (valid) await f.Check(); else await Assert.ThrowsAsync<UnauthorizedAccessException>(f.Check);
        Assert.Equal(1, f.Policy.Calls); var grant = f.Memory.Provider.Commands.IndexOf("LOCKED_AGENT_GRANT");
        var session = f.Memory.Provider.Commands.FindIndex(sql => sql.Contains("FROM `sys_session` ", StringComparison.Ordinal)); Assert.True(grant >= 0 && session > grant);
    }
    /// <summary>平台原管理员跨后台能力不增加身份门槛，来源后台必须在身份锁前预先声明并锁定。</summary>
    [Fact]
    public async Task Platform_administrator_source_realm_is_prelocked_without_requiring_page_update()
    {
        using var f = new Fixture(platform: true); await f.Authenticate(); Assert.True(f.Accessor.Identity.Actor.IsAdministrator);
        await f.Check(); Assert.Equal(2, f.Resources.Calls);
        var commands = f.Memory.Provider.Commands; var firstSession = commands.FindIndex(sql => sql.Contains("FROM `sys_session` ", StringComparison.Ordinal));
        Assert.Equal(2, commands.Take(firstSession).Count(sql => sql.Contains("FROM `yz_admin_type` ", StringComparison.Ordinal) && sql.Contains("FOR UPDATE", StringComparison.Ordinal)));
    }
    /// <summary>额外来源目录即使由适配器返回null也明确拒绝，不借身份后台的目录顶替。</summary>
    [Fact]
    public async Task Missing_declared_source_catalog_is_not_replaced_by_principal_or_published_catalog()
    {
        using var f = new Fixture(platform: true); await f.Authenticate(); f.Resources.FailAdditional = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(f.Check); Assert.Contains("不能回退发布快照", error.Message); Assert.Equal(2, f.Resources.Calls);
    }
    /// <summary>JSON数值比较不能把超出JS安全整数范围的相邻long误判相等。</summary>
    [Theory]
    [InlineData(9007199254740992L,9007199254740993L)]
    [InlineData(9223372036854775806L,9223372036854775807L)]
    public async Task Adjacent_large_numeric_match_values_cannot_reuse_old_request(long previous,long current)
    {
        using var f = new Fixture(); f.Memory.Account["real_name"] = previous; await f.Authenticate();
        Assert.Equal(previous, f.Accessor.Identity.ExactDataManageMatchValue!.Value.GetInt64()); f.Memory.Account["real_name"] = current;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(f.Check);
    }
    /// <summary>当前文件身份只在真实宿主的文件用途回调内生效，普通请求和普通页权不被替换。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task File_source_revocation_is_current_and_scope_is_restored_after_await_or_failure(bool fail)
    {
        using var f=new Fixture();f.FileGrant();await f.Authenticate();var original=f.Accessor.Identity;
        var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=original;
        using var services=new ServiceCollection().AddSingleton(f.Guard).BuildServiceProvider();
        var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Memory.Registry,services);
        auth.RequireFileSourcePagePermission(f.Donor);f.Memory.Row("yz_role_permission",80)["delete_no"]=DateTime.UtcNow;
        await f.Memory.Session.InTransactionAsync(async()=>
        {
            await f.Current();
            async Task Callback()
            {
                await Task.Yield();Assert.Same(original,auth.RequireIdentity());auth.RequirePagePermission(f.Donor,"detail");
                Assert.Throws<UnauthorizedAccessException>(()=>auth.RequireFileSourcePagePermission(f.Donor));
                if(fail)throw new FileScopeFailure();
            }
            var action=()=>auth.WithButtonFileCurrentAsync(Fixture.Page,Fixture.Button,original.Actor,f.Memory.Session.CurrentTransaction!,Callback);
            if(fail)await Assert.ThrowsAsync<FileScopeFailure>(action);else await action();
            auth.RequireFileSourcePagePermission(f.Donor);Assert.Same(original,http.Items[typeof(AuthenticatedIdentity)]);return true;
        });
        auth.RequireFileSourcePagePermission(f.Donor);
    }
    /// <summary>文件匹配使用锁后账号值；它不改变已完成写前范围授权的普通按钮快照。</summary>
    [Fact]
    public async Task Current_file_matching_value_does_not_overwrite_button_or_general_identity()
    {
        using var f=new Fixture();f.FileGrant();await f.Authenticate();var original=f.Accessor.Identity;
        var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=original;
        using var services=new ServiceCollection().AddSingleton(f.Guard).BuildServiceProvider();
        var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Memory.Registry,services);
        await f.Memory.Session.InTransactionAsync(async()=>
        {
            await f.Current();f.Memory.Account["real_name"]="仅乙";
            await auth.WithButtonFileCurrentAsync(Fixture.Page,Fixture.Button,original.Actor,f.Memory.Session.CurrentTransaction!,async()=>
            {
                await Task.Yield();var scope=auth.ResolveFileSourceExactDataManage(f.Donor,"real_name");
                Assert.True(scope.IsRequired);Assert.Equal(new[]{"仅乙"},scope.Read<string>());
                Assert.Equal("仅甲",auth.RequireIdentity().ExactDataManageMatchValue!.Value.GetString());
            });
            Assert.Equal(new[]{"仅甲"},auth.ResolveFileSourceExactDataManage(f.Donor,"real_name").Read<string>());return true;
        });
    }
    /// <summary>没有同事务成功按钮证明时，不执行任何文件回调，不在记录锁后首次规划安全锁。</summary>
    [Fact]
    public async Task File_current_read_without_prior_button_proof_fails_before_security_sql()
    {
        using var f=new Fixture();await f.Authenticate();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Memory.Session.InTransactionAsync(async()=>
        {
            await f.FileIdentity();return true;
        }));Assert.Empty(f.Memory.Provider.Commands);
    }
    /// <summary>第二次当前读拒绝新增角色集合，且拒绝发生在任何新角色SQL之前。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Late_role_expansion_cannot_take_new_role_locks(bool file)
    {
        using var f=new Fixture();await f.Authenticate();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Memory.Session.InTransactionAsync(async()=>
        {
            await f.Current();f.ExtraRole(false);f.Memory.Provider.Commands.Clear();
            if(file)await f.FileIdentity();else await f.Current();return true;
        }));Assert.DoesNotContain(f.Memory.Provider.Commands,sql=>sql.Contains("FROM `yz_role` ",StringComparison.Ordinal));
    }
    /// <summary>当前按钮撤权、错误事务和错误操作者都不能借已存在的文件证明继续。</summary>
    [Theory][InlineData("revoked")][InlineData("actor")][InlineData("transaction")][InlineData("other-button")]
    public async Task File_current_read_is_bound_to_live_button_actor_and_transaction(string change)
    {
        using var f=new Fixture();using var other=new PrincipalRealmFixture();await f.Authenticate();
        await f.Memory.Session.InTransactionAsync(async()=>
        {
            await f.Current();f.Memory.Provider.Commands.Clear();
            if(change=="revoked")f.Grant["delete_no"]=DateTime.UtcNow;
            var actor=f.Accessor.Identity.Actor;
            async Task Call()=>await f.Guard.ReadBusinessButtonFileIdentityAsync(Fixture.Page,change=="other-button"?"80032":Fixture.Button,
                change=="actor"?actor with{AccountId=9}:actor,change=="transaction"?other.Session.CurrentTransaction!:f.Memory.Session.CurrentTransaction!);
            if(change=="transaction")await other.Session.InTransactionAsync(async()=>{await Assert.ThrowsAsync<InvalidOperationException>(Call);return true;});
            else if(change=="other-button")await Assert.ThrowsAsync<InvalidOperationException>(Call);
            else if(change=="revoked")await Assert.ThrowsAsync<SecurityException>(Call);
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(Call);
            if(change!="revoked")Assert.Empty(f.Memory.Provider.Commands);return true;
        });
    }
    /// <summary>实际文件观察器与真实平台授权器共同执行；只有传输替身，不模拟文件来源权限结论。</summary>
    [Theory][InlineData("allowed")][InlineData("revoked")][InlineData("mismatch")][InlineData("tail-rollback")][InlineData("rebound")]
    public async Task Real_file_observer_uses_current_donor_permission_and_preserves_transaction(string scenario)
    {
        using var f=new Fixture();var model=f.FileModel();f.FileGrant();await f.Authenticate();var original=f.Accessor.Identity;
        var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=original;
        using var services=new ServiceCollection().AddSingleton(f.Guard).BuildServiceProvider();
        var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Memory.Registry,services);
        var observer=new FileReferenceMutationObserver(f.Memory.Store,new([model]),auth,f.Memory.Ids);
        f.Memory.Add("sample_contract",("id",77L),("yz_project_id",long.Parse(original.Actor.ProjectId)),("attachments",null));
        f.Memory.Add("yz_file_library",("id",500L),("yz_project_id",long.Parse(original.Actor.ProjectId)),("status","ready"),("owner_id",9L),("access_mode","PROTECTED"),("size",3L),("name","sample.txt"));
        var donorTable=model.ConfigTables.Single(value=>value.Id!= "80011").TableName;
        f.Memory.Add(donorTable,("id",78L),("yz_project_id",long.Parse(original.Actor.ProjectId)),("attachments","[\"500\"]"),("real_name","仅甲"));
        f.Memory.Add("yz_file_ref",("id",501L),("file_id",500L),("page_id",long.Parse(f.Donor)),("record_id",78L),("field_name","attachments"),("yz_project_id",long.Parse(original.Actor.ProjectId)));
        async Task<bool> Write()=>await f.Memory.Session.InTransactionAsync(async()=>
        {
            await f.Current();
            if(scenario=="revoked")f.Memory.Row("yz_role_permission",80)["delete_no"]=DateTime.UtcNow;
            if(scenario=="mismatch")f.Memory.Account["real_name"]="仅乙";
            if(scenario=="rebound")f.Resources.Catalog=f.Resources.Catalog with{Pages=f.Resources.Catalog.Pages.Select(page=>page.Id==f.Donor?page with{TableId="80011"}:page).ToArray()};
            var values=new Dictionary<string,object?>{{"attachments","[\"500\"]"}};
            f.Memory.Row("sample_contract",77)["attachments"]=values["attachments"];
            await observer.BeforeCommitAsync(new(Fixture.Page,"sample_contract",77,original.Actor.ProjectId,values,false,f.Memory.Session.CurrentTransaction!){BusinessButtonId=Fixture.Button});
            if(scenario=="tail-rollback")throw new FileScopeFailure();return true;
        });
        if(scenario=="allowed")
        {
            await Write();Assert.Equal(2,f.Memory.Provider.Rows["yz_file_ref"].Count);
            Assert.Equal("[\"500\"]",f.Memory.Row("sample_contract",77)["attachments"]);
        }
        else
        {
            if(scenario=="tail-rollback")await Assert.ThrowsAsync<FileScopeFailure>(async()=>await Write());
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await Write());
            Assert.Single(f.Memory.Provider.Rows["yz_file_ref"]);Assert.Null(f.Memory.Row("sample_contract",77)["attachments"]);
            if(scenario!="tail-rollback")Assert.DoesNotContain(f.Memory.Provider.Commands,sql=>sql.StartsWith("INSERT INTO `yz_file_ref`",StringComparison.Ordinal));
        }
        Assert.Same(original,auth.RequireIdentity());
    }
    /// <summary>文件字段省略不新增当前证明；真正提交时，旧宿主缺省拒绝且不能先查询业务行或写引用。</summary>
    [Fact]
    public async Task Omitted_file_field_keeps_original_path_but_legacy_file_host_cannot_write()
    {
        using var f=new Fixture();var model=f.FileModel();await f.Authenticate();
        var auth=new LegacyButtonHost(f.Accessor.Identity.Actor);
        var observer=new FileReferenceMutationObserver(f.Memory.Store,new([model]),auth,f.Memory.Ids);
        await f.Memory.Session.InTransactionAsync(async()=>
        {
            var context=new RecordMutationContext(Fixture.Page,"sample_contract",77,auth.RequireActor().ProjectId,new Dictionary<string,object?>(),false,f.Memory.Session.CurrentTransaction!){BusinessButtonId=Fixture.Button};
            await observer.BeforeCommitAsync(context);Assert.Equal(0,auth.ButtonCalls);
            await Assert.ThrowsAsync<NotSupportedException>(()=>observer.BeforeCommitAsync(context with{SubmittedFields=new Dictionary<string,object?>{{"attachments",null}}}));
            Assert.Equal(1,auth.ButtonCalls);return true;
        });Assert.Empty(f.Memory.Provider.Commands);
    }
    private sealed class LegacyButtonHost(RuntimeActor actor):IRuntimeAuthorization
    {
        internal int ButtonCalls;
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string pageId,string operation)=>throw new UnauthorizedAccessException();
        public void RequireButtonPermission(string pageId,string buttonId)=>ButtonCalls++;
    }
    private sealed class FileScopeFailure:Exception;
    private sealed class Fixture : IDisposable
    {
        internal const string Page = "80021", Button = "80031";
        internal readonly PrincipalRealmFixture Memory;
        internal readonly Accessor Accessor = new(); internal readonly Registry Registry; internal readonly Resources Resources; internal readonly Policy Policy;
        internal readonly AuthenticationService Authentication; internal readonly SecurityRealmMutationGuard Guard;
        internal string Donor=>Memory.DemoPage;
        internal Dictionary<string, object?> Grant => Memory.Row("yz_role_permission", 70);
        internal Fixture(bool platform = false)
        {
            Memory = new(business: !platform, super: platform);
            var project = platform ? Memory.PlatformProject : Memory.DemoProject; var admin = platform ? "91" : Memory.DemoAdmin;
            if (platform) Memory.Add("yz_admin_type", ("id", 91L), ("yz_project_id", long.Parse(project)));
            var catalog = new SecurityResourceCatalog(project, [new("80011", "sample_contract")], [new(Page, "80011", project, admin, false, false, false, false)], [], [new(Button, Page), new("80032", Page)]);
            if(!platform)
            {
                var donor=Memory.Registry.Demo.ConfigPages.Single(value=>value.Id==Donor);
                var table=Memory.Registry.Demo.ConfigTables.Single(value=>value.Id==donor.ConfigTableId);
                catalog=catalog with{Tables=[..catalog.Tables,new(table.Id,table.TableName)],Pages=[..catalog.Pages,new(donor.Id,table.Id,project,admin,false,false,false,false)]};
            }
            Registry = new(catalog, platform, Memory.PlatformAdmin); Resources = new(catalog, Memory); Policy = new(Memory);
            Memory.Role["is_scope"] = 1; Memory.Account["real_name"] = "仅甲";
            Memory.Add("yz_role_permission", ("id", 70L), ("yz_role_id", 10L), ("config_button_id", long.Parse(Button)), ("permission_values", "[]"));
            Authentication = new(Memory.Store, Memory.Ids, Registry, new PlatformSecuritySessionPolicy(Memory.Store));
            Guard = new(Memory.Store, Registry, Accessor, new(Memory.Store, Registry, Resources, Policy));
        }
        /// <summary>先通过真实认证获取旧快照，之后的当前读才允许观察持久数据变化。</summary>
        internal async Task Authenticate() { Accessor.Identity = await Authentication.AuthenticateAsync(Memory.Token, Registry.PublishedCatalogs[0].ProjectId) ?? throw new InvalidOperationException("测试身份未建立"); Memory.Provider.Commands.Clear(); }
        /// <summary>当前证明必须处于原写事务；不在测试中自己计算授权结果。</summary>
        internal Task<bool> Check() => Memory.Session.InTransactionAsync(async () => { await Current(); return true; });
        /// <summary>发布最小双页文件模型；平台宿主仍使用其真实页面/按钮授权方法。</summary>
        internal MetadataCatalog FileModel()
        {
            var donor=Memory.Registry.Demo.ConfigPages.Single(value=>value.Id==Donor);
            var donorTable=Memory.Registry.Demo.ConfigTables.Single(value=>value.Id==donor.ConfigTableId);
            var project=Memory.DemoProject;var target=new PageMetadata{Id=Page,ConfigTableId="80011",YzProjectId=project,YzAdminTypeId=Memory.DemoAdmin};
            var targetTable=new TableMetadata{Id="80011",TableName="sample_contract",YzProjectId=project};
            Memory.Registry.Demo.ConfigPages.Add(target);Memory.Registry.Demo.ConfigTables.Add(targetTable);
            Memory.Registry.Demo.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id=Button,configPageId=Page}));
            var model=new MetadataCatalog{YzProjectId=project,ConfigTables=[targetTable,donorTable],ConfigPages=[target,new(){Id=Donor,ConfigTableId=donorTable.Id,YzProjectId=project,YzAdminTypeId=Memory.DemoAdmin}]};
            var admin=new AdminTypeMetadata{Id=Memory.DemoAdmin,YzProjectId=project};admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");model.YzAdminTypes.Add(admin);
            model.ConfigPages[1].Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
            var i=80700;
            foreach(var table in model.ConfigTables)
            {
                foreach(var (name,type,control) in new[]{("id","int64",""),("yz_project_id","int64",""),("attachments","string","FileLibrary"),("real_name","string","")})
                {
                    var column=new ColumnMetadata{Id=(i++).ToString(),ConfigTableId=table.Id,ColumnName=name,LogicalType=type,ControlKind=control};model.ConfigColumns.Add(column);
                    if(name=="attachments")model.ConfigPageColumns.Add(new(){Id=(i++).ToString(),ConfigPageId=table.Id=="80011"?Page:Donor,ConfigColumnId=column.Id,ColumnName=name,IsEdit=1,IsShow=1});
                }
            }
            return model;
        }
        /// <summary>真实来源页授权参与角色聚合，文件用途不可借按钮权限或查询继承顶替。</summary>
        internal void FileGrant()=>Memory.Add("yz_role_permission",("id",80L),("yz_role_id",10L),("config_page_id",long.Parse(Donor)),("permission_values","[\"is_query\"]"));
        internal Task<AuthenticatedIdentity> FileIdentity()=>Guard.ReadBusinessButtonFileIdentityAsync(Page,Button,Accessor.Identity.Actor,Memory.Session.CurrentTransaction!);
        internal Task Current() => Guard.RequireBusinessButtonAsync(Page, Button, Accessor.Identity.Actor, Memory.Session.CurrentTransaction!);
        /// <summary>增添实际角色/授权行，当前策略只可按该按钮自己的授权决定是否解除范围。</summary>
        internal void ExtraRole(bool sameButton)
        {
            Memory.Account["yz_role_ids"] = "[\"10\",\"11\"]";
            Memory.Add("yz_role", ("id", 11L), ("yz_admin_type_id", long.Parse(Memory.DemoAdmin)), ("yz_project_id", long.Parse(Memory.DemoProject)), ("is_super", 0), ("is_scope", 0));
            Memory.Add("yz_role_permission", ("id", 71L), ("yz_role_id", 11L), ("config_button_id", sameButton ? long.Parse(Button) : 80032L), ("permission_values", "[]"));
            Memory.Add("yz_role_permission", ("id", 72L), ("yz_role_id", 11L), ("config_page_id", long.Parse(Page)), ("permission_values", "[\"is_query\"]"));
        }
        public void Dispose() => Memory.Dispose();
    }
    private sealed class Accessor : ISecurityIdentityAccessor { internal AuthenticatedIdentity Identity = null!; public AuthenticatedIdentity RequireIdentity() => Identity; }
    private sealed class Registry(SecurityResourceCatalog catalog, bool platform, string principalAdmin) : ISecurityResourceRegistry
    {
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs => [catalog];
        public bool IsPlatformRealm(string project, string admin) => platform && project == catalog.ProjectId && admin == principalAdmin;
        public bool IsAgentRealm(string project, string admin) => project == catalog.ProjectId && catalog.Pages.Any(page => page.AdminTypeId == admin);
        public string? ExactDataManageManagerField(string project, string admin) => "real_name";
    }
    /// <summary>只替入固定目录适配，连接/事务绑定和真实角色策略不替换。</summary>
    private sealed class Resources(SecurityResourceCatalog catalog, PrincipalRealmFixture memory) : ISecurityCurrentResources
    {
        internal SecurityResourceCatalog Catalog = catalog; internal int Calls; internal bool Unavailable, FailAdditional;
        public Task<SecurityResourceCatalog> ReadForUpdateAsync(DatabaseSession session, DbTransaction transaction, long project, long admin, CancellationToken token)
        { Assert.Same(memory.Session, session); Assert.Same(session.CurrentTransaction, transaction); Calls++; if (Unavailable) throw new InvalidOperationException("当前目录不可用"); return Task.FromResult(FailAdditional && Calls > 1 ? null! : Catalog); }
        public Task<bool> HasAdminMenuReferencesForUpdateAsync(DatabaseSession session, DbTransaction transaction, long admin, CancellationToken token) => throw new NotSupportedException("按钮当前读不查询后台删除引用。");
    }
    /// <summary>仅替入grant持锁证明，授权绑定求值仍由真实当前读取器在session锁后执行。</summary>
    private sealed class Policy(PrincipalRealmFixture memory) : ISecurityCurrentSessionPolicy
    {
        internal bool Valid = true; internal int Calls;
        public Task<ISecurityLockedAgentGrant?> ReadAgentGrantForUpdateAsync(DatabaseSession session, DbTransaction transaction, long grant, CancellationToken token)
        { Assert.Same(memory.Session, session); Assert.Same(session.CurrentTransaction, transaction); Calls++; memory.Provider.Commands.Add("LOCKED_AGENT_GRANT"); return Task.FromResult<ISecurityLockedAgentGrant?>(new Locked(this, grant)); }
        private sealed class Locked(Policy owner, long id) : ISecurityLockedAgentGrant
        { public bool IsValidFor(SecuritySessionBinding binding) => owner.Valid && binding.GrantId == id; }
    }
}
