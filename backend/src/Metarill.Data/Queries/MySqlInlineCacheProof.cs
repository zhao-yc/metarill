using System.Data.Common;
using MySqlConnector;

namespace Metarill.Data.Queries;

/// <summary>在已固定父子表的事务中证明缓存可以随插入写入。</summary>
internal static class MySqlInlineCacheProof
{
    /// <summary>独立连接核对本次有效账号和物理布局，避免用旧的业务快照推断触发器可见性。</summary>
    internal static async Task<bool> VerifyAsync(DatabaseSession session, MySqlConnection connection, DbTransaction transaction,
        string source, string field, string parent, string parentField, CancellationToken token)
    {
        if (source == parent || !connection.ServerVersion.StartsWith("8.4.", StringComparison.Ordinal) ||
            session.IsInlineCacheTriggerGrantMissing(source))
            return false;

        string instance, account;
        await using (var identity = connection.CreateCommand())
        {
            identity.Transaction = (MySqlTransaction)transaction;
            identity.CommandText = "SELECT DATABASE(),@@server_uuid,CURRENT_USER()";
            await using var reader = await identity.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || reader.IsDBNull(0) || reader.GetString(0) != session.Options.ExpectedDatabase)
                return false;
            instance = reader.GetString(1);
            account = reader.GetString(2);
        }

        // CURRENT_USER 含转义字符时保守退出，避免误认其他账号的元数据权限。
        var parts = account.Split('@');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 ||
            part.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.' or '%' or '-' or ':'))))
            return false;

        try
        {
            await using var metadata = session.CreateSharedAppendProofConnection();
            await metadata.OpenAsync(token);
            await using var command = metadata.CreateCommand();
            // 独立证明不能在排队的 DDL 后一直占用业务事务。无法及时取证时走原两阶段写入。
            command.CommandTimeout = 2;
            command.CommandText = """
                WITH direct_grants AS (
                    SELECT PRIVILEGE_TYPE, TABLE_NAME
                    FROM information_schema.TABLE_PRIVILEGES
                    WHERE TABLE_SCHEMA=@database AND BINARY TABLE_SCHEMA=BINARY @database AND BINARY GRANTEE=BINARY @grantee
                    UNION ALL
                    SELECT PRIVILEGE_TYPE, NULL
                    FROM information_schema.SCHEMA_PRIVILEGES
                    WHERE TABLE_SCHEMA=@database AND BINARY TABLE_SCHEMA=BINARY @database AND BINARY GRANTEE=BINARY @grantee
                ), trigger_grant AS (
                    SELECT EXISTS (SELECT 1 FROM direct_grants WHERE PRIVILEGE_TYPE='TRIGGER'
                                   AND (TABLE_NAME IS NULL OR TABLE_NAME=@source AND BINARY TABLE_NAME=BINARY @source)) AS allowed
                )
                SELECT CAST(CASE
                    WHEN NOT (BINARY DATABASE()<=>BINARY @database AND @@server_uuid=@instance
                              AND BINARY CURRENT_USER()=BINARY @account AND CURRENT_ROLE()='NONE') THEN 0
                    WHEN NOT allowed THEN 2
                    WHEN EXISTS (SELECT 1 FROM direct_grants WHERE PRIVILEGE_TYPE='SELECT'
                                AND (TABLE_NAME IS NULL OR TABLE_NAME=@source AND BINARY TABLE_NAME=BINARY @source))
                    AND EXISTS (SELECT 1 FROM direct_grants WHERE PRIVILEGE_TYPE='SELECT'
                                AND (TABLE_NAME IS NULL OR BINARY TABLE_NAME=BINARY @parent))
                    AND NOT EXISTS (
                        SELECT 1 FROM information_schema.TRIGGERS
                        WHERE EVENT_OBJECT_SCHEMA=@database AND BINARY EVENT_OBJECT_SCHEMA=BINARY @database AND EVENT_OBJECT_TABLE=@source AND BINARY EVENT_OBJECT_TABLE=BINARY @source
                    )
                    AND NOT EXISTS (
                        SELECT 1 FROM information_schema.TABLE_CONSTRAINTS
                        WHERE TABLE_SCHEMA=@database AND BINARY TABLE_SCHEMA=BINARY @database AND TABLE_NAME=@source AND BINARY TABLE_NAME=BINARY @source
                          AND CONSTRAINT_TYPE IN ('CHECK','FOREIGN KEY')
                    )
                    AND NOT EXISTS (
                        SELECT 1 FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA=@database AND BINARY TABLE_SCHEMA=BINARY @database AND TABLE_NAME=@source AND BINARY TABLE_NAME=BINARY @source
                          AND (GENERATION_EXPRESSION<>'' OR LOWER(EXTRA) LIKE '%on update%')
                    )
                    AND NOT EXISTS (
                        SELECT 1 FROM information_schema.STATISTICS
                        WHERE TABLE_SCHEMA=@database AND BINARY TABLE_SCHEMA=BINARY @database AND TABLE_NAME=@source AND BINARY TABLE_NAME=BINARY @source
                          AND (COLUMN_NAME IS NULL OR BINARY COLUMN_NAME=BINARY @field)
                    )
                    AND EXISTS (
                        SELECT 1 FROM information_schema.COLUMNS s JOIN information_schema.COLUMNS p
                          ON BINARY p.TABLE_SCHEMA=BINARY s.TABLE_SCHEMA AND BINARY p.TABLE_NAME=BINARY @parent
                          AND BINARY p.COLUMN_NAME=BINARY @parentField
                        WHERE s.TABLE_SCHEMA=@database AND BINARY s.TABLE_SCHEMA=BINARY @database AND s.TABLE_NAME=@source AND BINARY s.TABLE_NAME=BINARY @source
                          AND BINARY s.COLUMN_NAME=BINARY @field
                          AND s.IS_NULLABLE='YES' AND s.DATA_TYPE='varchar' AND p.DATA_TYPE='varchar'
                          AND s.CHARACTER_SET_NAME='utf8mb4' AND s.COLLATION_NAME=p.COLLATION_NAME
                    ) THEN 1
                    ELSE 0
                    END AS SIGNED) FROM trigger_grant
                """;
            command.Parameters.AddWithValue("@grantee", $"'{parts[0]}'@'{parts[1]}'");
            command.Parameters.AddWithValue("@database", session.Options.ExpectedDatabase);
            command.Parameters.AddWithValue("@instance", instance);
            command.Parameters.AddWithValue("@account", account);
            command.Parameters.AddWithValue("@source", source);
            command.Parameters.AddWithValue("@field", field);
            command.Parameters.AddWithValue("@parent", parent);
            command.Parameters.AddWithValue("@parentField", parentField);
            var result = await command.ExecuteScalarAsync(token);
            // 只有同库、同实例、同有效身份明确缺少直接授权才记入短期缓存。
            if (result is 2L)
                session.RememberMissingInlineCacheTriggerGrant(source);
            return result is 1L;
        }
        catch (MySqlException) when (!token.IsCancellationRequested)
        {
            // 只兜底独立证明连接上的失败。业务连接错误及取消仍由原事务处理。
            return false;
        }
    }
}
