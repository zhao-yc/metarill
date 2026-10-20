using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Core.Metadata;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed record RolePermissionGrants(IReadOnlyList<string> MenuIds,Dictionary<string,string[]> PagePermissions,IReadOnlyList<string> ButtonIds);
public sealed record RolePermissionSave(string RoleId,string Revision,RolePermissionGrants Grants);

/// <summary>独立保存菜单显示、页面动作和按钮授权，不从菜单推导页面写权限。</summary>
public sealed class RolePermissionBusiness(SqlRowStore store,MetadataRepository metadata,HttpRuntimeAuthorization authorization,IIdGenerator ids)
{
    private static readonly HashSet<string> Actions=["is_query","is_new","is_update","is_delete","is_export","is_all"];
    public async Task<object> RolesAsync(CancellationToken token=default)
    {
        var principal=Principal();
        return new{supported=true,roles=(await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",long.Parse(principal.AdminTypeId)}},token)).Select(r=>new{id=SqlRowStore.Text(r,"id"),name=SqlRowStore.Text(r,"name"),isSuper=SqlRowStore.Text(r,"is_super")=="1"})};
    }
    public async Task<object> SnapshotAsync(string roleId,CancellationToken token=default)
    {
        var principal=Principal();var role=await Role(roleId,principal.AdminTypeId,false,token);
        var catalog=await Catalog(principal,token);var rows=await Grants(roleId,false,token);
        object[] Tree(string? parent)=>catalog.ConfigMenus.Where(m=>string.IsNullOrEmpty(m.ParentMenuId)?parent is null:m.ParentMenuId==parent).OrderBy(m=>m.SortNo).Select(m=>(object)new{m.Id,m.Name,parentId=m.ParentMenuId,kind=m.Kind,children=Tree(m.Id)}).ToArray();
        var pageGrants=new Dictionary<string,string[]>();
        foreach(var row in rows){var page=SqlRowStore.Text(row,"config_page_id");if(page.Length>0)pageGrants[page]=Values(SqlRowStore.Text(row,"permission_values"));}
        return new{supported=true,mode="RESOURCE",roleId,roleName=SqlRowStore.Text(role,"name"),revision=Revision(role,catalog,rows),isSuper=SqlRowStore.Text(role,"is_super")=="1",issues=Array.Empty<string>(),
            catalog=new{issues=Array.Empty<string>(),menus=new{roots=Tree(null)},pages=catalog.ConfigPages.Select(p=>new{p.Id,p.Name,actions=PageActions(p)}),
                buttons=catalog.ConfigButtons.Select(b=>new{id=b.GetProperty("id").ToString(),name=b.TryGetProperty("name",out var name)?name.ToString():"",pageId=b.TryGetProperty("configPageId",out var page)?page.ToString():""})},
            grants=new RolePermissionGrants(rows.Select(r=>SqlRowStore.Text(r,"config_menu_id")).Where(x=>x.Length>0).Distinct().ToArray(),pageGrants,rows.Select(r=>SqlRowStore.Text(r,"config_button_id")).Where(x=>x.Length>0).Distinct().ToArray())};
    }
    /// <summary>聚合锁与版本比较均在实际数据库事务内；先验证整份草稿再写入。</summary>
    public async Task<object> SaveAsync(RolePermissionSave request,CancellationToken token=default)
    {
        var principal=Principal();var actor=principal.Actor;
        await store.Session.InTransactionAsync(async()=>
        {
            await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(principal.ProjectId)}},token);
            var role=await Role(request.RoleId,principal.AdminTypeId,true,token);
            if(SqlRowStore.Text(role,"is_super")=="1")throw new InvalidOperationException("超级管理员权限由角色属性确定，不能在资源工作台改写。");
            var existing=await Grants(request.RoleId,true,token);
            var catalog=await Catalog(principal,token);
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
            foreach(var old in existing)await store.UpdateAsync("yz_role_permission",SqlRowStore.Id(old),new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}},token);
            async Task Add(string field,string resource,string[] actions)
            {
                await store.InsertAsync("yz_role_permission",new Dictionary<string,object?>{{"id",ids.NextId()},{"yz_role_id",long.Parse(request.RoleId)},{field,long.Parse(resource)},{"permission_values",JsonSerializer.Serialize(actions)},{"yz_project_id",long.Parse(principal.ProjectId)},{"insert_time",DateTime.UtcNow},{"insert_manager_id",actor.AccountId}},token);
            }
            foreach(var menu in request.Grants.MenuIds.Distinct())await Add("config_menu_id",menu,[]);
            foreach(var(page,actions)in request.Grants.PagePermissions.OrderBy(p=>p.Key,StringComparer.Ordinal))if(actions.Length>0)await Add("config_page_id",page,actions.Distinct().Order(StringComparer.Ordinal).ToArray());
            foreach(var button in request.Grants.ButtonIds.Distinct())await Add("config_button_id",button,[]);
            return true;
        },token);
        return await SnapshotAsync(request.RoleId,token);
    }
    private async Task<Dictionary<string,object?>> Role(string id,string admin,bool locked,CancellationToken token)
    {
        if(!long.TryParse(id,out var roleId)||roleId<=0)throw new ArgumentException("角色标识无效。");
        var filters=new Dictionary<string,object?>{{"id",roleId},{"yz_admin_type_id",long.Parse(admin)}};
        return (locked?await store.ReadForUpdateAsync("yz_role",filters,token):await store.ReadAsync("yz_role",filters,token)).SingleOrDefault()??throw new UnauthorizedAccessException("角色不属于当前身份后台。");
    }
    private Task<List<Dictionary<string,object?>>> Grants(string role,bool locked,CancellationToken token)
    {
        var filters=new Dictionary<string,object?>{{"yz_role_id",long.Parse(role)}};
        return locked?store.ReadForUpdateAsync("yz_role_permission",filters,token):store.ReadAsync("yz_role_permission",filters,token);
    }
    private sealed record PrincipalScope(RuntimeActor Actor,string AdminTypeId,string ProjectId);
    private sealed record RealmCatalog(IReadOnlyList<MenuMetadata> ConfigMenus,IReadOnlyList<PageMetadata> ConfigPages,IReadOnlyList<JsonElement> ConfigButtons);

    /// <summary>本小片保留既有平台管理员入口；真实身份后台与顶栏工作区不能混用。</summary>
    private PrincipalScope Principal()
    {
        var actor=authorization.RequireAdministrator();var identity=authorization.RequireIdentity();
        if(!long.TryParse(identity.PrincipalAdminTypeId,out var admin)||admin<=0||
            !long.TryParse(identity.PrincipalProjectId,out var project)||project<=0||actor.AdminTypeId!=identity.PrincipalAdminTypeId)
            throw new UnauthorizedAccessException("当前身份后台无效。");
        return new(actor,identity.PrincipalAdminTypeId,identity.PrincipalProjectId);
    }

    /// <summary>先读身份项目，再筛真实同后台资源；同项目的其他后台也不能混进授权目录。</summary>
    private async Task<RealmCatalog> Catalog(PrincipalScope principal,CancellationToken token)
    {
        var catalog=await metadata.LoadProjectAsync(principal.ProjectId,token);
        var pages=catalog.ConfigPages.Where(page=>page.YzAdminTypeId==principal.AdminTypeId&&page.YzProjectId==principal.ProjectId).ToArray();
        var pageIds=pages.Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        return new(catalog.ConfigMenus.Where(menu=>menu.YzAdminTypeId==principal.AdminTypeId&&menu.YzProjectId==principal.ProjectId).ToArray(),pages,
            catalog.ConfigButtons.Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("configPageId",out var page)&&pageIds.Contains(page.ToString())).ToArray());
    }

    /// <summary>角色语义、同后台目录及全部源授权共同组成版本；摘要不输出目录原文或SQL。</summary>
    private static string Revision(Dictionary<string,object?> role,RealmCatalog catalog,IEnumerable<Dictionary<string,object?>> rows)
    {
        var source=new
        {
            role=new{id=SqlRowStore.Text(role,"id"),adminTypeId=SqlRowStore.Text(role,"yz_admin_type_id"),isSuper=SqlRowStore.Text(role,"is_super"),isScope=SqlRowStore.Text(role,"is_scope")},
            catalog=new{menus=catalog.ConfigMenus.OrderBy(menu=>menu.Id,StringComparer.Ordinal),pages=catalog.ConfigPages.OrderBy(page=>page.Id,StringComparer.Ordinal),buttons=catalog.ConfigButtons.OrderBy(button=>button.GetProperty("id").ToString(),StringComparer.Ordinal)},
            rows=rows.OrderBy(row=>SqlRowStore.Id(row)).Select(row=>row.OrderBy(field=>field.Key,StringComparer.Ordinal).ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal))
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source,JsonDefaults.Options)))).ToLowerInvariant();
    }
    private static string[] Values(string value){try{return JsonSerializer.Deserialize<string[]>(value)??[];}catch(JsonException){return [];}}
    private static string[] PageActions(PageMetadata page)
    {
        var result=new List<string>{"is_query","is_all"};if(page.IsNew==1)result.Add("is_new");if(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page))result.Add("is_update");if(page.IsDelete==1)result.Add("is_delete");
        if(page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var method)&&method.GetString() is "list" or "edit")result.Add("is_export");return result.ToArray();
    }
}
