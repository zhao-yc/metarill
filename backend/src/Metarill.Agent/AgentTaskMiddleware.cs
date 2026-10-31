using System.Buffers;
using System.Text.Json;
using Metarill.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Metarill.Runtime.Agent;

/// <summary>放在路由与严格认证之后；Agent只能访问真实能力和必要的自身授权管理端点。</summary>
public sealed class AgentTaskMiddleware(RequestDelegate next)
{
    /// <summary>确认、能力、编号和请求摘要均校验后才登记；账本成功后才调用真实MVC业务。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items[typeof(AgentRequestIdentity)] is not AgentRequestIdentity { Kind: "agent" } identity) { await next(context); return; }
        var path = context.Request.Path.Value ?? "";
        // MCP由宿主工具逐项鉴权，写工具已拒绝Agent；这里保留其只读协议通道。
        if (path.Equals("/api/mcp", StringComparison.Ordinal) || path.StartsWith("/api/mcp/", StringComparison.Ordinal)) { await next(context); return; }
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        // 人工管理动作仍由控制器RequireWeb拒绝Agent，不能把自身授权当作网页登录。
        if (action?.ControllerTypeInfo.FullName == "Metarill.Api.AgentControllers.AgentManagementController" && action.ActionName is "Me" or "Capabilities" or "Describe" or "Skill" or "Tasks" or "Revoke" or "Device" or "Approve" or "Grants") { await next(context); return; }
        // 助手控制通道由控制器再次校验Agent管理员及精确grant/lease；不放宽普通能力目录。
        if (action?.ControllerTypeInfo.FullName == "Metarill.Api.Controllers.GenerationAssistantController" && action.ActionName is "Bind" or "Claim" or "Report" or "Approvals" or "Authorize" or "VerifyTicket" or "ApplicationReport") { await next(context); return; }
        AgentTaskClaim? claim = null;
        AgentTaskStore? tasks = null;
        try
        {
            // 仅真实Agent业务调用解析目录和账本；普通Web与匿名请求不提前创建数据库服务。
            var catalog = context.RequestServices.GetRequiredService<AgentCapabilityCatalog>();
            tasks = context.RequestServices.GetRequiredService<AgentTaskStore>();
            var options = context.RequestServices.GetRequiredService<AgentTaskOptions>();
            var capabilityId = Header(context, "X-Yzp-Agent-Capability", 200);
            AgentCapability capability;
            try { capability = catalog.RequireInvocation(capabilityId, context); }
            catch (UnauthorizedAccessException) { throw new AgentTaskException("YZP_CAPABILITY_DENIED", "能力未授权，或与实际请求路径、方法不匹配。", 403, "not_applied", "fix_request"); }
            if (!capability.ConfirmationRequired && capability.Operation != "action") { await next(context); return; }
            if (capability.ConfirmationRequired && Header(context, "X-Yzp-Agent-Confirm", 5) != "true") throw new AgentTaskException("YZP_CONFIRMATION_REQUIRED", "写操作必须逐次明确确认。", 400, "not_applied", "fix_request");
            var requestId = Header(context, "X-Yzp-Agent-Request-Id", 36).ToLowerInvariant(); AgentTaskStore.ValidateRequestId(requestId);
            var body = await ReadBodyAsync(context, options.MaxRequestBytes);
            if (capability.Operation == "action")
            {
                try { await catalog.ValidateActionInvocationAsync(capabilityId, context, body); }
                catch (UnauthorizedAccessException) { throw new AgentTaskException("YZP_CAPABILITY_DENIED", "业务记录不存在或当前按钮无权访问。", 403, "not_applied", "fix_request"); }
            }
            if(capability.Operation is "update" or "delete" or "copy")
            {
                try{await catalog.ValidateRecordWriteInvocationAsync(capabilityId,context,body);}
                catch(UnauthorizedAccessException){throw new AgentTaskException("YZP_CAPABILITY_DENIED","原记录不存在或当前写操作无权访问。",403,"not_applied","fix_request");}
            }
            var hash = AgentTaskStore.Fingerprint(capability.Id, context.Request.Method, path, context.Request.QueryString.Value ?? "", body);
            claim = await tasks.BeginAsync(identity.Identity.Actor, capability.Id, requestId, hash, context.RequestAborted);
            var original = context.Response.Body;
            using var buffer = new BoundedResponseStream(options.MaxResponseBytes);
            context.Response.Body = buffer;
            try
            {
                if(capability.Operation=="action")
                {
                    // 与原页面按钮共用固定发布按钮键；账本已登记，忙或协调故障仍走既有unknown/查询任务规则。
                    var published=context.RequestServices.GetRequiredService<IAgentHostResources>().AgentCatalog.FindButton(capability.ButtonId??"")
                        ??throw new UnauthorizedAccessException("动作按钮未发布。");
                    if(published.Page.Id!=capability.PageId)throw new UnauthorizedAccessException("动作按钮归属不一致。");
                    await context.RequestServices.GetRequiredService<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>().ExecuteAsync(published.Page.ProjectId,published.Id,async()=>{await next(context);return true;},context.RequestAborted);
                }
                else await next(context);
                var result = ParseResult(buffer.ToArray(), context.Response.StatusCode);
                await tasks.CompleteAsync(claim, context.Response.StatusCode, result.Code, result.Id, result.Success);
                if (!result.Success)
                {
                    // 业务返回失败也可能已经提交某些动作，统一提示先查任务与回读。
                    context.Response.Body = original; context.Response.Clear();
                    await WriteErrorAsync(context, new("YZP_OUTCOME_UNKNOWN", "业务未返回可确认的成功结果，请查询任务并回读数据。", 409, "unknown", "query_task", requestId));
                    return;
                }
                context.Response.Body = original; buffer.Position = 0; await buffer.CopyToAsync(original, context.RequestAborted);
            }
            catch (AgentActionConflictException error) when (capability.Operation == "action")
            {
                // 仅保留显式动作冲突分类；账本仍记录unknown，错误不能授权重试或证明没有写入。
                var recorded = false;
                try { await tasks!.CompleteAsync(claim, 409, null, null, false); recorded = true; } catch (Exception) { /* 账本失败回退通用unknown。 */ }
                context.Response.Body = original;
                if (context.Response.HasStarted) throw;
                context.Response.Clear();
                await WriteErrorAsync(context, new(recorded ? "YZP_CONFLICT" : "YZP_OUTCOME_UNKNOWN",
                    recorded ? error.Message : "结果记录失败，请查询任务并回读数据。", recorded ? 409 : 503, "unknown", "query_task", requestId));
            }
            catch
            {
                try { await tasks!.CompleteAsync(claim, context.Response.StatusCode, null, null, false); } catch (Exception) { /* 账本故障由unknown响应兜底，禁止自动重发。 */ }
                context.Response.Body = original;
                if (context.Response.HasStarted) throw;
                context.Response.Clear();
                await WriteErrorAsync(context, new("YZP_OUTCOME_UNKNOWN", "请求中断或结果记录失败，请查询任务并回读数据。", 503, "unknown", "query_task", requestId));
            }
            finally { context.Response.Body = original; }
        }
        catch (AgentTaskException error) { await WriteErrorAsync(context, error); }
    }

    /// <summary>错误使用旧CLI兼容的结构化恢复提示，不泄漏SQL、凭据和业务正文。</summary>
    public static async Task WriteErrorAsync(HttpContext context, AgentTaskException error)
    {
        context.Response.StatusCode = error.HttpStatus; context.Response.Headers.CacheControl = "no-store";
        var envelope = new R<object>(500, error.Message, null, new Dictionary<string, object?> { ["agentError"] = new { code = error.Code, requestId = error.RequestId, outcome = error.Outcome, recovery = error.Recovery, retryable = false } });
        await context.Response.WriteAsJsonAsync(envelope, JsonDefaults.Options, CancellationToken.None);
    }
    /// <summary>拒绝重复头和控制字符，不以合并头或空字符串猜测用户确认。</summary>
    private static string Header(HttpContext context, string name, int maxLength)
    {
        var header = context.Request.Headers[name]; var value = header.ToString();
        if (header.Count != 1 || value.Length == 0 || value.Length > maxLength || value.Any(char.IsControl)) throw new AgentTaskException("YZP_INVALID_AGENT_HEADER", "缺少有效的Agent能力、确认或请求编号头。", 400, "not_applied", "fix_request");
        return value;
    }
    /// <summary>有界读取后复位，控制器仍从原始JSON解析；请求正文不写入任务表。</summary>
    private static async Task<byte[]> ReadBodyAsync(HttpContext context, int limit)
    {
        if (context.Request.ContentLength > limit) throw new AgentTaskException("YZP_REQUEST_TOO_LARGE", "Agent请求超过允许大小。", 413, "not_applied", "fix_request");
        context.Request.EnableBuffering(64 * 1024, limit + 1L);
        using var data = new MemoryStream(); var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int count;
            while ((count = await context.Request.Body.ReadAsync(rented, context.RequestAborted)) > 0)
            { if (data.Length + count > limit) throw new AgentTaskException("YZP_REQUEST_TOO_LARGE", "Agent请求超过允许大小。", 413, "not_applied", "fix_request"); await data.WriteAsync(rented.AsMemory(0, count), context.RequestAborted); }
            return data.ToArray();
        }
        catch (IOException) { throw new AgentTaskException("YZP_REQUEST_TOO_LARGE", "Agent请求正文无法安全读取。", 413, "not_applied", "fix_request"); }
        finally { context.Request.Body.Position = 0; ArrayPool<byte>.Shared.Return(rented); }
    }
    /// <summary>只把HTTP成功且业务code=200作为已应用证据；仅提取结果主键，不持久化完整响应。</summary>
    private static (bool Success, int? Code, long? Id) ParseResult(byte[] data, int status)
    {
        try
        {
            using var json = JsonDocument.Parse(data); var root = json.RootElement;
            int? code = root.TryGetProperty("code", out var value) && value.TryGetInt32(out var number) ? number : null;
            long? id = root.TryGetProperty("data", out var payload) && payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("id", out var identifier) && long.TryParse(identifier.ToString(), out var parsed) ? parsed : null;
            return (status is >= 200 and < 300 && code == 200, code, id);
        }
        catch (JsonException) { return (false, null, null); }
    }
    /// <summary>写响应先缓冲，账本落盘后才发送成功；内存上限触发时保持unknown而不静默截断。</summary>
    private sealed class BoundedResponseStream(int maximum) : MemoryStream
    {
        private void Check(int count) { if (Position + count > maximum) throw new IOException("Agent响应超过安全缓冲上限。"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) { Check(count); return base.WriteAsync(buffer, offset, count, token); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) { Check(buffer.Length); return base.WriteAsync(buffer, token); }
    }
}
