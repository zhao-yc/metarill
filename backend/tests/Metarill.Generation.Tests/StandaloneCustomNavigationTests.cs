using System.Security;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际独立宿主模板的菜单协议；固定身份不冒充真实登录或数据库验收。</summary>
public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>自定义菜单沿原菜单授权展示，不要求生成页查询权，也不制造CRUD或计数入口。</summary>
    [Fact]
    public async Task Published_custom_menu_keeps_original_route_protocol_and_action_boundary()
    {
        var model=NavigationCatalog();var result=NavigationMenu(model,["902"],false);var custom=Assert.Single(NavigationFlatten(result.GetProperty("menu")),n=>n.GetProperty("nodeType").GetString()=="CUSTOM_PAGE");
        Assert.Equal("902",custom.GetProperty("menu_id").GetString());var target=custom.GetProperty("target");Assert.Equal("/pages/main/set_role_permission",target.GetProperty("route").GetString());Assert.Equal("201",target.GetProperty("pageId").GetString());Assert.False(target.TryGetProperty("tableId",out _));Assert.False(target.TryGetProperty("countEndpoint",out _));
        Assert.False(result.GetProperty("permissions").GetProperty("isSuper").GetBoolean());Assert.Empty(result.GetProperty("permissions").GetProperty("pagePermissions").GetProperty("101").EnumerateArray());
        var repository=Path.GetFullPath(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,".."));var evidence=Path.Combine(repository,".local/custom-navigation/node-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);var payload=Path.Combine(evidence,"menu.json");await File.WriteAllTextAsync(payload,result.GetRawText());
        var start=NonRedundantApplicationFixture.Start("node",repository);start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;start.ArgumentList.Add(Path.Combine(repository,"backend/tests/Metarill.Generation.Tests/StandaloneCustomNavigationProbe.mjs"));start.ArgumentList.Add(Path.Combine(repository,"frontend/metarill_uni_admin/common/yzp_runtime/menuRuntime.js"));start.ArgumentList.Add(payload);await NonRedundantApplicationFixture.RunAsync(start);
    }
    /// <summary>未知/隐藏/跨项目后台入口不能因菜单ID提示放行；路径只来自当前发布目录。</summary>
    [Theory]
    [InlineData("grant")][InlineData("hidden")][InlineData("menuProject")][InlineData("menuAdmin")][InlineData("targetProject")][InlineData("targetAdmin")][InlineData("missing")]
    public void Unpublished_or_foreign_custom_targets_are_not_exposed(string defect)
    {
        var model=NavigationCatalog();var menu=model.ConfigMenus.Single(m=>m.Id=="902");var target=model.ConfigCustomPages.Single();
        if(defect=="hidden")menu.IsHidden=1;if(defect=="menuProject")menu.YzProjectId="200";if(defect=="menuAdmin")menu.YzAdminTypeId="6";if(defect=="targetProject")target.YzProjectId="200";if(defect=="targetAdmin")target.YzAdminTypeId="6";if(defect=="missing")model.ConfigCustomPages.Clear();
        var result=NavigationMenu(model,defect=="grant"?[]:["902"],false);Assert.DoesNotContain(NavigationFlatten(result.GetProperty("menu")),n=>n.GetProperty("menu_id").GetString()=="902");
    }
    /// <summary>发布目录中的外部地址或越界路径仍拒绝，不把自定义页当成任意URL跳转。</summary>
    [Theory]
    [InlineData("https://example.invalid/steal")][InlineData("pages/../private")][InlineData("pages/main/view?admin=1")][InlineData("javascript:alert(1)")]
    public void Invalid_custom_routes_reject_without_navigation(string path)
    {var model=NavigationCatalog();model.ConfigCustomPages[0].PagePath=path;Assert.Throws<InvalidOperationException>(()=>NavigationMenu(model,["902"],false));}
    /// <summary>原生成页的表身份与计数协议保持，不因加入自定义节点变化。</summary>
    [Fact]
    public void Generated_menu_preserves_the_original_table_and_count_endpoint()
    {var result=NavigationMenu(NavigationCatalog(),["901"],true);var page=Assert.Single(NavigationFlatten(result.GetProperty("menu")),n=>n.GetProperty("nodeType").GetString()=="GENERATED_PAGE");var target=page.GetProperty("target");Assert.Equal("101",target.GetProperty("pageId").GetString());Assert.Equal("1",target.GetProperty("tableId").GetString());Assert.Equal("records",target.GetProperty("tableName").GetString());Assert.Equal("/admin/auto/records/101/count",target.GetProperty("countEndpoint").GetString());}
    /// <summary>以真实编译模板执行菜单，仅用窄测试授权器指定动作；原身份对象在当前请求中传入。</summary>
    private JsonElement NavigationMenu(MetadataCatalog model,string[] menus,bool query)
    {
        var actor=new RuntimeActor(7,"导航协议测试",false,"100","5");var identity=new AuthenticatedIdentity(actor,new HashSet<string>(),1,null){PrincipalProjectId="100",PrincipalAdminTypeId="5",MenuIds=menus.ToHashSet(StringComparer.Ordinal)};
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};accessor.HttpContext.Items[typeof(AuthenticatedIdentity)]=identity;var mode=package.New("ApplicationAuthenticationMode",false,true);var navigation=package.New("DevelopmentNavigation",model,accessor,mode);
        return JsonSerializer.SerializeToElement(package.Invoke(navigation,"Menu",new NavigationAuthorization(actor,query)));
    }
    /// <summary>父目录和每个子节点逐一检查，不把目录存在当成叶节点已显示。</summary>
    private static IEnumerable<JsonElement> NavigationFlatten(JsonElement nodes)
    {foreach(var node in nodes.EnumerateArray()){yield return node;foreach(var child in NavigationFlatten(node.GetProperty("children")))yield return child;}}
    /// <summary>同一发布目录声明普通页面与自定义页面，不创建实际数据库。</summary>
    private static MetadataCatalog NavigationCatalog()=>new()
    {
        YzProjectId="100",YzAdminTypes=[new(){Id="5",YzProjectId="100"},new(){Id="6",YzProjectId="100"}],ConfigTables=[new(){Id="1",TableName="records",YzProjectId="100"}],ConfigPages=[new(){Id="101",ConfigTableId="1",YzProjectId="100",YzAdminTypeId="5"}],ConfigCustomPages=[new(){Id="201",YzProjectId="100",YzAdminTypeId="5",PagePath="pages/main/set_role_permission"}],
        ConfigMenus=[new(){Id="900",Name="管理",YzProjectId="100",YzAdminTypeId="5"},new(){Id="901",ParentMenuId="900",Name="记录",YzProjectId="100",YzAdminTypeId="5",Kind="GENERATED_PAGE",ConfigPageId="101",IsShowCount=1},new(){Id="902",ParentMenuId="900",Name="权限",YzProjectId="100",YzAdminTypeId="5",Kind="CUSTOM_PAGE",ConfigCustomPageId="201",IsShowCount=1}]
    };
    /// <summary>菜单可见不产生生成页或自定义接口的数据授权。</summary>
    private sealed class NavigationAuthorization(RuntimeActor actor,bool query):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation){if(!query||page!="101"||operation!="list")throw new SecurityException("导航测试拒绝页动作。");}
    }
}
