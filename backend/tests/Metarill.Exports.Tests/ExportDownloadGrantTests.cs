using Metarill.Core.Exports;
using Metarill.Core.Security;
using Metarill.Runtime.Exports;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Exports.Tests;

/// <summary>原页面无头下载使用明确签发的短凭证，不能把普通账号票据一律变成公开文件。</summary>
public sealed class ExportDownloadGrantTests
{
    private static PageExportRequest Request() => new("page1", "限时下载", ["名称"], [["本轮固定内容"]]);

    [Fact]
    public async Task Only_explicit_grants_work_without_headers_and_expire_after_two_minutes()
    {
        using var fixture = new ExportFixture();
        var ordinary = await fixture.Service.CreateAsync(Request(), fixture.Actor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenDownloadAsync(ordinary));
        var grant = await fixture.Service.CreateDownloadAsync(fixture.Actor, (_, _) => Task.FromResult(Request()));
        var content = await fixture.Service.OpenDownloadAsync(grant);
        await using (content.Stream) { Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(2), content.ExpiresAt); Assert.Equal((int)'P', content.Stream.ReadByte()); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenAsync(grant, fixture.Actor with { AccountId = 9 }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenAsync(grant, fixture.Actor with { ProjectId = "other" }));
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenDownloadAsync(grant));
        var authenticated = await fixture.Service.OpenAsync(grant, fixture.Actor); await authenticated.Stream.DisposeAsync();
        Assert.Equal(2, Directory.GetFiles(fixture.Root).Length);
        fixture.Clock.Advance(TimeSpan.FromMinutes(13));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenAsync(grant, fixture.Actor));
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task Short_file_ttl_and_restart_also_revoke_the_grant()
    {
        using var fixture = new ExportFixture(new ExportOptions { TimeToLive = TimeSpan.FromSeconds(30) });
        var grant = await fixture.Service.CreateDownloadAsync(fixture.Actor, (_, _) => Task.FromResult(Request()));
        var content = await fixture.Service.OpenDownloadAsync(grant); await content.Stream.DisposeAsync();
        Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(30), content.ExpiresAt);
        var restarted = new ExcelPageExportService(new ExportOptions { RootDirectory = fixture.Root }, fixture.Clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restarted.OpenDownloadAsync(grant));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenDownloadAsync(grant));
    }

    [Fact]
    public async Task Unknown_tokens_and_cancelled_issuance_do_not_create_a_download_capability()
    {
        using var fixture = new ExportFixture();
        foreach (var token in new[] { "", new string('f', 64), "../secret", "https://evil.invalid", new string('A', 64) })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenDownloadAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateDownloadAsync(fixture.Actor, (_, _) => Task.FromResult(Request()), new(true)));
        if (Directory.Exists(fixture.Root)) Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("GET", "", "?exportId=TOKEN", true)]
    [InlineData("HEAD", "", "?exportId=TOKEN", true)]
    [InlineData("POST", "", "?exportId=TOKEN", false)]
    [InlineData("GET", "loginStateId", "?exportId=TOKEN", false)]
    [InlineData("GET", "Authorization", "?exportId=TOKEN", false)]
    [InlineData("GET", "", "?exportId=TOKEN&projectId=1", false)]
    [InlineData("GET", "", "?exportId=TOKEN&exportId=TOKEN", false)]
    [InlineData("GET", "", "?exportId=../secret", false)]
    public void Anonymous_exception_has_exact_method_path_query_and_no_explicit_headers(string method, string header, string query, bool allowed)
    {
        var context = new DefaultHttpContext(); context.Request.Method = method; context.Request.Path = ExportDownloadAccess.DownloadPath;
        context.Request.QueryString = new(query.Replace("TOKEN", new string('a', 64), StringComparison.Ordinal));
        if (header.Length > 0) context.Request.Headers[header] = ""; // 即使显式空认证头也不能回退无头凭证。
        Assert.Equal(allowed, ExportDownloadAccess.CanUseGrant(context.Request));
        context.Request.Path += "/extra"; Assert.False(ExportDownloadAccess.CanUseGrant(context.Request));
    }

    [Fact]
    public async Task Hosts_that_only_implement_authenticated_export_fail_closed_before_preparing_data()
    {
        IPageExportService legacy = new LegacyExport(); var called = false;
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.CreateDownloadAsync(new(1, "测试", true, "1", "1"), (_, _) => { called = true; return Task.FromResult(Request()); }));
        Assert.False(called);
    }
    private sealed class LegacyExport : IPageExportService
    {
        public Task<string> CreateAsync(PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default) => Task.FromResult("旧账号票据");
    }
}
