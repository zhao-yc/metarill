using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>只验证纯文件批准边界及固定名单，不创建连接或远端对象。</summary>
public sealed class GlobalCachedCrossExcelMySqlLeaseTests
{
    /// <summary>旧用途、pending、未运行、结束和错误编号全部先于连接拒绝。</summary>
    [Theory]
    [InlineData("platform-seed-rollback-native-v1", "approved", "running", "0123456789abcdef")]
    [InlineData(GlobalCachedCrossExcelMySqlLease.Purpose, "pending", "not-started", "0123456789abcdef")]
    [InlineData(GlobalCachedCrossExcelMySqlLease.Purpose, "approved", "not-started", "0123456789abcdef")]
    [InlineData(GlobalCachedCrossExcelMySqlLease.Purpose, "approved", "ended", "0123456789abcdef")]
    [InlineData(GlobalCachedCrossExcelMySqlLease.Purpose, "approved", "running", "bad-run")]
    public void Unapproved_or_other_scope_is_rejected(string purpose, string authorization, string lease, string run)
    {
        var value = JsonSerializer.SerializeToElement(new { Purpose = purpose, AuthorizationStatus = authorization, LeaseStatus = lease, RunId = run });
        Assert.Throws<InvalidOperationException>(() => GlobalCachedCrossExcelMySqlLease.Read(value, 0, "string", "int64", false, false, false));
    }
    /// <summary>精确六模型32张表、共享两个六表模型及64字符标识符边界先离线核实。</summary>
    [Fact]
    public void Exact_complete_table_set_is_required_for_every_model()
    {
        const string run = "0123456789abcdef"; var map = GlobalCachedCrossExcelMySqlLease.ExpectedTables(run); var all = map.Values.SelectMany(value => value).ToArray();
        Assert.Equal(6, map.Count); Assert.Equal(32, all.Length); Assert.Equal(32, all.Distinct(StringComparer.Ordinal).Count()); Assert.Equal(2, map.Values.Count(value => value.Length == 6)); Assert.All(all, name => Assert.InRange(name.Length, 1, 64));
        foreach (var p in GlobalCachedCrossExcelMySqlLease.Profiles)
        {
            JsonElement Settings(string[] names, string database = "metarill_business_test") => JsonSerializer.SerializeToElement(new { Purpose = GlobalCachedCrossExcelMySqlLease.Purpose, AuthorizationStatus = "approved", LeaseStatus = "running", RunId = run, Server = "deve-server.yinzon.com", Database = database, Tables = names });
            var proof = GlobalCachedCrossExcelMySqlLease.Read(Settings(all), p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter);
            Assert.Equal(map[GlobalCachedCrossExcelMySqlLease.Profile(p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter)], proof.Tables);
            Assert.Throws<InvalidOperationException>(() => GlobalCachedCrossExcelMySqlLease.Read(Settings(all[..^1]), p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter));
            Assert.Throws<InvalidOperationException>(() => GlobalCachedCrossExcelMySqlLease.Read(Settings(all.Append("yz_project").ToArray()), p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter));
            Assert.Throws<InvalidOperationException>(() => GlobalCachedCrossExcelMySqlLease.Read(Settings(all.Select((name, i) => i == 0 ? all[1] : name).ToArray()), p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter));
            Assert.Throws<InvalidOperationException>(() => GlobalCachedCrossExcelMySqlLease.Read(Settings(all, "metarill_dev"), p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter));
        }
    }
}
