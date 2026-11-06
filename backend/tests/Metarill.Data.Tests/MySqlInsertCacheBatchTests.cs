using FreeSql.DataAnnotations;
using Metarill.Data.Relations;
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
