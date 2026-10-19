using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class ConfigurationMutationGuardTests
{
    [Fact]
    public async Task Platform_guard_rejects_forged_identity_wrong_table_and_missing_transaction_before_locking()
    {
        using var fixture = new PublishedFileFixture();
        fixture.Registry.Platform.ConfigTables.Single().TableName = "config_column";
        var actor = new RuntimeActor(7, "真实管理员", true, "1", "");
        var http = new DefaultHttpContext(); http.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(actor, new HashSet<string>(), 1, null);
        var authorization = new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = http }, fixture.Registry);
        var guard = new ConfigurationMutationGuard(new SqlRowStore(fixture.Sessions.Platform, fixture.Registry.Platform), fixture.Registry, authorization);
        var preparation = new RecordMutationPreparation("100", "config_column", RecordMutationOperation.Update, actor, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.BeforeMutationAsync(preparation with { Actor = actor with { AccountId = 9 } }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.BeforeMutationAsync(preparation with { Actor = actor with { ProjectId = "2" } }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.BeforeMutationAsync(preparation with { TableName = "business_record" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.BeforeMutationAsync(preparation));
        // 普通业务页不进入平台元数据事务约束；其宿主仍执行自己的权限和记录规则。
        await guard.BeforeMutationAsync(preparation with { PageId = "200", TableName = "business_record" });
        http.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(actor with { IsAdministrator = false }, new HashSet<string>(), 1, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.BeforeMutationAsync(preparation));
    }
    [MetadataIntegrationFact]
    public async Task Guard_rejects_another_database_session_transaction_even_for_the_authenticated_administrator()
    {
        await using var fixture = await MetadataWorkbenchFixture.CreateAsync();
        using var other = new DatabaseSession(fixture.Options);
        var actor = new RuntimeActor(8015, "准备事务验收", true, fixture.ProjectId.ToString(), "");
        var http = new DefaultHttpContext(); http.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(actor, new HashSet<string>(), 1, null);
        var authorization = new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = http }, fixture.Registry);
        var guard = new ConfigurationMutationGuard(fixture.Store, fixture.Registry, authorization);
        var table = fixture.Registry.Platform.ConfigTables.Single(candidate => candidate.TableName == "config_column");
        var page = fixture.Registry.Platform.ConfigPages.First(candidate => candidate.ConfigTableId == table.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.InTransactionAsync(async () =>
        {
            await guard.BeforeMutationAsync(new(page.Id, table.TableName, RecordMutationOperation.Update, actor, other.CurrentTransaction!));
            return true;
        }));
        Assert.Null(other.CurrentTransaction); Assert.Null(fixture.Session.CurrentTransaction);
    }

}
