using Metarill.Core.Exports;
using Metarill.Core.Security;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Exports.Tests;

/// <summary>读取前门禁必须真实限制准备委托，并在失败、取消后释放额度。</summary>
public sealed class ExportAdmissionTests
{
    [Fact]
    public async Task Preparation_waits_for_writer_slot_and_receives_server_limit()
    {
        using var fixture = new ExportFixture(new ExportOptions { MaxConcurrentExports = 1, MaxRows = 3 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;
        var first = fixture.Service.CreateAsync(fixture.Actor, async (maximum, token) =>
        {
            Assert.Equal(3, maximum); entered.SetResult(); await release.Task.WaitAsync(token); return Request();
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.Service.CreateAsync(fixture.Actor, (maximum, token) => { secondEntered = true; Assert.Equal(3, maximum); return Task.FromResult(Request()); });
        Assert.False(secondEntered); Assert.False(second.IsCompleted);
        release.SetResult(); await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(secondEntered); Assert.Equal(2, Directory.GetFiles(fixture.Root).Length);
    }
    [Fact]
    public async Task Reserved_and_existing_ticket_capacity_reject_before_preparation()
    {
        using var fixture = new ExportFixture(new ExportOptions { MaxConcurrentExports = 2, MaxActiveTickets = 1 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = fixture.Service.CreateAsync(fixture.Actor, async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return Request(); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<PageExportRequest> Prepare(int maximum, CancellationToken token) { calls++; return Task.FromResult(Request()); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(fixture.Actor, Prepare));
        Assert.Equal(0, calls);
        release.SetResult(); await first.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(fixture.Actor, Prepare));
        Assert.Equal(0, calls);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16)); await fixture.Service.CreateAsync(fixture.Actor, Prepare);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Canceled_wait_and_failed_preparation_release_all_reservations()
    {
        using var fixture = new ExportFixture(new ExportOptions { MaxConcurrentExports = 1, MaxActiveTickets = 1 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.Service.CreateAsync(fixture.Actor, async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); throw new InvalidOperationException("准备失败"); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var canceled = new CancellationTokenSource(); var called = false;
        var waiting = fixture.Service.CreateAsync(fixture.Actor, (_, _) => { called = true; return Task.FromResult(Request()); }, canceled.Token);
        canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); Assert.False(called);
        release.SetResult(); await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        var ticket = await fixture.Service.CreateAsync(fixture.Actor, (_, _) => Task.FromResult(Request()));
        Assert.NotEmpty(ticket); Assert.Single(Directory.GetFiles(fixture.Root));
    }
    [Fact]
    public async Task Oversized_preparation_is_rejected_without_partial_export()
    {
        using var fixture = new ExportFixture(new ExportOptions { MaxRows = 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(fixture.Actor, (maximum, _) =>
        {
            Assert.Equal(1, maximum); return Task.FromResult(new PageExportRequest("p", "超限", ["列"], [[1], [2]]));
        }));
        Assert.False(Directory.Exists(fixture.Root));
        Assert.NotEmpty(await fixture.Service.CreateAsync(fixture.Actor, (_, _) => Task.FromResult(Request())));
    }
    [Fact]
    public async Task Legacy_only_host_fails_closed_without_executing_prepare()
    {
        IPageExportService service = new LegacyOnlyExport(); var called = false;
        await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateAsync(new RuntimeActor(1, "测试", false, "100", "1"), (_, _) => { called = true; return Task.FromResult(Request()); }));
        Assert.False(called);
    }
    /// <summary>构造最小真实工作簿输入，避免把测试与SQL实现绑定。</summary>
    private static PageExportRequest Request() => new("p", "门禁测试", ["列"], [["测试"]]);
    private sealed class LegacyOnlyExport : IPageExportService
    {
        /// <summary>旧实现不能被新调用绕过资源门禁。</summary>
        public Task<string> CreateAsync(PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不应调用旧入口。");
    }
}
