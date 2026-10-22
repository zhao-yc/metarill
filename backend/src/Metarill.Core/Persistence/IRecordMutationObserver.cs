using System.Data.Common;
namespace Metarill.Core.Persistence;

/// <summary>标准变更在提交前通知基础设施，引用写入必须使用同一个数据库事务。</summary>
public sealed record RecordMutationContext(string PageId,string TableName,long RecordId,string ProjectId,
    IReadOnlyDictionary<string,object?> SubmittedFields,bool IsDelete,DbTransaction Transaction,bool IsCreate=false)
{
    /// <summary>仅由服务端生成入口设置原操作；不增加HTTP字段，也不改变既有构造函数签名。</summary>
    public RecordMutationOperation? AuthorizationOperation { get; init; }
}
public interface IRecordMutationObserver
{
    /// <summary>物理删除前、原范围与业务规则已通过时准备基础设施证明；默认空实现兼容既有观察者。</summary>
    Task BeforePhysicalDeleteAsync(RecordMutationContext context,CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken = default);
}
