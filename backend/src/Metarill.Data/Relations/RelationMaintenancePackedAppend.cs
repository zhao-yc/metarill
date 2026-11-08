using System.Data.Common;
using System.Text;
using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using MySqlConnector;

namespace Metarill.Data.Relations;

public sealed partial class RelationMaintenanceExecutor
{
    internal const int PackedAppendLimit = 7;

    /// <summary>只合并已固定结构、同一父候选的连续追加行。false 保证没有发送任何写入，异常由原事务整批回滚。</summary>
    internal async Task<bool> TryInsertPackedScalarAsync<T>(
        MaintenanceTable table, string project, IReadOnlyList<(int Row, long Id, T Entity)> rows,
        DbTransaction transaction, MySqlExactParentCandidate.BatchRecheck parent, CancellationToken token) where T : class
    {
        if (rows.Count is < 2 or > PackedAppendLimit || !ReferenceEquals(transaction, parent.Transaction) ||
            transaction.Connection is not MySqlConnection connection || !session.HasScalarCacheWriteProof(transaction) ||
            !session.UsesOwnedOrm || rows.Any(row => row.Row <= 0 || row.Id <= 0))
            return false;
        var options = new MySqlConnectionStringBuilder(connection.ConnectionString);
        if (!options.AllowUserVariables || !options.ConnectionReset)
            return false;
        if (table.Read is not {} plan || session.PinnedRelationLayouts(transaction)?.GetValueOrDefault(plan) is not long packetLimit || packetLimit <= 0)
            return false;
        if (project != plan.ProjectId || !CanLockExactScalarSource(table) || plan.Constants.Count != 0 ||
            plan.ScalarCacheTarget is not {} target || parent.Table != target.Table || !CanLookAhead(table, plan) ||
            plan.ImportMatchLock != " FOR UPDATE OF b FOR SHARE OF " + target.Alias)
            return false;

        // 只有同一事务已核验父表的单列物理主键，才把存在性与候选判断合并为一次当前读。
        var parentExpression = plan.PhysicalReferences.Any(item => item.Table == parent.Table &&
            item.Column.Name == "id" && item.Column.PrimaryKey) ? parent.PinnedPrimaryKeyExpression : parent.Expression;
        var database = Q(session.Options.ExpectedDatabase);
        if (plan.QualifiedMaintenanceFrom(database) is not {} from)
            return false;
        var field = table.Fields[0].Name;
        var output = plan.OutputExpression(field);
        if (!plan.Origins.TryGetValue(field, out var origin) || origin.Table != target.Table ||
            output != target.Alias + "." + Q(origin.Column))
            return false;

        var value = $"(SELECT {output} FROM {database}.{Q(target.Table)} {target.Alias} WHERE {target.Predicate} FOR SHARE OF {target.Alias})";
        // 无法证明插入和更新等价时，保留下面原有的逐行缓存更新。
        if (await TryInsertInlinePackedScalarAsync(table, rows, transaction, parent, packetLimit,
            field, origin.Column, plan, target, parentExpression, token))
            return true;

        await using var command = Command(transaction);
        Add(command, "expected_database", session.Options.ExpectedDatabase);
        foreach (var parameter in parent.Parameters)
            Add(command, parameter.Key, parameter.Value);
        var repeatedParameters = new List<DbParameter>(command.Parameters.Count + rows.Count);
        foreach (DbParameter parameter in command.Parameters)
            repeatedParameters.Add(parameter);
        long insertParameterBudget = 0;

        var sql = new StringBuilder("/* metarill:packed-append */ ").Append(ReadConnectionIdentity);
        // 独立会话变量名防止与宿主已有变量撞名。驱动只绑定参数，变量在借用连接结束时也由重置清除。
        var prefix = session.GetPackedAppendVariablePrefix(transaction);
        var state = prefix + "s";
        var verdict = prefix + "v";
        var inserted = prefix + "i";
        var readId = prefix + "r";
        var before = prefix + "b";
        var expected = prefix + "e";
        var affected = prefix + "a";
        var storedId = prefix + "d";
        var stored = prefix + "c";
        var reset = $"{verdict}=NULL,{inserted}=NULL,{readId}=NULL,{before}=NULL,{expected}=NULL,{affected}=NULL,{storedId}=NULL,{stored}=NULL";
        sql.Append($"; SET {state}=2,{reset}");

        var source = database + "." + Q(table.Option.TableName);
        var cached = "b." + Q(field);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowPrefix = "mp" + index + "_";
            var insert = MySqlInsertCacheBatch.Prepare(session, table.Option.TableName, row.Id, row.Entity, rowPrefix, (target.ForeignColumn, parent.ParentId));
            if (insert is null)
                return false;
            // Prepare 已证明每个带本行前缀的插入参数仅在 VALUES 对应位置绑定一次。
            insertParameterBudget += insert.ParameterBudget;
            foreach (var parameter in insert.Parameters)
                command.Parameters.Add(parameter);
            var id = rowPrefix + "exact_id";
            Add(command, id, row.Id);
            repeatedParameters.Add(command.Parameters[command.Parameters.Count - 1]);
            var sourceMatch = $"{Scope(table, "b.")} AND b.`id`=CASE WHEN {MySqlExactParentCandidate.BatchRecheck.DatabaseGate} AND {verdict}=2 THEN @{id} END";
            var where = " WHERE " + sourceMatch;
            // 首错状态只由上一行的最终核验更新。本行候选一次返回，同时清除上一行读回值。
            sql.Append($"; SELECT CASE WHEN {state}=2 THEN ({parentExpression}) ELSE {state} END,NULL,NULL,NULL,NULL,NULL")
                .Append($" INTO {verdict},{readId},{before},{expected},{storedId},{stored}")
                .Append($"; {insert.Sql} AND {verdict}=2; SET {inserted}=ROW_COUNT()")
                .Append($"; SELECT b.`id`,CONVERT({cached} USING utf8mb4),CONVERT({output} USING utf8mb4){from}{where}")
                .Append($" ORDER BY b.`id` LIMIT 2{plan.ImportMatchLock} INTO {readId},{before},{expected}")
                // 更新前把预期送入独立结果，驱动读回时不信任写入之后的会话变量。
                .Append($"; SELECT CAST({readId} AS SIGNED),CONVERT({before} USING utf8mb4),CONVERT({expected} USING utf8mb4)")
                .Append($"; UPDATE {source} b SET {Q(field)}={value}{where} AND {inserted}=1 AND b.`id`={readId}")
                // 两值已在上一语句锁住源行和目标行后读出，中间只有结果输出。赋值仍由原目标列完成。
                .Append($" AND NOT (CAST({before} AS BINARY) <=> CAST({expected} AS BINARY))")
                // 更新后同一次锁读取计数、物理值和状态。固定一行保留来源不可见时的 NULL 回执。
                .Append($"; SELECT ROW_COUNT(),b.`id`,CONVERT({cached} USING utf8mb4),")
                .Append($"CASE WHEN {verdict}<>2 THEN {verdict} ")
                .Append($"WHEN {inserted}=1 AND {readId} IS NULL AND b.`id` IS NULL AND ROW_COUNT()=0 THEN 2 ")
                .Append($"WHEN {inserted}=1 AND ({readId}<=>@{id}) AND (b.`id`<=>@{id}) ")
                .Append($"AND ROW_COUNT()=IF(CAST({before} AS BINARY)<=>CAST({expected} AS BINARY),0,1) ")
                .Append($"AND (CAST(CONVERT({cached} USING utf8mb4) AS BINARY)<=>CAST({expected} AS BINARY)) THEN 2 ELSE 5 END")
                .Append($" FROM (SELECT 1) receipt LEFT JOIN {source} b ON {sourceMatch}")
                .Append($" FOR UPDATE OF b INTO {affected},{storedId},{stored},{state}")
                .Append($"; SELECT CAST({state} AS SIGNED),CAST({verdict} AS SIGNED),CAST({inserted} AS SIGNED),")
                .Append($"CAST({affected} AS SIGNED),CAST({storedId} AS SIGNED),CONVERT({stored} USING utf8mb4)");
        }
        sql.Append($"; SET {state}=NULL,{reset}");
        command.CommandText = sql.ToString();
        if (!FitsPackedCommand(command, packetLimit, insertParameterBudget, repeatedParameters))
            return false;

