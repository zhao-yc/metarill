using System.Diagnostics;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>只证明正式门禁先于副作用；不把失效配置或回环宿主阴性当作实库通过。</summary>
public sealed class DeviceRuntimeNativePreparationTests
{
    /// <summary>旧用途、未批准、已消费和坏编号在读取任何证明或连接之前明确拒绝。</summary>
    [Theory]
    [InlineData("full-management-native-v1", "approved", "running", "0123456789abcdef")]
    [InlineData(DeviceRuntimeNativePreparation.Purpose, "pending", "running", "0123456789abcdef")]
    [InlineData(DeviceRuntimeNativePreparation.Purpose, "approved", "not-started", "0123456789abcdef")]
    [InlineData(DeviceRuntimeNativePreparation.Purpose, "approved", "ended", "0123456789abcdef")]
    [InlineData(DeviceRuntimeNativePreparation.Purpose, "approved", "running", "../bad")]
    public void Invalid_native_lease_rejects_before_proof_or_connection(string purpose, string approved, string status, string run)
    {
        var settings = JsonSerializer.SerializeToElement(new { Purpose = purpose, AuthorizationStatus = approved, LeaseStatus = status, RunId = run });
        var error = Assert.Throws<InvalidOperationException>(() => DeviceRuntimeNativePreparation.Read(settings, Path.GetTempPath())); Assert.Equal("设备批量正式验收未取得本轮精确运行租约。", error.Message);
    }

    /// <summary>重启端口只复用原回环根地址，坏地址在启动任何进程之前拒绝。</summary>
    [Theory]
    [InlineData("https://127.0.0.1:18777/")]
    [InlineData("http://example.com:18777/")]
    [InlineData("http://127.0.0.1:18777/path")]
    [InlineData("http://127.0.0.1:18777/?query=1")]
    public async Task Restart_address_rejects_before_child_process(string address)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => FreshHost.StartAsync(new ProcessStartInfo("不存在且不得启动的验收进程"), "/health", new Uri(address)));
    }
}
