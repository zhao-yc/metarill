using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原浏览器的用途和原Web集合护栏；合成文件不计HBuilderX或原页面业务通过。</summary>
public sealed class DeviceRuntimeBrowserPreparationTests
{
    /// <summary>两个正式入口不能互借旧用途；在缺省证明或连接读取前拒绝。</summary>
    [Theory]
    [InlineData(DeviceRuntimeBrowserPreparation.Purpose, false)]
    [InlineData(DeviceRuntimeNativePreparation.Purpose, true)]
    public void Browser_and_http_purposes_do_not_cross(string purpose, bool browser)
    {
        var settings = JsonSerializer.SerializeToElement(new { Purpose = purpose, AuthorizationStatus = "approved", LeaseStatus = "running", RunId = "0123456789abcdef" });
        Assert.Equal("设备批量正式验收未取得本轮精确运行租约。", Assert.Throws<InvalidOperationException>(() => DeviceRuntimeNativePreparation.Read(settings, Path.GetTempPath(), browser)).Message);
    }

    /// <summary>未知、修改产物和原源输入漂移都拒绝，不能替换原页面后仍冒称原Web证明。</summary>
    [Fact]
    public async Task Original_web_proof_rejects_asset_extra_file_and_source_drift()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-browser-proof"); var root = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var source = Path.Combine(root, "source"); var frontend = Path.Combine(root, "frontend-project"); var web = Path.Combine(frontend, "unpackage", "dist", "build", "web"); var manifest = Path.Combine(source, "frontend", "metarill_uni_admin", "manifest.json");
        Directory.CreateDirectory(web); Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(web, "index.html"), "仅做摘要护栏"); await File.WriteAllTextAsync(manifest, "{}");
            var proof = new DeviceRuntimeBrowserPreparation(frontend, PlatformBootstrapNativePreparation.CaptureHost(web), new() { ["frontend/metarill_uni_admin/manifest.json"] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(manifest))) }); proof.Verify(source);
            var extra = Path.Combine(web, "new-page.html"); await File.WriteAllTextAsync(extra, "未知页面"); Assert.Throws<InvalidOperationException>(() => proof.Verify(source)); File.Delete(extra);
            await File.AppendAllTextAsync(Path.Combine(web, "index.html"), "改写"); Assert.Throws<InvalidOperationException>(() => proof.Verify(source)); await File.WriteAllTextAsync(Path.Combine(web, "index.html"), "仅做摘要护栏");
            await File.AppendAllTextAsync(manifest, "改写"); Assert.Throws<InvalidOperationException>(() => proof.Verify(source));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root, boundary); }
    }
}
