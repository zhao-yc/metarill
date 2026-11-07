using System.Data.Common;
using System.Globalization;
using System.Text;
using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using MySqlConnector;

namespace Metarill.Data.Relations;

public sealed partial class RelationMaintenanceExecutor
{
    /// <summary>仅在已证明无插入与更新副作用的来源表，将缓存作为原 INSERT 的一列写入。</summary>
    private async Task<bool> TryInsertInlinePackedScalarAsync<T>(MaintenanceTable table, IReadOnlyList<(int Row, long Id, T Entity)> rows,
        DbTransaction transaction, MySqlExactParentCandidate.BatchRecheck parent, long packetLimit, string field,
        string parentField, MySqlNonRedundantReadPlan plan, MySqlScalarCacheTarget target, string parentExpression, CancellationToken token) where T : class
    {
        var database = Q(session.Options.ExpectedDatabase);
        var source = database + "." + Q(table.Option.TableName);
        // 投影的原外键表达式引用已落库的 b 行。这里仅用已证明的规范父键替换这一处来源。
        var foreign = "b." + Q(target.ForeignColumn);
        var predicate = target.Predicate.Replace(foreign, "@inline_parent_fk", StringComparison.Ordinal);
        if (predicate == target.Predicate || predicate.Contains("b.", StringComparison.Ordinal))
            return false;
        var fieldType = plan.PhysicalReferences.First(reference =>
            reference.Table == table.Option.TableName && reference.Column.Name == field).Column.StoreType;
        var foreignType = plan.PhysicalReferences.FirstOrDefault(reference =>
            reference.Table == table.Option.TableName && reference.Column.Name == target.ForeignColumn)?.Column.StoreType;
        if (!int.TryParse(fieldType.AsSpan(8, fieldType.Length - 9), NumberStyles.None, CultureInfo.InvariantCulture, out var width))
            return false;
        object? foreignValue = foreignType == "bigint" ? parent.ParentId :
            foreignType?.StartsWith("varchar(", StringComparison.Ordinal) == true ? parent.ParentId.ToString(CultureInfo.InvariantCulture) : null;
        if (foreignValue is null || !await MySqlInlineCacheProof.VerifyAsync(session, (MySqlConnection)transaction.Connection!,
            transaction, table.Option.TableName, field, parent.Table, parentField, token))
            return false;

        // 同一父锁下读实际值。值超出缓存列宽度时，让原两阶段路径保留数据库约束与报错行为。
        var value = $"(SELECT {target.Alias}.{Q(parentField)} FROM {database}.{Q(target.Table)} {target.Alias} WHERE {predicate} FOR SHARE OF {target.Alias})";
        await using var parentRead = Command(transaction);
        Add(parentRead, "expected_database", session.Options.ExpectedDatabase);
        foreach (var parameter in parent.Parameters)
            Add(parentRead, parameter.Key, parameter.Value);
        Add(parentRead, "inline_parent_fk", foreignValue);
        parentRead.CommandText = $"SELECT CASE WHEN ({parentExpression})=2 THEN CONVERT({value} USING utf8mb4) END";
        var current = await parentRead.ExecuteScalarAsync(token);
        if (current is string actual && actual.EnumerateRunes().Count() > width)
            return false;
        if (current is not (string or DBNull))
            return false;

        var prefix = session.GetPackedAppendVariablePrefix(transaction);
        var state = prefix + "s";
        var verdict = prefix + "v";
        var inserted = prefix + "i";
        const string expected = "@inline_expected";
        var storedId = prefix + "d";
        var stored = prefix + "c";
        var reset = $"{verdict}=NULL,{inserted}=NULL,{storedId}=NULL,{stored}=NULL";
        var sql = new StringBuilder("/* metarill:inline-packed-append */ ").Append(ReadConnectionIdentity)
            .Append($"; SET {state}=2,{reset}");

        await using var command = Command(transaction);
        Add(command, "expected_database", session.Options.ExpectedDatabase);
        foreach (var parameter in parent.Parameters)
            Add(command, parameter.Key, parameter.Value);
        Add(command, "inline_expected", current);
        var repeated = command.Parameters.Cast<DbParameter>().ToList();
        long insertBudget = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowPrefix = "mi" + index + "_";
            var insert = MySqlInsertCacheBatch.Prepare(session, table.Option.TableName, row.Id, row.Entity, rowPrefix,
                (target.ForeignColumn, parent.ParentId), (field, expected, table.Deleted));
            if (insert is null)
                return false;
            insertBudget += insert.ParameterBudget;
            foreach (var parameter in insert.Parameters)
                command.Parameters.Add(parameter);
            var id = rowPrefix + "exact_id";
            Add(command, id, row.Id);
            repeated.Add(command.Parameters[command.Parameters.Count - 1]);
            var where = $" WHERE {Scope(table, "b.")} AND b.{Q("id")}=CASE WHEN {MySqlExactParentCandidate.BatchRecheck.DatabaseGate} AND {verdict}=2 THEN @{id} END";

            // 仍按行检查原父候选。数据库拒绝或读回不符时，后续语句不能访问对应业务行。
            sql.Append($"; SELECT CASE WHEN {state}=2 THEN ({parentExpression}) ELSE {state} END,NULL,NULL INTO {verdict},{storedId},{stored}")
                .Append($"; {insert.Sql} AND {verdict}=2; SET {inserted}=ROW_COUNT()")
                .Append($"; SELECT b.{Q("id")},CONVERT(b.{Q(field)} USING utf8mb4) FROM {source} b{where} FOR UPDATE OF b INTO {storedId},{stored}")
                .Append($"; SET {state}=CASE WHEN {verdict}<>2 THEN {verdict} WHEN {inserted}=1 AND ({storedId}<=>@{id}) ")
                .Append($"AND (CAST({stored} AS BINARY)<=>CAST({expected} AS BINARY)) THEN 2 ELSE 5 END")
                .Append($"; SELECT CAST({state} AS SIGNED),CAST({verdict} AS SIGNED),CAST({inserted} AS SIGNED),")
                .Append($"CAST({storedId} AS SIGNED),CONVERT({expected} USING utf8mb4),CONVERT({stored} USING utf8mb4)");
        }
        sql.Append($"; SET {state}=NULL,{reset}");
        command.CommandText = sql.ToString();
        if (!FitsPackedCommand(command, packetLimit, insertBudget, repeated))
            return false;

        var rowNumber = rows[0].Row;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(token);
            _ = await RequireConnectionIdentityAsync(reader, token);
            foreach (var row in rows)
            {
                rowNumber = row.Row;
                await ReadPackedResultAsync(reader, token);
                MySqlExactParentCandidate.RequireCandidate(checked((int)reader.GetInt64(1)));
                if (reader.GetInt64(2) != 1)
                    throw new InvalidOperationException("新增失败。");
                var expectedValue = reader.IsDBNull(4) ? null : reader.GetString(4);
                var actualValue = reader.IsDBNull(5) ? null : reader.GetString(5);
                if (reader.GetInt64(0) != 2 || reader.IsDBNull(3) || reader.GetInt64(3) != row.Id ||
                    !string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
                    throw new InvalidOperationException("关系缓存在存储后不再匹配，当前事务未提交。");
                if (await reader.ReadAsync(token))
                    throw new InvalidOperationException("追加写入返回了额外结果行。");
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
}
