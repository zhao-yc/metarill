using System.Text.Json;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Generation;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>在线助手只传固定动作与回执，源码写入仍由本机显式策略和短ticket约束，没有SQL或任意脚本入口。</summary>
[ApiController, Route("api/admin/sys/generation-assistant"), RequestSizeLimit(32768)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GenerationAssistantController(GenerationAssistantQueue queue, MetadataRepository metadata, MetadataRegistry registry, HttpRuntimeAuthorization authorization, AgentOAuthService oauth,GenerationAssistantDeviceScopeReader scopes,AgentAuthenticationService authentication) : ControllerBase
{
    /// <summary>网页人工确认固定action后入队，绑定与项目先鉴权再读取发布元数据。</summary>
    [HttpPost("requests")]
    public async Task<R<AssistantTaskView>> Enqueue(JsonElement body, CancellationToken token)
    {
        var owner = Web(); var request = GenerationAssistantQueue.Parse<AssistantEnqueueRequest>(body);
        var requestId = Header("X-Yzp-Agent-Request-Id"); AgentTaskStore.ValidateRequestId(requestId);
        if (Header("X-Yzp-Agent-Confirm") != "true") throw new InvalidOperationException("队列请求必须来自用户对本次固定动作的明确确认。");
        var grant = queue.RequireBinding(owner, request.BindingId);
        await oauth.RequireApprovedGrantAsync(grant, authorization.RequireIdentity(), token);
        if (owner.ProjectId == registry.Platform.YzProjectId) throw new InvalidOperationException("在线助手首期只支持FullApplication业务工程，平台BackendOnly未应用。");
        var snapshot = LocalGenerationUpdater.CreateRequest(await metadata.LoadProjectAsync(owner.ProjectId, token), request.Action);
        return R<AssistantTaskView>.Success(queue.Enqueue(owner, requestId, request.BindingId, snapshot));
    }
    /// <summary>原批量按钮的只读计划入口：枚举全部项目并逐项给出跳过/失败/待授权，不创建任务或应用源码。</summary>
    [HttpPost("runtime-batch/plan")]
    public async Task<R<AssistantRuntimeBatchPlan>> RuntimeBatchPlan(JsonElement body,CancellationToken token)
    {
        var identity=AgentRequestAuthentication.RequireWeb(HttpContext);authorization.RequireAdministrator();
        var request=GenerationAssistantQueue.Parse<AssistantRuntimeBatchPlanRequest>(body);
        var discovery=await scopes.DiscoverAsync(identity.Identity.Actor,request.DeviceId,token);
        return R<AssistantRuntimeBatchPlan>.Success(GenerationAssistantBatchPlanning.Create(discovery));
    }
    /// <summary>逐项目授权预检：同一账号重新认证目标项目并检查已批准助手绑定，不创建任务或源码计划。</summary>
    [HttpPost("runtime-batch/preflight")]
    public async Task<R<AssistantRuntimeBatchAuthorizationPlan>> RuntimeBatchPreflight(JsonElement body,CancellationToken token)
    {
        var current=AgentRequestAuthentication.RequireWeb(HttpContext);authorization.RequireAdministrator();
        var request=GenerationAssistantQueue.Parse<AssistantRuntimeBatchPlanRequest>(body);
        var discovery=await scopes.DiscoverAsync(current.Identity.Actor,request.DeviceId,token);var items=new List<AssistantRuntimeBatchAuthorizationItem>();
        foreach(var project in discovery.Projects)
        {
            token.ThrowIfCancellationRequested();
            if(project.Status!="configured"||project.ProjectId is null){items.Add(new(project.ProjectId,project.ProjectName,project.Status,project.ErrorCode,null));continue;}
            try
            {
                var target=await AuthenticateTargetWebAsync(current.Identity,project.ProjectId,token);
                var owner=new AssistantOwner(target.Actor.AccountId,target.Actor.ProjectId);var binding=await FindApprovedBindingAsync(owner,target,token);
                items.Add(binding is null?new(project.ProjectId,project.ProjectName,"needs-agent-binding","approved_grant_or_binding_missing",null):new(project.ProjectId,project.ProjectName,"ready-for-enqueue",null,binding.BindingId));
            }
            catch(UnauthorizedAccessException){items.Add(new(project.ProjectId,project.ProjectName,"failed","target-project-reauthentication-required",null));}
        }
        return R<AssistantRuntimeBatchAuthorizationPlan>.Success(new(request.DeviceId,items.ToArray(),false));
    }

    /// <summary>只展示当前网页账号项目的任务状态和脱敏预览回执。</summary>
    [HttpGet("requests/{requestId}")]
    public R<AssistantTaskView> Status(string requestId) => R<AssistantTaskView>.Success(queue.Get(Web(), requestId));
    /// <summary>Agent注册显式本机绑定，只上传身份摘要，不能上传目录或命令。</summary>
    [HttpPost("helper/bind")]
    public R<AssistantBindingView> Bind(JsonElement body) => R<AssistantBindingView>.Success(queue.Bind(Agent(), GenerationAssistantQueue.Parse<AssistantBindRequest>(body)));
    /// <summary>出站轮询领取一个有界lease，真实grant在每次HTTP请求重新验证。</summary>
    [HttpPost("helper/claim")]
    public async Task<R<AssistantWork?>> Claim(JsonElement body,CancellationToken token)
    {
        var owner=Agent();var request=GenerationAssistantQueue.Parse<AssistantClaimRequest>(body);
        var prepared=queue.PrepareClaim(owner,request.BindingId);
        return R<AssistantWork?>.Success(prepared.PendingOrigin is null?prepared.Work:queue.Claim(owner,request.BindingId,await CurrentOrigin(prepared.PendingOrigin,token)));
    }
    /// <summary>只接收preview或失败，不提供伪造本机应用成功的状态。</summary>
    [HttpPost("helper/report")]
    public R<AssistantTaskView> Report(JsonElement body) => R<AssistantTaskView>.Success(queue.Report(Agent(), GenerationAssistantQueue.Parse<AssistantPreviewReceipt>(body)));
    /// <summary>网页只展示仍获真实批准的本账号项目绑定，不暴露路径或token。</summary>
    [HttpGet("bindings")]
    public async Task<R<AssistantBindingView[]>> Bindings(CancellationToken token)
    {
        var owner = Web(); if (Request.Query.Count != 0) throw new InvalidOperationException("助手绑定列表不接受客户端项目或路径。");
        var result = new List<AssistantBindingView>();
        foreach (var binding in queue.Bindings(owner))
        {
            try { await oauth.RequireApprovedGrantAsync(queue.RequireBinding(owner, binding.BindingId), authorization.RequireIdentity(), token); result.Add(binding); }
            catch (UnauthorizedAccessException) { /* 撤权绑定不再作为可选在线目标。 */ }
        }
        return R<AssistantBindingView[]>.Success(result.ToArray());
    }
    /// <summary>助手只读取同快照下真实确认集合，不能请求伪造action或目录。</summary>
    [HttpPost("helper/approvals")]
    public R<AssistantApproval[]> Approvals(JsonElement body) => R<AssistantApproval[]>.Success(queue.Approvals(Agent(), GenerationAssistantQueue.Parse<AssistantApprovalsRequest>(body)));
    /// <summary>应用组批准前重新读取当前发布快照，短ticket不能沿用过时元数据。</summary>
    [HttpPost("helper/authorize")]
    public async Task<R<AssistantApplyTicket>> Authorize(JsonElement body, CancellationToken token)
    {
        var owner = Agent(); var request = GenerationAssistantQueue.Parse<AssistantAuthorizeRequest>(body);
        return R<AssistantApplyTicket>.Success(queue.Authorize(owner, request, await CurrentMetadata(owner, token),await CurrentOrigin(queue.PendingAuthorizeOrigin(owner,request),token)));
    }
    /// <summary>紧邻本机写入再次通过真实grant和当前hash复核，不能拿领取时授权替代。</summary>
    [HttpPost("helper/verify-ticket")]
    public async Task<R<AssistantApplyTicket>> VerifyTicket(JsonElement body, CancellationToken token)
    {
        var owner=Agent();var request=GenerationAssistantQueue.Parse<AssistantTicketRequest>(body);
        return R<AssistantApplyTicket>.Success(queue.VerifyTicket(owner,request,await CurrentMetadata(owner,token),await CurrentOrigin(queue.PendingTicketOrigin(owner,request),token)));
    }
    /// <summary>只有独立应用回执能表达applied，preview状态继续明确未应用。</summary>
    [HttpPost("helper/application-report")]
    public R<AssistantApplicationReceipt> ApplicationReport(JsonElement body) => R<AssistantApplicationReceipt>.Success(queue.ReportApplication(Agent(), GenerationAssistantQueue.Parse<AssistantApplicationReceipt>(body)));
    /// <summary>只用本次真实Agent的Actor查当前设备配置，不克隆Actor或采用客户端声称的来源。</summary>
    private async Task<AssistantRuntimeOriginProof?> CurrentOrigin(AssistantPendingRuntimeOrigin? pending,CancellationToken token)
        =>pending is null?null:new(pending.RequestId,await scopes.LoadAsync(authorization.RequireActor(),pending.RuntimeOrigin.Device.DeviceId,token));
    /// <summary>目标项目必须由同一账号的真实网页会话重新认证，不能从客户端项目ID切换Actor。</summary>
    private async Task<AuthenticatedIdentity> AuthenticateTargetWebAsync(AuthenticatedIdentity current,string projectId,CancellationToken token)
    {
        if(current.Actor.ProjectId==projectId)return current;
        var login=Request.Headers["loginStateId"];var bearer=Request.Headers.Authorization;
        if(login.Count!=1&&bearer.Count!=1||login.Count==1&&bearer.Count==1)throw new UnauthorizedAccessException("目标项目需要重新登录。");
        var raw=login.Count==1?login.ToString():bearer.ToString();if(bearer.Count==1&&!raw.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("目标项目需要重新登录。");
        var identity=await authentication.AuthenticateAsync(bearer.Count==1?raw[7..]:raw,"web",projectId,token);
        if(identity.Identity.Actor.AccountId!=current.Actor.AccountId||!identity.Identity.Actor.IsAdministrator||identity.Kind!="web")throw new UnauthorizedAccessException("目标项目身份不匹配。");
        return identity.Identity;
    }
    /// <summary>绑定文件只用于定位同项目助手，不把grant或令牌返回给浏览器；每个绑定逐个复核批准状态。</summary>
    private async Task<AssistantBindingView?> FindApprovedBindingAsync(AssistantOwner owner,AuthenticatedIdentity identity,CancellationToken token)
    {
        foreach(var binding in queue.Bindings(owner))
        {
            try{await oauth.RequireApprovedGrantAsync(queue.RequireBinding(owner,binding.BindingId),identity,token);return binding;}
            catch(UnauthorizedAccessException){ }
        }
        return null;
    }
    /// <summary>服务端以同一发布脱敏规则计算摘要，不接收客户端声称的最新hash。</summary>
    private async Task<string> CurrentMetadata(AssistantOwner owner, CancellationToken token)
    {
        if (owner.ProjectId == registry.Platform.YzProjectId) throw new InvalidOperationException("平台BackendOnly不能进入在线FullApplication应用流程。");
        return LocalGenerationUpdater.CreateRequest(await metadata.LoadProjectAsync(owner.ProjectId, token), "sync-runtime").MetadataSha256;
    }
    /// <summary>网页角色只从已认证会话读取。</summary>
    private AssistantOwner Web() { if (Request.Query.Count != 0) throw new InvalidOperationException("助手入口不接受query参数。"); var identity = AgentRequestAuthentication.RequireWeb(HttpContext); authorization.RequireAdministrator(); return new(identity.Identity.Actor.AccountId, identity.Identity.Actor.ProjectId); }
    /// <summary>助手控制面要求真实Agent管理员及其批准grant。</summary>
    private AssistantOwner Agent() { if (Request.Query.Count != 0) throw new InvalidOperationException("助手控制入口不接受query参数。"); var identity = AgentRequestAuthentication.RequireAgent(HttpContext); authorization.RequireAdministrator(); return new(identity.Identity.Actor.AccountId, identity.Identity.Actor.ProjectId, identity.GrantId); }
    /// <summary>确认头不得缺失、多值或超过有界长度。</summary>
    private string Header(string name) { var values = Request.Headers[name]; if (values.Count != 1 || values[0] is not { Length: > 0 and <= 100 } value) throw new InvalidOperationException("队列确认头缺失或重复。"); return value; }
}
