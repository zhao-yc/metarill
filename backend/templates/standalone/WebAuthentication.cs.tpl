using System.Globalization;
using System.Security;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>认证模式只取可信宿主配置；新完整包默认数据库Web，显式开发模式不能成为错误回退。</summary>
internal sealed record ApplicationAuthenticationMode(bool Development, bool DatabaseConfigured)
{
    internal static ApplicationAuthenticationMode Read(IConfiguration configuration, IHostEnvironment environment)
    {
        var selected=configuration["Authentication:Mode"];
        if(selected is not(null or "Web" or "Development"))throw new InvalidOperationException("Authentication:Mode仅允许Web或Development。");
        var explicitDevelopment=selected=="Development" || selected is null && configuration.GetValue<bool>("Authentication:DevelopmentBootstrap:Enabled");
        var development=explicitDevelopment && environment.IsDevelopment() && configuration.GetValue<bool>("Authentication:DevelopmentBootstrap:Enabled");
        // 显式Development在正式环境只拒绝，不自动改走数据库或配置账号。
        var database=!explicitDevelopment && !string.IsNullOrWhiteSpace(configuration["Database:ConnectionString"]) && !string.IsNullOrWhiteSpace(configuration["Database:ExpectedDatabase"]);
        return new(development,database);
    }
}

/// <summary>仅投影本包发布资源；本应用后台super不具有平台管理员身份；OAuth证明由宿主模块独立接线。</summary>
internal sealed class ApplicationSecurityRegistry(VerifiedPackageSecurityCatalog catalog):ISecurityResourceRegistry,ISecuritySessionPolicy
{
    internal string ProjectId=>catalog.ProjectId;
    internal string ProjectName=>catalog.ProjectName;
    internal bool ContainsAdmin(string id)=>catalog.ContainsAdmin(id);
    public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>catalog.PublishedCatalogs;
    /// <summary>使用同一可信发布目录的匹配字段，不依赖数据库初始化外的手工操作。</summary>
    public string? ExactDataManageManagerField(string projectId,string adminTypeId)=>catalog.ExactDataManageManagerField(projectId,adminTypeId);
    /// <summary>普通应用不能声明自己是平台，不能借业务super切换包外项目。</summary>
    public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
    /// <summary>Web基础模板默认不提供OAuth证明；完整宿主由Agent模块替换会话策略注册。</summary>
    public Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding,CancellationToken cancellationToken)=>Task.FromResult(false);
    /// <summary>数据库身份仍必须属于可信包内项目及已发布后台。</summary>
    internal bool Contains(AuthenticatedIdentity identity)=>identity.GrantId is null&&ContainsPrincipal(identity);
    /// <summary>宿主模块认证后仍核对固定项目和后台，不能由Agent或工作区声明扩权。</summary>
    internal bool ContainsPrincipal(AuthenticatedIdentity identity)=>!identity.Actor.IsAdministrator &&
        identity.Actor.ProjectId==catalog.ProjectId && identity.PrincipalProjectId==catalog.ProjectId &&
        identity.Actor.AdminTypeId==identity.PrincipalAdminTypeId && catalog.ContainsAdmin(identity.PrincipalAdminTypeId);
}

