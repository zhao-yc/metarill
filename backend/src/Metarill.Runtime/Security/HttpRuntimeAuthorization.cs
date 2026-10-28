using Microsoft.Extensions.DependencyInjection;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Http;

namespace Metarill.Runtime.Security;

/// <summary>权限依据中间件建立的真实身份，页面/项目选择参数本身不授予权限。</summary>
public sealed class HttpRuntimeAuthorization : IRuntimeAuthorization, ISecurityIdentityAccessor
{
    private readonly IHttpContextAccessor accessor;private readonly MetadataRegistry registry;private readonly IServiceProvider? currentServices;
    /// <summary>保留原双参数构造供只读/普通权限使用；锁后当前读仍须真实宿主提供服务。</summary>
    public HttpRuntimeAuthorization(IHttpContextAccessor accessor,MetadataRegistry registry):this(accessor,registry,null){}
    /// <summary>Scoped容器只延迟解析当前读协调器，避免授权器与身份访问器在构造时循环依赖。</summary>
    public HttpRuntimeAuthorization(IHttpContextAccessor accessor,MetadataRegistry registry,IServiceProvider? currentServices)
    {this.accessor=accessor;this.registry=registry;this.currentServices=currentServices;}
    /// <summary>固定生成入口传入原写事务，不创建另一连接的权限证明。</summary>
    public Task RequireButtonCurrentAsync(string pageId,string buttonId,RuntimeActor actor,System.Data.Common.DbTransaction transaction,CancellationToken cancellationToken=default)=>
        (currentServices??throw new NotSupportedException("宿主尚未接入按钮当前权限协调。")).GetRequiredService<SecurityRealmMutationGuard>().RequireBusinessButtonAsync(pageId,buttonId,actor,transaction,cancellationToken);

    public AuthenticatedIdentity RequireIdentity()=>accessor.HttpContext?.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException("登录超时");
    public RuntimeActor RequireActor()=>RequireIdentity().Actor;
    /// <summary>超级角色必须来自本次真实Web身份并匹配当前项目及后台，不能由工作区选择或Agent声明扩权。</summary>
    public bool IsCurrentRealmSuper()
    {
        var identity=RequireIdentity();return identity.GrantId is null&&identity.IsRealmSuper&&!string.IsNullOrWhiteSpace(identity.PrincipalAdminTypeId)
            &&identity.Actor.ProjectId==identity.PrincipalProjectId&&identity.Actor.AdminTypeId==identity.PrincipalAdminTypeId;
    }
    /// <summary>普通业务按钮只要求实际按钮授权及发布归属，不暗中索要页面查询或修改权。</summary>
    public void RequireButtonPermission(string pageId,string buttonId)
    {
        var identity=RequireIdentity();
        try{BusinessButtonAuthorization.Require(identity,((ISecurityResourceRegistry)registry).PublishedCatalogs,pageId,buttonId,true);}
        catch(System.Security.SecurityException){throw new UnauthorizedAccessException("无此按钮访问权限。");}
    }
    /// <summary>权限与范围均来自同一个真实请求身份，固定按钮不会借页面普通角色解除范围。</summary>
    public ExactDataManageValues ResolveButtonExactDataManage(string pageId,string buttonId,string managerField)
    {
        RequireButtonPermission(pageId,buttonId);return ExactDataManageIdentity.ResolveButton(RequireIdentity(),buttonId,managerField);
    }
    /// <summary>先复用当前真实页面授权，再提供固定计划所需的窄匹配值。</summary>
    public ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField)
    {
        RequirePagePermission(pageId,operation);return ExactDataManageIdentity.Resolve(RequireIdentity(),pageId,operation,managerField);
    }
    /// <summary>文件来源查询不接受仅由选择页引用继承的权限。</summary>
    public void RequireFileSourcePagePermission(string pageId)=>RequirePagePermissionCore(pageId,"detail",true);
    /// <summary>先校验独立文件来源，再解析该来源自己的精确范围。</summary>
    public ExactDataManageValues ResolveFileSourceExactDataManage(string pageId,string managerField)
    {
        RequireFileSourcePagePermission(pageId);return ExactDataManageIdentity.ResolveFileSource(RequireIdentity(),pageId,managerField);
    }
    /// <summary>普通生成页保留原调用，授权用途只由宿主方法选择，不从HTTP接收。</summary>
    public void RequirePagePermission(string pageId,string operation)=>RequirePagePermissionCore(pageId,operation,false);
    /// <summary>同一项目/后台及动作检查使用用途对应的真实来源集合。</summary>
    private void RequirePagePermissionCore(string pageId,string operation,bool fileSource)
    {
        var identity=RequireIdentity();var page=registry.FindPage(pageId)??throw new UnauthorizedAccessException("页面未登记。");
        if(identity.Actor.IsAdministrator)return;
        var table=registry.Platform.ConfigTables.Concat(registry.Demo.ConfigTables).FirstOrDefault(t=>t.Id==page.ConfigTableId);
        var safety=table is not null&&SecurityRealmMutationGuard.IsStandardSecurityTable(table.Id,table.TableName);
        if(registry.Platform.ConfigPages.Any(p=>p.Id==pageId)&&!safety)throw new UnauthorizedAccessException("平台配置需要平台管理员权限。");
        if(table?.Extra.TryGetValue("scopeKind",out var scope)==true&&scope.GetString()=="global")throw new UnauthorizedAccessException("全局目录需要管理员权限。");
        if((!safety&&page.YzProjectId!=identity.Actor.ProjectId)||page.YzProjectId!=identity.PrincipalProjectId||page.YzAdminTypeId!=identity.PrincipalAdminTypeId)throw new UnauthorizedAccessException("无此页面访问权限。");
        var action=operation switch{"list" or "detail" or "count" or "options"=>"is_query","create"=>"is_new","update"=>"is_update","delete"=>"is_delete","export"=>"is_export","copy" or "import"=>"is_all",_=>throw new UnauthorizedAccessException("操作类型无效。")};
        var permissions=fileSource?identity.FileQueryPermissions??identity.Permissions:identity.Permissions;
        if(!permissions.Contains(pageId+":"+action)&&!permissions.Contains(pageId+":is_all")&&!(action=="is_all"&&!fileSource&&identity.InheritedAllPermissions.Contains(pageId+":is_all")))throw new UnauthorizedAccessException("无此操作权限。");
    }
    public RuntimeActor RequireAdministrator()
    {
        var actor=RequireActor();if(!actor.IsAdministrator)throw new UnauthorizedAccessException("需要平台管理员权限。");return actor;
    }
}
