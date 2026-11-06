using System.Data.Common;
using System.Runtime.CompilerServices;
using MySqlConnector;

namespace Metarill.Data.Queries;

/// <summary>复用经原字典校验的完整定义；每次复用仍须由当前业务事务固定结构并核对定义。</summary>
internal sealed class MySqlImportLayoutProofs
{
    // 仅启用已在真实实例验证 SHOW CREATE 不建立业务快照的版本。
    private const string VerifiedServerVersion = "8.4.11";
    private const int MaximumDefinitionLength = 65536;
    private readonly ConditionalWeakTable<MySqlNonRedundantReadPlan, Proof> _verified = new();

    internal sealed record Attempt(MySqlNonRedundantReadPlan Plan, string Parent, string Source)
    {
        internal bool Reusable { get; set; }
    }

    private sealed record Identity(string ServerUuid, string Account, string Roles, string SqlMode);
    private sealed record Proof(Identity Identity, string Parent, string Source);

    /// <summary>冷启动只登记。已有定义时才做锁前比对，字典校验仍留在原位置。</summary>
    internal async Task PrepareAsync(DatabaseSession session, DbTransaction transaction,
        MySqlNonRedundantReadPlan plan, string parent, string source, CancellationToken token)
    {
        if (transaction.Connection is not MySqlConnection connection || connection.ServerVersion != VerifiedServerVersion ||
            session.GetImportLayoutAttempt(transaction) is not null || parent == source ||
            plan.PhysicalReferences.Any(reference => reference.Table != parent && reference.Table != source) ||
            !plan.PhysicalReferences.Any(reference => reference.Table == parent) ||
            !plan.PhysicalReferences.Any(reference => reference.Table == source))
            return;

        var options = new MySqlConnectionStringBuilder(connection.ConnectionString);
        if (!options.ConnectionReset || !options.AllowUserVariables)
            return;

        var attempt = new Attempt(plan, parent, source);
        session.SetImportLayoutAttempt(transaction, attempt);
        if (_verified.TryGetValue(plan, out var previous))
            attempt.Reusable = previous == await ReadAsync(session, transaction, attempt, previous.Identity, token);
    }

    /// <summary>原校验成功且固定了两张物理表后，记录当前实例的完整定义供下次复核。</summary>
    internal async Task RememberAsync(DatabaseSession session, DbTransaction transaction,
        MySqlNonRedundantReadPlan plan, CancellationToken token)
    {
        if (session.GetImportLayoutAttempt(transaction) is { Reusable: false } attempt &&
            ReferenceEquals(attempt.Plan, plan) &&
            await ReadAsync(session, transaction, attempt, null, token) is { } proof)
            _verified.AddOrUpdate(plan, proof);
    }

    private static async Task<Proof?> ReadAsync(DatabaseSession session, DbTransaction transaction,
        Attempt attempt, Identity? expected, CancellationToken token)
    {
        var connection = (MySqlConnection)transaction.Connection!;
        try
        {
            Identity identity;
            await using (var command = MySqlFloatingCommand.Wrap(connection.CreateCommand()))
            {
                command.Transaction = (MySqlTransaction)transaction;
                // 固定定义输出选项；库和服务器必须在查询实际表之前确认。
                command.CommandText = "SELECT DATABASE(),@@server_uuid,CURRENT_USER(),CURRENT_ROLE(),@@SESSION.sql_mode," +
                    "@@SESSION.sql_quote_show_create,@@SESSION.show_gipk_in_create_table_and_information_schema";
                await using var reader = await command.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token) || reader.IsDBNull(0) || reader.GetString(0) != session.Options.ExpectedDatabase ||
                    !Guid.TryParseExact(reader.GetString(1), "D", out _) || reader.GetInt32(5) != 1 || reader.GetInt32(6) != 1)
                    return null;
                identity = new(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
                if (expected is not null && expected != identity)
                    return null;
            }

            var database = session.Provider.QuoteIdentifier(session.Options.ExpectedDatabase);
            var parent = session.Provider.QuoteIdentifier(attempt.Parent);
            var source = session.Provider.QuoteIdentifier(attempt.Source);
            var qualifiedParent = database + "." + parent;
            var qualifiedSource = database + "." + source;
            await using var definitions = MySqlFloatingCommand.Wrap(connection.CreateCommand());
            definitions.Transaction = (MySqlTransaction)transaction;
            // 先固定父和明细的结构。零行当前读和 SHOW CREATE 不提前开启一致性快照。
            definitions.CommandText = expected is null ? "" :
                Pin(attempt.Parent, qualifiedParent) + "; " + Pin(attempt.Source, qualifiedSource) + "; ";
            definitions.CommandText += $"SHOW CREATE TABLE {qualifiedParent}; SHOW CREATE TABLE {qualifiedSource}";
            await using var results = await definitions.ExecuteReaderAsync(token);
            if (expected is not null && (!await results.NextResultAsync(token) || !await results.NextResultAsync(token)))
                return null;
            var parentDefinition = await ReadDefinitionAsync(results, attempt.Parent, parent, token);
            if (parentDefinition is null || !await results.NextResultAsync(token))
                return null;
            var sourceDefinition = await ReadDefinitionAsync(results, attempt.Source, source, token);
            return sourceDefinition is null || await results.NextResultAsync(token) ? null :
                new(identity, parentDefinition, sourceDefinition);

            // 与原核验读取同一组列，命中证明也不能跳过列级访问检查。
            string Pin(string table, string qualified) =>
                "SELECT " + string.Join(",", attempt.Plan.PhysicalReferences.Where(reference => reference.Table == table)
                    .Select(reference => session.Provider.QuoteIdentifier(reference.Column.Name))) + $" FROM {qualified} LIMIT 0 FOR UPDATE";
        }
        catch (MySqlException error) when (error.Number is 1054 or 1142 or 1146 or 1193 or 1227 or 1347 or 1356)
        {
            // 结构或访问证明缺失时由原路径给出业务错误。事务超时、取消和死锁继续向上回滚。
            return null;
        }
    }

    private static async Task<string?> ReadDefinitionAsync(DbDataReader reader, string table, string quotedTable, CancellationToken token)
    {
        if (reader.FieldCount != 2 || !await reader.ReadAsync(token) || reader.GetString(0) != table)
            return null;
        var definition = reader.GetString(1);
        // 临时表、视图和无法确认的输出不能代替已验证物理表。
        return definition.Length <= MaximumDefinitionLength &&
            definition.StartsWith("CREATE TABLE " + quotedTable + " (", StringComparison.Ordinal) &&
            !await reader.ReadAsync(token) ? definition : null;
    }
}
