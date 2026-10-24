using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.AgentControllers;

/// <summary>网页人工管理与Agent能力入口使用不同的会话类型校验。</summary>
[ApiController]
[Route("api/admin/sys/agent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AgentManagementController(AgentOAuthService oauth, AgentPublicRateLimiter limiter, AgentCapabilityCatalog catalog, SqlRowStore store, AgentTaskStore tasks) : ControllerBase
{
    [HttpGet("device")]
    public async Task<R<AgentDeviceView>> Device([FromQuery] string userCode, CancellationToken cancellationToken)
    {
        var identity = AgentRequestAuthentication.RequireWeb(HttpContext);
        limiter.Require("code", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local", 60);
        return R<AgentDeviceView>.Success(await oauth.ViewDeviceAsync(userCode, identity.Identity, cancellationToken));
    }
    [HttpPost("approve")]
    public async Task<R<object>> Approve(AgentApproveRequest request, CancellationToken cancellationToken)
    {
        var identity = AgentRequestAuthentication.RequireWeb(HttpContext);
        await oauth.ApproveAsync(request.UserCode, request.Approve, identity.Identity, cancellationToken); return R<object>.Success();
    }
    [HttpGet("grants")]
    public async Task<R<object>> Grants(CancellationToken cancellationToken) => R<object>.Success(await oauth.GrantsAsync(AgentRequestAuthentication.RequireWeb(HttpContext).Identity, cancellationToken));
    [HttpPost("revoke")]
    public async Task<R<object>> Revoke(AgentRevokeRequest request, CancellationToken cancellationToken)
    { await oauth.RevokeAsync(request.GrantId, AgentRequestAuthentication.Require(HttpContext), cancellationToken); return R<object>.Success(); }
    [HttpGet("me")]
    public async Task<R<object>> Me(CancellationToken cancellationToken)
    {
        var request = AgentRequestAuthentication.RequireAgent(HttpContext); var actor = request.Identity.Actor;
        var project = (await store.ReadAsync("yz_project", new Dictionary<string, object?> { ["id"] = long.Parse(actor.ProjectId) }, cancellationToken)).SingleOrDefault() ?? throw new UnauthorizedAccessException("项目未登记。");
        return R<object>.Success(new { projectId = actor.ProjectId, projectName = SqlRowStore.Text(project, "name"), accountId = actor.AccountId, accountName = actor.AccountName, grantId = request.GrantId });
    }
    [HttpGet("capabilities")]
    public R<object> Capabilities() { AgentRequestAuthentication.RequireAgent(HttpContext); return R<object>.Success(catalog.List()); }
    [HttpGet("capabilities/{id}")]
    public R<AgentCapability> Describe(string id) { AgentRequestAuthentication.RequireAgent(HttpContext); return R<AgentCapability>.Success(catalog.Describe(id)); }
    /// <summary>只查询当前Agent账号项目的任务，未找到不能据此断言业务没有执行。</summary>
    [HttpGet("tasks/{requestId}")]
    public async Task<IActionResult> Tasks(string requestId, CancellationToken cancellationToken)
    {
        try { return Ok(R<AgentTaskView>.Success(await tasks.GetAsync(AgentRequestAuthentication.RequireAgent(HttpContext).Identity.Actor, requestId.ToLowerInvariant(), cancellationToken))); }
        catch (AgentTaskException error) { await AgentTaskMiddleware.WriteErrorAsync(HttpContext, error); return new EmptyResult(); }
    }
    [HttpGet("skill")]
    public R<object> Skill()
    {
        AgentRequestAuthentication.RequireAgent(HttpContext);
        return R<object>.Success(new { content = "---\nname: metarill-agent\ndescription: 使用当前项目的真实授权能力执行受控业务操作。\n---\n\n先运行 metarill-agent capabilities list，再 describe 核对请求schema。写操作先 --preview，取得用户明确授权后 --confirm。请求超时或结果未知时使用 tasks show 并回读数据，不自动重发；不调用目录外的路径，不执行数据库DDL。\n" });
    }
}
