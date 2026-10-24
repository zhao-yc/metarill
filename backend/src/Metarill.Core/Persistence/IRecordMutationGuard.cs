using System.Data.Common;
using Metarill.Core.Security;

namespace Metarill.Core.Persistence;

/// <summary>准备阶段发生于业务记录锁和写入前；导入的外层事务也必须先准备。</summary>
public enum RecordMutationOperation { Create, Update, Delete, Copy, Import }

/// <summary>仅传递固定页面、真实身份与窄归属提示；提示仍需宿主通过当前读核验，不是授权。</summary>
public sealed record RecordMutationPreparation(string PageId, string TableName, RecordMutationOperation Operation, RuntimeActor Actor, DbTransaction Transaction)
{
    /// <summary>更新、删除及复制的固定目标主键；新增及整批导入尚无对象主键。</summary>
    public long? TargetId { get; init; }
    /// <summary>仅含生成器已编译的项目、后台及父归属键，不传密码、任意实体或 SQL。</summary>
    public IReadOnlyDictionary<string, object?> ScopeValues { get; init; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

/// <summary>原记录已锁定、手写规则及关系校验完成后，在实际 Mapper 写入前复核固定安全范围。</summary>
public sealed record RecordMutationValidation(string PageId, string TableName, RecordMutationOperation Operation,
    long RecordId, RuntimeActor Actor, IReadOnlyDictionary<string, object?> OriginalScopeValues,
    IReadOnlyDictionary<string, object?> ScopeValues, IReadOnlySet<string> SubmittedFields, DbTransaction Transaction);

/// <summary>宿主可在首次记录锁之前取得聚合范围锁，未注册守卫的普通应用保持原写入流程。</summary>
public interface IRecordMutationGuard
{
    /// <summary>与写入共用事务，失败必须回滚整个操作，不能在事务外预先加锁。</summary>
    Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken cancellationToken = default);
    /// <summary>默认空实现保护现有宿主；安全协调器可复核准备后未变化的归属和锁后权限。</summary>
    Task BeforeWriteAsync(RecordMutationValidation validation, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
