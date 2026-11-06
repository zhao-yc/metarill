using System.Text.Json;

namespace Metarill.Generation;

/// <summary>固定父追加仅缓冲没有逐行宿主副作用的页面，所有回退复用原写入后半段。</summary>
internal static class PackedAppendRenderer
{
    private const string Propagator = "global::Metarill.Data.Relations.ProjectRelationMutationPropagator";
    private const string Parent = "global::Metarill.Data.Queries.MySqlExactParentCandidate.BatchRecheck";

    /// <summary>没有手写扩展，且全部守卫及观察者都声明当前页面可以安全缓冲时才接管。</summary>
    internal static string ImportSetup(GenerationModel model, TableModel table, PageModel page, RelationModel relation) => $$"""
        bool MayPackRows() => parentRecheck is not null && request.RequestJson.GetArrayLength() >= 3 &&
            guards.All(guard => guard.CanBufferScalarAppend({{JsonSerializer.Serialize(table.Source.TableName)}})) &&
            observers.All(observer => observer.CanBufferScalarAppend({{JsonSerializer.Serialize(page.Source.Id)}}, {{JsonSerializer.Serialize(table.Source.TableName)}}));
        var hasSharedProof = session.Options.MySqlSharedAppendProofs?.ContainsKey({{JsonSerializer.Serialize(table.Source.TableName)}}) == true;
        if (!hasSharedProof && MayPackRows())
            await new {{Propagator}}(session).PreparePackedAppendLayoutAsync(
                global::{{model.Namespace}}.Generated.GeneratedRelationMaintenance.Catalog, {{JsonSerializer.Serialize(table.Source.TableName)}}, actor.ProjectId,
                {{JsonSerializer.Serialize(relation.Target.Source.TableName)}}, request.ParentId, cancellationToken);
        var sharedParentLocked = hasSharedProof &&
            MayPackRows() && await new {{Propagator}}(session).TryLockSharedAppendParentAsync(
                global::{{model.Namespace}}.Generated.GeneratedRelationMaintenance.Catalog, {{JsonSerializer.Serialize(table.Source.TableName)}}, actor.ProjectId,
                {{JsonSerializer.Serialize(relation.Target.Source.TableName)}}, request.ParentId,
                {{(relation.Target.Columns.Any(column => column.Source.ColumnName == "delete_no") ? "true" : "false")}}, parentRecheck!, cancellationToken);
        if (!sharedParentLocked) await ValidateParents{{page.Source.Id}}Async(request.ParentId, actor, cancellationToken);
        var canPackRows = sharedParentLocked || MayPackRows();
        var pendingRows = canPackRows ? new List<(int Row, long Id, {{table.EntityType}} Entity, {{Parent}} Parent, Func<Task> Persist)>({{Propagator}}.PackedAppendLimit) : null;

""" + "\n";

    /// <summary>先处理前序已验证行，再报告后续行本地错误；发出命令后禁止回放。</summary>
    internal static string ImportFunctions(GenerationModel model, TableModel table) => $$"""

        void QueuePackedRow({{table.EntityType}} entity, {{Parent}} parent, Func<Task> persist)
        {
            pendingRows!.Add((rowNumber, entity.Id, entity, parent, persist));
        }

        async Task FlushPackedRowsAsync()
        {
            if (pendingRows is not { Count: > 0 }) return;
            var rows = pendingRows.ToArray();
            pendingRows.Clear();
            await PersistPackedChunkAsync(rows);
        }

        async Task PersistPackedChunkAsync((int Row, long Id, {{table.EntityType}} Entity, {{Parent}} Parent, Func<Task> Persist)[] rows)
        {
            if (rows.Length > 1 && await new {{Propagator}}(session).TryInsertPackedScalarSourceAsync(
                global::{{model.Namespace}}.Generated.GeneratedRelationMaintenance.Catalog, {{JsonSerializer.Serialize(table.Source.TableName)}}, actor.ProjectId,
                rows.Select(row => (row.Row, row.Id, row.Entity)).ToArray(), rows[0].Parent, cancellationToken))
                return;
            // 接管失败保证本组从未发送。先按行序试更短的命令，再保留逐行回退。
            if (rows.Length > 4)
            {
                await PersistPackedChunkAsync(rows[..4]);
                await PersistPackedChunkAsync(rows[4..]);
                return;
            }
            foreach (var row in rows)
            {
                try { await row.Persist(); }
                catch (Exception error)
                {
                    var failure = ImportFailure(error, row.Row);
                    if (ReferenceEquals(failure, error)) throw;
                    throw failure;
                }
            }
        }

        static Exception ImportFailure(Exception error, int row)
        {
            if (error is PageImportException { IsInputError: true } input) return input.AtRow(row);
            if (error is PageImportException { RowNumber: not null } or OperationCanceledException or UnauthorizedAccessException or System.Security.SecurityException or BusinessRuleException)
                return error;
            return PageImportException.Execution(row, error);
        }

""" + "\n";

    internal static string CreateArguments(TableModel table) =>
        $", Action<{table.EntityType}, {Parent}, Func<Task>>? queuePackedRow = null, Func<Task>? flushEarlierRows = null, bool reuseInitialParentLock = false";

    /// <summary>准备逻辑与普通新增相同，缓冲只延迟持久化及已证明无副作用的通知。</summary>
    internal static string CreateBuffer(PageModel page) => $$"""
        if (queuePackedRow is not null && deferredParent is not null)
        {
            queuePackedRow(entity, deferredParent, PersistAsync);
            return null!; // Excel 只使用整批回执。
        }
        return await PersistAsync();

        async Task<{{page.Name}}Vo> PersistAsync()
        {
""" + "\n";

    internal const string FlushAfterRow = "                if (pendingRows?.Count == " + Propagator + ".PackedAppendLimit) await FlushPackedRowsAsync();\n";
}
