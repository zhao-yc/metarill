using System.Diagnostics;

namespace Metarill.Generation.Tests;

/// <summary>同一测试进程中，只有写入共享源码项目 obj/bin 的嵌套构建需要串行。</summary>
internal sealed class SharedSourceBuildGate
{
    internal static SharedSourceBuildGate Shared { get; } = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>构建排队不占用子进程超时；普通 CLI 命令无需等待，取消的排队不取得租约。</summary>
    internal async ValueTask<IDisposable?> EnterAsync(string? command = "build", CancellationToken token = default)
    {
        if (!string.Equals(command, "build", StringComparison.Ordinal)) return null;
        await gate.WaitAsync(token);
        return new Lease(gate);
    }

    /// <summary>只停止调用方启动的进程树；确认退出并读完两条管道后，外层才能释放构建租约。</summary>
    internal static async Task StopOwnProcessAsync(Process process, Task<string> output, Task<string> error, Exception? interrupted = null)
    {
        try
        {
            // Kill 返回不表示退出完成；即使停止失败，也必须等到该进程真正退出才允许后续构建。
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            finally { await process.WaitForExitAsync(); await Task.WhenAll(output, error); }
        }
        catch (Exception cleanup) when (interrupted is not null)
        {
            throw new AggregateException("嵌套构建中断且进程终止核验失败，保留原始及清理异常。", interrupted, cleanup);
        }
    }

    /// <summary>作用域退出或异常都释放唯一租约，重复 Dispose 不会多释放构建名额。</summary>
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? held = gate;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
