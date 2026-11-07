using System.Text.Json;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>本机一次性关联原设备；真实Agent/grant与设备拥有权均复核，不接收项目身份或本机目录。</summary>
[ApiController,Route("api/admin/sys/generation-assistant/helper"),RequestSizeLimit(32768)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class GenerationAssistantDeviceController(GenerationAssistantQueue queue,GenerationAssistantDeviceScopeReader scopes,
    HttpRuntimeAuthorization authorization,MetadataRegistry registry):ControllerBase
{
    /// <summary>当前实际批准的Agent只关联自己项目的设备配置；HTTP成功不代表已经同步源码。</summary>
    [HttpPost("bind-device")]
    public async Task<R<AssistantDeviceBindingView>> Bind(JsonElement body,CancellationToken token)
    {
        var owner=Agent();var request=GenerationAssistantQueue.Parse<AssistantDeviceBindRequest>(body);
        var scope=await scopes.LoadAsync(authorization.RequireActor(),request.DeviceId,token);
        return R<AssistantDeviceBindingView>.Success(queue.AssociateDevice(owner,request,scope));
    }
    /// <summary>一次性确认先固定当前设备/配置摘要，网络未知重传原意图不能自动采纳新配置。</summary>
    [HttpPost("device-configuration")]
    public async Task<R<AssistantDeviceConfigurationView>> Configuration(JsonElement body,CancellationToken token)
    {
        _=Agent();var request=GenerationAssistantQueue.Parse<AssistantDeviceLookupRequest>(body);
        var scope=await scopes.LoadAsync(authorization.RequireActor(),request.DeviceId,token);
        return R<AssistantDeviceConfigurationView>.Success(new(scope.DeviceId,scope.ProjectId,scope.DeviceSha256,scope.ConfigurationSha256,scope.Parts,scope.BackendPathSha256,scope.FrontendPathSha256));
    }
    /// <summary>回读旧关联摘要以便明确替换，配置已变时保留旧ETag并明确标记不再匹配。</summary>
    [HttpPost("device-binding")]
    public async Task<R<AssistantDeviceBindingView?>> Lookup(JsonElement body,CancellationToken token)
    {
        var owner=Agent();var request=GenerationAssistantQueue.Parse<AssistantDeviceLookupRequest>(body);
        var scope=await scopes.LoadAsync(authorization.RequireActor(),request.DeviceId,token);
        return R<AssistantDeviceBindingView?>.Success(queue.FindDeviceBinding(owner,scope));
    }
    /// <summary>选择器和请求字段不授予身份，严格复用中间件真实Agent与当前平台管理权限。</summary>
    private AssistantOwner Agent()
    {
        if(Request.Query.Count!=0)throw new InvalidOperationException("设备关联不接受query参数。");
        var identity=AgentRequestAuthentication.RequireAgent(HttpContext);var actor=authorization.RequireAdministrator();
        if(identity.Identity.Actor!=actor||identity.GrantId is not >0)throw new UnauthorizedAccessException("设备关联身份不一致。");
        if(actor.ProjectId==registry.Platform.YzProjectId)throw new InvalidOperationException("平台BackendOnly不能绑定完整业务工程运行时。");
        return new(actor.AccountId,actor.ProjectId,identity.GrantId);
    }
}
