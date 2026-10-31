using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.GlobalSample.Business;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.GlobalSample.Controllers;

/// <summary>原三个显式业务能力；标准查询与CRUD继续使用正式生成接口。</summary>
[ApiController, Route("api/admin/custom/sample-contract-agent")]
public sealed class SampleContractAgentController(SampleContractAgentBusiness business) : ControllerBase
{
    /// <summary>返回真实原值和拟变更摘要，预览本身不修改合同。</summary>
    [HttpPost("preview"), AgentAction("sample-contract-preview", "预览合同处理", "86000")]
    public Task<R<SampleContractPreview>> Preview([FromBody] ContractChangeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => business.PreviewAsync(request.Id, cancellationToken));
    /// <summary>记录锁及摘要/状态检查通过后调用同一生成更新核心。</summary>
    [HttpPost("complete"), AgentAction("sample-contract-complete", "处理合同", "86001")]
    public Task<R<SampleContractCompletion>> Complete([FromBody] ContractChangeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => business.CompleteAsync(request.Id, request.ExpectedDigest, false, cancellationToken));
    /// <summary>仅此验收样板发布，验证对象规则失败撤销前一条真实更新。</summary>
    [HttpPost("rollback"), AgentAction("sample-contract-rollback", "回滚验收", "86002")]
    public Task<R<SampleContractCompletion>> Rollback([FromBody] ContractChangeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => business.CompleteAsync(request.Id, request.ExpectedDigest, true, cancellationToken));
    /// <summary>Web保持原显式状态；Agent冲突保留原代码并要求先查任务，不宣称业务未执行。</summary>
    private async Task<R<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return R<T>.Success(await action()); }
        catch (SampleContractConflictException error)
        {
            if (HttpContext.Items[typeof(AgentRequestIdentity)] is AgentRequestIdentity { Kind: "agent" }) throw new AgentActionConflictException(error.Message);
            Response.StatusCode = 409; return R<T>.Error(error.Message, 409);
        }
        catch (SampleContractMissingException error) { Response.StatusCode = 404; return R<T>.Error(error.Message, 404); }
    }
}
/// <summary>只接受记录与已审阅摘要，不允许账号、项目、按钮、任意字段或客户端回滚开关。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ContractChangeRequest
{
    public long Id { get; init; }
    public string? ExpectedDigest { get; init; }
}
