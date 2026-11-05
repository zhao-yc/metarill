using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Relations;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

[Collection(GenerationMySqlCollection.Name)]
public sealed class PairedMaintenanceSqlMySqlTests(ITestOutputHelper output)
{
    private const string Source = "mt_maint_pair_source";
    private const string Target = "mt_maint_pair_target";
    private const long BaseId = 9007199254740993;
    private const int ActiveRows = 132;

    // 固定复现原 YZP SystemMaintenanceBusiness.executeBackfill 在单级显式联接字段上的 SQL。
    private const string LegacyUpdate = """
        UPDATE mt_maint_pair_source s LEFT JOIN mt_maint_pair_target t
        ON s.target_id = t.id AND t.delete_no IS NULL
        SET s.target_name = t.name WHERE s.delete_no IS NULL
        """;

    /// <summary>正式对照夹具必须是双方共同支持的真实物理单值联接列。</summary>
    [Fact]
    public void Fixture_declares_a_physical_single_join()
    {
        var catalog = Catalog();
        var schema = MySqlSchemaPlanner.Compile(catalog, "offline_maintenance_pair");
        Assert.Equal(2, schema.Tables.Count);
        Assert.Contains(schema.Tables.Single(table => table.Name == Source).Columns, column => column.Name == "target_name" && column.StoreType == "varchar(100)");
        Assert.Equal("1", Assert.Single(RelationMaintenanceCatalog.Compile(catalog).Tables).Id);
    }

    /// <summary>在同一已核实的独占库、同一快照比较原 Java SQL 和 C# 生产执行器的物理结果。</summary>
    [FreshInstallMySqlFact]
    public async Task Explicit_join_legacy_sql_and_csharp_executor_repair_the_same_rows()
    {
        using var session = new DatabaseSession(FreshInstallConfiguration.Read("PackagedDatabase", "package"));
        await FreshInstallConfiguration.VerifyEmptyLeaseAsync(session);
        var catalog = Catalog();
        var schema = new SchemaService(session);
        var plan = await schema.PreviewAsync(catalog);
        Assert.Equal(new[] { Source, Target }, plan.Changes.Select(change => change.Table).Order(StringComparer.Ordinal));
        Assert.All(plan.Changes, change => Assert.Equal("createTable", change.Kind));
        Assert.True((await schema.ApplyAsync(plan)).Succeeded);
        var store = new SqlRowStore(session, catalog);
        await SeedAsync(store);
        var before = await SnapshotAsync(store);
        var targetsBefore = Serialize((await store.ReadIncludingDeletedAsync(Target)).OrderBy(row => SqlRowStore.Id(row)));

        await ExecuteLegacyAsync(session);
        var javaResult = await SnapshotAsync(store);
        AssertRepaired(javaResult);
        await ExecuteLegacyAsync(session);
        Assert.Equal(Serialize(javaResult), Serialize(await SnapshotAsync(store)));

        // 仅恢复本轮活动行的缓存值，核对整个来源表回到相同状态后再测 C#。
        await session.InTransactionAsync(async () =>
        {
            foreach (var row in before.Take(ActiveRows))
                Assert.Equal(1, await store.UpdateAsync(Source, SqlRowStore.Id(row), new Dictionary<string, object?> { ["target_name"] = row["target_name"] }));
            return true;
        });
        Assert.Equal(Serialize(before), Serialize(await SnapshotAsync(store)));

        var executor = new RelationMaintenanceExecutor(session);
        var compiled = RelationMaintenanceCatalog.Compile(catalog);
        var observation = DatabaseCommandObservation.Begin(RandomNumberGenerator.GetBytes(32));
        MaintenanceResult result;
        using (observation) result = await executor.BackfillAsync(compiled, "1");
        var trace = observation.Snapshot();
        Assert.True(trace.InternallyConsistent);
        Assert.Equal(1, result.JoinColumnCount);
        Assert.Equal(99, result.AffectedRows);
        Assert.Equal(result.AffectedRows, Assert.Single(result.Details).AffectedRows);
        Assert.Equal(Serialize(javaResult), Serialize(await SnapshotAsync(store)));
        Assert.Equal(0, (await executor.BackfillAsync(compiled, "1")).AffectedRows);
        Assert.Equal(Serialize(javaResult), Serialize(await SnapshotAsync(store)));
        Assert.Equal(targetsBefore, Serialize((await store.ReadIncludingDeletedAsync(Target)).OrderBy(row => SqlRowStore.Id(row))));
        Assert.Equal(new[] { DatabaseTransactionOperation.Begin, DatabaseTransactionOperation.Commit }, trace.Transactions.Select(entry => entry.Operation));
        Assert.All(trace.Commands, command => Assert.True(command.Succeeded));
        var writes = trace.Commands.Count(command => command.Operation == DatabaseCommandOperation.NonQuery);
        Assert.Equal(result.AffectedRows, writes);
        Assert.Equal(2, VerificationReads(trace));
        Assert.Equal(113, trace.Commands.Count);
        output.WriteLine($"显式单级联接物理结果一致。活动行={ActiveRows}，实际变化={result.AffectedRows}，C# 观测SQL={trace.Commands.Count}，其中写入={writes}。未计时。");
        await VerifyLateTruncationAsync(session, catalog, store);
    }

