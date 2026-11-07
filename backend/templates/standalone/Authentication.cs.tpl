using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>开发专用内存会话；只有 Development 环境且明确启用时才接受配置账号。</summary>
internal sealed class DevelopmentSessions
{
    private sealed record Session(RuntimeActor Actor, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly string? username;
    private readonly object sync = new();
    private readonly DevelopmentPasswordStore? passwords;
    private DevelopmentPasswordStore.Credential? credential;
    private bool persisted;
    private readonly RuntimeActor actor;
    private readonly int lifetime;
    private readonly bool enabled;
    public DevelopmentSessions(IConfiguration configuration, IHostEnvironment environment, MetadataCatalog metadata)
    {
        var section = configuration.GetSection("Authentication:DevelopmentBootstrap");
        enabled = environment.IsDevelopment() && section.GetValue<bool>("Enabled");
        username = section["Username"]; var password = section["Password"];
        lifetime = Math.Clamp(section.GetValue<int?>("SessionLifetimeSeconds") ?? 3600, 1, 86400);
        if (enabled && (string.IsNullOrWhiteSpace(username) || password is null || password.Length < 12)) throw new InvalidOperationException("开发认证需要显式配置用户名和至少 12 位密码。");
        actor = new RuntimeActor(1, username ?? "", true, metadata.YzProjectId, metadata.YzAdminTypes.FirstOrDefault()?.Id ?? "");
        if(enabled) { passwords=new(environment.ContentRootPath,metadata.YzProjectId,username!); credential=passwords.Read(); persisted=credential is not null; credential ??=passwords.Create(password!); }
    }
    /// <summary>固定时间比较凭据，令牌仅在内存保存摘要，项目来自服务端元数据。</summary>
    public string? Login(string? suppliedUsername, string? suppliedPassword)
    {
        if (!enabled || suppliedUsername is null || suppliedPassword is null || suppliedUsername.Length > 256 || suppliedPassword.Length > 1024) return null;
        lock(sync)
        {
        RefreshCredential();
        var validUsername = CryptographicOperations.FixedTimeEquals(Digest(suppliedUsername), Digest(username!));
        var validPassword = DevelopmentPasswordStore.Verify(credential!, suppliedPassword);
        if (!(validUsername & validPassword)) return null;
        foreach (var expired in sessions.Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow)) sessions.TryRemove(expired.Key, out _);
        if (sessions.Count >= 1000) return null;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        sessions[Key(token)] = new(actor, DateTimeOffset.UtcNow.AddSeconds(lifetime)); return token;
        }
    }
    /// <summary>过期或重启后的会话立即失效，不接受请求选择其他项目。</summary>
    public RuntimeActor? Resolve(string token)
    {
        if(!enabled||token.Length!=64)return null;
        lock(sync) { RefreshCredential(); if(!sessions.TryGetValue(Key(token),out var session))return null;
        if(session.ExpiresAt>DateTimeOffset.UtcNow)return session.Actor; sessions.TryRemove(Key(token),out _);return null; }
    }
    /// <summary>退出时只移除当前令牌，不影响其他会话。</summary>
    public void Logout(string token) { lock(sync) { if (token.Length == 64) sessions.TryRemove(Key(token), out _); } }
    /// <summary>仅当前显式开发账户可改自己的密码；写入成功才替换摘要并使全部旧会话失效。</summary>
    public void ChangePassword(string token,string? oldPassword,string? newPassword)
    {
        if(!enabled)throw new NotSupportedException("开发改密仅在显式Development账户可用。");
        if(oldPassword is null||newPassword is null||oldPassword.Length>1024||newPassword.Length is <12 or >1024)throw new ArgumentException("开发密码需要12至1024个字符。");
        lock(sync)
        {
            RefreshCredential(); if(Resolve(token) is not {AccountId:1} current||current.ProjectId!=actor.ProjectId)throw new UnauthorizedAccessException();
            if(!DevelopmentPasswordStore.Verify(credential!,oldPassword))throw new SecurityException("原密码不正确。");
            var next=passwords!.Create(newPassword); passwords.Write(credential!,next); credential=next;persisted=true;sessions.Clear();
        }
    }
    /// <summary>其他本机进程改密也撤销本进程旧会话；凭据缺失/损坏绝不复活启动配置密码。</summary>
    private void RefreshCredential()
    {
        var next=passwords!.Read(); if(next is null) {if(persisted)throw new InvalidOperationException("开发凭据缺失，未恢复旧密码。");return;}
        if(next!=credential) {credential=next;persisted=true;sessions.Clear();}
    }
    /// <summary>沿用 loginStateId，也接受标准 Bearer 头；不允许通过 URL 传令牌。</summary>
    public static string ReadToken(HttpContext context)
    {
        var token = context.Request.Headers["loginStateId"].ToString();
        if (string.IsNullOrEmpty(token)) { var authorization = context.Request.Headers.Authorization.ToString(); if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = authorization[7..]; }
        return token.Length <= 128 ? token : "";
    }
    /// <summary>将凭据转为固定长度摘要再比较。</summary>
    private static byte[] Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    /// <summary>会话表仅保存令牌摘要。</summary>
    private static string Key(string token) => Convert.ToHexStringLower(Digest(token));
}

/// <summary>默认认证拒绝全部匿名请求，页面和写入能力由固定元数据授权。</summary>
internal sealed class DevelopmentAuthorization(IHttpContextAccessor accessor, DevelopmentSessions sessions, MetadataCatalog metadata) : IRuntimeAuthorization
{
    /// <summary>身份始终来自服务端有效会话。</summary>
    public RuntimeActor RequireActor() => accessor.HttpContext is { } context && sessions.Resolve(DevelopmentSessions.ReadToken(context)) is { } actor ? actor : throw new UnauthorizedAccessException();
    /// <summary>先认证，再按当前项目和声明的页面能力授权。</summary>
    public void RequirePagePermission(string pageId, string operation)
    {
        var actor = RequireActor();
        var page = metadata.ConfigPages.SingleOrDefault(item => item.Id == pageId) ?? throw new SecurityException();
        if (actor.ProjectId != metadata.YzProjectId || !actor.IsAdministrator && page.YzAdminTypeId != actor.AdminTypeId) throw new SecurityException();
        var allowed = operation switch
        {
            "list" or "detail" or "count" => true,
            "create" => page.IsNew == 1,
            "update" => page.IsSave == 1,
            "delete" => page.IsDelete == 1,
            "copy" => page.IsCopy == 1,
            "import" => page.IsImport == 1 || page.Extra.TryGetValue("importMethod", out var import) && !string.IsNullOrWhiteSpace(import.ToString()),
            "export" => page.IsExport == 1 || page.Extra.TryGetValue("exportMethod", out var export) && export.ToString() is "list" or "edit",
            _ => false
        };
        if (!allowed) throw new SecurityException();
    }
}
