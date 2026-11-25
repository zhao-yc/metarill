using Metarill.Core.Metadata;
using Metarill.Core.Persistence;

namespace Metarill.Runtime.Files;

public sealed partial class FileLibraryService
{
    /// <summary>修复已保存字段的引用。旧接口的标识仅用于核对已发布来源，不能据此更新业务字段。</summary>
    public Task SynchronizeReferencesAsync(FileReferenceSyncRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = authorization.RequireActor();
        var catalog = registry.Catalogs.SingleOrDefault(candidate => candidate.YzProjectId == actor.ProjectId)
            ?? throw new UnauthorizedAccessException("文件来源不属于当前项目。");
        var page = catalog.ConfigPages.SingleOrDefault(candidate => candidate.Id == request.SourceConfigPageId &&
            candidate.ConfigTableId == request.SourceConfigTableId && PageUpdatePolicy.CanUpdate(candidate))
            ?? throw new UnauthorizedAccessException("来源页面未开放该表修改。");
        var table = catalog.ConfigTables.Single(candidate => candidate.Id == page.ConfigTableId);
        var field = registry.PhysicalColumns(catalog, table).SingleOrDefault(column =>
            column.Id == request.SourceConfigColumnId && column.ControlKind == "FileLibrary");
        if (table.IsFunctionTable == 1 || table.TableName != request.SourceTableName || field is null || field.ColumnName != request.SourceColumnName ||
            !catalog.ConfigPageColumns.Any(link => link.ConfigPageId == page.Id && link.ConfigColumnId == field.Id && link.IsEdit == 1))
            throw new UnauthorizedAccessException("文件字段与已发布的来源表不一致。");
        authorization.RequirePagePermission(page.Id, "update");
        var recordId = FileReferenceScope.CanonicalId(request.SourceRecordId);
        var scope=registry.PageScope(catalog,page);
        var parentId=string.IsNullOrWhiteSpace(request.SourceParentId)?null:
            scope.Crosses.Any(parent=>parent.TextKey)?request.SourceParentId:
                FileReferenceScope.CanonicalId(request.SourceParentId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var values = FileValidation.ParseFileIds(request.FileIds);
        // 原引用观察器逐项核对已存值、文件可用性和权限，引用与来源锁全部使用同一会话。
        var observer = new FileReferenceMutationObserver(store, registry, authorization, ids, options);
        return store.Session.InTransactionAsync(async () =>
        {
            var context = new RecordMutationContext(page.Id, table.TableName, recordId, actor.ProjectId,
                new Dictionary<string, object?> { [field.ColumnName] = values }, false, store.Session.CurrentTransaction!)
                { AuthorizationOperation = RecordMutationOperation.Update };
            await observer.SynchronizeExistingAsync(context, parentId, token);
            return true;
        }, token);
    }
}
