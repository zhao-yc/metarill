using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Schema;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class SchemaMetadataImportPlanTests
{
    /// <summary>会话/后台均为固定测试身份，不包含真实令牌。</summary>
    private static AuthenticatedIdentity Owner(long account = 1, long session = 2, string project = "123", string admin = "11") => new(new RuntimeActor(account, "导入计划测试", true, project, admin), new HashSet<string>(), session, null) { PrincipalProjectId = "1", PrincipalAdminTypeId = admin };
    /// <summary>结构由既有正向编译器建立，再提供明确目标身份给计划缓存。</summary>
    private static (SchemaImportInspection Source, MetadataCatalog Model) Model()
    {
        var source = new SchemaImportInspection(new("mysql", new("test_import", [new("source_table", [new("id", "bigint", false, true), new("name", "varchar(64)", true)])]), "source-fingerprint"), [], true, new("mysql", "test_import", "actual-target", true));
        return (source, SchemaImportDraftCompiler.Compile(source, "123").LogicalMetadata);
    }

    /// <summary>错误确认、跨用户/会话/项目/后台均不能消费计划；合法确认只能消费一次。</summary>
    [Fact]
    public void Plan_is_bound_to_real_context_and_consumed_once()
    {
        var cache = new SchemaMetadataImportPlans(); var (source, model) = Model(); var owner = Owner(); var plan = cache.Create(owner, source, "metadata-version", model);
        foreach (var other in new[] { Owner(account: 9), Owner(session: 9), Owner(project: "456"), Owner(admin: "99") })
        { Assert.Throws<InvalidOperationException>(() => cache.Take(plan.PlanId, plan.ConfirmSha256, other)); Assert.Equal("unknown", cache.Status(plan.PlanId, plan.ConfirmSha256, other).State); }
        Assert.Throws<InvalidOperationException>(() => cache.Take(plan.PlanId, "wrong", owner)); Assert.Equal("prepared", cache.Status(plan.PlanId, plan.ConfirmSha256, owner).State);
        var binding = cache.Take(plan.PlanId, plan.ConfirmSha256, owner); Assert.Equal("processing", cache.Status(plan.PlanId, plan.ConfirmSha256, owner).State);
        Assert.Throws<InvalidOperationException>(() => cache.Take(plan.PlanId, plan.ConfirmSha256, owner)); cache.Unconfirmed(binding);
        Assert.Equal("unconfirmed", cache.Status(plan.PlanId, plan.ConfirmSha256, owner).State); Assert.Null(cache.Status(plan.PlanId, plan.ConfirmSha256, owner).ConfigurationSaved);
    }

    /// <summary>客户端改审核模型不能更改服务器绑定；已确认提交的回执不被后续源连接异常抹除。</summary>
    [Fact]
    public void Committed_receipt_is_immutable_and_unknown_does_not_mean_not_written()
    {
        var cache = new SchemaMetadataImportPlans(); var (source, model) = Model(); var owner = Owner(); var plan = cache.Create(owner, source, "version", model);
        var original = SchemaMetadataImportPlans.Fingerprint(model); model.ConfigTables.Clear();
        var binding = cache.Take(plan.PlanId, plan.ConfirmSha256, owner); Assert.Equal(original, binding.ModelFingerprint);
        var tables = new List<ImportedSchemaTable> { new("100", "source_table", new[] { "101" }, new[] { "102" }) };
        cache.Committed(binding, new("123", plan.PlanId, tables)); tables.Clear(); cache.Unconfirmed(binding);
        var status = cache.Status(plan.PlanId, plan.ConfirmSha256, owner); Assert.Equal("saved", status.State); Assert.True(status.ConfigurationSaved); Assert.Single(status.Tables);
        var restarted = new SchemaMetadataImportPlans().Status(plan.PlanId, plan.ConfirmSha256, owner); Assert.Equal("unknown", restarted.State); Assert.Null(restarted.ConfigurationSaved);
    }

    /// <summary>过期计划不能执行，执行期间过期也不能提交；缓存上限明确拒绝而非丢失旧计划。</summary>
    [Fact]
    public void Expiry_and_per_session_budget_are_enforced()
    {
        var clock = new Clock(); var cache = new SchemaMetadataImportPlans(clock); var (source, model) = Model(); var owner = Owner();
        var plans = Enumerable.Range(0, 8).Select(_ => cache.Create(owner, source, "version", model)).ToArray();
        Assert.Throws<InvalidOperationException>(() => cache.Create(owner, source, "version", model));
        var taken = cache.Take(plans[0].PlanId, plans[0].ConfirmSha256, owner); clock.Now = clock.Now.AddMinutes(6);
        Assert.Throws<InvalidOperationException>(() => cache.RequireNotExpired(taken)); Assert.Throws<InvalidOperationException>(() => cache.Take(plans[1].PlanId, plans[1].ConfirmSha256, owner));
        Assert.Equal("unknown", cache.Status(plans[1].PlanId, plans[1].ConfirmSha256, owner).State); cache.Create(owner, source, "version", model);
    }

    /// <summary>真实并行消费只有一个请求可以取得执行资格，不靠串行测试推断原子性。</summary>
    [Fact]
    public async Task Concurrent_take_has_exactly_one_winner()
    {
        var cache = new SchemaMetadataImportPlans(); var (source, model) = Model(); var owner = Owner(); var plan = cache.Create(owner, source, "version", model);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => { try { cache.Take(plan.PlanId, plan.ConfirmSha256, owner); return true; } catch (InvalidOperationException) { return false; } })));
        Assert.Equal(1, outcomes.Count(outcome => outcome));
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        /// <summary>只影响计划测试，不调整系统或数据库时间。</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
