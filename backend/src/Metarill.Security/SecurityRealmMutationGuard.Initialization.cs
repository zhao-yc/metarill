using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed partial class SecurityRealmMutationGuard
{
    internal const string InitializationButtonId="1696805746482847746";
    internal const string InitializationPageId="1696799309006348290";

    /// <summary>初始化只证明原按钮权，不附加页编辑权或平台超级身份；暂只接本应用已发布的同库后台。</summary>
    internal async Task<Dictionary<string,object?>> PrepareAdminInitializationAsync(string adminTypeId,CancellationToken token)
    {
        var transaction=store.Session.CurrentTransaction??throw new InvalidOperationException("账号初始化必须在事务内。");
        var request=RequireRequest(authorization.RequireIdentity().Actor,transaction);
        // 独立应用首期没有Agent重置与刷新授权适配，不能只撤session却保留可重新发令牌的grant。
        if(request.GrantId is not null||request.Actor.IsAdministrator||request.Actor.ProjectId!=request.PrincipalProjectId)
            throw new UnauthorizedAccessException("账号初始化需要本应用管理后台的真实Web身份。");
        var id=Id(adminTypeId);
        var state=await PrepareLocksAsync(request,"yz_admin_type",id,new Dictionary<string,object?>(),token);
        var current=await reader.ReadAsync(request,state.Locks,token);
        var page=PublishedPage(InitializationPageId,"yz_admin_type");
        var identity=current.Identity;
        if(page.ProjectId!=identity.PrincipalProjectId||page.AdminTypeId!=identity.PrincipalAdminTypeId||
            !current.Catalog.Pages.Any(item=>item.Id==page.Id&&item.TableId==page.TableId&&item.ProjectId==page.ProjectId&&item.AdminTypeId==page.AdminTypeId)||
            !registry.PublishedCatalogs.SelectMany(catalog=>catalog.Buttons).Any(button=>button.Id==InitializationButtonId&&button.PageId==page.Id)||
            !current.Catalog.Buttons.Any(button=>button.Id==InitializationButtonId&&button.PageId==page.Id)||
            !identity.IsRealmSuper&&!identity.ButtonIds.Contains(InitializationButtonId))
            throw new UnauthorizedAccessException("缺少当前后台初始化账号按钮的操作权限。");
        var target=await LockedTarget("yz_admin_type",id,state,token)??throw Changed();
        var project=SqlRowStore.Text(target,"yz_project_id");
        if(project!=identity.PrincipalProjectId||
            !registry.PublishedCatalogs.SelectMany(catalog=>catalog.Pages).Any(item=>item.ProjectId==project&&item.AdminTypeId==adminTypeId)||
            registry.PublishedCatalogs.SelectMany(catalog=>catalog.Pages).Any(item=>item.ProjectId==project&&registry.IsPlatformRealm(project,item.AdminTypeId)))
            throw new UnauthorizedAccessException("账号初始化只允许本应用已发布后台，不得初始化平台或跨库后台。");
        return target;
    }
}
