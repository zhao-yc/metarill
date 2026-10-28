using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Mvc;

namespace {{NAMESPACE}}.Host;

/// <summary>沿用原详情和列表按钮入口，实际动作由发布元数据及显式手写处理器共同决定。</summary>
[ApiController]
public sealed class OriginalBusinessButtonController(global::Metarill.Runtime.Agent.OriginalBusinessButtonExecutor executor):ControllerBase
{
    /// <summary>返回手写业务原R结果，不嵌套包装，不转发客户端地址。</summary>
    [HttpPost("api/admin/sys/config_button/execute")]
    public Task<IActionResult> Execute([FromBody] global::Metarill.Runtime.Agent.OriginalBusinessButtonRequest request,CancellationToken cancellationToken)=>executor.ExecuteAsync(request,HttpContext,cancellationToken);
}
