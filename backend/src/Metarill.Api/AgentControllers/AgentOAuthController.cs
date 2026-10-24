using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.AgentControllers;

/// <summary>OAuth公开端点仅处理设备授权表单，返回标准JSON而非业务R。</summary>
[ApiController]
[Route("api/agent/oauth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AgentOAuthController(AgentOAuthService oauth, AgentPublicRateLimiter limiter) : ControllerBase
{
    [HttpPost("device"), RequestSizeLimit(16384)]
    public Task<IActionResult> Device(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        limiter.Require("device", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local", 20);
        var form = await FormAsync(["client_id", "client_name"], cancellationToken);
        return await oauth.StartDeviceAsync(form["client_id"].ToString(), form["client_name"].ToString(), cancellationToken);
    });
    [HttpPost("token"), RequestSizeLimit(16384)]
    public Task<IActionResult> Token(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        limiter.Require("token", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local", 120);
        var form = await FormAsync(["client_id", "grant_type", "device_code", "refresh_token"], cancellationToken);
        return await oauth.TokenAsync(form["client_id"].ToString(), form["grant_type"].ToString(), form["device_code"].ToString(), form["refresh_token"].ToString(), cancellationToken);
    });
    /// <summary>拒绝客户端额外声明项目或管理员字段，权限只能来自网页人工批准。</summary>
    private async Task<IFormCollection> FormAsync(IReadOnlyCollection<string> allowed, CancellationToken token)
    {
        if (!Request.HasFormContentType) throw new AgentOAuthException("invalid_request", "授权请求必须使用标准表单。");
        var form = await Request.ReadFormAsync(token);
        if (form.Files.Count > 0 || form.Keys.Any(k => !allowed.Contains(k)) || form.Any(pair => pair.Value.Count != 1)) throw new AgentOAuthException("invalid_request", "授权请求包含未声明字段。");
        return form;
    }
    /// <summary>凭据响应禁止缓存，数据库异常不回显连接详情。</summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task<Dictionary<string, object?>>> action)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers.Pragma = "no-cache";
        try { return Ok(await action()); }
        catch (AgentOAuthException exception) { return StatusCode(exception.StatusCode, new { error = exception.Error, error_description = exception.Message }); }
        catch (System.Data.Common.DbException) { return StatusCode(503, new { error = "temporarily_unavailable", error_description = "授权存储暂不可用，请稍后重试。" }); }
    }
}
