using System.Diagnostics;
using System.Net;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>测试探针预算本身；实际独立包HTTP仍由ApplicationPackagerTests验证，不能互相替代。</summary>
public sealed class StandaloneCorsHttpProbeTests
{
    /// <summary>可控模拟首个请求单次超时取消，整体token仍有效；真实宿主HTTP由独立包用例另验。</summary>
    [Fact]
    public async Task Single_http_timeout_does_not_end_the_overall_startup_budget()
    {
        var calls = 0;
        using var handler = new ProbeHandler(token =>
        {
            Assert.False(token.IsCancellationRequested);
            if (Interlocked.Increment(ref calls) == 1) return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1") };
        Assert.True(await StandaloneCorsHttpProbe.WaitUntilReadyAsync(client, () => false, TimeSpan.FromSeconds(10)));
        Assert.Equal(2, calls);
    }

    /// <summary>总预算可以中断正在等待的health；不能把有限重试变成100次单次超时累加。</summary>
    [Fact]
    public async Task Overall_deadline_bounds_the_active_health_request()
    {
        var clock = new DeadlineClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        using var handler = new ProbeHandler(async token => { requestToken = token; entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(HttpStatusCode.OK); });
        // 单次请求没有自带期限，必须由整体预算取消；10秒仅防止有缺陷的测试永久挂起。
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1"), Timeout = Timeout.InfiniteTimeSpan };
        var pending = StandaloneCorsHttpProbe.WaitUntilReadyAsync(client, () => false, TimeSpan.FromMilliseconds(80), deadlineClock: clock);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromMilliseconds(80), clock.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Period);
        Assert.Equal(1, clock.TimerCount);
        Assert.False(requestToken.IsCancellationRequested);
        Assert.False(pending.IsCompleted);
        clock.Fire();
        Assert.True(requestToken.IsCancellationRequested, "整体预算必须传入正在等待的HTTP请求。");
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
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

    /// <summary>只控制预算计时器，精确断言注册期限和取消传递，不推进宿主或模拟其健康响应。</summary>
    private sealed class DeadlineClock : TimeProvider
    {
        private DeadlineTimer? timer;
        internal TimeSpan DueTime { get; private set; }
        internal TimeSpan Period { get; private set; }
        internal int TimerCount { get; private set; }
        /// <summary>记录探针实际申请的预算，回调由测试显式触发。</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime; Period = period; TimerCount++;
            return timer = new DeadlineTimer(callback, state, (due, repeat) => { DueTime = due; Period = repeat; });
        }
        /// <summary>触发已注册的真实取消回调，不能直接完成HTTP或替代其token。</summary>
        internal void Fire() => (timer ?? throw new InvalidOperationException("预算计时器尚未注册。")).Fire();
        /// <summary>实现预算计时器的生命周期，不创建系统线程或额外期限。</summary>
        private sealed class DeadlineTimer(TimerCallback callback, object? state, Action<TimeSpan, TimeSpan> reschedule) : ITimer
        {
            private bool disposed;
            /// <summary>仅在有效计时器上触发原回调。</summary>
            internal void Fire() { ObjectDisposedException.ThrowIf(disposed, this); callback(state); }
            /// <summary>遵守ITimer重排合同，断言观察最终申请期限，不绑定框架内部的安排顺序。</summary>
            public bool Change(TimeSpan dueTime, TimeSpan period) { if (disposed) return false; reschedule(dueTime, period); return true; }
            /// <summary>释放后不能再触发计时器。</summary>
            public void Dispose() => disposed = true;
            /// <summary>异步释放与同步释放采用相同状态。</summary>
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    /// <summary>handler只控制探针单元场景，不注册宿主、业务接口或模拟数据库成功。</summary>
    private sealed class ProbeHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(cancellationToken);
    }
}
