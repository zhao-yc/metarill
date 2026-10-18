using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Http;

namespace Metarill.Runtime.Security;

/// <summary>权限依据中间件建立的真实身份，页面/项目选择参数本身不授予权限。</summary>
public sealed class HttpRuntimeAuthorization(IHttpContextAccessor accessor,MetadataRegistry registry) : IRuntimeAuthorization
{
    public AuthenticatedIdentity RequireIdentity()=>accessor.HttpContext?.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException("登录超时");
    public RuntimeActor RequireActor()=>RequireIdentity().Actor;
    public void RequirePagePermission(string pageId,string operation)
    {
        var identity=RequireIdentity();var page=registry.FindPage(pageId)??throw new UnauthorizedAccessException("页面未登记。");
        if(identity.Actor.IsAdministrator)return;
        if(registry.Platform.ConfigPages.Any(p=>p.Id==pageId))throw new UnauthorizedAccessException("平台配置和账号角色管理需要平台管理员权限。");
        var table=registry.Platform.ConfigTables.Concat(registry.Demo.ConfigTables).FirstOrDefault(t=>t.Id==page.ConfigTableId);
        if(table?.Extra.TryGetValue("scopeKind",out var scope)==true&&scope.GetString()=="global")throw new UnauthorizedAccessException("全局目录需要管理员权限。");
        if(page.YzProjectId!=identity.Actor.ProjectId)throw new UnauthorizedAccessException("无此页面访问权限。");
        var action=operation switch{"list" or "detail" or "count" or "options"=>"is_query","create"=>"is_new","update"=>"is_update","delete"=>"is_delete","export"=>"is_export","copy" or "import"=>"is_all",_=>throw new UnauthorizedAccessException("操作类型无效。")};
        if(!identity.Permissions.Contains(pageId+":"+action)&&!identity.Permissions.Contains(pageId+":is_all"))throw new UnauthorizedAccessException("无此操作权限。");
    }
    public RuntimeActor RequireAdministrator()
    {
        var actor=RequireActor();if(!actor.IsAdministrator)throw new UnauthorizedAccessException("需要平台管理员权限。");return actor;
    }
}
