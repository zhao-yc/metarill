using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

/// <summary>人工审核的配置导入计划；符号ID只用于展示，真实ID在保存事务内分配。</summary>
public sealed record SchemaMetadataImportPlan(string PlanId, string ProjectId, DatabaseTargetIdentity Target, string SourceFingerprint,
    string MetadataFingerprint, MetadataCatalog LogicalMetadata, DateTimeOffset ExpiresAt, string ConfirmSha256, IReadOnlyList<string> Notices)
{
    public bool Executable => false;
    public bool CanSaveMetadata => true;
    public bool ExecutesDdl => false;
    public bool CreatesPages => false;
}

internal sealed record PendingMetadataImport(string PlanId, string ProjectId, long AccountId, long SessionId, string PrincipalProjectId, string PrincipalAdminTypeId, string[] Names,
    DatabaseTargetIdentity Target, string SourceFingerprint, string MetadataFingerprint, string ModelFingerprint, DateTimeOffset ExpiresAt, string ConfirmSha256);

/// <summary>只缓存有界的小型绑定，不保留连接、凭据或大型DDL/元数据副本；重启后旧计划失效。</summary>
public sealed class SchemaMetadataImportPlans(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string,PendingMetadataImport> pending = new(StringComparer.Ordinal);
    private sealed record Attempt(PendingMetadataImport Binding, string State, IReadOnlyList<ImportedSchemaTable> Tables, DateTimeOffset RetainUntil);
    private readonly Dictionary<string,Attempt> attempts = new(StringComparer.Ordinal);

    /// <summary>计划摘要覆盖真实目标、版本及完整审核模型；导出模型被客户端修改不会改变服务器绑定。</summary>
    public SchemaMetadataImportPlan Create(AuthenticatedIdentity owner, SchemaImportInspection source, string metadataFingerprint, MetadataCatalog model)
    {
        if (owner.GrantId is not null || !owner.Actor.IsAdministrator || owner.SessionId <= 0) throw new UnauthorizedAccessException("计划要求真实平台管理员会话。");
        var target = source.Target ?? throw new InvalidOperationException("源检查缺少真实目标绑定。");
        if (model.YzProjectId != owner.Actor.ProjectId || target.Provider != source.Snapshot.Provider || target.Database != source.Snapshot.Schema.Database)
            throw new InvalidOperationException("计划模型、身份和目标不一致。");
        var planId = Guid.NewGuid().ToString("N"); var expires = clock.GetUtcNow().AddMinutes(5);
        string[] notices = ["仅登记源中已有的表/字段/索引，不补系统列、不创建页面或菜单", "保留源库数据、字符集/排序规则/注释及触发器，不翻译或重建源业务逻辑", "只保存平台配置，不执行DDL，不生成或发布应用；后续页面沿原配置流程建立"];
        var hash = Fingerprint(new { planId, projectId = owner.Actor.ProjectId, target, sourceFingerprint = source.Snapshot.Fingerprint, metadataFingerprint, logicalMetadata = model, expiresAt = expires, notices });
        var binding = new PendingMetadataImport(planId, owner.Actor.ProjectId, owner.Actor.AccountId, owner.SessionId, owner.PrincipalProjectId, owner.PrincipalAdminTypeId,
            source.Snapshot.Schema.Tables.Select(table => table.Name).Order(StringComparer.Ordinal).ToArray(), target, source.Snapshot.Fingerprint, metadataFingerprint, Fingerprint(model), expires, hash);
        lock (gate)
        {
            Prune();
            if (pending.Count + attempts.Count >= 512 || pending.Values.Count(value => value.SessionId == owner.SessionId) >= 8) throw new InvalidOperationException("未使用导入计划过多，请等待过期或先完成已有计划。");
            pending.Add(planId, binding);
        }
        return new(planId, binding.ProjectId, target, binding.SourceFingerprint, metadataFingerprint, model, expires, hash, notices);
    }

    /// <summary>合法确认一次性消费，保存失败必须重新预览；其他身份或错误摘要不能消费他人计划。</summary>
    internal PendingMetadataImport Take(string planId, string confirmation, AuthenticatedIdentity owner)
    {
        lock (gate)
        {
            Prune();
            if (!pending.TryGetValue(planId, out var value) || !Matches(value, owner, confirmation))
                throw new InvalidOperationException("导入计划不存在、过期、身份不匹配或确认摘要不符；请重新预览。");
            pending.Remove(planId); attempts.Add(planId, new(value, "processing", [], clock.GetUtcNow().AddMinutes(30))); return value;
        }
    }

    /// <summary>平台配置提交已确认后记录回执；源只读事务后续失败不能抹去已经保存的事实。</summary>
    internal void Committed(PendingMetadataImport binding, SchemaMetadataImportResult result)
    {
        lock (gate) { if (attempts.ContainsKey(binding.PlanId)) attempts[binding.PlanId] = new(binding, "saved", Copy(result.Tables), clock.GetUtcNow().AddMinutes(30)); }
    }
    /// <summary>异常或连接丢失不能推断未写入；保留未知结果，且不覆盖已确认提交。</summary>
    internal void Unconfirmed(PendingMetadataImport binding)
    {
        lock (gate) { if (attempts.TryGetValue(binding.PlanId, out var value) && value.State != "saved") attempts[binding.PlanId] = value with { State = "unconfirmed" }; }
    }
    /// <summary>执行前和提交前复核期限；超时后必须重新生成计划。</summary>
    internal void RequireNotExpired(PendingMetadataImport binding)
    { if (binding.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException("导入计划已过期，请重新预览。"); }
    /// <summary>仅同一身份和摘要可查询；未知/过期回执不等价于未写入，重启后也只能返回unknown。</summary>
    internal SchemaMetadataImportStatus Status(string planId, string confirmation, AuthenticatedIdentity owner)
    {
        lock (gate)
        {
            Prune();
            if (pending.TryGetValue(planId, out var value) && Matches(value, owner, confirmation)) return new(owner.Actor.ProjectId, planId, "prepared", null, []);
            if (attempts.TryGetValue(planId, out var attempt) && Matches(attempt.Binding, owner, confirmation))
                return new(owner.Actor.ProjectId, planId, attempt.State, attempt.State == "saved" ? true : null, Copy(attempt.Tables));
            return new(owner.Actor.ProjectId, planId, "unknown", null, []);
        }
    }
    /// <summary>计划绑定真实身份后台，切换后台或会话不能复用旧审核结果。</summary>
    private static bool Matches(PendingMetadataImport value, AuthenticatedIdentity owner, string confirmation) =>
        value.ProjectId == owner.Actor.ProjectId && value.AccountId == owner.Actor.AccountId && value.SessionId == owner.SessionId &&
        value.PrincipalProjectId == owner.PrincipalProjectId && value.PrincipalAdminTypeId == owner.PrincipalAdminTypeId &&
        owner.GrantId is null && owner.Actor.IsAdministrator && confirmation == value.ConfirmSha256;
    /// <summary>回执集合逐层复制，调用者修改返回数组不能改变服务器记录。</summary>
    private static IReadOnlyList<ImportedSchemaTable> Copy(IReadOnlyList<ImportedSchemaTable> tables) => tables.Select(table => new ImportedSchemaTable(table.TableId, table.TableName, table.ColumnIds.ToArray(), table.IndexIds.ToArray())).ToArray();

    /// <summary>只有服务端固定JSON模型进入指纹，不执行输入或依赖客户端规范化。</summary>
    internal static string Fingerprint<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    /// <summary>过期绑定在每次创建和消费时清除，无后台任务或持久化秘密。</summary>
    private void Prune()
    {
        foreach (var id in pending.Where(pair => pair.Value.ExpiresAt <= clock.GetUtcNow()).Select(pair => pair.Key).ToArray()) pending.Remove(id);
        foreach (var id in attempts.Where(pair => pair.Value.RetainUntil <= clock.GetUtcNow()).Select(pair => pair.Key).ToArray()) attempts.Remove(id);
    }
}