/// <summary>正式Web认证按需解析数据库服务；无配置或匿名令牌在创建连接前拒绝。</summary>
internal sealed class ApplicationWebAuthentication(IServiceProvider services,ApplicationAuthenticationMode mode,ApplicationSecurityRegistry registry)
{
    private SqlRowStore Store=>services.GetRequiredService<SqlRowStore>();
    private Metarill.Data.DatabaseSession Session=>services.GetRequiredService<Metarill.Data.DatabaseSession>();
    private AuthenticationService Authentication=>services.GetRequiredService<AuthenticationService>();
    /// <summary>每个真实请求重读持久化会话/账号/角色；不接受请求指定工作区，不缓存旧权限。</summary>
    internal async Task<AuthenticatedIdentity?> ResolveAsync(string token,CancellationToken cancellationToken)
    {
        if(!mode.DatabaseConfigured||token.Length!=64||token.Any(value=>value is not(>='0' and <='9' or >='a' and <='f')))return null;
        var identity=await Authentication.AuthenticateAsync(token,registry.ProjectId,cancellationToken);
        return identity is not null&&registry.Contains(identity)?identity:null;
    }
    /// <summary>原登录form/JSON由HTTP边界解析；同事务锁定真实身份后再创建持久会话。</summary>
    internal async Task<(Dictionary<string,object?> User,string Token)> LoginAsync(string? username,string? password,CancellationToken cancellationToken)
    {
        if(!mode.DatabaseConfigured||username is null||password is null||username.Length is <1 or >50||password.Length is <1 or >1024)throw InvalidLogin();
        var candidates=await Store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken);
        if(candidates.Count!=1||!long.TryParse(SqlRowStore.Text(candidates[0],"yz_admin_type_id"),NumberStyles.None,CultureInfo.InvariantCulture,out var admin)||
            !registry.ContainsAdmin(admin.ToString(CultureInfo.InvariantCulture)))throw InvalidLogin();
        var account=SqlRowStore.Id(candidates[0]);
        return await Session.InTransactionAsync(async()=>
        {
            await LockRealm(admin,cancellationToken);
            var current=(await Store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"id",account}},cancellationToken)).SingleOrDefault();
            if(current is null||SqlRowStore.Text(current,"name")!=username||SqlRowStore.Id(current,"yz_admin_type_id")!=admin||!AuthenticationService.AccountActive(current)||!PasswordHasher.Verify(password,SqlRowStore.Text(current,"password")))throw InvalidLogin();
            var result=await Authentication.LoginAsync(username,password,cancellationToken);
            var identity=await Authentication.AuthenticateAsync(result.Token,registry.ProjectId,cancellationToken);
            if(identity is null||!registry.Contains(identity))throw InvalidLogin();
            result.User["id"]=account.ToString(CultureInfo.InvariantCulture);result.User["merchantName"]=registry.ProjectName;return result;
        },cancellationToken);
    }
    /// <summary>只注销已验证的当前Web会话，不接受客户端账户或会话主键。</summary>
    internal async Task LogoutAsync(AuthenticatedIdentity identity,CancellationToken cancellationToken)
    {
        if(!registry.Contains(identity))throw new UnauthorizedAccessException();
        await Session.InTransactionAsync(async()=>
        {
            var current=(await Store.ReadForUpdateAsync("sys_session",new Dictionary<string,object?>{{"id",identity.SessionId}},cancellationToken)).SingleOrDefault();
            if(current is not null)
            {
                if(SqlRowStore.Id(current,"account_id")!=identity.Actor.AccountId||SqlRowStore.Text(current,"kind")!="web"||SqlRowStore.Text(current,"project_id")!=registry.ProjectId)throw new UnauthorizedAccessException();
                await Authentication.LogoutAsync(identity.SessionId,cancellationToken);
            }
            return true;
        },cancellationToken);
    }
    /// <summary>自助改密沿项目→后台→会话→账号顺序取锁，同事务更新摘要并撤销该账号的全部旧会话。</summary>
    internal async Task ChangePasswordAsync(AuthenticatedIdentity identity,string? oldPassword,string? newPassword,CancellationToken cancellationToken)
    {
        if(!registry.Contains(identity))throw new UnauthorizedAccessException();
        if(oldPassword is null||newPassword is null||oldPassword.Length>1024||newPassword.Length is <12 or >1024)throw new ArgumentException("密码需要12至1024个字符。");
        await Session.InTransactionAsync(async()=>
        {
            await LockRealm(long.Parse(identity.PrincipalAdminTypeId,CultureInfo.InvariantCulture),cancellationToken);
            var currentSession=(await Store.ReadForUpdateAsync("sys_session",new Dictionary<string,object?>{{"id",identity.SessionId}},cancellationToken)).SingleOrDefault();
            if(currentSession is null||SqlRowStore.Text(currentSession,"kind")!="web"||currentSession.GetValueOrDefault("grant_id") is not null||
                SqlRowStore.Id(currentSession,"account_id")!=identity.Actor.AccountId||SqlRowStore.Text(currentSession,"project_id")!=registry.ProjectId||
                Convert.ToDateTime(currentSession["expires_at"],CultureInfo.InvariantCulture)<=DateTime.UtcNow)throw new UnauthorizedAccessException();
            var account=(await Store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"id",identity.Actor.AccountId}},cancellationToken)).SingleOrDefault();
            if(account is null||!AuthenticationService.AccountActive(account)||SqlRowStore.Text(account,"yz_admin_type_id")!=identity.PrincipalAdminTypeId)throw new UnauthorizedAccessException();
            if(!PasswordHasher.Verify(oldPassword,SqlRowStore.Text(account,"password")))throw new SecurityException("原密码不正确。");
            await Store.UpdateAsync("yz_manager",identity.Actor.AccountId,new Dictionary<string,object?>{{"password",PasswordHasher.Hash(newPassword)},{"edit_pwd_time",DateTime.UtcNow}},cancellationToken);
            foreach(var session in await Store.ReadForUpdateAsync("sys_session",new Dictionary<string,object?>{{"account_id",identity.Actor.AccountId}},cancellationToken))
                await Authentication.LogoutAsync(SqlRowStore.Id(session),cancellationToken);
            return true;
        },cancellationToken);
    }
    /// <summary>可信目录决定项目和后台；账号兼容项目列及客户端参数不能改变真实归属。</summary>
    private async Task LockRealm(long admin,CancellationToken cancellationToken)
    {
        var project=long.Parse(registry.ProjectId,CultureInfo.InvariantCulture);
        if((await Store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken)).Count!=1)throw InvalidLogin();
        var rows=await Store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin}},cancellationToken);
        if(rows.Count!=1||SqlRowStore.Id(rows[0],"yz_project_id")!=project)throw InvalidLogin();
    }
    private static UnauthorizedAccessException InvalidLogin()=>new("账号、密码或应用认证配置无效。");
}

