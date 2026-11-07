using System.Text;
using FreeSql.DataAnnotations;
using Metarill.Data.Relations;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class MySqlInsertCacheBatchTests
{
    [Fact]
    public void Reuses_the_mapper_bindings_without_opening_mysql()
    {
        using var session = CreateSession();
        var row = new CacheSource { Id = 9007199254740993, ParentId = "中文' ", Caption = null };
        var batch = Assert.IsType<MySqlInsertCacheBatch>(
            MySqlInsertCacheBatch.Prepare(session, "cache_source", row.Id, row));

        Assert.StartsWith("INSERT INTO `offline`.`cache_source`(", batch.Sql);
        Assert.EndsWith("WHERE CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY)", batch.Sql);
        Assert.Equal(new object?[] { row.Id, row.ParentId, null }, batch.Parameters
            .Select(parameter => parameter.Value is DBNull ? null : parameter.Value));
        Assert.Null(MySqlInsertCacheBatch.Prepare(session, "cache_source", row.Id - 1, row));
    }

    [Fact]
    public void Registered_audit_returns_to_the_mapper_without_invoking_the_hook()
    {
        using var session = CreateSession();
        var auditCalls = 0;
        session.Orm.Aop.AuditValue += (_, _) => auditCalls++;

        Assert.Null(MySqlInsertCacheBatch.Prepare(session, "cache_source", 1, new CacheSource { Id = 1 }));
        Assert.Equal(0, auditCalls);
    }

    [Fact]
    public void Packed_parameters_keep_types_and_require_the_same_canonical_parent()
    {
        using var session = CreateSession();
        var row = new CacheSource { Id = 9007199254740993, ParentId = "11", Caption = "中文🌲 " };
        var first = Assert.IsType<MySqlInsertCacheBatch>(
            MySqlInsertCacheBatch.Prepare(session, "cache_source", row.Id, row, "first_", ("parent_id", 11)));
        var second = Assert.IsType<MySqlInsertCacheBatch>(
            MySqlInsertCacheBatch.Prepare(session, "cache_source", row.Id, row, "second_", ("parent_id", 11)));
        Assert.All(first.Parameters, parameter => Assert.StartsWith("@first_", parameter.ParameterName));
        Assert.All(second.Parameters, parameter => Assert.StartsWith("@second_", parameter.ParameterName));
        Assert.Equal(first.Parameters.Select(parameter => (parameter.DbType, parameter.Value)),
            second.Parameters.Select(parameter => (parameter.DbType, parameter.Value)));
        Assert.All(first.Parameters, parameter => Assert.Contains(parameter.ParameterName, first.Sql));
        Assert.Null(MySqlInsertCacheBatch.Prepare(session, "cache_source", row.Id, row, "first_", ("parent_id", 12)));
        row.ParentId = "011";
        Assert.Null(MySqlInsertCacheBatch.Prepare(session, "cache_source", row.Id, row, "first_", ("parent_id", 11)));
    }

    /// <summary>按原语句形状核对单次绑定、重复绑定和 UTF-8 字节同时计入，且超限仍拒绝合并。</summary>
    [Fact]
    public void Packed_budget_charges_mapper_bindings_once_and_repeated_guards_at_each_use()
    {
        using var session = CreateSession();
        using var command = new MySqlCommand();
        var database = command.Parameters.AddWithValue("expected_database", "offline");
        var parent = command.Parameters.AddWithValue("parent_candidate", "父🌲");
        var first = Assert.IsType<MySqlInsertCacheBatch>(MySqlInsertCacheBatch.Prepare(session, "cache_source", 1,
            new CacheSource { Id = 1, ParentId = "11", Caption = "中文'🌲 " }, "mp0_", ("parent_id", 11)));
        var second = Assert.IsType<MySqlInsertCacheBatch>(MySqlInsertCacheBatch.Prepare(session, "cache_source", 2,
            new CacheSource { Id = 2, ParentId = "11", Caption = null }, "mp1_", ("parent_id", 11)));
        command.Parameters.AddRange(first.Parameters);
        command.Parameters.AddRange(second.Parameters);
        var firstId = command.Parameters.AddWithValue("mp0_exact_id", 1L);
        var secondId = command.Parameters.AddWithValue("mp1_exact_id", 2L);
        command.CommandText = $"/* 中文 */ SELECT @parent_candidate; {first.Sql}; SELECT @mp0_exact_id,@mp0_exact_id; " +
            $"SELECT @parent_candidate; {second.Sql}; SELECT @mp1_exact_id,@expected_database";
        var insertBudget = first.ParameterBudget + second.ParameterBudget;
        var repeated = new[] { database, parent, firstId, secondId };
        var exactBudget = Encoding.UTF8.GetByteCount(command.CommandText) + 128L +
            command.Parameters.Cast<MySqlParameter>().Sum(parameter => MySqlInsertCacheBatch.ParameterBytes(parameter.Value) *
                command.CommandText.AsSpan().Count("@" + parameter.ParameterName.TrimStart('@')));

        Assert.True(RelationMaintenanceExecutor.FitsPackedCommand(command, exactBudget, insertBudget, repeated));
        Assert.False(RelationMaintenanceExecutor.FitsPackedCommand(command, exactBudget - 1, insertBudget, repeated));
    }

    private static DatabaseSession CreateSession() => new(new()
    {
        ExpectedDatabase = "offline",
        ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
    });

    [Table(Name = "cache_source")]
    private sealed class CacheSource
    {
        [Column(Name = "id", IsPrimary = true)] public long Id { get; set; }
        [Column(Name = "parent_id")] public string? ParentId { get; set; }
        [Column(Name = "caption")] public string? Caption { get; set; }
    }
}
