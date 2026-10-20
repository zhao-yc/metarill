using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>不启动构建或访问数据库，验证共享源码构建的排队、异常和取消边界。</summary>
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
