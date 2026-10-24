using System.Globalization;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;

namespace Metarill.Runtime.Agent;

/// <summary>认证头类型必须与数据库会话种类一致，不能把网页登录令牌放进Bearer冒充Agent。</summary>
public sealed class AgentAuthenticationService(SqlRowStore store, AuthenticationService authentication, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    /// <summary>Agent默认项目来自已经批准的会话，选中其他项目即使管理员也拒绝。</summary>
    public async Task<AgentRequestIdentity> AuthenticateAsync(string token, string transport, string? selectedProject = null, CancellationToken cancellationToken = default)
    {
        if (token.Length is < 32 or > 200 || transport is not ("agent" or "web")) throw new UnauthorizedAccessException("登录超时");
        var session = (await store.ReadAsync("sys_session", new Dictionary<string, object?> { ["token_hash"] = PasswordHasher.Digest(token) }, cancellationToken)).SingleOrDefault();
        if (session is null || SqlRowStore.Text(session, "kind") != transport || Convert.ToDateTime(session["expires_at"], CultureInfo.InvariantCulture) <= _clock.GetUtcNow().UtcDateTime) throw new UnauthorizedAccessException("登录超时");
        // 每次请求重新校验账号有效期，停用、到期或尚未到登录时间立即拒绝。
        var account = (await store.ReadAsync("yz_manager", new Dictionary<string, object?> { ["id"] = SqlRowStore.Id(session, "account_id") }, cancellationToken)).SingleOrDefault();
        if (account is null || !AuthenticationService.AccountActive(account)) throw new UnauthorizedAccessException("登录超时");
        if (transport == "agent")
        {
            if (session.GetValueOrDefault("grant_id") is null) throw new UnauthorizedAccessException("登录超时");
            var grant = (await store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["id"] = SqlRowStore.Id(session, "grant_id") }, cancellationToken)).SingleOrDefault();
            var project = SqlRowStore.Text(session, "project_id");
            if (grant is null || SqlRowStore.Text(grant, "status") != "approved" || SqlRowStore.Text(grant, "client_id") != AgentOAuthService.ClientId ||
                grant.GetValueOrDefault("refresh_expires_at") is null || Convert.ToDateTime(grant["refresh_expires_at"], CultureInfo.InvariantCulture) <= _clock.GetUtcNow().UtcDateTime ||
                SqlRowStore.Id(grant, "account_id") != SqlRowStore.Id(session, "account_id") || SqlRowStore.Text(grant, "project_id") != project ||
                !string.IsNullOrEmpty(selectedProject) && selectedProject != project) throw new UnauthorizedAccessException("登录超时");
            selectedProject = project;
        }
        else if (session.GetValueOrDefault("grant_id") is not null) throw new UnauthorizedAccessException("登录超时");
        var identity = await authentication.AuthenticateAsync(token, selectedProject, cancellationToken) ?? throw new UnauthorizedAccessException("登录超时");
        return new(identity, transport, identity.GrantId);
    }
}

/// <summary>放在统一异常处理之后、控制器和任务保护之前；不会覆盖宿主其他认证文件。</summary>
public sealed class AgentAuthenticationMiddleware(RequestDelegate next)
{
    /// <summary>请求的项目选择只用于缩小已认证上下文，实际项目存在性和授权由服务端验证。</summary>
    public async Task InvokeAsync(HttpContext context, AgentAuthenticationService authentication)
    {
        var web = context.Request.Headers["loginStateId"];
        var authorization = context.Request.Headers.Authorization;
        if (web.Count > 1 || authorization.Count > 1) throw new UnauthorizedAccessException("登录超时");
        var webToken = web.ToString(); var bearer = authorization.ToString();
        if (!string.IsNullOrEmpty(webToken) && !string.IsNullOrEmpty(bearer)) throw new UnauthorizedAccessException("登录超时");
        if (!string.IsNullOrEmpty(bearer) && !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("登录超时");
        var token = string.IsNullOrEmpty(bearer) ? webToken : bearer[7..];
        if (!string.IsNullOrEmpty(token))
        {
            string? project = null;
            var selected = context.Request.Headers["sysParams"].ToString();
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.Length > 8192) throw new UnauthorizedAccessException("登录超时");
                using var json = JsonDocument.Parse(Uri.UnescapeDataString(selected));
                if (json.RootElement.TryGetProperty("yzProjectId", out var value)) project = value.ToString();
            }
            var identity = await authentication.AuthenticateAsync(token, string.IsNullOrEmpty(bearer) ? "web" : "agent", project, context.RequestAborted);
            context.Items[typeof(AgentRequestIdentity)] = identity;
            context.Items[typeof(AuthenticatedIdentity)] = identity.Identity;
        }
        await next(context);
    }
}

/// <summary>授权管理端点明确区分网页人工决定和CLI自身授权管理。</summary>
public static class AgentRequestAuthentication
{
    public static AgentRequestIdentity Require(HttpContext context) => context.Items[typeof(AgentRequestIdentity)] as AgentRequestIdentity ?? throw new UnauthorizedAccessException("登录超时");
    public static AgentRequestIdentity RequireWeb(HttpContext context) { var identity = Require(context); if (identity.Kind != "web") throw new UnauthorizedAccessException("需要真实网页登录会话。"); return identity; }
    public static AgentRequestIdentity RequireAgent(HttpContext context) { var identity = Require(context); if (identity.Kind != "agent") throw new UnauthorizedAccessException("需要Agent授权。"); return identity; }
}