    /// <summary>末批出现真实非严格模式截断时，已核验并写入的前批也必须回滚。</summary>
    private async Task VerifyLateTruncationAsync(DatabaseSession session, MetadataCatalog catalog, SqlRowStore store)
    {
        await using (var connection = session.Provider.CreateConnection(session.Options))
        {
            await connection.OpenAsync();
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = "ALTER TABLE mt_maint_pair_target MODIFY COLUMN name varchar(120) NULL";
            await ddl.ExecuteNonQueryAsync();
        }
        catalog.ConfigColumns.Single(column => column.ConfigTableId == "2" && column.ColumnName == "name").ColumnLength = "120";
        var lateTarget = BaseId + 4;
        await store.InsertAsync(Target, new Dictionary<string, object?> { ["id"] = lateTarget, ["name"] = new string('x', 110), ["yz_project_id"] = 54L });
        await store.UpdateAsync(Source, BaseId + 1000, new Dictionary<string, object?> { ["target_name"] = "故障前旧值" });
        await store.UpdateAsync(Source, BaseId + 1128, new Dictionary<string, object?> { ["target_id"] = lateTarget });
        var before = Serialize(await SnapshotAsync(store));
        var compiled = RelationMaintenanceCatalog.Compile(catalog);
        var observation = DatabaseCommandObservation.Begin(RandomNumberGenerator.GetBytes(32));
        using (observation)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InTransactionAsync(async () =>
            {
                var transaction = session.CurrentTransaction!;
                await using var command = transaction.Connection!.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT @@SESSION.sql_mode";
                var original = Assert.IsType<string>(await command.ExecuteScalarAsync());
                await SetSqlModeAsync(command, "");
                try { return await new RelationMaintenanceExecutor(session).BackfillAsync(compiled, "1"); }
                finally
                {
                    await SetSqlModeAsync(command, original);
                    command.CommandText = "SELECT @@SESSION.sql_mode";
                    command.Parameters.Clear();
                    Assert.Equal(original, Assert.IsType<string>(await command.ExecuteScalarAsync()));
                }
            }));
            Assert.Contains("存储后发生变化", error.Message);
        }
        var trace = observation.Snapshot();
        Assert.True(trace.InternallyConsistent);
        Assert.Equal(2, VerificationReads(trace));
        Assert.Equal(2, trace.Commands.Count(command => command.Succeeded && command.Sql.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(new[] { DatabaseTransactionOperation.Begin, DatabaseTransactionOperation.Rollback }, trace.Transactions.Select(entry => entry.Operation));
        Assert.Equal(before, Serialize(await SnapshotAsync(store)));
        output.WriteLine("第二批实际截断被读回拦截，两批写入整体回滚，已恢复会话 SQL 模式。");
    }

    /// <summary>只切换当前专属测试连接的会话模式，原值在同连接恢复。</summary>
    private static async Task SetSqlModeAsync(DbCommand command, string mode)
    {
        command.CommandText = "SET SESSION sql_mode=@mode";
        command.Parameters.Clear();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "mode";
        parameter.Value = mode;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private static int VerificationReads(DatabaseCommandTraceSnapshot trace) =>
        trace.Commands.Count(command => command.Operation == DatabaseCommandOperation.Reader && command.Sql.Contains("@row0", StringComparison.Ordinal));

    /// <summary>种入跨批量边界的同项目样本，含活动目标、已删目标、孤儿和正确缓存。</summary>
    private static async Task SeedAsync(SqlRowStore store)
    {
        var deleted = new DateTime(2026, 1, 1);
        foreach (var (id, date) in new[] { (BaseId, (DateTime?)null), (BaseId + 1, deleted) })
            await store.InsertAsync(Target, new Dictionary<string, object?>
            {
                ["id"] = id, ["name"] = "目标新名称", ["yz_project_id"] = 54L, ["delete_no"] = date
            });
        for (var index = 0; index <= ActiveRows; index++)
        {
            var variant = index % 4;
            await store.InsertAsync(Source, new Dictionary<string, object?>
            {
                ["id"] = BaseId + 1000 + index, ["yz_project_id"] = 54L,
                ["target_id"] = BaseId + (variant == 3 ? 0 : variant),
                ["target_name"] = variant == 3 ? "目标新名称" : "旧缓存",
                ["note"] = "保留业务值" + index, ["delete_no"] = index == ActiveRows ? deleted : null
            });
        }
    }

    /// <summary>直接运行原语句，不把 SQL 重放描述为 Java 宿主或性能验收。</summary>
    private static async Task ExecuteLegacyAsync(DatabaseSession session)
    {
        await session.InTransactionAsync(async () =>
        {
            var transaction = session.CurrentTransaction!;
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = LegacyUpdate;
            await command.ExecuteNonQueryAsync();
            return true;
        });
    }

    private static async Task<Dictionary<string, object?>[]> SnapshotAsync(SqlRowStore store) =>
        (await store.ReadIncludingDeletedAsync(Source)).OrderBy(row => SqlRowStore.Id(row)).ToArray();

    private static string Serialize(IEnumerable<Dictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows.Select(row => new SortedDictionary<string, object?>(row, StringComparer.Ordinal)));

    /// <summary>逐格验证重算结果与软删范围，比较时同时包含未参与回填的物理字段。</summary>
    private static void AssertRepaired(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        Assert.Equal(ActiveRows + 1, rows.Count);
        for (var index = 0; index <= ActiveRows; index++)
        {
            var row = rows[index];
            Assert.Equal(BaseId + 1000 + index, SqlRowStore.Id(row));
            Assert.Equal(54L, SqlRowStore.Id(row, "yz_project_id"));
            Assert.Equal("保留业务值" + index, row["note"]);
            var expected = index == ActiveRows ? "旧缓存" : index % 4 is 0 or 3 ? "目标新名称" : null;
            Assert.Equal(expected, row["target_name"]);
        }
    }

    private static MetadataCatalog Catalog()
    {
        var catalog = new MetadataCatalog
        {
            YzProjectId = "54",
            ConfigTables =
            [
                new() { Id = "1", TableName = Source, Name = "回填来源", IsRongyu = 1 },
                new() { Id = "2", TableName = Target, Name = "回填目标", IsRongyu = 0 }
            ]
        };
        foreach (var id in new[] { "1", "2" })
            catalog.ConfigColumns.AddRange([
                Column(id, "id", "int64", required: true),
                Column(id, "yz_project_id", "int64"),
                Column(id, "delete_no", "datetime")
            ]);
        var relation = Column("1", "target_id", "int64");
        relation.LinkConfigTableId = "2";
        var cache = Column("1", "target_name", "string");
        cache.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        cache.Extra["columnFullName"] = JsonSerializer.SerializeToElement("target.name");
        catalog.ConfigColumns.AddRange([relation, cache, Column("1", "note", "string"), Column("2", "name", "string")]);
        return catalog;
    }

    /// <summary>物理列和元数据同时使用同一类型，避免对照中引入隐式类型转换。</summary>
    private static ColumnMetadata Column(string table, string name, string type, bool required = false) =>
        new() { Id = table + "_" + name, ConfigTableId = table, ColumnName = name, LogicalType = type, ColumnLength = "100", IsDbNotNull = required ? 1 : 0 };
}
