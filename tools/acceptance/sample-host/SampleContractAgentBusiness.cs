using System.Globalization;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.GlobalSample.Generated.DTOs;
using Metarill.GlobalSample.Generated.Entities;
using Metarill.GlobalSample.Generated.Services;

namespace Metarill.GlobalSample.Business;

/// <summary>原样板手写业务编排，全部持久化进入生成Service并加入同一外层事务。</summary>
public sealed class SampleContractAgentBusiness(DatabaseSession session, SampleContractService contracts, IRuntimeAuthorization authorization)
{
    /// <summary>只读取当前按钮范围，不写合同或伪造页面query权限。</summary>
    public async Task<SampleContractPreview> PreviewAsync(long id, CancellationToken cancellationToken)
    {
        var actor = Actor(id, "86000");
        var row = await AccessibleAsync(id, "86000", actor, cancellationToken);
        return new(id.ToString(CultureInfo.InvariantCulture), row.Name, row.Remarks ?? "", "已处理", Digest(row));
    }
    /// <summary>锁定后重算摘要与状态；回滚动作的第二次写入必须触发原手写金额规则。</summary>
    public Task<SampleContractCompletion> CompleteAsync(long id, string? expectedDigest, bool verifyRollback, CancellationToken cancellationToken)
    {
        var button = verifyRollback ? "86002" : "86001"; var actor = Actor(id, button);
        return session.InTransactionAsync(async () =>
        {
            await AccessibleAsync(id, button, actor, cancellationToken);
            var current = await contracts.LockButtonRecord81002Async(id, null, button, actor, cancellationToken)
                ?? throw new SampleContractMissingException();
            // 读取的是当前锁定实体；不能使用客户端原值或之前预览的DTO代替。
            if (!SampleContractDigest.Matches(Digest(current), expectedDigest))
                throw new SampleContractConflictException("合同在预览后已变化，请重新查询和预览");
            if (current.Remarks != "待处理") throw new SampleContractConflictException("合同当前不是待处理状态");
            await contracts.UpdateButtonRecord81002Async(new SampleContractPage81002UpdateRequest { Id = id, Remarks = new Optional<string?>("已处理") }, null, button, actor, cancellationToken);
            if (verifyRollback)
            {
                // 第一条真实更新仍未提交；第二次沿同一生成核心触发BeforeSave，任意内层失败均回滚外层。
                await contracts.UpdateButtonRecord81002Async(new SampleContractPage81002UpdateRequest { Id = id, Amount = new Optional<decimal?>(-1.00m) }, null, button, actor, cancellationToken);
                throw new InvalidOperationException("样板对象规则未拒绝负金额");
            }
            return new SampleContractCompletion(id.ToString(CultureInfo.InvariantCulture), "已处理", expectedDigest!);
        }, cancellationToken);
    }
    /// <summary>固定按钮与真实身份在打开写事务前检查，不能从正文选择权限来源。</summary>
    private RuntimeActor Actor(long id, string button)
    {
        if (id <= 0) throw new ArgumentException("合同标识无效。");
        authorization.RequireButtonPermission("81002", button); return authorization.RequireActor();
    }
    /// <summary>专用记录入口复用生成的固定条件及按钮数据范围，普通页面详情不能替代。</summary>
    private async Task<SampleContract> AccessibleAsync(long id, string button, RuntimeActor actor, CancellationToken cancellationToken) =>
        await contracts.ReadButtonRecord81002Async(id, null, button, actor, cancellationToken) ?? throw new SampleContractMissingException();
    /// <summary>摘要只取当前实体的业务值。</summary>
    private static string Digest(SampleContract row) => SampleContractDigest.Calculate(row.Id, row.Name, row.Amount, row.Remarks);
}
public sealed record SampleContractPreview(string Id, string? Name, string CurrentRemarks, string NextRemarks, string ExpectedDigest);
public sealed record SampleContractCompletion(string Id, string Remarks, string PreviousDigest);
/// <summary>显式原样板版本冲突，不包含数据库、凭据或内部异常链。</summary>
public sealed class SampleContractConflictException(string message) : Exception(message);
/// <summary>范围内记录在实际访问时已不存在。</summary>
public sealed class SampleContractMissingException() : Exception("合同不存在或无权限");
