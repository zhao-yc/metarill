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
        var current=await ReadCurrentAsync(request,state,token);
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
    /// <summary>跨库入口只接受真实平台Web身份；原按钮当前授权与目标后台/项目在同一平台事务中固定。</summary>
    public async Task<PlatformAdminInitializationScope> PreparePlatformAdminInitializationAsync(string adminTypeId,CancellationToken token=default)
    {
        var transaction=store.Session.CurrentTransaction??throw new InvalidOperationException("平台账号初始化授权必须在事务内。");
        var request=RequireRequest(authorization.RequireIdentity().Actor,transaction);
        if(request.GrantId is not null||!request.Actor.IsAdministrator||!registry.IsPlatformRealm(request.PrincipalProjectId,request.PrincipalAdminTypeId))
            throw new UnauthorizedAccessException("跨库初始化需要真实平台Web管理员身份。");
        adminTypeId=AdminAccountInitializationBusiness.RequireTargetId(adminTypeId);
        BusinessButtonAuthorization.Require(request,registry.PublishedCatalogs,InitializationPageId,InitializationButtonId,true);
        var state=await PrepareLocksAsync(request,"yz_admin_type",Id(adminTypeId),new Dictionary<string,object?>(),token);
        var current=await ReadCurrentAsync(request,state,token);
        await RequireCurrentButtonBindingAsync(request,current,state,InitializationPageId,InitializationButtonId,true,token);
        var admin=await LockedTarget("yz_admin_type",Id(adminTypeId),state,token)??throw Changed();
        var projectId=SqlRowStore.Text(admin,"yz_project_id");
        if(projectId!=request.Actor.ProjectId||projectId==request.PrincipalProjectId||!state.Locks.Projects.TryGetValue(Id(projectId),out var project)
            ||string.Equals(SqlRowStore.Text(project,"name").Trim(),"YZP",StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("初始化只允许当前业务项目的后台，不得初始化平台项目。");
        Metarill.Core.Metadata.TemplateVersionCatalog.ValidateStoredProjectVersion(project.GetValueOrDefault("yz_yzp_version_id"));
        var identity=await store.Session.Provider.ReadPhysicalDatabaseIdentityAsync(store.Session.Options,transaction,token);
        return new(store,transaction,admin,project,identity,request.PrincipalProjectId,async currentToken=>
        {
            RequireSameRequest(request,RequireRequest(request.Actor,transaction));
            await RefreshLocks(state,currentToken);var latest=await ReadCurrentAsync(request,state,currentToken);
            if(!latest.Identity.Actor.IsAdministrator)throw new UnauthorizedAccessException("当前身份已不具备平台管理员权限。");
            await RequireCurrentButtonBindingAsync(request,latest,state,InitializationPageId,InitializationButtonId,true,currentToken);
            var currentAdmin=await LockedTarget("yz_admin_type",Id(adminTypeId),state,currentToken)??throw Changed();
            foreach(var field in new[]{"id","yz_project_id","user_name","password"})
                if(SqlRowStore.Text(currentAdmin,field)!=SqlRowStore.Text(admin,field))throw Changed();
            var currentProject=state.Locks.Projects[Id(projectId)];
            foreach(var field in new[]{"id","name","yz_yzp_version_id"})
                if(SqlRowStore.Text(currentProject,field)!=SqlRowStore.Text(project,field))throw Changed();
        });
    }

}
