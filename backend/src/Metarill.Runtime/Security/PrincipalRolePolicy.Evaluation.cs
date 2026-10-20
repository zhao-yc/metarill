using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public static partial class PrincipalRolePolicy
{
    /// <summary>普通认证与锁后复核共用聚合决策；当前目录只与已发布稳定资源取交集，不从工作区推导身份。</summary>
    internal static PrincipalRoleRealm Evaluate(MetadataRegistry registry,IReadOnlyDictionary<string,object?> account,
        IReadOnlyDictionary<string,object?> admin,long projectId,IReadOnlyList<Dictionary<string,object?>> roleRows,
        IReadOnlyList<Dictionary<string,object?>> grantRows,MetadataCatalog? currentCatalog=null)
    {
        var adminId=SqlRowStore.Id(admin);
        var adminText=adminId.ToString(CultureInfo.InvariantCulture);var projectText=projectId.ToString(CultureInfo.InvariantCulture);
        var requested=Identifiers(SqlRowStore.Text(account,"yz_role_ids"));
        // 只查同真实后台角色，兼容项目列不能把别的后台超级角色带入当前身份。
        var roles=roleRows
            .Where(row=>row.GetValueOrDefault("delete_no") is null && SqlRowStore.Text(row,"yz_admin_type_id")==adminText && requested.Contains(SqlRowStore.Text(row,"id"))).ToArray();
        var roleIds=roles.Select(row=>SqlRowStore.Text(row,"id")).ToHashSet(StringComparer.Ordinal);
        var super=roles.Any(row=>SqlRowStore.Text(row,"is_super")=="1");
        var platformRealm=projectText==registry.Platform.YzProjectId && registry.Platform.YzAdminTypes.Any(item=>item.Id==adminText);
        var platform=super && platformRealm;
        var catalogs=new[]{registry.Platform,registry.Demo}.Where(catalog=>catalog.YzProjectId==projectText).ToArray();
        var publishedPages=catalogs.SelectMany(catalog=>catalog.ConfigPages).Where(page=>page.YzAdminTypeId==adminText && page.YzProjectId==projectText).ToDictionary(page=>page.Id,StringComparer.Ordinal);
        var tableIds=currentCatalog is null?null:currentCatalog.ConfigTables.Where(table=>catalogs.SelectMany(catalog=>catalog.ConfigTables).Any(fixedTable=>fixedTable.Id==table.Id&&fixedTable.TableName==table.TableName)).Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        var pages=(currentCatalog?.ConfigPages??publishedPages.Values.ToList()).Where(page=>page.YzAdminTypeId==adminText&&page.YzProjectId==projectText&&
            publishedPages.TryGetValue(page.Id,out var fixedPage)&&fixedPage.ConfigTableId==page.ConfigTableId&&(tableIds is null||tableIds.Contains(page.ConfigTableId))).ToDictionary(page=>page.Id,StringComparer.Ordinal);
        var publishedMenus=catalogs.SelectMany(catalog=>catalog.ConfigMenus).Where(menu=>menu.YzAdminTypeId==adminText&&menu.YzProjectId==projectText).Select(menu=>menu.Id).ToHashSet(StringComparer.Ordinal);
        var menus=(currentCatalog?.ConfigMenus??catalogs.SelectMany(catalog=>catalog.ConfigMenus).ToList()).Where(menu=>menu.YzAdminTypeId==adminText&&menu.YzProjectId==projectText&&publishedMenus.Contains(menu.Id)).Select(menu=>menu.Id).ToHashSet(StringComparer.Ordinal);
        var publishedButtons=catalogs.SelectMany(catalog=>catalog.ConfigButtons).Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("id",out _)).ToDictionary(button=>button.GetProperty("id").ToString(),StringComparer.Ordinal);
        var buttons=(currentCatalog?.ConfigButtons??publishedButtons.Values.ToList()).Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("id",out var id)&&publishedButtons.TryGetValue(id.ToString(),out var fixedButton)&&
            button.TryGetProperty("configPageId",out var page)&&pages.ContainsKey(page.ToString())&&fixedButton.TryGetProperty("configPageId",out var fixedTarget)&&fixedTarget.ToString()==page.ToString()).Select(button=>button.GetProperty("id").ToString()).ToHashSet(StringComparer.Ordinal);
        var permissions=new HashSet<string>(StringComparer.Ordinal);var grantedMenus=new HashSet<string>(StringComparer.Ordinal);var grantedButtons=new HashSet<string>(StringComparer.Ordinal);
        if (super)
        {
            foreach(var page in pages.Values)foreach(var action in PageActions(page))permissions.Add(page.Id+":"+action);
            grantedMenus.UnionWith(menus);grantedButtons.UnionWith(buttons);
        }
        // 保留单次授权表读取，不增加逐页面/菜单查询；仅实际同后台角色的已发布资源参与聚合。
        if (roleIds.Count>0)
        foreach(var row in grantRows)
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
}
