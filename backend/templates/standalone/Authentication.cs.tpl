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
    private readonly string? password;
    private readonly RuntimeActor actor;
    private readonly int lifetime;
    private readonly bool enabled;
    public DevelopmentSessions(IConfiguration configuration, IHostEnvironment environment, MetadataCatalog metadata)
    {
        var section = configuration.GetSection("Authentication:DevelopmentBootstrap");
        enabled = environment.IsDevelopment() && section.GetValue<bool>("Enabled");
        username = section["Username"]; password = section["Password"];
        lifetime = Math.Clamp(section.GetValue<int?>("SessionLifetimeSeconds") ?? 3600, 1, 86400);
        if (enabled && (string.IsNullOrWhiteSpace(username) || password is null || password.Length < 12)) throw new InvalidOperationException("开发认证需要显式配置用户名和至少 12 位密码。");
        actor = new RuntimeActor(1, username ?? "", true, metadata.YzProjectId, metadata.YzAdminTypes.FirstOrDefault()?.Id ?? "");
    }
    /// <summary>固定时间比较凭据，令牌仅在内存保存摘要，项目来自服务端元数据。</summary>
    public string? Login(string? suppliedUsername, string? suppliedPassword)
    {
        if (!enabled || suppliedUsername is null || suppliedPassword is null || suppliedUsername.Length > 256 || suppliedPassword.Length > 1024) return null;
        var validUsername = CryptographicOperations.FixedTimeEquals(Digest(suppliedUsername), Digest(username!));
        var validPassword = CryptographicOperations.FixedTimeEquals(Digest(suppliedPassword), Digest(password!));
        if (!(validUsername & validPassword)) return null;
        foreach (var expired in sessions.Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow)) sessions.TryRemove(expired.Key, out _);
        if (sessions.Count >= 1000) return null;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        sessions[Key(token)] = new(actor, DateTimeOffset.UtcNow.AddSeconds(lifetime)); return token;
    }
    /// <summary>过期或重启后的会话立即失效，不接受请求选择其他项目。</summary>
    public RuntimeActor? Resolve(string token)
    {
        if (!enabled || token.Length != 64 || !sessions.TryGetValue(Key(token), out var session)) return null;
        if (session.ExpiresAt > DateTimeOffset.UtcNow) return session.Actor;
        sessions.TryRemove(Key(token), out _); return null;
    }
    /// <summary>退出时只移除当前令牌，不影响其他会话。</summary>
    public void Logout(string token) { if (token.Length == 64) sessions.TryRemove(Key(token), out _); }
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
