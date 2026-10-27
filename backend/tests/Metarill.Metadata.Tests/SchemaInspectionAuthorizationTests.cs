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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.CatalogAsync(JsonSerializer.SerializeToElement(new { })));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => business.PreviewImportAsync(JsonSerializer.SerializeToElement(new { tableNames = new[] { "table" } })));
    }
    /// <summary>合法管理员的非法目录请求也在任何元数据或数据库读取前拒绝。</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("{\"database\":\"mysql\"}")]
    [InlineData("{\"sql\":\"SELECT 1\",\"sql\":\"SELECT 2\"}")]
    public async Task Invalid_catalog_requests_are_rejected_before_database_reads(string request)
    {
        using var fixture = new PublishedFileFixture();
        var context = new DefaultHttpContext();
        context.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(new RuntimeActor(11, "目录授权验收", true, "1", ""), new HashSet<string>(), 1, null);
        var authorization = new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = context }, fixture.Registry);
        var business = new ProjectSchemaInspectionBusiness(null!, null!, null!, authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() => business.CatalogAsync(JsonSerializer.Deserialize<JsonElement>(request)));
    }
    /// <summary>反向预览的非法选择在任何目录读取前拒绝，不能依靠数据库连接失败充当校验。</summary>
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"tableNames\":null}", false)]
    [InlineData("{\"tableNames\":[]}", false)]
    [InlineData("{\"tableNames\":[1]}", false)]
    [InlineData("{\"tableNames\":[\"other.table\"]}", true)]
    [InlineData("{\"tableNames\":[\"table\"],\"confirmed\":true}", true)]
    public async Task Invalid_import_preview_requests_are_rejected_before_metadata_reads(string request, bool invalidOperation)
    {
        using var fixture = new PublishedFileFixture(); var context = new DefaultHttpContext();
        context.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(new RuntimeActor(11, "反向预览验收", true, "1", ""), new HashSet<string>(), 1, null);
        var authorization = new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = context }, fixture.Registry);
        var business = new ProjectSchemaInspectionBusiness(null!, null!, null!, authorization);
        if (invalidOperation) await Assert.ThrowsAsync<InvalidOperationException>(() => business.PreviewImportAsync(JsonSerializer.Deserialize<JsonElement>(request)));
        else await Assert.ThrowsAsync<ArgumentException>(() => business.PreviewImportAsync(JsonSerializer.Deserialize<JsonElement>(request)));
    }
}
