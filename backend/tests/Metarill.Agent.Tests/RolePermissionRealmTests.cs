using System.Text.Json;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>真实工作台Business与SqlRowStore只接固定离线SQL传输，不建立网络或冒充MySQL锁验收。</summary>
public sealed class RolePermissionRealmTests
{
    /// <summary>选中业务工作区仍按身份后台列角色；兼容项目列不会引入别的后台。</summary>
    [Fact]
    public async Task Roles_follow_principal_admin_instead_of_selected_workspace_or_compatibility_project()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var business=Business(f,await f.Identity(f.DemoProject));
        var result=Json(await business.RolesAsync());var roles=result.GetProperty("roles").EnumerateArray().ToArray();
        Assert.Contains(roles,role=>role.GetProperty("id").GetString()=="11");
        Assert.DoesNotContain(roles,role=>role.GetProperty("id").GetString()=="12");
    }
    /// <summary>角色、菜单、页面及按钮目录都固定在真实身份后台，不能按工作区换目录。</summary>
    [Fact]
    public async Task Snapshot_uses_same_admin_catalog_and_rejects_other_admin_role()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var business=Business(f,await f.Identity(f.DemoProject));
        var result=Json(await business.SnapshotAsync("11"));Assert.Equal("11",result.GetProperty("roleId").GetString());
        var pages=result.GetProperty("catalog").GetProperty("pages").EnumerateArray().ToArray();
        Assert.NotEmpty(pages);Assert.Contains(pages,page=>page.GetProperty("id").GetString()==f.Registry.Platform.ConfigPages[0].Id);
        Assert.DoesNotContain(pages,page=>page.GetProperty("id").GetString()==f.DemoPage);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("12"));
    }
    /// <summary>从同一草稿重读稳定；角色超级属性和目录变化都必须使旧revision失效。</summary>
    [Fact]
    public async Task Revision_covers_role_super_and_directory_without_mutating_grants()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var business=Business(f,await f.Identity());
        var original=Json(await business.SnapshotAsync("13")).GetProperty("revision").GetString()!;
        Assert.Equal(original,Json(await business.SnapshotAsync("13")).GetProperty("revision").GetString());
        f.Row("yz_role",13)["is_super"]=1;
        Assert.NotEqual(original,Json(await business.SnapshotAsync("13")).GetProperty("revision").GetString());
        f.Row("yz_role",13)["is_super"]=0;
        var page=f.Provider.Rows["config_page"].First(row=>SqlRowStore.Text(row,"yz_admin_type_id")==f.PlatformAdmin);
        page["name"]="已经变化的资源目录";
        Assert.NotEqual(original,Json(await business.SnapshotAsync("13")).GetProperty("revision").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.SaveAsync(new("13",original,new([],new(),[]))));
        Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_role_permission")??[]);
    }
    /// <summary>保存使用身份项目，且跨后台资源失败不能改掉已有授权。</summary>
    [Fact]
    public async Task Save_uses_principal_project_and_foreign_resource_rolls_back()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var business=Business(f,await f.Identity(f.DemoProject));
        var snapshot=Json(await business.SnapshotAsync("11"));var page=f.Registry.Platform.ConfigPages[0].Id;
        var saved=Json(await business.SaveAsync(new("11",snapshot.GetProperty("revision").GetString()!,new([],new(){[page]=["is_query"]},[]))));
        var grant=Assert.Single(f.Provider.Rows["yz_role_permission"]);Assert.Equal(f.PlatformProject,grant["yz_project_id"]!.ToString());
        var before=JsonSerializer.Serialize(f.Provider.Rows["yz_role_permission"]);
        await Assert.ThrowsAsync<ArgumentException>(()=>business.SaveAsync(new("11",saved.GetProperty("revision").GetString()!,new([],new(){[f.DemoPage]=["is_query"]},[]))));
        Assert.Equal(before,JsonSerializer.Serialize(f.Provider.Rows["yz_role_permission"]));
    }
    /// <summary>同项目下的其他后台资源也不得进入目录或保存白名单。</summary>
    [Fact]
    public async Task Other_admin_in_same_project_never_enters_role_or_resource_catalog()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);
        f.Add("yz_admin_type",("id",44L),("name","同项目另一后台"),("yz_project_id",long.Parse(f.PlatformProject)));
        f.Add("yz_role",("id",14L),("name","同项目另一后台角色"),("yz_admin_type_id",44L),("yz_project_id",long.Parse(f.PlatformProject)),("is_super",0));
        f.Add("config_page",("id",45L),("name","另一后台页面"),("config_table_id",long.Parse(f.Registry.Platform.ConfigTables[0].Id)),("yz_admin_type_id",44L),("yz_project_id",long.Parse(f.PlatformProject)));
        f.Add("config_menu",("id",46L),("name","另一后台菜单"),("kind","DIRECTORY"),("yz_admin_type_id",44L),("yz_project_id",long.Parse(f.PlatformProject)));
        f.Add("config_button",("id",47L),("name","另一后台按钮"),("config_page_id",45L),("yz_project_id",long.Parse(f.PlatformProject)));
        var business=Business(f,await f.Identity());var snapshot=Json(await business.SnapshotAsync("13"));var catalog=snapshot.GetProperty("catalog");
        Assert.DoesNotContain(Json(await business.RolesAsync()).GetProperty("roles").EnumerateArray(),role=>role.GetProperty("id").GetString()=="14");
        Assert.DoesNotContain(catalog.GetProperty("pages").EnumerateArray(),page=>page.GetProperty("id").GetString()=="45");
        Assert.DoesNotContain(catalog.GetProperty("buttons").EnumerateArray(),button=>button.GetProperty("id").GetString()=="47");
        Assert.DoesNotContain(catalog.GetProperty("menus").GetProperty("roots").EnumerateArray(),menu=>menu.GetProperty("id").GetString()=="46");
        await Assert.ThrowsAsync<ArgumentException>(()=>business.SaveAsync(new("13",snapshot.GetProperty("revision").GetString()!,new([],new(){["45"]=["is_query"]},[]))));
        Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_role_permission")??[]);
    }
    /// <summary>按钮和菜单目录变化也使旧草稿冲突，不能只覆盖页开关。</summary>
    [Theory]
    [InlineData("config_menu")] [InlineData("config_button")]
    public async Task Revision_covers_menu_and_button_catalog_changes(string table)
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var business=Business(f,await f.Identity());
        var revision=Json(await business.SnapshotAsync("13")).GetProperty("revision").GetString()!;
        var row=f.Provider.Rows[table].First(value=>SqlRowStore.Text(value,"yz_project_id")==f.PlatformProject);row["name"]="已变化目录";
        Assert.NotEqual(revision,Json(await business.SnapshotAsync("13")).GetProperty("revision").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>business.SaveAsync(new("13",revision,new([],new(),[]))));
        Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_role_permission")??[]);
    }
    /// <summary>普通账号仅持原角色页查询权可进入只读工作台，不能借查询或菜单授权保存。</summary>
    [Fact]
    public async Task Original_role_page_query_permission_allows_snapshot_but_not_save()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);GrantRolePage(f,"is_query");var business=Business(f,await f.Identity());
        Assert.NotEmpty(Json(await business.RolesAsync()).GetProperty("roles").EnumerateArray());
        var snapshot=Json(await business.SnapshotAsync("13"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SaveAsync(new("13",snapshot.GetProperty("revision").GetString()!,new([],new(),[]))));
    }
    /// <summary>原角色页明确修改或全功能授权可保存；不是平台超级身份才可操作。</summary>
    [Theory]
    [InlineData("is_update")][InlineData("is_all")][InlineData("is_update_only")]
    public async Task Original_role_page_edit_permission_can_save_same_realm_grants(string action)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);GrantRolePage(f,"is_query",action=="is_update_only"?"is_update":action);var business=Business(f,await f.Identity());
        var snapshot=Json(await business.SnapshotAsync("13"));var page=f.Registry.Platform.ConfigPages[0].Id;
        if(action=="is_update_only")
        {
            f.Row("yz_role_permission",71)["permission_values"]=JsonSerializer.Serialize(new[]{"is_update"});business=Business(f,await f.Identity());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.RolesAsync());
        }
        var result=Json(await business.SaveAsync(new("13",snapshot.GetProperty("revision").GetString()!,new([],new(){[page]=["is_query"]},[]))));
        Assert.Equal("13",result.GetProperty("roleId").GetString());Assert.Single(f.Provider.Rows["yz_role_permission"],row=>SqlRowStore.Text(row,"yz_role_id")=="13");
    }
    /// <summary>业务后台超级角色仅管理自己的真实后台，兼容project列不决定资源归属。</summary>
    [Fact]
    public async Task Business_realm_super_uses_real_target_admin_project_without_platform_super_gate()
    {
        using var f=new PrincipalRealmFixture(business:true,super:true);await Seed(f);f.Row("yz_role",12)["yz_project_id"]=long.Parse(f.PlatformProject);
        var identity=await f.Identity();Assert.False(identity.Actor.IsAdministrator);Assert.True(identity.IsRealmSuper);var business=Business(f,identity);
        var snapshot=Json(await business.SnapshotAsync("12"));
        Assert.All(snapshot.GetProperty("catalog").GetProperty("pages").EnumerateArray(),page=>Assert.Contains(f.Registry.Demo.ConfigPages,definition=>definition.Id==page.GetProperty("id").GetString()));
        await business.SaveAsync(new("12",snapshot.GetProperty("revision").GetString()!,new([],new(){[f.DemoPage]=["is_query"]},[])));
        Assert.Equal(f.DemoProject,Assert.Single(f.Provider.Rows["yz_role_permission"])["yz_project_id"]!.ToString());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("13"));
    }
    /// <summary>同后台其他页面的权限不能冒充角色管理权限；跨后台污染ID也不能扩大入口。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Non_role_page_or_foreign_published_permissions_never_authorize_workbench(bool foreign)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);
        var page=foreign?f.DemoPage:f.Registry.Platform.ConfigPages.First(page=>f.Registry.Platform.ConfigTables.Single(table=>table.Id==page.ConfigTableId).TableName!="yz_role").Id;
        var identity=(await f.Identity()) with {Permissions=new HashSet<string>{page+":is_query",page+":is_update",page+":is_all"}};
        var business=Business(f,identity);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.RolesAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("13"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SaveAsync(new("13",new string('a',64),new([],new(),[]))));
    }
    /// <summary>实际配置中存在的同后台角色页若未发布，也不能凭内部污染的permission开放入口。</summary>
    [Fact]
    public async Task Unpublished_role_page_permission_is_not_a_trusted_management_resource()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);
        var table=f.Registry.Platform.ConfigTables.Single(table=>table.TableName=="yz_role").Id;
        f.Add("config_page",("id",909L),("name","未发布角色页"),("config_table_id",long.Parse(table)),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("is_edit",1),("is_update",1));
        var identity=(await f.Identity()) with {Permissions=new HashSet<string>{"909:is_query","909:is_update","909:is_all"}};
        var business=Business(f,identity);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.RolesAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("13"));
    }
    /// <summary>原目录已关闭角色页修改时，旧内部权限快照不能把该动作重新打开。</summary>
    [Fact]
    public async Task Current_role_page_action_must_still_be_enabled_for_ordinary_editor()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);GrantRolePage(f,"is_query","is_update");var identity=await f.Identity();var business=Business(f,identity);
        var snapshot=Json(await business.SnapshotAsync("13"));
        var page=f.Provider.Rows["config_page"].Single(row=>SqlRowStore.Text(row,"id")==SqlRowStore.Text(f.Row("yz_role_permission",71),"config_page_id"));page["is_update"]=0;
        var denied=await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SaveAsync(new("13",snapshot.GetProperty("revision").GetString()!,new([],new(),[]))));
        Assert.Contains("修改权限",denied.Message);
    }
    /// <summary>没有角色页动作权的真实普通账号继续拒绝，不能把恢复原规则变成配置页全面开放。</summary>
    [Fact]
    public async Task Ordinary_without_role_page_permission_remains_denied()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);var business=Business(f,await f.Identity());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.RolesAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("13"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SaveAsync(new("13",new string('a',64),new([],new(),[]))));
    }
    /// <summary>已经认证的内部快照也不能覆盖实际账号后台迁移，目标始终由服务器真实记录验证。</summary>
    [Fact]
    public async Task Principal_admin_change_after_authentication_is_not_accepted_as_workspace_switch()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var identity=await f.Identity(f.DemoProject);f.Account["yz_admin_type_id"]=long.Parse(f.DemoAdmin);
        var business=Business(f,identity);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("13"));
    }
    /// <summary>原页面依赖真实目标页和表名，长ID不能经数字转换或从菜单路由猜测。</summary>
    [Fact]
    public async Task Snapshot_restores_original_menu_target_and_page_table_fields()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);
        const string root="9007199254740993",child="9007199254740994";
        var roleTable=f.Registry.Platform.ConfigTables.Single(table=>table.TableName=="yz_role");
        var rolePage=f.Registry.Platform.ConfigPages.First(page=>page.ConfigTableId==roleTable.Id&&page.YzAdminTypeId==f.PlatformAdmin);
        f.Add("config_menu",("id",long.Parse(root)),("name","固定测试目录"),("kind","DIRECTORY"),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)));
        f.Add("config_menu",("id",long.Parse(child)),("name","固定角色入口"),("kind","GENERATED_PAGE"),("parent_menu_id",long.Parse(root)),("config_page_id",long.Parse(rolePage.Id)),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)));
        var snapshot=Json(await Business(f,await f.Identity()).SnapshotAsync("13"));var catalog=snapshot.GetProperty("catalog");
        var menu=catalog.GetProperty("menus").GetProperty("roots").EnumerateArray().Single(item=>item.GetProperty("id").GetString()==root).GetProperty("children")[0];
        Assert.Equal(child,menu.GetProperty("id").GetString());Assert.Equal(rolePage.Id,menu.GetProperty("targetPageId").GetString());
        var page=catalog.GetProperty("pages").EnumerateArray().Single(item=>item.GetProperty("id").GetString()==rolePage.Id);
        Assert.Equal(roleTable.Id,page.GetProperty("tableId").GetString());Assert.Equal(roleTable.TableName,page.GetProperty("tableName").GetString());
        Assert.Equal(snapshot.GetProperty("revision").GetString(),Json(await Business(f,await f.Identity()).SnapshotAsync("13")).GetProperty("revision").GetString());
    }
    /// <summary>原编辑权可保存super角色资源，资源草稿既不改超级属性，也不把目标角色赋给操作者。</summary>
    [Theory]
    [InlineData("is_update")][InlineData("is_all")]
    public async Task Ordinary_editor_can_save_super_role_resources_without_becoming_super(string action)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);GrantRolePage(f,"is_query",action);f.Row("yz_role",13)["is_super"]=1;
        var identity=await f.Identity();Assert.False(identity.IsRealmSuper);Assert.False(identity.Actor.IsAdministrator);
        var roleBefore=Fingerprint(f.Row("yz_role",13));var accountBefore=Fingerprint(f.Account);var business=Business(f,identity);
        var snapshot=Json(await business.SnapshotAsync("13"));var page=f.Registry.Platform.ConfigPages[0].Id;
        var saved=Json(await business.SaveAsync(new("13",snapshot.GetProperty("revision").GetString()!,new([],new(){[page]=["is_query"]},[]))));
        Assert.Equal("13",saved.GetProperty("roleId").GetString());Assert.True(saved.GetProperty("isSuper").GetBoolean());
        Assert.Equal(roleBefore,Fingerprint(f.Row("yz_role",13)));Assert.Equal(accountBefore,Fingerprint(f.Account));
        var target=f.Provider.Rows["yz_role_permission"].Where(row=>SqlRowStore.Text(row,"yz_role_id")=="13").ToArray();Assert.True(target.Length==1,"仅应新增目标角色的一笔资源授权。");Assert.Equal(page,SqlRowStore.Text(target[0],"config_page_id"));
        var after=await f.Identity();Assert.False(after.IsRealmSuper);Assert.False(after.Actor.IsAdministrator);
    }
    /// <summary>恢复目标super草稿写入不能绕过真实后台、当前修改权、旧版本及动作白名单。</summary>
    [Theory]
    [InlineData("foreign-realm")][InlineData("query-only")][InlineData("old-revision")][InlineData("invalid-action")]
    public async Task Super_role_resource_mutations_keep_original_denials_and_atomicity(string scenario)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);GrantRolePage(f,"is_query","is_update");f.Row("yz_role",13)["is_super"]=1;
        var business=Business(f,await f.Identity());var snapshot=Json(await business.SnapshotAsync("13"));
        if(scenario=="query-only")f.Row("yz_role_permission",71)["permission_values"]=JsonSerializer.Serialize(new[]{"is_query"});
        if(scenario=="old-revision")f.Row("yz_role",13)["is_scope"]=1;
        var before=Fingerprint(f.Provider.Rows);var page=f.Registry.Platform.ConfigPages[0].Id;
        var request=new RolePermissionSave(scenario=="foreign-realm"?"12":"13",snapshot.GetProperty("revision").GetString()!,new([],new(){[page]=[scenario=="invalid-action"?"is_super":"is_query"]},[]));
        var businessIdentity=await f.Identity();
        Task Save()=>Business(f,businessIdentity).SaveAsync(request);
        if(scenario is "foreign-realm" or "query-only")await Assert.ThrowsAsync<UnauthorizedAccessException>(Save);
        else if(scenario=="old-revision")await Assert.ThrowsAsync<InvalidOperationException>(Save);
        else await Assert.ThrowsAsync<ArgumentException>(Save);
        Assert.Equal(before,Fingerprint(f.Provider.Rows));Assert.Equal("1",SqlRowStore.Text(f.Row("yz_role",13),"is_super"));Assert.False((await f.Identity()).IsRealmSuper);
    }
    /// <summary>只比较完整状态摘要，失败输出不格式化账号或会话原始行。</summary>
    private static string Fingerprint(object value)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    /// <summary>授权字段是服务器固定角色页，账号仍保普通身份，不把夹具改超级角色绕过入口。</summary>
    private static void GrantRolePage(PrincipalRealmFixture f,params string[] actions)
    {
        var roleTable=f.Registry.Platform.ConfigTables.Single(table=>table.TableName=="yz_role").Id;
        var rolePage=f.Registry.Platform.ConfigPages.First(page=>page.ConfigTableId==roleTable&&page.YzAdminTypeId==f.PlatformAdmin).Id;
        f.Add("yz_role_permission",("id",71L),("yz_role_id",10L),("config_page_id",long.Parse(rolePage)),("permission_values",JsonSerializer.Serialize(actions)),("yz_project_id",long.Parse(f.PlatformProject)));
    }
    /// <summary>目录与授权保留原JSON合同；通过真实种子仓储准备内存数据，不造宽松Business替身。</summary>
    private static async Task Seed(PrincipalRealmFixture f)
    {
        var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);await metadata.SeedAsync(f.Registry.Demo);
        f.Add("yz_role",("id",11L),("name","本后台普通角色"),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.DemoProject)),("is_super",0));
        f.Add("yz_role",("id",12L),("name","另一后台角色"),("yz_admin_type_id",long.Parse(f.DemoAdmin)),("yz_project_id",long.Parse(f.DemoProject)),("is_super",0));
        f.Add("yz_role",("id",13L),("name","同项目同后台角色"),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("is_super",0));
    }
    /// <summary>使用产品真实权限适配器，不把客户端workspace伪造为身份realm。</summary>
    private static RolePermissionBusiness Business(PrincipalRealmFixture f,AuthenticatedIdentity identity)
    {
        var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=identity;
        var authorization=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Registry);
        var metadata=new MetadataRepository(f.Store,f.Registry);
        var currentResources=new PlatformSecurityCurrentResources(f.Store,metadata);
        var coordinator=new SecurityRealmMutationGuard(f.Store,f.Registry,authorization,new(f.Store,f.Registry,currentResources,new PlatformSecurityCurrentSessionPolicy(f.Store)));
        return new(f.Store,currentResources,authorization,f.Ids,f.Registry,coordinator);
    }
    private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,JsonDefaults.Options);
}
