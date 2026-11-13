using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Agent;

public sealed record AgentTaskOptions
{
    public int RetentionHours { get; init; } = 24;
    public int RunningTimeoutMinutes { get; init; } = 5;
    public int MaxRequestBytes { get; init; } = 1024 * 1024;
    public int MaxResponseBytes { get; init; } = 4 * 1024 * 1024;
}
public sealed record AgentTaskView(string RequestId, string CapabilityId, string Status, string Outcome, string ProjectId,
    long? ResultId, int? HttpStatus, int? BusinessCode, DateTime CreatedAt, DateTime? CompletedAt, DateTime ExpiresAt, string Recovery, bool Retryable = false);
public sealed class AgentTaskException(string code, string message, int httpStatus, string outcome, string recovery, string? requestId = null) : Exception(message)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
    public string Outcome { get; } = outcome;
    public string Recovery { get; } = recovery;
    public string? RequestId { get; } = requestId;
}
public sealed record AgentTaskClaim(long Id, string RequestId, long AccountId, string ProjectId);

/// <summary>数据库持久账本只保存请求摘要与结果状态；同账号同项目编号去重，不缓存或重放业务响应。</summary>
public sealed class AgentTaskStore(SqlRowStore store, IIdGenerator ids, AgentTaskOptions options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <summary>唯一索引负责并发互斥；登记成功前不调用业务，存储不可用明确阻止本次写入。</summary>
    public async Task<AgentTaskClaim> BeginAsync(RuntimeActor actor, string capability, string requestId, string hash, CancellationToken cancellationToken = default)
    {
        ValidateOptions(); ValidateRequestId(requestId);
        var scope = Scope(actor, requestId);
        try
        {
            return await store.Session.InTransactionAsync(async () =>
            {
                // 直接插入依靠唯一键排队，避免不存在记录的FOR UPDATE间隙锁造成并发死锁。
                var id = ids.NextId(); var now = _clock.GetUtcNow().UtcDateTime;
                await store.InsertAsync("sys_agent_task", new Dictionary<string, object?>
                {
                    ["id"] = id, ["account_id"] = actor.AccountId, ["project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture),
                    ["request_id"] = requestId, ["capability"] = capability, ["request_hash"] = hash, ["status"] = "running", ["outcome"] = "unknown",
                    ["insert_time"] = now, ["expires_at"] = now.AddHours(options.RetentionHours)
                }, cancellationToken);
                return new AgentTaskClaim(id, requestId, actor.AccountId, actor.ProjectId);
            }, cancellationToken);
        }
        catch (DbException)
        {
            // 唯一键竞争/死锁后读取已提交账本；不能把存储错误当成执行授权。
            try { var existing = (await store.ReadAsync("sys_agent_task", scope, cancellationToken)).SingleOrDefault(); if (existing is not null) throw Recorded(existing, hash); }
            catch (DbException) { }
            throw new AgentTaskException("YZP_TASK_STORE_UNAVAILABLE", "任务登记不可用，本次业务尚未调用。", 503, "not_applied", "fix_request", requestId);
        }
    }

    /// <summary>业务结束后独立持久化；失败/中断不能证明整体未写入，统一保守记录unknown。</summary>
    public Task CompleteAsync(AgentTaskClaim claim, int? httpStatus, int? businessCode, long? resultId, bool applied) => store.Session.InTransactionAsync(async () =>
    {
        var row = (await store.ReadForUpdateAsync("sys_agent_task", new Dictionary<string, object?> { ["id"] = claim.Id, ["account_id"] = claim.AccountId, ["project_id"] = long.Parse(claim.ProjectId, CultureInfo.InvariantCulture) }, CancellationToken.None)).SingleOrDefault()
            ?? throw new InvalidOperationException("任务账本丢失，必须回读业务结果。");
        if (SqlRowStore.Text(row, "status") != "running") return false;
        await store.UpdateAsync("sys_agent_task", claim.Id, new Dictionary<string, object?> { ["status"] = applied ? "succeeded" : "unknown", ["outcome"] = applied ? "applied" : "unknown",
            ["http_status"] = httpStatus, ["business_code"] = businessCode, ["result_id"] = applied ? resultId : null, ["update_time"] = _clock.GetUtcNow().UtcDateTime }, CancellationToken.None);
        return true;
    }, CancellationToken.None);

    /// <summary>查询必须绑定真实Agent账户与项目，过期或运行超时只返回unknown，不允许复用旧编号。</summary>
    public Task<AgentTaskView> GetAsync(RuntimeActor actor, string requestId, CancellationToken cancellationToken = default)
    {
        ValidateRequestId(requestId);
        return store.Session.InTransactionAsync(async () =>
        {
            var row = (await store.ReadForUpdateAsync("sys_agent_task", Scope(actor, requestId), cancellationToken)).SingleOrDefault()
                ?? throw new AgentTaskException("YZP_TASK_NOT_FOUND", "未找到当前账号项目的任务，请先回读业务数据。", 404, "unknown", "read_back", requestId);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (Date(row, "expires_at") <= now || SqlRowStore.Text(row, "status") == "running" && Date(row, "insert_time").AddMinutes(options.RunningTimeoutMinutes) <= now)
            {
                await store.UpdateAsync("sys_agent_task", SqlRowStore.Id(row), new Dictionary<string, object?> { ["status"] = "unknown", ["outcome"] = "unknown", ["update_time"] = now }, cancellationToken);
                row["status"] = "unknown"; row["outcome"] = "unknown"; row["update_time"] = now;
            }
            return View(row);
        }, cancellationToken);
    }

    /// <summary>UUID使用CLI约定的D格式，规范化必须由入口完成。</summary>
    public static void ValidateRequestId(string value)
    { if (!Guid.TryParseExact(value, "D", out var id) || id.ToString("D") != value) throw new AgentTaskException("YZP_INVALID_REQUEST_ID", "请求编号必须是小写标准UUID。", 400, "not_applied", "fix_request"); }

    /// <summary>长度前缀避免字段拼接歧义，敏感业务请求正文只参与摘要。</summary>
    public static string Fingerprint(string capability, string method, string path, string query, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in new[] { capability, method, path, query, "true" })
        { var bytes = Encoding.UTF8.GetBytes(value); hash.AppendData(Encoding.ASCII.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":")); hash.AppendData(bytes); }
        hash.AppendData(body); return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static Dictionary<string, object?> Scope(RuntimeActor actor, string requestId) => new() { ["account_id"] = actor.AccountId, ["project_id"] = long.Parse(actor.ProjectId, CultureInfo.InvariantCulture), ["request_id"] = requestId };
    private static AgentTaskException Recorded(IReadOnlyDictionary<string, object?> row, string hash) => new(SqlRowStore.Text(row, "request_hash") == hash ? "YZP_REQUEST_ALREADY_RECORDED" : "YZP_CONFLICT",
        "该请求编号已经登记，不能重复执行；请查询任务并回读结果。", 409, SqlRowStore.Text(row, "outcome"), "query_task", SqlRowStore.Text(row, "request_id"));
    private static AgentTaskView View(IReadOnlyDictionary<string, object?> row) => new(SqlRowStore.Text(row, "request_id"), SqlRowStore.Text(row, "capability"), SqlRowStore.Text(row, "status"), SqlRowStore.Text(row, "outcome"), SqlRowStore.Text(row, "project_id"),
        row.GetValueOrDefault("result_id") is null ? null : SqlRowStore.Id(row, "result_id"), Number(row, "http_status"), Number(row, "business_code"), Date(row, "insert_time"), row.GetValueOrDefault("update_time") is null ? null : Date(row, "update_time"), Date(row, "expires_at"), SqlRowStore.Text(row, "status") == "running" ? "query_task" : "read_back");
    private static int? Number(IReadOnlyDictionary<string, object?> row, string field) => row.GetValueOrDefault(field) is { } value ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
    private static DateTime Date(IReadOnlyDictionary<string, object?> row, string field) => Convert.ToDateTime(row[field], CultureInfo.InvariantCulture);
    private void ValidateOptions()
    { if (options.RetentionHours is < 1 or > 168 || options.RunningTimeoutMinutes is < 1 or > 60 || options.MaxRequestBytes is < 1024 or > 16 * 1024 * 1024 || options.MaxResponseBytes is < 1024 or > 32 * 1024 * 1024) throw new InvalidOperationException("Agent任务限制配置无效。"); }
}
