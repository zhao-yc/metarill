using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Http;

namespace Metarill.Runtime.Security;

/// <summary>权限依据中间件建立的真实身份，页面/项目选择参数本身不授予权限。</summary>
public sealed class HttpRuntimeAuthorization(IHttpContextAccessor accessor,MetadataRegistry registry) : IRuntimeAuthorization, ISecurityIdentityAccessor
{
    public AuthenticatedIdentity RequireIdentity()=>accessor.HttpContext?.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException("登录超时");
    public RuntimeActor RequireActor()=>RequireIdentity().Actor;
    /// <summary>超级角色必须来自本次真实Web身份并匹配当前项目及后台，不能由工作区选择或Agent声明扩权。</summary>
    public bool IsCurrentRealmSuper()
    {
        var identity=RequireIdentity();return identity.GrantId is null&&identity.IsRealmSuper&&!string.IsNullOrWhiteSpace(identity.PrincipalAdminTypeId)
            &&identity.Actor.ProjectId==identity.PrincipalProjectId&&identity.Actor.AdminTypeId==identity.PrincipalAdminTypeId;
    }
    public void RequirePagePermission(string pageId,string operation)
    {
        var identity=RequireIdentity();var page=registry.FindPage(pageId)??throw new UnauthorizedAccessException("页面未登记。");
        if(identity.Actor.IsAdministrator)return;
        var table=registry.Platform.ConfigTables.Concat(registry.Demo.ConfigTables).FirstOrDefault(t=>t.Id==page.ConfigTableId);
        var safety=table is not null&&SecurityRealmMutationGuard.IsStandardSecurityTable(table.Id,table.TableName);
        if(registry.Platform.ConfigPages.Any(p=>p.Id==pageId)&&!safety)throw new UnauthorizedAccessException("平台配置需要平台管理员权限。");
        if(table?.Extra.TryGetValue("scopeKind",out var scope)==true&&scope.GetString()=="global")throw new UnauthorizedAccessException("全局目录需要管理员权限。");
        if((!safety&&page.YzProjectId!=identity.Actor.ProjectId)||page.YzProjectId!=identity.PrincipalProjectId||page.YzAdminTypeId!=identity.PrincipalAdminTypeId)throw new UnauthorizedAccessException("无此页面访问权限。");
        var action=operation switch{"list" or "detail" or "count" or "options"=>"is_query","create"=>"is_new","update"=>"is_update","delete"=>"is_delete","export"=>"is_export","copy" or "import"=>"is_all",_=>throw new UnauthorizedAccessException("操作类型无效。")};
        if(!identity.Permissions.Contains(pageId+":"+action)&&!identity.Permissions.Contains(pageId+":is_all"))throw new UnauthorizedAccessException("无此操作权限。");
    }
    public RuntimeActor RequireAdministrator()
    {
        var actor=RequireActor();if(!actor.IsAdministrator)throw new UnauthorizedAccessException("需要平台管理员权限。");return actor;
    }
}
