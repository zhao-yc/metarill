using Metarill.Core.Persistence;
using Metarill.Core.Security;

namespace Metarill.Runtime.Files;

/// <summary>按照实际发布页面选择文件事务，不能把平台文件连接借给业务记录或跨库补写引用。</summary>
public sealed class ProjectFileReferenceMutationObserver(ProjectFileRuntimes runtimes, IRuntimeAuthorization authorization, IIdGenerator ids) : IRecordMutationObserver
{
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
        return new FileReferenceMutationObserver(runtime.Store, runtime.Metadata, authorization, ids, runtime.Options)
            .BeforeCommitAsync(context, cancellationToken);
    }
}
