using System.Security;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>独立应用宿主导航只面向包内已发布生成页，不包含平台配置、自定义页面或项目切换。</summary>
internal sealed class DevelopmentNavigation(MetadataCatalog metadata,IHttpContextAccessor accessor,ApplicationAuthenticationMode mode)
{
    /// <summary>菜单显示不代表操作授权，权限仍逐次调用宿主IRuntimeAuthorization核验。</summary>
    public object Permissions(IRuntimeAuthorization authorization)
    {
        var actor = authorization.RequireActor();
        var pages = metadata.ConfigPages.ToDictionary(page => page.Id, page => new[] { ("list", "is_query"), ("create", "is_new"), ("update", "is_update"), ("delete", "is_delete"), ("copy", "is_all"), ("import", "is_all"), ("export", "is_export") }.Where(item => Allowed(authorization, page.Id, item.Item1)).Select(item => item.Item2).Distinct().ToArray());
        var identity=Identity();
        return new { isSuper = identity?.IsRealmSuper ?? actor.IsAdministrator, pagePermissions = pages, buttonPermissions = identity?.ButtonIds.ToArray() ?? Array.Empty<string>() };
    }

    /// <summary>沿用公共菜单树协议，计数与CRUD路由均使用生成页真实身份。</summary>
    public object Menu(IRuntimeAuthorization authorization)
    {
        var actor = authorization.RequireActor();
        if (actor.ProjectId != metadata.YzProjectId) throw new UnauthorizedAccessException();
        var identity=Identity();
        var menus = metadata.ConfigMenus.Where(menu => menu.IsHidden != 1 && menu.Kind is "DIRECTORY" or "GENERATED_PAGE")
            .Where(menu=>identity is null || menu.YzAdminTypeId==identity.PrincipalAdminTypeId && (menu.Kind=="DIRECTORY" || identity.MenuIds.Contains(menu.Id))).ToArray();
        object? Page(MenuMetadata menu, HashSet<string> ancestors)
        {
            if (!ancestors.Add(menu.Id)) throw new InvalidOperationException("菜单存在循环引用。");
            try
            {
                var page = metadata.ConfigPages.SingleOrDefault(candidate => candidate.Id == menu.ConfigPageId);
                if (menu.Kind == "GENERATED_PAGE" && (page is null || !Allowed(authorization, page.Id, "list"))) return null;
                var table = page is null ? null : metadata.ConfigTables.Single(candidate => candidate.Id == page.ConfigTableId);
                var route = table is null ? null : "/pages/yzp/" + table.TableName + "-list-" + page!.Id;
                return new { menu_id = menu.Id, text = menu.Name, icon = menu.IconName ?? "", projectId = metadata.YzProjectId, adminTypeId = menu.YzAdminTypeId, nodeType = menu.Kind, isShowCount = menu.IsShowCount == 1,
                    target = table is null ? null : new { pageId = page!.Id, tableId = table.Id, tableName = table.TableName, route, countEndpoint = "/admin/auto/" + table.TableName + "/" + page.Id + "/count" },
                    children = menus.Where(candidate => candidate.ParentMenuId == menu.Id).OrderBy(candidate => candidate.SortNo).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).Select(candidate => Page(candidate, ancestors)).Where(node => node is not null).ToArray() };
            }
            finally { ancestors.Remove(menu.Id); }
        }
        var roots = menus.Where(menu => string.IsNullOrEmpty(menu.ParentMenuId)).OrderBy(menu => menu.SortNo).ThenBy(menu => menu.Id, StringComparer.Ordinal).Select(menu => Page(menu, new HashSet<string>())).Where(node => node is not null).ToArray();
        if (metadata.ConfigMenus.Count == 0) roots = metadata.ConfigPages.Where(page => Allowed(authorization, page.Id, "list")).OrderBy(page => page.SortNo).ThenBy(page => page.Id, StringComparer.Ordinal)
            .Select(page => Page(new MenuMetadata { Id = page.Id, Name = page.Name, Kind = "GENERATED_PAGE", ConfigPageId = page.Id, YzAdminTypeId = page.YzAdminTypeId }, new HashSet<string>())).ToArray();
        return new { code = 200, menu = roots, permissions = Permissions(authorization) };
    }
    /// <summary>正式模式只取本请求的真实身份；缺失不回退为开发超级账户。</summary>
    private AuthenticatedIdentity? Identity()=>mode.Development?null:accessor.HttpContext?.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException();
    /// <summary>拒绝项只影响菜单和按钮展示，不把异常当成默认授权。</summary>
    private static bool Allowed(IRuntimeAuthorization authorization, string page, string operation)
    {
        try { authorization.RequirePagePermission(page, operation); return true; }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException) { return false; }
    }
}
