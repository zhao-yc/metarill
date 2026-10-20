using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>真实账号所属后台与开发工作区分开；后台、项目和角色均来自当前未删除记录。</summary>
public sealed record PrincipalRoleRealm(string AdminTypeId, string ProjectId, bool IsSuper, bool IsPlatformRealm, bool IsPlatformAdministrator,
    IReadOnlySet<string> RoleIds, IReadOnlySet<string> Permissions, IReadOnlySet<string> MenuIds, IReadOnlySet<string> ButtonIds);

/// <summary>Web认证与Agent复核共用同一身份后台策略，不把客户端工作区或账号兼容项目列当授权。</summary>
public static class PrincipalRolePolicy
{
    private static readonly HashSet<string> Actions = ["is_query", "is_new", "is_update", "is_delete", "is_export", "is_all"];

    /// <summary>每次请求读取真实后台链和本后台角色；授权目录仅使用宿主已发布的两个固定模型。</summary>
    public static async Task<PrincipalRoleRealm?> LoadAsync(SqlRowStore store, MetadataRegistry registry,
        IReadOnlyDictionary<string,object?> account, CancellationToken token=default, bool includePermissions=true)
    {
        if (!AuthenticationService.AccountActive(account) || account.GetValueOrDefault("delete_no") is not null ||
            !PositiveId(SqlRowStore.Text(account,"yz_admin_type_id"), out var adminId)) return null;
        var admin=(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"id",adminId}},token)).SingleOrDefault();
        if (admin is null || !PositiveId(SqlRowStore.Text(admin,"yz_project_id"),out var projectId)) return null;
        if ((await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",projectId}},token)).Count!=1) return null;
        var adminText=adminId.ToString(CultureInfo.InvariantCulture);var projectText=projectId.ToString(CultureInfo.InvariantCulture);
        var requested=Identifiers(SqlRowStore.Text(account,"yz_role_ids"));
        // 只查同真实后台角色，兼容项目列不能把别的后台超级角色带入当前身份。
        var roles=(await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",adminId}},token))
            .Where(row=>row.GetValueOrDefault("delete_no") is null && SqlRowStore.Text(row,"yz_admin_type_id")==adminText && requested.Contains(SqlRowStore.Text(row,"id"))).ToArray();
        var roleIds=roles.Select(row=>SqlRowStore.Text(row,"id")).ToHashSet(StringComparer.Ordinal);
        var super=roles.Any(row=>SqlRowStore.Text(row,"is_super")=="1");
        var platformRealm=projectText==registry.Platform.YzProjectId && registry.Platform.YzAdminTypes.Any(item=>item.Id==adminText);
        var platform=super && platformRealm;
        var catalogs=new[]{registry.Platform,registry.Demo}.Where(catalog=>catalog.YzProjectId==projectText).ToArray();
        var pages=catalogs.SelectMany(catalog=>catalog.ConfigPages).Where(page=>page.YzAdminTypeId==adminText && page.YzProjectId==projectText).ToDictionary(page=>page.Id,StringComparer.Ordinal);
        var menus=catalogs.SelectMany(catalog=>catalog.ConfigMenus).Where(menu=>menu.YzAdminTypeId==adminText && menu.YzProjectId==projectText).Select(menu=>menu.Id).ToHashSet(StringComparer.Ordinal);
        var buttons=catalogs.SelectMany(catalog=>catalog.ConfigButtons).Where(button=>button.ValueKind==JsonValueKind.Object &&
            button.TryGetProperty("configPageId",out var page) && pages.ContainsKey(page.ToString()) && button.TryGetProperty("id",out _))
            .Select(button=>button.GetProperty("id").ToString()).ToHashSet(StringComparer.Ordinal);
        var permissions=new HashSet<string>(StringComparer.Ordinal);var grantedMenus=new HashSet<string>(StringComparer.Ordinal);var grantedButtons=new HashSet<string>(StringComparer.Ordinal);
        if (super)
        {
            foreach(var page in pages.Values)foreach(var action in PageActions(page))permissions.Add(page.Id+":"+action);
            grantedMenus.UnionWith(menus);grantedButtons.UnionWith(buttons);
        }
        // 保留单次授权表读取，不增加逐页面/菜单查询；仅实际同后台角色的已发布资源参与聚合。
        if (includePermissions && roleIds.Count>0)
        foreach(var row in await store.ReadAsync("yz_role_permission",cancellationToken:token))
        {
            if(row.GetValueOrDefault("delete_no") is not null || !roleIds.Contains(SqlRowStore.Text(row,"yz_role_id")))continue;
            var menu=SqlRowStore.Text(row,"config_menu_id");if(menus.Contains(menu))grantedMenus.Add(menu);
            var button=SqlRowStore.Text(row,"config_button_id");if(buttons.Contains(button))grantedButtons.Add(button);
            var pageId=SqlRowStore.Text(row,"config_page_id");if(!pages.TryGetValue(pageId,out var definition))continue;
            var enabled=PageActions(definition);
            foreach(var action in PermissionValues(SqlRowStore.Text(row,"permission_values")))if(enabled.Contains(action))permissions.Add(pageId+":"+action);
        }
        return new(adminText,projectText,super,platformRealm,platform,roleIds,permissions,grantedMenus,grantedButtons);
    }

    /// <summary>操作表保留现有is_all语义，具体生成路由和Service仍负责页面开关及数据范围。</summary>
    private static HashSet<string> PageActions(PageMetadata page)
    {
        var result=new HashSet<string>(StringComparer.Ordinal){"is_query","is_all"};
        if(page.IsNew==1)result.Add("is_new");if(PageUpdatePolicy.CanUpdate(page))result.Add("is_update");if(page.IsDelete==1)result.Add("is_delete");
        if(page.IsExport==1 || page.Extra.TryGetValue("exportMethod",out var method) && method.ValueKind==JsonValueKind.String && method.GetString() is "list" or "edit")result.Add("is_export");
        return result;
    }
    /// <summary>损坏、对象、浮点或非规范角色ID整体按无角色处理，不能借字符串转换提权。</summary>
    private static HashSet<string> Identifiers(string encoded)
    {
        var result=new HashSet<string>(StringComparer.Ordinal);
        if(string.IsNullOrWhiteSpace(encoded))return result;
        if(encoded.Length>65536)return result;
        try
        {
            using var document=JsonDocument.Parse(encoded);if(document.RootElement.ValueKind==JsonValueKind.Null)return result;
            if(document.RootElement.ValueKind!=JsonValueKind.Array || document.RootElement.GetArrayLength()>1000)return result;
            foreach(var value in document.RootElement.EnumerateArray())
            {
                var text=value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():null;
                if(text is null || !PositiveId(text,out var id) || text!=id.ToString(CultureInfo.InvariantCulture))return [];
                result.Add(text);
            }
        }
        catch(JsonException){return [];}
        return result;
    }
    /// <summary>权限只接受固定字符串动作；损坏文档不会转换成布尔、对象或旧菜单授权。</summary>
    private static HashSet<string> PermissionValues(string encoded)
    {
        try
        {
            using var document=JsonDocument.Parse(encoded);if(document.RootElement.ValueKind!=JsonValueKind.Array)return [];
            var values=document.RootElement.EnumerateArray().ToArray();if(values.Any(value=>value.ValueKind!=JsonValueKind.String))return [];
            return values.Select(value=>value.GetString()!).Where(Actions.Contains).ToHashSet(StringComparer.Ordinal);
        }
        catch(JsonException){return [];}
    }
    private static bool PositiveId(string text,out long id)=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0;
}
