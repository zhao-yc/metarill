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

/// <summary>仅投影本包发布资源；本应用后台super不具有平台管理员身份，Agent始终不开放。</summary>
internal sealed class ApplicationSecurityRegistry(MetadataCatalog metadata):ISecurityResourceRegistry,ISecuritySessionPolicy
{
    public MetadataCatalog Metadata=>metadata;
    public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs {get;}=[new(metadata.YzProjectId,
        metadata.ConfigTables.Select(table=>new SecurityTableResource(table.Id,table.TableName)).ToArray(),
        metadata.ConfigPages.Select(page=>new SecurityPageResource(page.Id,page.ConfigTableId,page.YzProjectId,page.YzAdminTypeId,
            page.IsNew==1,PageUpdatePolicy.CanUpdate(page),page.IsDelete==1,
            page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var export)&&export.ToString() is "list" or "edit",
            page.IsCopy==1,page.IsImport==1||page.Extra.TryGetValue("importMethod",out var import)&&!string.IsNullOrWhiteSpace(import.ToString()))).ToArray(),
        metadata.ConfigMenus.Select(menu=>new SecurityMenuResource(menu.Id,menu.YzProjectId,menu.YzAdminTypeId)).ToArray(),
        metadata.ConfigButtons.Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("id",out _)&&button.TryGetProperty("configPageId",out _))
            .Select(button=>new SecurityButtonResource(button.GetProperty("id").ToString(),button.GetProperty("configPageId").ToString())).ToArray())];
    /// <summary>普通应用不能声明自己是平台，不能借业务super切换包外项目。</summary>
    public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
    /// <summary>本宿主没有OAuth/设备控制面，任何Agent会话均不能作为Web身份。</summary>
    public Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding,CancellationToken cancellationToken)=>Task.FromResult(false);
    /// <summary>数据库身份仍必须属于可信包内项目及已发布后台。</summary>
    internal bool Contains(AuthenticatedIdentity identity)=>!identity.Actor.IsAdministrator && identity.GrantId is null &&
        identity.Actor.ProjectId==metadata.YzProjectId && identity.PrincipalProjectId==metadata.YzProjectId &&
        identity.Actor.AdminTypeId==identity.PrincipalAdminTypeId && metadata.YzAdminTypes.Any(admin=>admin.Id==identity.PrincipalAdminTypeId&&admin.YzProjectId==metadata.YzProjectId);
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
        var identity=await Authentication.AuthenticateAsync(token,registry.Metadata.YzProjectId,cancellationToken);
        return identity is not null&&registry.Contains(identity)?identity:null;
    }
    /// <summary>原登录form/JSON由HTTP边界解析；同事务锁定真实身份后再创建持久会话。</summary>
    internal async Task<(Dictionary<string,object?> User,string Token)> LoginAsync(string? username,string? password,CancellationToken cancellationToken)
    {
        if(!mode.DatabaseConfigured||username is null||password is null||username.Length is <1 or >50||password.Length is <1 or >1024)throw InvalidLogin();
        var candidates=await Store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken);
        if(candidates.Count!=1||!long.TryParse(SqlRowStore.Text(candidates[0],"yz_admin_type_id"),NumberStyles.None,CultureInfo.InvariantCulture,out var admin)||
            !registry.Metadata.YzAdminTypes.Any(item=>item.Id==admin.ToString(CultureInfo.InvariantCulture)))throw InvalidLogin();
        var account=SqlRowStore.Id(candidates[0]);
        return await Session.InTransactionAsync(async()=>
        {
            await LockRealm(admin,cancellationToken);
            var current=(await Store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"id",account}},cancellationToken)).SingleOrDefault();
            if(current is null||SqlRowStore.Text(current,"name")!=username||SqlRowStore.Id(current,"yz_admin_type_id")!=admin||!AuthenticationService.AccountActive(current)||!PasswordHasher.Verify(password,SqlRowStore.Text(current,"password")))throw InvalidLogin();
            var result=await Authentication.LoginAsync(username,password,cancellationToken);
            var identity=await Authentication.AuthenticateAsync(result.Token,registry.Metadata.YzProjectId,cancellationToken);
            if(identity is null||!registry.Contains(identity))throw InvalidLogin();
            result.User["id"]=account.ToString(CultureInfo.InvariantCulture);result.User["merchantName"]=registry.Metadata.YzProjectName;return result;
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
                if(SqlRowStore.Id(current,"account_id")!=identity.Actor.AccountId||SqlRowStore.Text(current,"kind")!="web"||SqlRowStore.Text(current,"project_id")!=registry.Metadata.YzProjectId)throw new UnauthorizedAccessException();
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
                SqlRowStore.Id(currentSession,"account_id")!=identity.Actor.AccountId||SqlRowStore.Text(currentSession,"project_id")!=registry.Metadata.YzProjectId||
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
        var project=long.Parse(registry.Metadata.YzProjectId,CultureInfo.InvariantCulture);
        if((await Store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken)).Count!=1)throw InvalidLogin();
        var rows=await Store.ReadForUpdateAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin}},cancellationToken);
        if(rows.Count!=1||SqlRowStore.Id(rows[0],"yz_project_id")!=project)throw InvalidLogin();
    }
    private static UnauthorizedAccessException InvalidLogin()=>new("账号、密码或应用认证配置无效。");
}

/// <summary>同步授权只使用本请求已真实认证的身份，包外项目、后台和动作始终拒绝。</summary>
internal sealed class ApplicationWebAuthorization(IHttpContextAccessor accessor,MetadataCatalog metadata):IRuntimeAuthorization,ISecurityIdentityAccessor
{
    public AuthenticatedIdentity RequireIdentity()=>accessor.HttpContext?.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException();
    public RuntimeActor RequireActor()=>RequireIdentity().Actor;
    public void RequirePagePermission(string pageId,string operation)
    {
        var identity=RequireIdentity();var page=metadata.ConfigPages.SingleOrDefault(item=>item.Id==pageId)??throw new SecurityException();
        if(identity.Actor.IsAdministrator||identity.Actor.ProjectId!=metadata.YzProjectId||identity.PrincipalProjectId!=metadata.YzProjectId||page.YzProjectId!=metadata.YzProjectId||page.YzAdminTypeId!=identity.PrincipalAdminTypeId||identity.Actor.AdminTypeId!=identity.PrincipalAdminTypeId)throw new SecurityException();
        var enabled=operation switch
        {
            "list" or "detail" or "count" or "options"=>true,"create"=>page.IsNew==1,"update"=>PageUpdatePolicy.CanUpdate(page),"delete"=>page.IsDelete==1,"copy"=>page.IsCopy==1,
            "export"=>page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var export)&&export.ToString() is "list" or "edit",
            "import"=>page.IsImport==1||page.Extra.TryGetValue("importMethod",out var import)&&!string.IsNullOrWhiteSpace(import.ToString()),_=>false
        };
        if(!enabled)throw new SecurityException();
        var action=operation switch{"list" or "detail" or "count" or "options"=>"is_query","create"=>"is_new","update"=>"is_update","delete"=>"is_delete","export"=>"is_export","copy" or "import"=>"is_all",_=>""};
        if(!identity.Permissions.Contains(pageId+":"+action)&&!identity.Permissions.Contains(pageId+":is_all"))throw new SecurityException();
    }
}
