using Metarill.Data.Queries;
using MySqlConnector;

namespace Metarill.Data.Relations;

/// <summary>为固定独立应用生成需由部署管理员审阅的离线安装计划。</summary>
public static class MySqlSharedAppendInstallation
{
    public sealed record Entry(string Source, string Parent, string Schema, string Routine, string TargetCheckSql,
        IReadOnlyList<string> InstallSql, string AdministratorReviewSql, string RuntimeProbeSql);
    public sealed record Plan(string ExpectedDatabase, string ExpectedServerUuid, IReadOnlyList<Entry> Entries);

    /// <summary>校验私有配置及冻结关系目录，只返回计划，不连接数据库。</summary>
    public static Plan Preview(DatabaseOptions options, RelationMaintenanceCatalog catalog, string? runtimeHost)
    {
        if (!string.Equals(options.Provider, "mysql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("共享追加安装计划只支持 MySQL。");
        new MySqlDatabaseProvider().Validate(options);
        RequireIdentifier(options.ExpectedDatabase);
        var runtimeUser = new MySqlConnectionStringBuilder(options.ConnectionString).UserID;
        if (!IsUser(runtimeUser) || runtimeHost is not { Length: > 0 and <= 255 } ||
            !runtimeHost.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or '%'))
            throw new ArgumentException("需要规范的运行账号和明确的账号 Host。");
        if (options.MySqlSharedAppendProofs is not { Count: > 0 } proofs)
            throw new ArgumentException("至少配置一个共享追加证明。");

        var entries = new List<Entry>();
        var definers = new HashSet<string>(StringComparer.Ordinal);
        var routines = new HashSet<(string Schema, string Routine)>();
        Guid? instance = null;
        foreach (var (source, proof) in proofs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (proof is null || !Guid.TryParseExact(proof.ServerUuid, "D", out var uuid) || uuid == Guid.Empty ||
                instance is not null && instance != uuid)
                throw new ArgumentException("共享证明必须绑定同一非空实例 UUID。");
            instance = uuid;
            RequireIdentifier(source);
            RequireIdentifier(proof.ParentTable);
            RequireIdentifier(proof.Schema);
            RequireIdentifier(proof.Procedure);
            if (proof.Schema.ToLowerInvariant() is "mysql" or "sys" or "information_schema" or "performance_schema")
                throw new ArgumentException("不能在系统库安装共享证明。");
            if (!ProjectRelationMutationPropagator.MatchesSharedAppendParent(catalog, source, proof.ParentTable))
                throw new ArgumentException("共享证明未匹配发布目录中的固定父表和叶子明细。");

            if (proof.Definer?.Split('@') is not [var definer, "localhost"] || !IsUser(definer) ||
                definer == runtimeUser || !definers.Add(definer) ||
                !routines.Add((proof.Schema.ToUpperInvariant(), proof.Procedure.ToUpperInvariant())))
                throw new ArgumentException("每项证明须使用独立且唯一的 localhost 定义者与例程。");
            entries.Add(CreateEntry(options.ExpectedDatabase, uuid, source, proof, definer, runtimeUser, runtimeHost));
        }
        return new(options.ExpectedDatabase, instance!.Value.ToString("D"), entries.AsReadOnly());
    }

    /// <summary>生成精确目标检查、安装和安装后核验。名称已限定为不依赖 SQL 模式的字符。</summary>
    private static Entry CreateEntry(string database, Guid uuid, string source, MySqlSharedAppendProofOptions proof,
        string definer, string runtimeUser, string runtimeHost)
    {
        var parent = proof.ParentTable;
        var routine = $"{Quote(proof.Schema)}.{Quote(proof.Procedure)}";
        var sourceName = $"{Quote(database)}.{Quote(source)}";
        var definitionAccount = $"'{definer}'@'localhost'";
        var runtimeAccount = $"'{runtimeUser}'@'{runtimeHost}'";
        var contract = MySqlSharedAppendProof.Contract;
        var verify = $"""
            SELECT LOWER(@@server_uuid) = '{uuid:D}'
               AND EXISTS(SELECT 1 FROM information_schema.SCHEMATA WHERE BINARY SCHEMA_NAME = '{proof.Schema}')
               AND (SELECT COUNT(*) FROM information_schema.TABLES
                     WHERE BINARY TABLE_SCHEMA = '{database}' AND BINARY TABLE_NAME IN ('{source}', '{parent}')
                       AND TABLE_TYPE = 'BASE TABLE' AND ENGINE = 'InnoDB') = 2
               AND (SELECT COUNT(*) FROM information_schema.STATISTICS
                     WHERE BINARY TABLE_SCHEMA = '{database}' AND BINARY TABLE_NAME IN ('{source}', '{parent}')
                       AND INDEX_NAME = 'PRIMARY') = 2
               AND (SELECT COUNT(*) FROM information_schema.STATISTICS
                     WHERE BINARY TABLE_SCHEMA = '{database}' AND BINARY TABLE_NAME IN ('{source}', '{parent}')
                       AND INDEX_NAME = 'PRIMARY' AND BINARY COLUMN_NAME = 'id' AND SEQ_IN_INDEX = 1) = 2
               AND NOT EXISTS(SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS
                              WHERE BINARY CONSTRAINT_SCHEMA = '{database}' AND BINARY TABLE_NAME = '{parent}')
               AND NOT EXISTS(SELECT 1 FROM information_schema.TRIGGERS
                              WHERE BINARY EVENT_OBJECT_SCHEMA = '{database}' AND BINARY EVENT_OBJECT_TABLE = '{source}'
                                AND EVENT_MANIPULATION IN ('INSERT', 'UPDATE'))
               AND EXISTS(SELECT 1 FROM mysql.user WHERE BINARY User = '{runtimeUser}' AND Host = '{runtimeHost}')
               AND NOT EXISTS(SELECT 1 FROM mysql.user WHERE BINARY User = '{definer}' AND Host = 'localhost')
               AND NOT EXISTS(SELECT 1 FROM information_schema.ROUTINES
                              WHERE ROUTINE_SCHEMA = '{proof.Schema}' AND ROUTINE_NAME = '{proof.Procedure}'
                                AND ROUTINE_TYPE = 'PROCEDURE') AS target_ready;
            """;
        var create = $"""
            CREATE DEFINER = {definitionAccount} PROCEDURE {routine}()
            READS SQL DATA SQL SECURITY DEFINER COMMENT '{contract}'
            SELECT '{contract}', '{database}', '{source}', '{parent}',
                EXISTS(SELECT 1 FROM information_schema.TABLE_PRIVILEGES
                       WHERE GRANTEE = CONCAT(CHAR(39), '{definer}', CHAR(39), '@', CHAR(39), 'localhost', CHAR(39))
                         AND BINARY TABLE_SCHEMA = '{database}' AND BINARY TABLE_NAME = '{source}' AND PRIVILEGE_TYPE = 'TRIGGER'),
                EXISTS(SELECT 1 FROM information_schema.TRIGGERS
                       WHERE BINARY EVENT_OBJECT_SCHEMA = '{database}' AND BINARY EVENT_OBJECT_TABLE = '{source}'
                         AND EVENT_MANIPULATION IN ('INSERT', 'UPDATE'));
            """;
        return new(source, parent, proof.Schema, proof.Procedure, verify,
            Array.AsReadOnly(new[] {
                $"CREATE USER {definitionAccount} ACCOUNT LOCK;",
                $"GRANT TRIGGER ON {sourceName} TO {definitionAccount};",
                create,
                $"GRANT EXECUTE ON PROCEDURE {routine} TO {definitionAccount};",
                $"GRANT EXECUTE ON PROCEDURE {routine} TO {runtimeAccount};"
            }), $"SHOW CREATE PROCEDURE {routine};", $"CALL {routine}();");
    }

    private static void RequireIdentifier(string? value)
    {
        if (value is not { Length: > 0 and <= 64 } ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            throw new ArgumentException("共享证明库、表及例程名称必须使用规范 ASCII 标识符。");
    }

    private static string Quote(string name) => ((char)96) + name + (char)96;
    private static bool IsUser(string? name) => name is { Length: > 0 and <= 32 } &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
