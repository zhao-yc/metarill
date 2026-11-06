using System.Data.Common;
using MySqlConnector;

namespace Metarill.Data.Queries;

/// <summary>部署端只读证明只用于已核验的叶子追加。任何证据缺失均在取得行锁前回到原路径。</summary>
internal static class MySqlSharedAppendProof
{
    internal const string Contract = "metarill-shared-append-v1";

    /// <summary>在业务事务固定结构，在独立连接读取权限和触发器，避免提前建立业务一致性快照。</summary>
    internal static async Task<bool> VerifyAsync(DatabaseSession session, MySqlConnection connection, DbTransaction transaction,
        string source, string parent, MySqlSharedAppendProofOptions proof, CancellationToken token, string? expectedAccount = null)
    {
        if (source == parent || proof.ParentTable != parent || !Guid.TryParseExact(proof.ServerUuid, "D", out _) ||
            !IsIdentifier(proof.Schema) || !IsIdentifier(proof.Procedure) || string.IsNullOrWhiteSpace(proof.Definer))
            return false;

        try
        {
            if (!await MatchesInstanceAsync(connection, transaction, session.Options.ExpectedDatabase, proof.ServerUuid, expectedAccount, token))
                return false;
            var database = session.Provider.QuoteIdentifier(session.Options.ExpectedDatabase);
            var parentName = database + "." + session.Provider.QuoteIdentifier(parent);
            var sourceName = database + "." + session.Provider.QuoteIdentifier(source);

            await using (var pin = connection.CreateCommand())
            {
                pin.Transaction = (MySqlTransaction)transaction;
                // 零行当前读只固定表定义。证明返回后直到事务结束，触发器和父表外键不能发生 DDL 变化。
                pin.CommandText = $"SELECT `id` FROM {parentName} LIMIT 0 FOR SHARE; SELECT `id` FROM {sourceName} LIMIT 0 FOR SHARE";
                await pin.ExecuteNonQueryAsync(token);
            }

            await using var metadata = session.Provider.CreateConnection(session.Options);
            await metadata.OpenAsync(token);
            if (!await MatchesInstanceAsync(metadata, null, session.Options.ExpectedDatabase, proof.ServerUuid, expectedAccount, token))
                return false;
            if (!await VerifyLayoutAsync(metadata, parentName, sourceName, token) ||
                !await VerifyRoutineAsync(metadata, proof, token))
                return false;

            await using var call = metadata.CreateCommand();
            call.CommandText = $"CALL {session.Provider.QuoteIdentifier(proof.Schema)}.{session.Provider.QuoteIdentifier(proof.Procedure)}()";
            await using var reader = await call.ExecuteReaderAsync(token);
            return reader.FieldCount == 6 && await reader.ReadAsync(token) &&
                ReadText(reader, 0) == Contract && ReadText(reader, 1) == session.Options.ExpectedDatabase &&
                ReadText(reader, 2) == source && ReadText(reader, 3) == parent &&
                !reader.IsDBNull(4) && reader.GetInt64(4) == 1 &&
                !reader.IsDBNull(5) && reader.GetInt64(5) == 0 &&
                !await reader.ReadAsync(token) && !await reader.NextResultAsync(token);
        }
        catch (MySqlException error) when (error.Number is 1142 or 1146 or 1305 or 1318 or 1356 or 1370 or 1449)
        {
            // 缺对象、授权或版本不支持时仍走排他锁。并发、超时和连接失败由原事务处理。
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>实际实例及库名均须符合部署绑定。安装复核另核对两条连接实际命中的 MySQL 账号。</summary>
    private static async Task<bool> MatchesInstanceAsync(DbConnection connection, DbTransaction? transaction,
        string database, string serverUuid, string? expectedAccount, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedAccount is null ? "SELECT DATABASE(),@@server_uuid" : "SELECT DATABASE(),@@server_uuid,CURRENT_USER()";
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) && ReadText(reader, 0) == database &&
            string.Equals(ReadText(reader, 1), serverUuid, StringComparison.OrdinalIgnoreCase) &&
            (expectedAccount is null || string.Equals(ReadText(reader, 2), expectedAccount, StringComparison.Ordinal));
    }

    /// <summary>保守拒绝非 InnoDB、非单列主键及父表外键，防止明细缓存更新通过级联写回父表。</summary>
    private static async Task<bool> VerifyLayoutAsync(DbConnection connection, string parent, string source, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SHOW CREATE TABLE {parent}; SHOW CREATE TABLE {source}";
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || !ValidTable(ReadText(reader, 1), rejectForeignKeys: true) ||
            !await reader.NextResultAsync(token) || !await reader.ReadAsync(token))
            return false;
        return ValidTable(ReadText(reader, 1), rejectForeignKeys: false);

        // 保守识别 MySQL 输出的标准表定义；不同格式或无法辨认的引擎继续原排他锁。
        static bool ValidTable(string? ddl, bool rejectForeignKeys)
        {
            if (ddl is null || !ddl.StartsWith("CREATE TABLE `", StringComparison.Ordinal) ||
                !ddl.Contains("\n  PRIMARY KEY (`id`)", StringComparison.Ordinal) ||
                rejectForeignKeys && ddl.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
                return false;
            var options = ddl.LastIndexOf("\n) ENGINE=InnoDB", StringComparison.Ordinal);
            if (options < 0)
                return false;
            var end = options + "\n) ENGINE=InnoDB".Length;
            return end == ddl.Length || ddl[end] == ' ';
        }
    }

    /// <summary>普通账号可核验公开例程属性。正文与运维归属由安装时的管理员核验，不授全局读权限。</summary>
    private static async Task<bool> VerifyRoutineAsync(DbConnection connection, MySqlSharedAppendProofOptions proof,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DEFINER, SECURITY_TYPE, SQL_DATA_ACCESS, ROUTINE_COMMENT
            FROM information_schema.ROUTINES
            WHERE ROUTINE_SCHEMA=@schema AND ROUTINE_NAME=@routine AND ROUTINE_TYPE='PROCEDURE'
            """;
        Add(command, "schema", proof.Schema);
        Add(command, "routine", proof.Procedure);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) && ReadText(reader, 0) == proof.Definer &&
            ReadText(reader, 1) == "DEFINER" && ReadText(reader, 2) == "READS SQL DATA" &&
            ReadText(reader, 3) == Contract && !await reader.ReadAsync(token);
    }

    private static string? ReadText(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static bool IsIdentifier(string? name) => name is { Length: > 0 and <= 64 } &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
