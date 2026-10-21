using System.Globalization;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public static partial class PrincipalRolePolicy
{
    /// <summary>普通认证与锁后复核共用聚合决策；当前目录只与已发布稳定资源取交集，不从工作区推导身份。</summary>
    internal static PrincipalRoleRealm Evaluate(ISecurityResourceRegistry registry,IReadOnlyDictionary<string,object?> account,
        IReadOnlyDictionary<string,object?> admin,long projectId,IReadOnlyList<Dictionary<string,object?>> roleRows,
        IReadOnlyList<Dictionary<string,object?>> grantRows,SecurityResourceCatalog? currentCatalog=null)
    {
        var adminId=SqlRowStore.Id(admin);
        var adminText=adminId.ToString(CultureInfo.InvariantCulture);var projectText=projectId.ToString(CultureInfo.InvariantCulture);
        var requested=Identifiers(SqlRowStore.Text(account,"yz_role_ids"));
        // 只查同真实后台角色，兼容项目列不能把别的后台超级角色带入当前身份。
        var roles=roleRows
            .Where(row=>row.GetValueOrDefault("delete_no") is null && SqlRowStore.Text(row,"yz_admin_type_id")==adminText && requested.Contains(SqlRowStore.Text(row,"id"))).ToArray();
        var roleIds=roles.Select(row=>SqlRowStore.Text(row,"id")).ToHashSet(StringComparer.Ordinal);
        var super=roles.Any(row=>SqlRowStore.Text(row,"is_super")=="1");
        var platformRealm=registry.IsPlatformRealm(projectText,adminText);
        var platform=super && platformRealm;
        var catalogs=registry.PublishedCatalogs.Where(catalog=>catalog.ProjectId==projectText).ToArray();
        var publishedPages=catalogs.SelectMany(catalog=>catalog.Pages).Where(page=>page.AdminTypeId==adminText && page.ProjectId==projectText).ToDictionary(page=>page.Id,StringComparer.Ordinal);
        var tableIds=currentCatalog is null?null:currentCatalog.Tables.Where(table=>catalogs.SelectMany(catalog=>catalog.Tables).Any(fixedTable=>fixedTable.Id==table.Id&&fixedTable.TableName==table.TableName)).Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        var pages=(currentCatalog?.Pages??publishedPages.Values.ToList()).Where(page=>page.AdminTypeId==adminText&&page.ProjectId==projectText&&
            publishedPages.TryGetValue(page.Id,out var fixedPage)&&fixedPage.TableId==page.TableId&&(tableIds is null||tableIds.Contains(page.TableId))).ToDictionary(page=>page.Id,StringComparer.Ordinal);
        var publishedMenus=catalogs.SelectMany(catalog=>catalog.Menus).Where(menu=>menu.AdminTypeId==adminText&&menu.ProjectId==projectText).Select(menu=>menu.Id).ToHashSet(StringComparer.Ordinal);
        var menus=(currentCatalog?.Menus??catalogs.SelectMany(catalog=>catalog.Menus).ToList()).Where(menu=>menu.AdminTypeId==adminText&&menu.ProjectId==projectText&&publishedMenus.Contains(menu.Id)).Select(menu=>menu.Id).ToHashSet(StringComparer.Ordinal);
        var publishedButtons=catalogs.SelectMany(catalog=>catalog.Buttons).ToDictionary(button=>button.Id,StringComparer.Ordinal);
        var buttons=(currentCatalog?.Buttons??publishedButtons.Values.ToList()).Where(button=>publishedButtons.TryGetValue(button.Id,out var fixedButton)&&
            pages.ContainsKey(button.PageId)&&fixedButton.PageId==button.PageId).Select(button=>button.Id).ToHashSet(StringComparer.Ordinal);
        var scopedGrants=new HashSet<string>(StringComparer.Ordinal);var unrestrictedGrants=new HashSet<string>(StringComparer.Ordinal);
        var scopeRoleIds=roles.Where(row=>SqlRowStore.Text(row,"is_scope")=="1").Select(row=>SqlRowStore.Text(row,"id")).ToHashSet(StringComparer.Ordinal);
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
            foreach(var action in PermissionValues(SqlRowStore.Text(row,"permission_values")))
            {
                if(!enabled.Contains(action))continue;var key=pageId+":"+action;permissions.Add(key);
                // 只有当前页当前动作的有效授权参与范围聚合，其他动作或菜单授权不能取消限制。
                (scopeRoleIds.Contains(SqlRowStore.Text(row,"yz_role_id"))?scopedGrants:unrestrictedGrants).Add(key);
            }
        }
        var exactPermissions=new HashSet<string>(StringComparer.Ordinal);
        if(!super)
        foreach(var page in pages.Values)
        foreach(var action in PageActions(page))
        {
            var key=page.Id+":"+action;var all=page.Id+":is_all";
            // is_all是当前动作的另一条有效来源；任一普通角色确实授予当前动作即可解除精确限制。
            if((scopedGrants.Contains(key)||scopedGrants.Contains(all))&&!unrestrictedGrants.Contains(key)&&!unrestrictedGrants.Contains(all))exactPermissions.Add(key);
        }
        return new(adminText,projectText,super,platformRealm,platform,roleIds,permissions,grantedMenus,grantedButtons){ExactDataManagePermissions=exactPermissions};
    }
}
