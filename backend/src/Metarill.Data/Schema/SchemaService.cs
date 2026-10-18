using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using MySqlConnector;

namespace Metarill.Data.Schema;

/// <summary>显式结构变更入口。预览令牌绑定当前实例、连接目标和结构快照，SQL 不由 HTTP 调用者决定。</summary>
public sealed class SchemaService(DatabaseSession session, ISchemaChangeJournal? journal = null)
{
    private sealed record PendingPlan(SchemaChangePlan Plan, DatabaseSchema Desired, string Identity, string Signature, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, PendingPlan> _pending = new();
    private readonly ConcurrentQueue<SchemaApplyResult> _history = new();
    public IReadOnlyList<SchemaApplyResult> History => _history.ToArray();

    /// <summary>读取实际库并编译差异，只展示计划，绝不执行 DDL。</summary>
    public async Task<SchemaChangePlan> PreviewAsync(MetadataCatalog catalog, CancellationToken cancellationToken = default)
    {
        EnsureMySql();
        var desired = MySqlSchemaPlanner.Compile(catalog, session.Options.ExpectedDatabase);
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync(cancellationToken);
        var identity = await ValidateTargetAsync(connection, cancellationToken);
        var current = await ReadSchemaAsync(connection, desired.Tables.Select(t => t.Name).ToArray(), cancellationToken);
        var changes = MySqlSchemaPlanner.Compare(current, desired);
        var plan = new SchemaChangePlan(Guid.NewGuid().ToString("N"), "mysql", desired.Database,
            MySqlSchemaPlanner.Fingerprint(current), MySqlSchemaPlanner.Fingerprint(desired), changes);
        // 存储不可变序列化副本，调用者修改 Changes 集合不能影响待执行指令。
        var copy = JsonSerializer.Deserialize<SchemaChangePlan>(JsonSerializer.Serialize(plan))!;
        foreach (var expired in _pending.Where(x => x.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(x => x.Key)) _pending.TryRemove(expired, out _);
        if (_pending.Count >= 1000) throw new InvalidOperationException("未执行计划过多，请稍后重新预览。");
        _pending[plan.Id] = new PendingPlan(copy, desired, identity, Sign(copy), DateTimeOffset.UtcNow.AddMinutes(30));
        return plan;
    }

    /// <summary>只执行本实例预览过且未变更的计划。MySQL DDL 隐式提交，失败记录已生效数量。</summary>
    public async Task<SchemaApplyResult> ApplyAsync(SchemaChangePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureMySql();
        if (!_pending.TryRemove(plan.Id, out var pending)) throw new InvalidOperationException("计划不存在、已使用或进程已重启，请重新预览。");
        if (pending.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("计划已过期，请重新预览。");
        if (pending.Signature != Sign(plan)) throw new InvalidOperationException("预览计划被修改，必须重新预览。");
        if (session.CurrentTransaction is not null) throw new InvalidOperationException("结构变更不能加入业务事务。");
        var started = DateTimeOffset.UtcNow;
        var applied = 0;
        await using var connection = session.Provider.CreateConnection(session.Options);
        var locked = false;
        try
        {
            await connection.OpenAsync(cancellationToken);
            if (await ValidateTargetAsync(connection, cancellationToken) != pending.Identity) throw new InvalidOperationException("数据库服务器身份发生变化。");
            locked = Convert.ToInt32(await ScalarAsync(connection, "SELECT GET_LOCK(@name, 10)", cancellationToken,
                ("@name", LockName())), CultureInfo.InvariantCulture) == 1;
            if (!locked) throw new InvalidOperationException("其他结构任务正在执行，未取得数据库结构锁。");
            var names = pending.Desired.Tables.Select(t => t.Name).ToArray();
            var snapshot = await ReadSchemaAsync(connection, names, cancellationToken);
            if (MySqlSchemaPlanner.Fingerprint(snapshot) != pending.Plan.BeforeFingerprint) throw new InvalidOperationException("结构在预览后发生变化，必须重新预览。");
            foreach (var change in pending.Plan.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.CommandText = change.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
                applied++;
            }
            var actual = await ReadSchemaAsync(connection, names, cancellationToken);
            if (MySqlSchemaPlanner.Compare(actual, pending.Desired).Count != 0) throw new InvalidOperationException("DDL 执行后的结构与预期不符。");
            var result = new SchemaApplyResult(plan.Id, plan.Database, started, DateTimeOffset.UtcNow, true, applied, null);
            await RecordAsync(pending.Plan, result);
            return result;
        }
        catch (Exception exception)
        {
            // 只记录异常类型和安全说明，连接异常不得将凭据写入历史或客户端。
            var result = new SchemaApplyResult(plan.Id, plan.Database, started, DateTimeOffset.UtcNow, false, applied, exception.GetType().Name);
            try { await RecordAsync(pending.Plan, result); }
            catch (Exception journalError) { exception = new AggregateException("结构执行或审计记录失败。", exception, journalError); }
            throw new SchemaApplyException(result, exception);
        }
        finally
        {
            if (locked && connection.State == System.Data.ConnectionState.Open)
            {
                try { await ScalarAsync(connection, "SELECT RELEASE_LOCK(@name)", CancellationToken.None, ("@name", LockName())); }
                catch { /* 关闭连接也会释放会话锁，避免覆盖原始执行异常。 */ }
            }
        }
    }

    /// <summary>反查仅涉及已验证的受管辖表；完整 SHOW CREATE 快照额外覆盖索引、外键和存储属性。</summary>
    public async Task<DatabaseSchema> InspectAsync(MetadataCatalog catalog, CancellationToken cancellationToken = default)
    {
        EnsureMySql();
        var desired = MySqlSchemaPlanner.Compile(catalog, session.Options.ExpectedDatabase);
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync(cancellationToken);
        await ValidateTargetAsync(connection, cancellationToken);
        return await ReadSchemaAsync(connection, desired.Tables.Select(t => t.Name).ToArray(), cancellationToken);
    }

    /// <summary>保留有界运行历史，并通过宿主注入的持久化审计写入完整结果。</summary>
    private async Task RecordAsync(SchemaChangePlan plan, SchemaApplyResult result)
    {
        _history.Enqueue(result);
        while (_history.Count > 1000) _history.TryDequeue(out _);
        if (journal is not null) await journal.AppendAsync(new SchemaAuditEntry(plan, result), CancellationToken.None);
    }

    private void EnsureMySql()
    {
        if (session.Provider.Name != "mysql") throw new NotSupportedException("当前结构服务只实现了 MySQL，其他数据库必须使用对应适配器。");
        session.Provider.Validate(session.Options);
    }

    private async Task<string> ValidateTargetAsync(DbConnection connection, CancellationToken token)
    {
        var database = Convert.ToString(await ScalarAsync(connection, "SELECT DATABASE()", token), CultureInfo.InvariantCulture);
        if (database != session.Options.ExpectedDatabase) throw new InvalidOperationException("实际连接数据库与预期不一致。");
        var uuid = Convert.ToString(await ScalarAsync(connection, "SELECT @@server_uuid", token), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(uuid)) throw new InvalidOperationException("无法确认 MySQL 服务器身份。");
        return uuid + ":" + database;
    }

    private async Task<DatabaseSchema> ReadSchemaAsync(DbConnection connection, IReadOnlyList<string> managedTables, CancellationToken token)
    {
        var tables = new List<TableSchema>();
        foreach (var name in managedTables.OrderBy(n => n, StringComparer.Ordinal))
        {
            MetadataValidation.RequireIdentifier(name);
            var tableKind = Convert.ToString(await ScalarAsync(connection,
                "SELECT TABLE_TYPE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table", token, ("@table", name)), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(tableKind)) continue;
            if (tableKind != "BASE TABLE") throw new InvalidOperationException("实体表名称被视图占用，禁止覆盖。");
            var engine = Convert.ToString(await ScalarAsync(connection,
                "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table", token, ("@table", name)), CultureInfo.InvariantCulture);
            if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("受管辖表必须使用 InnoDB，以保证业务事务。");
            var columns = new List<ColumnSchema>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE,COLUMN_KEY,COLUMN_DEFAULT,EXTRA,DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table ORDER BY ORDINAL_POSITION";
                AddParameters(command, [("@table", name)]);
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    var type = NormalizeType(reader.GetString(1));
                    var logicalString = reader.GetString(6) is "varchar" or "char" or "text" or "longtext" or "mediumtext" or "tinytext";
                    var rawDefault = reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture);
                    var defaultSql = rawDefault is null ? null : logicalString ? "'" + rawDefault.Replace("\\", "\\\\").Replace("'", "''") + "'" : rawDefault;
                    columns.Add(new ColumnSchema(reader.GetString(0), type, reader.GetString(2) == "YES", reader.GetString(3) == "PRI", defaultSql,
                        !string.IsNullOrWhiteSpace(reader.GetString(5))));
                }
            }
            var indexes = new List<IndexSchema>();
            await using (var indexCommand = connection.CreateCommand())
            {
                indexCommand.CommandText = "SELECT INDEX_NAME,NON_UNIQUE,COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND INDEX_NAME <> 'PRIMARY' ORDER BY INDEX_NAME,SEQ_IN_INDEX";
                AddParameters(indexCommand, [("@table", name)]);
                await using var indexReader = await indexCommand.ExecuteReaderAsync(token);
                var rows = new List<(string Name, bool Unique, string Column)>();
                while (await indexReader.ReadAsync(token))
                {
                    // 函数索引不能误识别为普通字段索引；其原始定义仍进入漂移摘要。
                    rows.Add((indexReader.GetString(0), Convert.ToInt32(indexReader.GetValue(1), CultureInfo.InvariantCulture) == 0,
                        indexReader.IsDBNull(2) ? "<expression>" : indexReader.GetString(2)));
                }
                indexes.AddRange(rows.GroupBy(r => r.Name).Select(g => new IndexSchema(g.Key, g.First().Unique, g.Select(r => r.Column).ToArray())));
            }
            await using var ddlCommand = connection.CreateCommand();
            ddlCommand.CommandText = "SHOW CREATE TABLE " + MySqlSchemaPlanner.Quote(name);
            await using var ddlReader = await ddlCommand.ExecuteReaderAsync(token);
            if (!await ddlReader.ReadAsync(token)) throw new InvalidOperationException("结构反查期间表发生变化。");
            var ddl = Regex.Replace(ddlReader.GetString(1), @"\bAUTO_INCREMENT=\d+\s*", "", RegexOptions.CultureInvariant);
            tables.Add(new TableSchema(name, columns, ddl, indexes));
        }
        return new DatabaseSchema(session.Options.ExpectedDatabase, tables);
    }

    private static string NormalizeType(string type)
    {
        var result = type.ToLowerInvariant();
        if (Regex.IsMatch(result, @"^int(?:\(\d+\))?$")) return "int";
        if (Regex.IsMatch(result, @"^bigint(?:\(\d+\))?$")) return "bigint";
        return result;
    }

    private string LockName() => "metarill:schema:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.Options.ExpectedDatabase)))[..32];
    private static string Sign(SchemaChangePlan plan) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan)));
    private static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken token, params (string, object)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return await command.ExecuteScalarAsync(token);
    }
    private static void AddParameters(DbCommand command, (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
