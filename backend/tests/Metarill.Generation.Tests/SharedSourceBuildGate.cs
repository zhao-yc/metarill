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

    /// <summary>作用域退出或异常都释放唯一租约，重复 Dispose 不会多释放构建名额。</summary>
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? held = gate;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
