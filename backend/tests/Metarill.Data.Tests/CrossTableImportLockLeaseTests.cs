using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>离线检验覆盖匹配实库门禁和一次性模型占用。</summary>
public sealed class CrossTableImportLockLeaseTests
{
    private const string Run = "abcdef0123456789";

    /// <summary>模型顺序与原夹具逐项一致，四组精确表名均互不相同。</summary>
    [Theory]
    [InlineData("string", "string", "ss")]
    [InlineData("string", "int64", "sl")]
    [InlineData("int64", "string", "ls")]
    [InlineData("int64", "int64", "ll")]
    public void Approved_scope_matches_the_actual_four_tables(string source, string parent, string profile)
    {
        var lease = CrossTableImportLockLease.Validate(JsonSerializer.SerializeToElement(Scope()), source, parent);
        Assert.Equal(profile, lease.Profile);
        Assert.Equal("mt_nrlock_" + Run + "_" + profile, lease.Prefix);
        Assert.Equal(NonRedundantCrossTableReadTests.Catalog(lease.Prefix, source, parent, twoTrees: true).ConfigTables.Select(table => table.TableName), lease.Tables);
    }

    /// <summary>租约过期、实例漂移、表名替换和重复表必须在连接前被拒绝。</summary>
    [Fact]
    public void Pending_ended_and_altered_scopes_are_rejected()
    {
        foreach (var (key, value) in new[]
        {
            ("authorizationStatus", "pending"), ("leaseStatus", "ended"), ("purpose", "other"),
            ("server", "localhost"), ("database", "production"), ("runId", Run + "\n")
        })
        {
            var changed = Scope();
            changed[key] = value;
            Assert.ThrowsAny<Exception>(() => Validate(changed));
        }
        var wrongTable = Scope();
        wrongTable["tables"]![0] = "business_customer";
        Assert.Throws<InvalidOperationException>(() => Validate(wrongTable));
        var duplicate = Scope();
        duplicate["tables"]![0] = duplicate["tables"]![1]!.DeepClone();
        Assert.Throws<InvalidOperationException>(() => Validate(duplicate));
        var badPin = Scope();
        badPin["instancePin"]!["expectedServerUuid"] = "not-a-uuid";
        Assert.Throws<InvalidOperationException>(() => Validate(badPin));
        Assert.Throws<InvalidOperationException>(() => CrossTableImportLockLease.Validate(JsonSerializer.SerializeToElement(Scope()), "decimal", "string"));
    }

    /// <summary>同模型不能二次启动，另一模型不受影响。文件仅使用本机临时路径。</summary>
    [Fact]
    public void Started_marker_is_exclusive_per_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "metarill-lease-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            CrossTableImportLockLease.MarkStarted(path, "ss");
            Assert.Throws<IOException>(() => CrossTableImportLockLease.MarkStarted(path, "ss"));
            CrossTableImportLockLease.MarkStarted(path, "sl");
            Assert.Throws<InvalidOperationException>(() => CrossTableImportLockLease.MarkStarted(path, "../foreign"));
        }
        finally
        {
            File.Delete(path + ".ss.started");
            File.Delete(path + ".sl.started");
        }
    }

    /// <summary>测试用批准形状只在内存构造，不包含实际凭据或远端资源。</summary>
    private static JsonObject Scope() => new()
    {
        ["purpose"] = "cross-table-import-lock-mysql-v1",
        ["authorizationStatus"] = "approved",
        ["leaseStatus"] = "running",
        ["runId"] = Run,
        ["server"] = "deve-server.yinzon.com",
        ["database"] = "metarill_business_test",
        ["instancePin"] = new JsonObject { ["expectedHostname"] = "offline-host", ["expectedServerUuid"] = "12345678-abcd-4321-abcd-0123456789ab" },
        ["tables"] = JsonSerializer.SerializeToNode(
            from profile in new[] { "ss", "sl", "ls", "ll" }
            from suffix in new[] { "source", "country", "area", "place" }
            select $"mt_nrlock_{Run}_{profile}_{suffix}")
    };

    /// <summary>校验失败时不会进入占用或连接入口。</summary>
    private static CrossTableImportLockLease Validate(JsonObject scope) =>
        CrossTableImportLockLease.Validate(JsonSerializer.SerializeToElement(scope), "string", "int64");
}
