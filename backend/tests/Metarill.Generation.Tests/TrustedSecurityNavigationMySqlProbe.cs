using System.Globalization;
using System.Net;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>复用严格实例/自有项目租约的真实Web夹具，导航仍执行实际宿主模板。</summary>
internal static class TrustedSecurityNavigationMySqlProbe
{
    /// <summary>菜单授权与角色页动作分别变化，下一真实HTTP请求必须反映当前持久权限。</summary>
    internal static async Task VerifyAsync(HttpClient client,SqlRowStore store,MetadataCatalog catalog,long role,long menuGrant)
    {
        const string rolePage="1692532858529329154";var custom=Assert.Single(catalog.ConfigMenus,menu=>menu.Kind=="CUSTOM_PAGE");var target=Assert.Single(catalog.ConfigCustomPages,page=>page.Id==custom.ConfigCustomPageId);
        var grant=Assert.Single(await store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",role},{"config_page_id",long.Parse(rolePage,CultureInfo.InvariantCulture)}}));var grantId=SqlRowStore.Id(grant);
        var original=grant.GetValueOrDefault("permission_values");
        try
        {
            await Visible(true);
            await store.UpdateAsync("yz_role_permission",grantId,new Dictionary<string,object?>{{"permission_values","[]"}});
            await Visible(true);
            using(var denied=await client.GetAsync("/api/admin/sys/yzp_permission/roles"))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            await store.UpdateAsync("yz_role_permission",grantId,new Dictionary<string,object?>{{"permission_values",original}});
            await store.UpdateAsync("yz_role_permission",menuGrant,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});await Visible(false);
            await store.UpdateAsync("yz_role_permission",menuGrant,new Dictionary<string,object?>{{"delete_no",null}});await Visible(true);
        }
        finally
        {
            await store.UpdateAsync("yz_role_permission",grantId,new Dictionary<string,object?>{{"permission_values",original}});
            await store.UpdateAsync("yz_role_permission",menuGrant,new Dictionary<string,object?>{{"delete_no",null}});
        }
        /// <summary>实际JSON验证原route/pageId、真实归属及无伪造计数端点，不只看菜单名称。</summary>
        async Task Visible(bool expected)
        {
            using var response=await client.GetAsync("/api/admin/sys/yz_manager/getMenu");Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var menu=json.RootElement;Assert.Equal(200,menu.GetProperty("code").GetInt32());Assert.False(menu.GetProperty("permissions").GetProperty("isSuper").GetBoolean());
            var nodes=Flatten(menu.GetProperty("menu")).Where(node=>node.GetProperty("menu_id").GetString()==custom.Id).ToArray();
            if(!expected){Assert.Empty(nodes);return;}
            var node=Assert.Single(nodes);Assert.Equal(catalog.YzProjectId,node.GetProperty("projectId").GetString());Assert.Equal(target.YzAdminTypeId,node.GetProperty("adminTypeId").GetString());var actual=node.GetProperty("target");Assert.Equal("/"+target.PagePath,actual.GetProperty("route").GetString());Assert.Equal(target.Id,actual.GetProperty("pageId").GetString());Assert.False(actual.TryGetProperty("countEndpoint",out _));Assert.False(actual.TryGetProperty("tableName",out _));
        }
    }
    /// <summary>目录本身不代表叶节点已授权，逐层寻找实际导航叶。</summary>
    private static IEnumerable<JsonElement> Flatten(JsonElement nodes)
    {foreach(var node in nodes.EnumerateArray()){yield return node;foreach(var child in Flatten(node.GetProperty("children")))yield return child;}}
}
