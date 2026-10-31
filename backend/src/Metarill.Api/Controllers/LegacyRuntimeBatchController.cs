using System.Text.Json;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留原项目列表的批量按钮路由和一次确认；内部转为逐项目助手任务，不重写页面。</summary>
[ApiController,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class LegacyRuntimeBatchController(GenerationAssistantBatchEnqueuer enqueuer,HttpRuntimeAuthorization authorization):ControllerBase
{
    /// <summary>原页面继续POST旧地址并只传parentId；parentId作为设备标识，服务端仍重新查拥有权和配置。</summary>
    [HttpPost("/api/admin/custom/index/sync_runtime_all_projects")]
    public async Task<R<AssistantRuntimeBatchEnqueueResult>> Execute(CancellationToken token)
    {
        var identity=AgentRequestAuthentication.RequireWeb(HttpContext);authorization.RequireAdministrator();
        if(Request.Query.Count!=1||!Request.Query.ContainsKey("parentId")||Request.Query["parentId"].Count!=1||!long.TryParse(Request.Query["parentId"],out var device)||device<=0||Request.ContentLength>0)throw new InvalidOperationException("批量运行时入口只接受一个有效parentId。");
        var login=Request.Headers["loginStateId"];var bearer=Request.Headers.Authorization;if(login.Count>1||bearer.Count>1||login.Count==1&&bearer.Count==1)throw new UnauthorizedAccessException("登录超时");
        var batchId=Request.Headers["X-Yzp-Agent-Request-Id"].Count==1?Request.Headers["X-Yzp-Agent-Request-Id"].ToString():Guid.NewGuid().ToString("D");AgentTaskStore.ValidateRequestId(batchId);
        var result=await enqueuer.EnqueueAsync(identity.Identity,device.ToString(),batchId,login.Count==1?login.ToString():null,bearer.Count==1?bearer.ToString():null,token);Response.Headers.CacheControl="private, no-store";return R<AssistantRuntimeBatchEnqueueResult>.Success(result);
    }
}
