using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Generation;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留首页原三个GET和人工确认，每次只排队对应动作；平台不接收本机目录或执行命令。</summary>
[ApiController, Route("api/admin/custom/index")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GenerationRequestController(MetadataRepository metadata, MetadataRegistry registry, HttpRuntimeAuthorization authorization,
    GenerationAssistantQueue queue, AgentOAuthService oauth) : ControllerBase
{
    /// <summary>生成配置只确认配置范围，不扩大为页面和运行时。</summary>
    [HttpGet("generate_project_config")]
    public Task<R<AssistantTaskView>> ProjectConfiguration(CancellationToken token) => Enqueue("generate-project-config", token);
    /// <summary>同步运行时只确认运行时范围，缺依赖回原按钮分别确认。</summary>
    [HttpGet("sync_runtime")]
    public Task<R<AssistantTaskView>> Runtime(CancellationToken token) => Enqueue("sync-runtime", token);
    /// <summary>生成全部页面只确认页面范围，没有完整ZIP伪成功。</summary>
    [HttpGet("generate_all_page")]
    public Task<R<AssistantTaskView>> Pages(CancellationToken token) => Enqueue("generate-all-page", token);

    /// <summary>账号和项目只来自真实Web会话；UUID/确认/绑定头限定本次动作，任何query或正文均拒绝。</summary>
    private async Task<R<AssistantTaskView>> Enqueue(string action, CancellationToken token)
    {
        AgentRequestAuthentication.RequireWeb(HttpContext); var actor = authorization.RequireAdministrator();
        if (Request.Query.Count != 0 || Request.ContentLength > 0 || Request.Headers.TransferEncoding.Count != 0) throw new InvalidOperationException("生成入口不接受项目、路径、命令或请求参数，请使用当前授权项目。");
        if (Header("X-Yzp-Agent-Confirm") != "true") throw new InvalidOperationException("请先确认本次原按钮动作。");
        var requestId = Header("X-Yzp-Agent-Request-Id"); AgentTaskStore.ValidateRequestId(requestId);
        var binding = Header("X-Metarill-Assistant-Binding"); AgentTaskStore.ValidateRequestId(binding);
        if (actor.ProjectId == registry.Platform.YzProjectId) throw new InvalidOperationException("平台BackendOnly不能由这三个业务工程按钮应用，请使用平台专用自举流程；未写入源码。");
        var owner = new AssistantOwner(actor.AccountId, actor.ProjectId);
        await oauth.RequireApprovedGrantAsync(queue.RequireBinding(owner, binding), authorization.RequireIdentity(), token);
        var snapshot = LocalGenerationUpdater.CreateRequest(await metadata.LoadProjectAsync(actor.ProjectId, token), action);
        Response.Headers.CacheControl = "private, no-store";
        return R<AssistantTaskView>.Success(queue.Enqueue(owner, requestId, binding, snapshot));
    }
    /// <summary>固定头只允许唯一有界值，不从query选择身份、设备或本机目录。</summary>
    private string Header(string name) { var values = Request.Headers[name]; if (values.Count != 1 || values[0] is not { Length: > 0 and <= 100 } value) throw new InvalidOperationException("生成助手确认头缺失或重复。"); return value; }
}
