using System.Diagnostics;
using System.Net;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>测试探针预算本身；实际独立包HTTP仍由ApplicationPackagerTests验证，不能互相替代。</summary>
public sealed class StandaloneCorsHttpProbeTests
{
    /// <summary>由真实HttpClient单次期限取消首个请求，第二个请求仍可在总预算内就绪。</summary>
    [Fact]
    public async Task Single_http_timeout_does_not_end_the_overall_startup_budget()
    {
        var calls = 0;
        using var handler = new ProbeHandler(async token =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1"), Timeout = TimeSpan.FromMilliseconds(30) };
        Assert.True(await StandaloneCorsHttpProbe.WaitUntilReadyAsync(client, () => false, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, calls);
    }

    /// <summary>总预算可以中断正在等待的health；不能把有限重试变成100次单次超时累加。</summary>
    [Fact]
    public async Task Overall_deadline_bounds_the_active_health_request()
    {
        using var handler = new ProbeHandler(async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(HttpStatusCode.OK); });
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1"), Timeout = TimeSpan.FromSeconds(5) };
        var elapsed = Stopwatch.StartNew();
        Assert.False(await StandaloneCorsHttpProbe.WaitUntilReadyAsync(client, () => false, TimeSpan.FromMilliseconds(80)));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), "启动整体预算未能取消仍在等待的HTTP探测。");
    }

    /// <summary>外部取消必须抛回调用者，不得伪装成可重试单次超时或普通启动失败。</summary>
    [Fact]
    public async Task External_cancellation_is_not_swallowed()
    {
        using var handler = new ProbeHandler(async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(HttpStatusCode.OK); });
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1"), Timeout = TimeSpan.FromSeconds(5) };
        using var external = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StandaloneCorsHttpProbe.WaitUntilReadyAsync(client, () => false, TimeSpan.FromSeconds(5), external.Token));
    }

    /// <summary>子进程提前退出或启动期限耗尽都保留自身输出；退出后不再探测，仍运行时仅结束本进程。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Child_failure_reports_its_own_stdout_and_stderr(bool stillRunning)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-e"); info.ArgumentList.Add("console.log('probe-stdout-marker');console.error('probe-stderr-marker');" + (stillRunning ? "setInterval(()=>{},1000);" : "process.exitCode=7;"));
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); if (!stillRunning) await process.WaitForExitAsync();
        using var handler = new ProbeHandler(_ => stillRunning ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) : throw new InvalidOperationException("进程已退出，不应发送HTTP。")); using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1") };
        var error = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => StandaloneCorsHttpProbe.EnsureReadyAsync(client, process, stdout, stderr, TimeSpan.FromSeconds(stillRunning ? 2 : 1)));
        Assert.True(process.HasExited); if (!stillRunning) Assert.Contains("退出码：7", error.Message); Assert.Contains("标准输出：", error.Message); Assert.Contains("probe-stdout-marker", error.Message); Assert.Contains("标准错误：", error.Message); Assert.Contains("probe-stderr-marker", error.Message);
    }

    /// <summary>handler只控制探针单元场景，不注册宿主、业务接口或模拟数据库成功。</summary>
    private sealed class ProbeHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(cancellationToken);
    }
}
