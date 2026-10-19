using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class SchemaInspectionAuthorizationTests
{
    /// <summary>无效回环连接确保拒绝发生于查库之前，而非依靠连接失败掩盖授权缺口。</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Unauthorized_contexts_are_rejected_before_schema_or_metadata_reads(bool authenticated, bool administrator, bool agent)
    {
        using var fixture = new PublishedFileFixture();
        var context = new DefaultHttpContext();
        if (authenticated) context.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(new RuntimeActor(11, "结构授权验收", administrator, "1", ""), new HashSet<string>(), 1, agent ? 1 : null);
        var authorization = new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = context }, fixture.Registry);
        var store = new SqlRowStore(fixture.Sessions.Platform, fixture.Registry.Platform);
        var business = new ProjectSchemaInspectionBusiness(new(store, fixture.Registry), null!, null!, authorization);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.InspectAsync(JsonSerializer.SerializeToElement(new { })));
    }
}
