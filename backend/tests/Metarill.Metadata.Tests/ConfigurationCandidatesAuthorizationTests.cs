using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class ConfigurationCandidatesAuthorizationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Anonymous_non_administrator_and_agent_contexts_are_rejected_before_database_access(bool authenticated, bool administrator, bool agent)
    {
        using var fixture = new PublishedFileFixture();
        var context = new DefaultHttpContext(); var actor = new RuntimeActor(11, "候选授权测试", administrator, "1", "");
        if (authenticated) context.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(actor, new HashSet<string>(), 1, agent ? 1 : null);
        var authorization = new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = context }, fixture.Registry);
        var store = new SqlRowStore(fixture.Sessions.Platform, fixture.Registry.Platform);
        var metadata = new MetadataRepository(store, fixture.Registry);
        var security = new SecurityRealmMutationGuard(store, fixture.Registry, authorization, new(store, fixture.Registry, new PlatformSecurityCurrentResources(store,metadata), new PlatformSecurityCurrentSessionPolicy(store)));
        var business = new ConfigurationCandidatesBusiness(store, metadata, fixture.Registry, authorization, new(store, fixture.Registry, authorization, security));
        var empty = JsonSerializer.SerializeToElement(new { }); var id = JsonSerializer.SerializeToElement(new { id = "11" });
        // 虚构连接位于回环无效端口；若鉴权顺序错误，测试会出现连接错误而非权限拒绝。
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.TablesAsync(empty));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.PagesAsync(empty));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.ControlMetadataAsync(id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.ReferencesAsync(id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.ClearPageControlDetailAsync(id));
    }
}
