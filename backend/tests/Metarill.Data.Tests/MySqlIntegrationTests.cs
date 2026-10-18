using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using FreeSql.DataAnnotations;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>只有显式提供专用测试配置才运行，普通 CI 必须报告跳过而不能冒充真实数据库验收。</summary>
public sealed class MySqlIntegrationFactAttribute : FactAttribute
{
    public MySqlIntegrationFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) Skip = "未提供 METARILL_TEST_SETTINGS 专用 MySQL 配置。";
    }
}

public sealed class MySqlIntegrationTests
{
    [MySqlIntegrationFact]
    public async Task Preview_apply_indexes_crud_and_transaction_rollback_are_real()
    {
        var options = ReadOptions();
        using var session = new DatabaseSession(options);
        var schema = new SchemaService(session);
        var table = "mt_test_schema_" + Guid.NewGuid().ToString("N")[..12];
        var catalog = Catalog(table);
        try
        {
            var preview = await schema.PreviewAsync(catalog);
            Assert.Single(preview.Changes);
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var result = await schema.ApplyAsync(preview);
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.AppliedStatements);
            await Assert.ThrowsAsync<InvalidOperationException>(() => schema.ApplyAsync(preview));
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            var actual = Assert.Single((await schema.InspectAsync(catalog)).Tables);
            Assert.Contains(actual.Indexes!, index => index.Name == "ix_name" && index.Unique);

            await session.InTransactionAsync(async () =>
            {
                await session.Insert<TestRow>().AsTable(table).AppendData(new TestRow { Id = 9007199254740993, Name = "中文'参数;--" }).ExecuteAffrowsAsync();
                await session.InTransactionAsync(async () =>
                {
                    Assert.Same(session.CurrentTransaction, session.CurrentTransaction);
                    await session.Update<TestRow>().AsTable(table).Set(r => r.Name, "已提交").Where(r => r.Id == 9007199254740993).ExecuteAffrowsAsync();
                    return 0;
                });
                return 0;
            });
            Assert.Equal("已提交", (await session.Select<TestRow>().AsTable((_, _) => table).Where(r => r.Id == 9007199254740993).FirstAsync()).Name);

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.InTransactionAsync<int>(async () =>
            {
                await session.Insert<TestRow>().AsTable(table).AppendData(new TestRow { Id = 2, Name = "应回滚" }).ExecuteAffrowsAsync();
                throw new InvalidOperationException("验证完整回滚");
            }));
            Assert.Equal(1, await session.Select<TestRow>().AsTable((_, _) => table).CountAsync());
            var missingRequired = Catalog(table);
            missingRequired.ConfigColumns.Add(new() { Id = "required", ConfigTableId = "table", ColumnName = "required_value", LogicalType = "string", IsDbNotNull = 1 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => schema.PreviewAsync(missingRequired));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Data.Common.DbTransaction? firstTransaction = null;
            var first = session.InTransactionAsync(async () =>
            {
                firstTransaction = session.CurrentTransaction;
                entered.SetResult();
                await release.Task;
                await session.Insert<TestRow>().AsTable(table).AppendData(new TestRow { Id = 3, Name = "并发一" }).ExecuteAffrowsAsync();
                return 0;
            });
            await entered.Task;
            var second = session.InTransactionAsync(async () =>
            {
                Assert.NotSame(firstTransaction, session.CurrentTransaction);
                await session.Insert<TestRow>().AsTable(table).AppendData(new TestRow { Id = 4, Name = "并发二" }).ExecuteAffrowsAsync();
                return 0;
            });
            try { await second; }
            finally { release.TrySetResult(); }
            await first;
            Assert.Equal(3, await session.Select<TestRow>().AsTable((_, _) => table).CountAsync());
            Assert.Contains(schema.History, h => h.Succeeded);
        }
        finally { await DropTemporaryAsync(session, table); }
    }

    [MySqlIntegrationFact]
    public async Task Drift_and_tampered_sql_are_rejected_before_execution()
    {
        using var session = new DatabaseSession(ReadOptions());
        var schema = new SchemaService(session);
        var table = "mt_test_drift_" + Guid.NewGuid().ToString("N")[..12];
        var catalog = Catalog(table);
        try
        {
            var preview = await schema.PreviewAsync(catalog);
            var tampered = preview with { Changes = [new("createTable", table, null, "DROP TABLE yz_manager")] };
            await Assert.ThrowsAsync<InvalidOperationException>(() => schema.ApplyAsync(tampered));
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            await schema.ApplyAsync(await schema.PreviewAsync(catalog));
            preview = await schema.PreviewAsync(catalog);
            await using (var connection = session.Provider.CreateConnection(session.Options))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE {session.Provider.QuoteIdentifier(table)} ADD COLUMN drift_value int NULL";
                await command.ExecuteNonQueryAsync();
            }
            var error = await Assert.ThrowsAsync<SchemaApplyException>(() => schema.ApplyAsync(preview));
            Assert.Equal(0, error.Result.AppliedStatements);
            Assert.False(error.Result.Succeeded);
        }
        finally { await DropTemporaryAsync(session, table); }
    }

    private static MetadataCatalog Catalog(string name) => new()
    {
        ConfigTables = [new() { Id = "table", TableName = name }],
        ConfigColumns = [
            new() { Id = "id", ConfigTableId = "table", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "name", ConfigTableId = "table", ColumnName = "name", LogicalType = "string", ColumnLength = "100" },
            new() { Id = "delete", ConfigTableId = "table", ColumnName = "delete_no", LogicalType = "datetime" },
            new() { Id = "payload", ConfigTableId = "table", ColumnName = "payload", LogicalType = "json" },
            new() { Id = "amount", ConfigTableId = "table", ColumnName = "amount", LogicalType = "decimal" },
            new() { Id = "enabled", ConfigTableId = "table", ColumnName = "enabled", LogicalType = "bool" }
        ],
        ConfigIndexes = [JsonSerializer.SerializeToElement(new { id = "index", indexName = "ix_name", configTableId = "table", isUnique = 1 })],
        ConfigIndexColumns = [JsonSerializer.SerializeToElement(new { configIndexId = "index", configColumnId = "name", sortNo = 0 })]
    };

    private static DatabaseOptions ReadOptions()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.ExpectedDatabase is not "metarill_test" and not "metarill_business_test") throw new InvalidOperationException("真实集成测试只允许指定隔离测试库。");
        return options;
    }

    private static async Task DropTemporaryAsync(DatabaseSession session, string table)
    {
        if (!table.StartsWith("mt_test_", StringComparison.Ordinal)) throw new InvalidOperationException("只能清理当前测试临时表。");
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS " + session.Provider.QuoteIdentifier(table);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestRow
    {
        [Column(Name = "id", IsPrimary = true)] public long Id { get; set; }
        [Column(Name = "name")] public string? Name { get; set; }
        [Column(Name = "delete_no")] public DateTime? DeleteNo { get; set; }
    }
}

