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
    /// <summary>本片不开放普通平台账号或业务后台超级账号的工作台操作。</summary>
    [Theory]
    [InlineData(false,false)] [InlineData(true,true)]
    public async Task Transitional_entry_gate_keeps_non_platform_administrator_denied(bool businessRealm,bool super)
    {
        using var f=new PrincipalRealmFixture(businessRealm,super);await Seed(f);var business=Business(f,await f.Identity());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.RolesAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SnapshotAsync("11"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SaveAsync(new("11",new string('a',64),new([],new(),[]))));
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
        return new(f.Store,new MetadataRepository(f.Store,f.Registry),authorization,f.Ids);
    }
    private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,JsonDefaults.Options);
}
