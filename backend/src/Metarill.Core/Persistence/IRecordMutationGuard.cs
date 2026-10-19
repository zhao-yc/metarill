using System.Data.Common;
using Metarill.Core.Security;

namespace Metarill.Core.Persistence;

/// <summary>准备阶段发生于业务记录锁和写入前；导入的外层事务也必须先准备。</summary>
public enum RecordMutationOperation { Create, Update, Delete, Copy, Import }

/// <summary>准备守卫只接收服务端身份、固定生成页面及当前实际事务，不接收客户端实体或 SQL。</summary>
public sealed record RecordMutationPreparation(string PageId, string TableName, RecordMutationOperation Operation, RuntimeActor Actor, DbTransaction Transaction);

/// <summary>宿主可在首次记录锁之前取得聚合范围锁，未注册守卫的普通应用保持原写入流程。</summary>
public interface IRecordMutationGuard
{
    /// <summary>与写入共用事务，失败必须回滚整个操作，不能在事务外预先加锁。</summary>
    Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken cancellationToken = default);
}
