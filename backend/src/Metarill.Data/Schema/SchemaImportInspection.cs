using System.Data;
using System.Data.Common;
using Metarill.Core.Metadata;

namespace Metarill.Data.Schema;

/// <summary>位置和原因均来自适配器；不会将未表达的结构特征静默转换为普通列或索引。</summary>
public sealed record SchemaImportIssue(string Table, string? Member, string Code, string Message);
public sealed record SchemaImportInspection(SchemaInspectionSnapshot Snapshot, IReadOnlyList<SchemaImportIssue> Issues, bool CaseSensitiveNames = true, DatabaseTargetIdentity? Target = null)
{
    /// <summary>只读预览中按实际物理名称记录的视图，不能作为可保存实体使用。</summary>
    public IReadOnlySet<string> Views { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed partial class SchemaService
{
    /// <summary>检查明确选中的物理表，不要求它已登记；名称与预算先校验，不执行DDL、业务数据或元数据写入。</summary>
    public Task<SchemaImportInspection> InspectForImportAsync(IReadOnlyList<string> names, CancellationToken token = default) =>
        WithImportInspectionAsync(names, (inspection, _) => Task.FromResult(inspection), token);

    /// <summary>同一事务持有每张源表的元数据锁，检查及调用者复核结束前不释放；回调不获得数据库连接。</summary>
    public Task<T> WithImportInspectionAsync<T>(IReadOnlyList<string> names, Func<SchemaImportInspection, CancellationToken, Task<T>> inspect, CancellationToken token = default) =>
        WithImportInspectionCoreAsync(names, inspect, allowViewPreview: false, token);

    /// <summary>反向逻辑草案可预览视图列；审核与保存仍复用实体专用入口。</summary>
    public Task<T> WithImportPreviewAsync<T>(IReadOnlyList<string> names, Func<SchemaImportInspection, CancellationToken, Task<T>> inspect, CancellationToken token = default) =>
        WithImportInspectionCoreAsync(names, inspect, allowViewPreview: true, token);

    private async Task<T> WithImportInspectionCoreAsync<T>(IReadOnlyList<string> names,
        Func<SchemaImportInspection, CancellationToken, Task<T>> inspect, bool allowViewPreview, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(names); ArgumentNullException.ThrowIfNull(inspect);
        if (names.Count is < 1 or > 16 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new ArgumentException("反向结构检查要求1至16个不重复的物理表名。");
        foreach (var name in names) MetadataValidation.RequireIdentifier(name);
        EnsureMySql();
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync(token);
        var identity = await ValidateTargetAsync(connection, token);
        // 结构反查拥有独立MDL事务；只观察原API边界，不加入业务session或改变隔离级别。
        var beginObservation=DatabaseCommandObservation.StartTransaction(connection,null,DatabaseTransactionOperation.Begin);
        DbTransaction transaction;
        try{transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,token);beginObservation?.Complete(transaction,null);}
        catch(Exception error){beginObservation?.Complete(null,error);throw;}
        await using var ownedTransaction=transaction;
        try
        {
            var caseMode = Convert.ToInt32(await ScalarInTransactionAsync(connection, transaction, "SELECT @@lower_case_table_names", token));
            if (caseMode is < 0 or > 2) throw new InvalidOperationException("无法核实数据库名称比较规则。");
            var comparison = caseMode == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var physicalNames = new List<string>(names.Count);
            var views = new List<TableSchema>();
            var total = 0L;
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                var kind = Convert.ToString(await ScalarInTransactionAsync(connection, transaction,
                    "SELECT TABLE_TYPE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table", token, ("@table", name)));
                if (allowViewPreview && kind == "VIEW")
                {
                    // 视图预览只使用目录结果，不执行 LIMIT 0，以免展开视图或依赖其运行权限。
                    var view = await ReadViewFromCatalogAsync(connection, transaction, name, token)
                        ?? throw new InvalidOperationException("源视图在检查期间缺失。");
                    MetadataValidation.RequireIdentifier(view.Name);
                    views.Add(view);
                    total += view.Columns.Count;
                    if (total > 4096) throw new InvalidOperationException("源结构超过本轮4096列预算，未返回截断模型。");
                    continue;
                }
                if (kind != "BASE TABLE") throw new InvalidOperationException("反向审核和保存只接受存在的实体表，视图只能预览列投影。");
                // SHOW CREATE的MDL只保留到语句结束；固定LIMIT 0查询不读取业务行，显式事务持有表级MDL。
                await using var command = session.Provider.CreateCommand(connection); command.Transaction = transaction; command.CommandTimeout = 15;
                command.CommandText = "SELECT 1 FROM " + MySqlSchemaPlanner.Quote(name) + " LIMIT 0";
                await using (var reader = await command.ExecuteReaderAsync(token)) { while (await reader.ReadAsync(token)) { } }
                // 取得 MDL 后以 MySQL 实际表名编译快照，避免将大小写别名保存到配置。
                command.CommandText = "SELECT TABLE_NAME,COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table GROUP BY TABLE_NAME";
                AddParameters(command, [("@table", name)]);
                await using var layout = await command.ExecuteReaderAsync(token);
                if (!await layout.ReadAsync(token)) throw new InvalidOperationException("源表在检查期间缺失。");
                var physicalName = layout.GetString(0);
                var count = layout.GetInt64(1);
                if (!string.Equals(name, physicalName, comparison) || await layout.ReadAsync(token))
                    throw new InvalidOperationException("无法唯一核实源表的物理名称。");
                MetadataValidation.RequireIdentifier(physicalName);
                physicalNames.Add(physicalName);
                total += count;
                if (count is < 1 or > 512 || total > 4096) throw new InvalidOperationException("源结构超过每表512列或本轮4096列预算，未返回截断模型。");
            }
            var schema = physicalNames.Count == 0
                ? new DatabaseSchema(session.Options.ExpectedDatabase, [])
                : await ReadSchemaAsync(connection, physicalNames, token, transaction);
            if (schema.Tables.Count != physicalNames.Count) throw new InvalidOperationException("源表在检查期间缺失。");
            if (views.Count > 0)
                schema = schema with { Tables = schema.Tables.Concat(views).OrderBy(table => table.Name, StringComparer.Ordinal).ToArray() };
            if (schema.Tables.Sum(table => (long)(table.SnapshotDefinition?.Length ?? 0)) > 2 * 1024 * 1024)
                throw new InvalidOperationException("源DDL快照超过2 MiB预算，未生成截断模型。");
            var issues = new List<SchemaImportIssue>();
            foreach (var name in physicalNames.Order(StringComparer.Ordinal)) await ReadImportFeaturesAsync(connection, transaction, name, issues, token);
            var inspection = new SchemaImportInspection(new(session.Provider.Name, schema, MySqlSchemaPlanner.Fingerprint(schema)),
                issues.Distinct().ToArray(), caseMode == 0, TargetIdentity(identity, caseMode))
            {
                Views = views.Select(view => view.Name).ToHashSet(StringComparer.Ordinal)
            };
            var result = await inspect(inspection, token);
            var commitObservation=DatabaseCommandObservation.StartTransaction(connection,transaction,DatabaseTransactionOperation.Commit);
            try{await transaction.CommitAsync(token);commitObservation?.Complete(transaction,null);}
            catch(Exception error){commitObservation?.Complete(transaction,error);throw;}
            return result;
        }
        catch
        {
            // 保持原取消后回滚及原异常优先级；采集不重试、不替换事务。
            var rollbackObservation=DatabaseCommandObservation.StartTransaction(connection,transaction,DatabaseTransactionOperation.Rollback);
            try{await transaction.RollbackAsync(CancellationToken.None);rollbackObservation?.Complete(transaction,null);}
            catch(Exception error){rollbackObservation?.Complete(transaction,error);throw;}
            throw;
        }
    }

    /// <summary>普通快照未表达前缀/隐藏/非BTREE索引与外键、CHECK；逐项列缺口，不伪装成普通索引。</summary>
    private async Task ReadImportFeaturesAsync(DbConnection connection, DbTransaction transaction, string table, List<SchemaImportIssue> issues, CancellationToken token)
    {
        await using var command = session.Provider.CreateCommand(connection); command.Transaction = transaction;
        command.CommandText = "SELECT INDEX_NAME,SUB_PART,COLLATION,INDEX_TYPE,IS_VISIBLE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table ORDER BY INDEX_NAME,SEQ_IN_INDEX";
        AddParameters(command, [("@table", table)]);
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
            {
                var unsupported = !reader.IsDBNull(1) || !reader.IsDBNull(2) && (reader.GetString(2) is not("A" or "D") || reader.GetString(0)=="PRIMARY"&&reader.GetString(2)=="D") || reader.GetString(3) != "BTREE" || reader.GetString(4) != "YES";
                if (unsupported) issues.Add(new(table, reader.GetString(0), "INDEX_FEATURE_UNSUPPORTED", "前缀、隐藏、降序主键、未知方向或非BTREE索引尚不能无损映射。"));
            }
        command.Parameters.Clear();
        command.CommandText = "SELECT CONSTRAINT_NAME,CONSTRAINT_TYPE FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND CONSTRAINT_TYPE NOT IN ('PRIMARY KEY','UNIQUE') ORDER BY CONSTRAINT_NAME";
        AddParameters(command, [("@table", table)]);
        await using var constraints = await command.ExecuteReaderAsync(token);
        while (await constraints.ReadAsync(token)) issues.Add(new(table, constraints.GetString(0), "CONSTRAINT_UNSUPPORTED", "当前逻辑元数据尚未表达该约束：" + constraints.GetString(1)));
    }
}
