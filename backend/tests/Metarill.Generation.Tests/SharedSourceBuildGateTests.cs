using System.Diagnostics;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>子进程生命周期断言独占测试集合，避免无关独立包编译的资源争用改变就绪握手；不放宽超时或生产互斥。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BuildProcessLifecycleCollection
{
    public const string Name = "构建子进程生命周期隔离";
}

/// <summary>不启动构建或访问数据库，验证共享源码构建的排队、异常和取消边界。</summary>
[Collection(BuildProcessLifecycleCollection.Name)]
public sealed class SharedSourceBuildGateTests
{
    /// <summary>前一个构建未退出时不能进入第二个构建，普通 CLI 命令仍可立即执行。</summary>
    [Fact]
    public async Task Build_queue_does_not_serialize_other_commands_or_release_twice()
    {
        var gate = new SharedSourceBuildGate();
        using var first = (await gate.EnterAsync())!;
        var second = gate.EnterAsync().AsTask();
        Assert.False(second.IsCompleted);
        Assert.Null(await gate.EnterAsync("preview").AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        first.Dispose();
        using var next = await second.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        using var cancel = new CancellationTokenSource();
        var third = gate.EnterAsync(token: cancel.Token).AsTask();
        Assert.False(third.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
    }

    /// <summary>真实自有子进程停止并排空输出之前，下一构建不能取得名额；不执行编译或数据库操作。</summary>
    [Fact]
    public async Task Interrupted_child_exits_and_drains_both_streams_before_the_next_build()
    {
        var gate = new SharedSourceBuildGate();
        using var lease = (await gate.EnterAsync())!;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("process.stdout.write('ready\\nremaining\\n');process.stderr.write('diagnostic\\n');setInterval(()=>{},1000)");
        using var process = Process.Start(start)!;
        var ready = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 同一输出管道始终只有一个读取者，ready 超时也能在 finally 杀进程并排空。
        async Task<string> ReadOutputAsync()
        {
            ready.TrySetResult(await process.StandardOutput.ReadLineAsync());
            return await process.StandardOutput.ReadToEndAsync();
        }
        var output = ReadOutputAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            Assert.Equal("ready", await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            var waiting = gate.EnterAsync().AsTask(); Assert.False(waiting.IsCompleted);
            await SharedSourceBuildGate.StopOwnProcessAsync(process, output, error);
            Assert.True(process.HasExited); Assert.True(output.IsCompletedSuccessfully); Assert.True(error.IsCompletedSuccessfully);
            Assert.Contains("remaining", await output); Assert.Contains("diagnostic", await error);
            Assert.False(waiting.IsCompleted);
            lease.Dispose();
            using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await SharedSourceBuildGate.StopOwnProcessAsync(process, output, error);
        }
    }

    /// <summary>失败构建的作用域必须释放名额，取消等待不能偷走下一次合法构建的名额。</summary>
    [Fact]
    public async Task Failed_build_and_canceled_waiter_leave_the_next_build_available()
    {
        var gate = new SharedSourceBuildGate();
        using (await gate.EnterAsync())
        {
            using var cancel = new CancellationTokenSource();
            var waiting = gate.EnterAsync(token: cancel.Token).AsTask();
            Assert.False(waiting.IsCompleted);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var build = await gate.EnterAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("模拟构建失败，必须释放门闩。");
        });
        using var resumed = await gate.EnterAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(resumed);
    }
}
