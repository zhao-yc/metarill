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
        var actor=authorization.RequireAdministrator();
        return new{supported=true,roles=(await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"yz_project_id",long.Parse(actor.ProjectId)}},token)).Select(r=>new{id=SqlRowStore.Text(r,"id"),name=SqlRowStore.Text(r,"name"),isSuper=SqlRowStore.Text(r,"is_super")=="1"})};
    }
    public async Task<object> SnapshotAsync(string roleId,CancellationToken token=default)
    {
        var actor=authorization.RequireAdministrator();var role=await Role(roleId,actor.ProjectId,false,token);
        var catalog=await metadata.LoadProjectAsync(actor.ProjectId,token);var rows=await Grants(roleId,false,token);
        object[] Tree(string? parent)=>catalog.ConfigMenus.Where(m=>string.IsNullOrEmpty(m.ParentMenuId)?parent is null:m.ParentMenuId==parent).OrderBy(m=>m.SortNo).Select(m=>(object)new{m.Id,m.Name,parentId=m.ParentMenuId,kind=m.Kind,children=Tree(m.Id)}).ToArray();
        var pageGrants=new Dictionary<string,string[]>();
        foreach(var row in rows){var page=SqlRowStore.Text(row,"config_page_id");if(page.Length>0)pageGrants[page]=Values(SqlRowStore.Text(row,"permission_values"));}
        return new{supported=true,mode="RESOURCE",roleId,roleName=SqlRowStore.Text(role,"name"),revision=Revision(rows),isSuper=SqlRowStore.Text(role,"is_super")=="1",issues=Array.Empty<string>(),
            catalog=new{issues=Array.Empty<string>(),menus=new{roots=Tree(null)},pages=catalog.ConfigPages.Select(p=>new{p.Id,p.Name,actions=PageActions(p)}),
                buttons=catalog.ConfigButtons.Select(b=>new{id=b.GetProperty("id").ToString(),name=b.TryGetProperty("name",out var name)?name.ToString():"",pageId=b.TryGetProperty("configPageId",out var page)?page.ToString():""})},
            grants=new RolePermissionGrants(rows.Select(r=>SqlRowStore.Text(r,"config_menu_id")).Where(x=>x.Length>0).Distinct().ToArray(),pageGrants,rows.Select(r=>SqlRowStore.Text(r,"config_button_id")).Where(x=>x.Length>0).Distinct().ToArray())};
    }
    /// <summary>聚合锁与版本比较均在实际数据库事务内；先验证整份草稿再写入。</summary>
    public async Task<object> SaveAsync(RolePermissionSave request,CancellationToken token=default)
    {
        var actor=authorization.RequireAdministrator();
        await store.Session.InTransactionAsync(async()=>
        {
            await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(actor.ProjectId)}},token);
            var role=await Role(request.RoleId,actor.ProjectId,true,token);
            if(SqlRowStore.Text(role,"is_super")=="1")throw new InvalidOperationException("超级管理员权限由角色属性确定，不能在资源工作台改写。");
            var existing=await Grants(request.RoleId,true,token);if(request.Revision!=Revision(existing))throw new InvalidOperationException("YZP_CONFLICT：角色授权已变化，请重新读取。");
            var catalog=await metadata.LoadProjectAsync(actor.ProjectId,token);
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
                await store.InsertAsync("yz_role_permission",new Dictionary<string,object?>{{"id",ids.NextId()},{"yz_role_id",long.Parse(request.RoleId)},{field,long.Parse(resource)},{"permission_values",JsonSerializer.Serialize(actions)},{"yz_project_id",long.Parse(actor.ProjectId)},{"insert_time",DateTime.UtcNow},{"insert_manager_id",actor.AccountId}},token);
            }
            foreach(var menu in request.Grants.MenuIds.Distinct())await Add("config_menu_id",menu,[]);
            foreach(var(page,actions)in request.Grants.PagePermissions.OrderBy(p=>p.Key,StringComparer.Ordinal))if(actions.Length>0)await Add("config_page_id",page,actions.Distinct().Order(StringComparer.Ordinal).ToArray());
            foreach(var button in request.Grants.ButtonIds.Distinct())await Add("config_button_id",button,[]);
            return true;
        },token);
        return await SnapshotAsync(request.RoleId,token);
    }
    private async Task<Dictionary<string,object?>> Role(string id,string project,bool locked,CancellationToken token)
    {
        if(!long.TryParse(id,out var roleId)||roleId<=0)throw new ArgumentException("角色标识无效。");
        var filters=new Dictionary<string,object?>{{"id",roleId},{"yz_project_id",long.Parse(project)}};
        return (locked?await store.ReadForUpdateAsync("yz_role",filters,token):await store.ReadAsync("yz_role",filters,token)).SingleOrDefault()??throw new UnauthorizedAccessException("角色不属于当前项目。");
    }
    private Task<List<Dictionary<string,object?>>> Grants(string role,bool locked,CancellationToken token)
    {
        var filters=new Dictionary<string,object?>{{"yz_role_id",long.Parse(role)}};
        return locked?store.ReadForUpdateAsync("yz_role_permission",filters,token):store.ReadAsync("yz_role_permission",filters,token);
    }
    private static string Revision(IEnumerable<Dictionary<string,object?>> rows)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows.OrderBy(r=>SqlRowStore.Id(r)),JsonDefaults.Options)))).ToLowerInvariant();
    private static string[] Values(string value){try{return JsonSerializer.Deserialize<string[]>(value)??[];}catch(JsonException){return [];}}
    private static string[] PageActions(PageMetadata page)
    {
        var result=new List<string>{"is_query","is_all"};if(page.IsNew==1)result.Add("is_new");if(page.IsSave==1)result.Add("is_update");if(page.IsDelete==1)result.Add("is_delete");
        if(page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var method)&&method.GetString() is "list" or "edit")result.Add("is_export");return result.ToArray();
    }
}