        var rowNumber = rows[0].Row;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(token);
            _ = await RequireConnectionIdentityAsync(reader, token);
            foreach (var row in rows)
            {
                rowNumber = row.Row;
                await RequirePackedRowAsync(reader, row.Id, token);
            }
            if (await reader.NextResultAsync(token))
                throw new InvalidOperationException("追加写入返回了额外结果。");
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException and not UnauthorizedAccessException and not System.Security.SecurityException)
        {
            throw PageImportException.Execution(rowNumber, error);
        }
    }

    /// <summary>MySQL 文本命令会重复替换每次参数引用，预算覆盖每次绑定，超限时原路径接管。</summary>
    internal static bool FitsPackedCommand(DbCommand command, long packetLimit, long insertParameterBudget,
        IReadOnlyList<DbParameter> repeatedParameters)
    {
        // 保持完整 SQL 与参数证据可由原观察器核对，超过边界按逐行命令处理。
        if (command.CommandText.Length > DatabaseCommandObservation.MaximumSqlLength ||
            command.Parameters.Count > DatabaseCommandObservation.MaximumParameters)
            return false;
        var limit = Math.Min(packetLimit, 256 * 1024);
        var bytes = Encoding.UTF8.GetByteCount(command.CommandText) + 128 + insertParameterBudget;
        if (bytes > limit)
            return false;
        foreach (var parameter in repeatedParameters)
        {
            var parameterBytes = MySqlInsertCacheBatch.ParameterBytes(parameter.Value);
            if (parameterBytes < 0)
                return false;
            var references = command.CommandText.AsSpan().Count("@" + parameter.ParameterName.TrimStart('@'));
            bytes += parameterBytes * references;
            if (bytes > limit)
                return false;
        }
        return bytes <= limit;
    }

    /// <summary>按原顺序独立验证父状态、写入计数与物理读回，任何不一致都带当前 Excel 行号回滚。</summary>
    private static async Task RequirePackedRowAsync(DbDataReader reader, long id, CancellationToken token)
    {
        await ReadPackedResultAsync(reader, token);
        var source = reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0);
        var before = reader.IsDBNull(1) ? null : reader.GetString(1);
        var expected = reader.IsDBNull(2) ? null : reader.GetString(2);
        await ReadPackedResultAsync(reader, token);
        var state = reader.GetInt64(0);
        MySqlExactParentCandidate.RequireCandidate(checked((int)reader.GetInt64(1)));
        if (reader.GetInt64(2) != 1)
            throw new InvalidOperationException("新增失败。");
        var actualId = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4);
        var actual = reader.IsDBNull(5) ? null : reader.GetString(5);
        var changed = source.HasValue && !string.Equals(before, expected, StringComparison.Ordinal);
        if (state != 2 || source.HasValue && source.Value != id || actualId != source ||
            reader.GetInt64(3) != (changed ? 1 : 0) || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("关系缓存在存储后不再匹配，当前事务未提交。");
        if (await reader.ReadAsync(token))
            throw new InvalidOperationException("追加写入返回了额外行。");
    }

    /// <summary>每个结果必须返回唯一证据行，不能将下一语句的结果误认为当前行。</summary>
    private static async Task ReadPackedResultAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.ReadAsync(token) || !await reader.NextResultAsync(token) || !await reader.ReadAsync(token))
            throw new InvalidOperationException("追加写入没有返回唯一的核验结果。");
    }
}