/// <summary>同步授权只使用本请求已真实认证的身份，包外项目、后台和动作始终拒绝。</summary>
internal sealed class ApplicationWebAuthorization(IHttpContextAccessor accessor,VerifiedPackageSecurityCatalog catalog):IRuntimeAuthorization,ISecurityIdentityAccessor
{
    public AuthenticatedIdentity RequireIdentity()=>accessor.HttpContext?.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException();
    public RuntimeActor RequireActor()=>RequireIdentity().Actor;
    /// <summary>超级角色必须来自本次真实Web身份并匹配当前项目及后台，不能由工作区选择或Agent声明扩权。</summary>
    public bool IsCurrentRealmSuper()
    {
        var identity=RequireIdentity();return identity.GrantId is null&&identity.IsRealmSuper&&!identity.Actor.IsAdministrator&&identity.PrincipalProjectId==catalog.ProjectId&&catalog.ContainsAdmin(identity.PrincipalAdminTypeId)
            &&identity.Actor.ProjectId==identity.PrincipalProjectId&&identity.Actor.AdminTypeId==identity.PrincipalAdminTypeId;
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
        var identity=RequireIdentity();var page=catalog.PublishedCatalogs.SelectMany(item=>item.Pages).SingleOrDefault(item=>item.Id==pageId)??throw new SecurityException();
        if(identity.Actor.IsAdministrator||identity.Actor.ProjectId!=catalog.ProjectId||identity.PrincipalProjectId!=catalog.ProjectId||page.ProjectId!=catalog.ProjectId||page.AdminTypeId!=identity.PrincipalAdminTypeId||identity.Actor.AdminTypeId!=identity.PrincipalAdminTypeId)throw new SecurityException();
        var enabled=operation switch
        {
            "list" or "detail" or "count" or "options"=>true,"create"=>page.CanCreate,"update"=>page.CanUpdate,"delete"=>page.CanDelete,"copy"=>page.CanCopy,
            "export"=>page.CanExport,"import"=>page.CanImport,_=>false
        };
        if(!enabled)throw new SecurityException();
        var action=operation switch{"list" or "detail" or "count" or "options"=>"is_query","create"=>"is_new","update"=>"is_update","delete"=>"is_delete","export"=>"is_export","copy" or "import"=>"is_all",_=>""};
        var permissions=fileSource?identity.FileQueryPermissions??identity.Permissions:identity.Permissions;
        if(!permissions.Contains(pageId+":"+action)&&!permissions.Contains(pageId+":is_all")&&!(action=="is_all"&&!fileSource&&identity.InheritedAllPermissions.Contains(pageId+":is_all")))throw new SecurityException();
    }
}
