using Metarill.Core.Persistence;
using System.Runtime.CompilerServices;
using Metarill.Core.Security;

namespace Metarill.Runtime.Files;

/// <summary>按照实际发布页面选择文件事务，不能把平台文件连接借给业务记录或跨库补写引用。</summary>
public sealed class ProjectFileReferenceMutationObserver(ProjectFileRuntimes runtimes, IRuntimeAuthorization authorization, IIdGenerator ids) : IRecordMutationObserver
{
    private readonly ConditionalWeakTable<ProjectFileRuntime,FileReferenceMutationObserver> observers=new();
    /// <summary>按实际发布槽复用观察器，删除前后证明不得因包装器临时new实例而丢失。</summary>
    private FileReferenceMutationObserver Observer(ProjectFileRuntime runtime)=>observers.GetValue(runtime,current=>new(current.Store,current.Metadata,authorization,ids,current.Options));
    /// <summary>准备与通知都走同一发布业务会话；内部观察器负责实际行、父范围及原事务证明。</summary>
    public Task BeforePhysicalDeleteAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
    {
        var (runtime,page)=runtimes.ResolvePage(context.PageId);return Observer(runtime).BeforePhysicalDeleteAsync(context,cancellationToken);
    }
    /// <summary>无文件字段的配置写入保持原逻辑，有文件变更才验证身份并委托同会话观察器。</summary>
    public Task BeforeCommitAsync(RecordMutationContext context, CancellationToken cancellationToken = default)
    {
        var (runtime, page) = runtimes.ResolvePage(context.PageId);
        var catalog = runtime.Metadata.Catalogs.Single();
        var table = catalog.ConfigTables.Single(candidate => candidate.Id == page.ConfigTableId);
        if (table.TableName != context.TableName) throw new UnauthorizedAccessException("来源页面与业务表不一致。");
        var affected = catalog.ConfigColumns.Any(column => column.ConfigTableId == table.Id && column.IsVirtualColumn == 0
            && column.ControlKind.Equals("FileLibrary", StringComparison.OrdinalIgnoreCase)
            && (context.IsDelete || context.SubmittedFields.ContainsKey(column.ColumnName)));
        if (!affected) return Task.CompletedTask;
        var actor = authorization.RequireActor();
        if (actor.ProjectId != context.ProjectId || catalog.YzProjectId != context.ProjectId)
            throw new UnauthorizedAccessException("文件引用项目范围无效。");
        return Observer(runtime).BeforeCommitAsync(context, cancellationToken);
    }
}
