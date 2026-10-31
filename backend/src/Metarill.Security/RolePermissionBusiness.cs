using Metarill.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed record RolePermissionGrants(IReadOnlyList<string> MenuIds,Dictionary<string,string[]> PagePermissions,IReadOnlyList<string> ButtonIds);
public sealed record RolePermissionSave(string RoleId,string Revision,RolePermissionGrants Grants);

/// <summary>独立保存菜单显示、页面动作和按钮授权，不从菜单推导页面写权限。</summary>
public sealed class RolePermissionBusiness(SqlRowStore store,IRolePermissionCatalog resourceCatalog,ISecurityIdentityAccessor authorization,IIdGenerator ids,ISecurityResourceRegistry registry,SecurityRealmMutationGuard coordinator)
{
    // 完整目录适配是必要依赖；缺失不能退回发布快照或把未知存储当作空目录。
    private readonly IRolePermissionCatalog catalogs=resourceCatalog??throw new ArgumentNullException(nameof(resourceCatalog));
    private static readonly HashSet<string> Actions=["is_query","is_new","is_update","is_delete","is_export","is_all"];
    public async Task<object> RolesAsync(CancellationToken token=default)
    {
        var principal=await Principal(token);
        if(!principal.Identity.IsRealmSuper)RequireRoleAction(principal,await Catalog(principal,token),"is_query");
        return new{supported=true,roles=(await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",long.Parse(principal.AdminTypeId)}},token)).Select(r=>new{id=SqlRowStore.Text(r,"id"),name=SqlRowStore.Text(r,"name"),isSuper=SqlRowStore.Text(r,"is_super")=="1"})};
    }
    public async Task<object> SnapshotAsync(string roleId,CancellationToken token=default)
    {
        var principal=await Principal(token);var role=await Role(roleId,principal,false,token);
        var catalog=await Catalog(principal,token);RequireRoleAction(principal,catalog,"is_query");
        return Snapshot(role,catalog,await Grants(roleId,false,token));
    }
    /// <summary>写入口只需原修改动作；内部生成回执不额外要求查询权，避免提交成功后又返回无权。</summary>
    // 原页面展示字段只从真实目录派生；TableNames不进入既有revision预像。
    private static object Snapshot(Dictionary<string,object?> role,RealmCatalog catalog,IReadOnlyList<Dictionary<string,object?>> rows)
    {
        var roleId=SqlRowStore.Text(role,"id");
        object[] Tree(string? parent)=>catalog.ConfigMenus.Where(m=>string.IsNullOrEmpty(m.ParentMenuId)?parent is null:m.ParentMenuId==parent).OrderBy(m=>m.SortNo).Select(m=>(object)new{m.Id,m.Name,parentId=m.ParentMenuId,kind=m.Kind,targetPageId=m.ConfigPageId,children=Tree(m.Id)}).ToArray();
        var pageGrants=new Dictionary<string,string[]>();
        foreach(var row in rows){var page=SqlRowStore.Text(row,"config_page_id");if(page.Length>0)pageGrants[page]=Values(SqlRowStore.Text(row,"permission_values"));}
        return new{supported=true,mode="RESOURCE",roleId,roleName=SqlRowStore.Text(role,"name"),revision=Revision(role,catalog,rows),isSuper=SqlRowStore.Text(role,"is_super")=="1",issues=Array.Empty<string>(),
            catalog=new{issues=Array.Empty<string>(),menus=new{roots=Tree(null)},pages=catalog.ConfigPages.Select(p=>new{p.Id,p.Name,tableId=p.ConfigTableId,tableName=catalog.TableNames[p.ConfigTableId],actions=PageActions(p)}),
                buttons=catalog.ConfigButtons.Select(b=>new{id=b.GetProperty("id").ToString(),name=b.TryGetProperty("name",out var name)?name.ToString():"",pageId=b.TryGetProperty("configPageId",out var page)?page.ToString():""})},
            grants=new RolePermissionGrants(rows.Select(r=>SqlRowStore.Text(r,"config_menu_id")).Where(x=>x.Length>0).Distinct().ToArray(),pageGrants,rows.Select(r=>SqlRowStore.Text(r,"config_button_id")).Where(x=>x.Length>0).Distinct().ToArray())};
    }
    /// <summary>聚合锁与版本比较均在实际数据库事务内；先验证整份草稿再写入。</summary>
    public async Task<object> SaveAsync(RolePermissionSave request,CancellationToken token=default)
    {
        authorization.RequireIdentity();
        return await store.Session.InTransactionAsync(async()=>
        {
            var scope=await coordinator.PrepareRolePermissionsAsync(request.RoleId,token);
            var identity=scope.Identity;var actor=identity.Actor;
            var principal=new PrincipalScope(actor,identity,identity.PrincipalAdminTypeId,identity.PrincipalProjectId);
            var role=scope.Role;
            // 必须取回同一适配在本事务锁读的原始目录；revision继续消费完整JSON，不能用瘦投影重算。
            var catalog=RealmResources(catalogs.RequireLoaded(store.Session,store.Session.CurrentTransaction??throw new InvalidOperationException("角色工作台事务已结束。"),scope.Catalog)??throw new InvalidOperationException("角色工作台缺少本事务完整目录。"),principal);
            // 原工作台可编辑super角色的资源草稿；这里只写授权行，不改角色超级属性或操作者角色。
            var existing=await Grants(request.RoleId,true,token);
            if(request.Revision!=Revision(role,catalog,existing))throw new InvalidOperationException("YZP_CONFLICT：角色授权或资源目录已变化，请重新读取。");
            if(request.Grants.MenuIds.Count+request.Grants.ButtonIds.Count+request.Grants.PagePermissions.Count>5000)throw new ArgumentException("授权资源数量过多。");
            foreach(var menu in request.Grants.MenuIds)if(!catalog.ConfigMenus.Any(m=>m.Id==menu&&!string.IsNullOrEmpty(m.ParentMenuId)))throw new ArgumentException("菜单授权包含不存在或不可授权的节点。");
            foreach(var(page,actions)in request.Grants.PagePermissions)
            {
                var definition=catalog.ConfigPages.SingleOrDefault(p=>p.Id==page)??throw new ArgumentException("页面授权包含无效页面。");
                var enabled=PageActions(definition);
                if(actions.Any(a=>!Actions.Contains(a)||!enabled.Contains(a)))throw new ArgumentException("页面授权包含未开放动作。");
            }
            foreach(var button in request.Grants.ButtonIds)if(!catalog.ConfigButtons.Any(b=>b.GetProperty("id").ToString()==button))throw new ArgumentException("按钮授权包含无效按钮。");
            foreach(var old in existing)await store.UpdateAsync("yz_role_permission",SqlRowStore.Id(old),new Dictionary<string,object?>{{"delete_no",BusinessCalendar.Now}},token);
            async Task Add(string field,string resource,string[] actions)
            {
                await store.InsertAsync("yz_role_permission",new Dictionary<string,object?>{{"id",ids.NextId()},{"yz_role_id",long.Parse(request.RoleId)},{field,long.Parse(resource)},{"permission_values",JsonSerializer.Serialize(actions)},{"yz_project_id",long.Parse(principal.ProjectId)},{"insert_time",BusinessCalendar.Now},{"insert_manager_id",actor.AccountId}},token);
            }
            foreach(var menu in request.Grants.MenuIds.Distinct())await Add("config_menu_id",menu,[]);
            foreach(var(page,actions)in request.Grants.PagePermissions.OrderBy(p=>p.Key,StringComparer.Ordinal))if(actions.Length>0)await Add("config_page_id",page,actions.Distinct().Order(StringComparer.Ordinal).ToArray());
            foreach(var button in request.Grants.ButtonIds.Distinct())await Add("config_button_id",button,[]);
            return Snapshot(role,catalog,await Grants(request.RoleId,true,token));
        },token);
    }
    /// <summary>请求只定位真实角色；后台和项目从角色及实际后台记录推导，不采用兼容project列。</summary>
    private async Task<Dictionary<string,object?>> Role(string id,PrincipalScope principal,bool locked,CancellationToken token)
    {
        if(!long.TryParse(id,out var roleId)||roleId<=0)throw new ArgumentException("角色标识无效。");
        var filters=new Dictionary<string,object?>{{"id",roleId}};
        var role=(locked?await store.ReadForUpdateAsync("yz_role",filters,token):await store.ReadAsync("yz_role",filters,token)).SingleOrDefault();
        if(role is null||SqlRowStore.Text(role,"yz_admin_type_id")!=principal.AdminTypeId)throw new UnauthorizedAccessException("角色不属于当前身份后台。");
        var admin=(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"id",long.Parse(SqlRowStore.Text(role,"yz_admin_type_id"))}},token)).SingleOrDefault();
        if(admin is null||SqlRowStore.Text(admin,"yz_project_id")!=principal.ProjectId)throw new UnauthorizedAccessException("角色所属后台已变化，请重新登录。");
        return role;
    }
    private Task<List<Dictionary<string,object?>>> Grants(string role,bool locked,CancellationToken token)
    {
        var filters=new Dictionary<string,object?>{{"yz_role_id",long.Parse(role)}};
        return locked?store.ReadForUpdateAsync("yz_role_permission",filters,token):store.ReadAsync("yz_role_permission",filters,token);
    }
    private sealed record PrincipalScope(RuntimeActor Actor,AuthenticatedIdentity Identity,string AdminTypeId,string ProjectId);
    private sealed record RealmCatalog(IReadOnlyList<MenuMetadata> ConfigMenus,IReadOnlyList<PageMetadata> ConfigPages,IReadOnlyList<JsonElement> ConfigButtons,IReadOnlySet<string> RoleTableIds,IReadOnlyDictionary<string,string> TableNames);

    /// <summary>真实账号、后台、项目逐项核对；工作区选择不会切换身份或给跨后台角色授权。</summary>
    private async Task<PrincipalScope> Principal(CancellationToken token)
    {
        var identity=authorization.RequireIdentity();var actor=identity.Actor;
        if(actor.AccountId<=0||!long.TryParse(identity.PrincipalAdminTypeId,out var adminId)||adminId<=0||
            !long.TryParse(identity.PrincipalProjectId,out var projectId)||projectId<=0||actor.AdminTypeId!=identity.PrincipalAdminTypeId)
            throw new UnauthorizedAccessException("当前身份后台无效。");
        var account=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",actor.AccountId}},token)).SingleOrDefault();
        if(account is null||!AuthenticationService.AccountActive(account)||SqlRowStore.Text(account,"yz_admin_type_id")!=identity.PrincipalAdminTypeId)
            throw new UnauthorizedAccessException("当前身份后台已变化，请重新登录。");
        var admin=(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"id",adminId}},token)).SingleOrDefault();
        if(admin is null||SqlRowStore.Text(admin,"yz_project_id")!=identity.PrincipalProjectId||
            (await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",projectId}},token)).Count!=1)
            throw new UnauthorizedAccessException("当前身份后台或项目不存在。");
        return new(actor,identity,SqlRowStore.Text(admin,"id"),SqlRowStore.Text(admin,"yz_project_id"));
    }

    /// <summary>沿用原V8同后台super或角色页动作权；其它页面、未发布页和跨后台脏权限均不能替代。</summary>
    private void RequireRoleAction(PrincipalScope principal,RealmCatalog catalog,string action)
    {
        if(action is not ("is_query" or "is_update"))throw new InvalidOperationException("角色工作台动作无效。");
        if(principal.Identity.IsRealmSuper)return;
        var published=registry.PublishedCatalogs.Where(model=>model.ProjectId==principal.ProjectId).ToArray();
        foreach(var page in catalog.ConfigPages.Where(page=>catalog.RoleTableIds.Contains(page.ConfigTableId)&&PageActions(page).Contains(action)))
        {
            var fixedPage=published.SelectMany(model=>model.Pages).SingleOrDefault(candidate=>candidate.Id==page.Id&&candidate.TableId==page.ConfigTableId&&candidate.AdminTypeId==principal.AdminTypeId&&candidate.ProjectId==principal.ProjectId);
            if(fixedPage is null||!published.SelectMany(model=>model.Tables).Any(table=>table.Id==fixedPage.TableId&&table.TableName=="yz_role"))continue;
            if(principal.Identity.Permissions.Contains(page.Id+":"+action)||principal.Identity.Permissions.Contains(page.Id+":is_all"))return;
        }
        throw new UnauthorizedAccessException(action=="is_query"?"缺少当前后台角色配置页的查询权限。":"缺少当前后台角色配置页的修改权限。");
    }

    /// <summary>先读身份项目，再筛真实同后台资源；同项目的其他后台也不能混进授权目录。</summary>
    private async Task<RealmCatalog> Catalog(PrincipalScope principal,CancellationToken token)
    {
        var catalog=await catalogs.ReadAsync(store.Session,principal.ProjectId,token)??throw new InvalidOperationException("角色工作台完整目录不可用。");
        return RealmResources(catalog,principal);
    }
    /// <summary>普通快照与锁后保存使用同一目录归属筛选，revision解释保持一致。</summary>
    private static RealmCatalog RealmResources(MetadataCatalog catalog,PrincipalScope principal)
    {
        var pages=catalog.ConfigPages.Where(page=>page.YzAdminTypeId==principal.AdminTypeId&&page.YzProjectId==principal.ProjectId).ToArray();
        var pageIds=pages.Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        return new(catalog.ConfigMenus.Where(menu=>menu.YzAdminTypeId==principal.AdminTypeId&&menu.YzProjectId==principal.ProjectId).ToArray(),pages,
            catalog.ConfigButtons.Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("configPageId",out var page)&&pageIds.Contains(page.ToString())).ToArray(),
            catalog.ConfigTables.Where(table=>table.TableName=="yz_role").Select(table=>table.Id).ToHashSet(StringComparer.Ordinal),
            catalog.ConfigTables.ToDictionary(table=>table.Id,table=>table.TableName,StringComparer.Ordinal));
    }

    /// <summary>角色语义、同后台目录及全部源授权共同组成版本；摘要不输出目录原文或SQL。</summary>
    private static string Revision(Dictionary<string,object?> role,RealmCatalog catalog,IEnumerable<Dictionary<string,object?>> rows)
    {
        var source=new
        {
            role=new{id=SqlRowStore.Text(role,"id"),adminTypeId=SqlRowStore.Text(role,"yz_admin_type_id"),isSuper=SqlRowStore.Text(role,"is_super"),isScope=SqlRowStore.Text(role,"is_scope")},
            catalog=new{roleTableIds=catalog.RoleTableIds.Order(StringComparer.Ordinal),menus=catalog.ConfigMenus.OrderBy(menu=>menu.Id,StringComparer.Ordinal),pages=catalog.ConfigPages.OrderBy(page=>page.Id,StringComparer.Ordinal),buttons=catalog.ConfigButtons.OrderBy(button=>button.GetProperty("id").ToString(),StringComparer.Ordinal)},
            rows=rows.OrderBy(row=>SqlRowStore.Id(row)).Select(row=>row.OrderBy(field=>field.Key,StringComparer.Ordinal).ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal))
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source,RolePermissionRevisionJson.Options)))).ToLowerInvariant();
    }
    private static string[] Values(string value){try{return JsonSerializer.Deserialize<string[]>(value)??[];}catch(JsonException){return [];}}
    private static string[] PageActions(PageMetadata page)
    {
        var result=new List<string>{"is_query","is_all"};if(page.IsNew==1)result.Add("is_new");if(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page))result.Add("is_update");if(page.IsDelete==1)result.Add("is_delete");
        if(page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var method)&&method.GetString() is "list" or "edit")result.Add("is_export");return result.ToArray();
    }
}
