using System.Security.Cryptography;
using System.Text.Json;
namespace Metarill.Generation.Tests;

/// <summary>原浏览器专用入口；旧后台/设备租约不能启用人工页面验收。</summary>
public sealed class DeviceRuntimeBrowserNativeFactAttribute : Xunit.FactAttribute
{
    public DeviceRuntimeBrowserNativeFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_DEVICE_RUNTIME_BROWSER_SETTINGS"))) Skip = "未提供原浏览器真实链路专属租约，页面业务验收未执行。";
    }
}

/// <summary>原Web构建产物与前端源码绑定；只服务HBuilderX实际产物，不替换页面、登录或接口响应。</summary>
internal sealed record DeviceRuntimeBrowserPreparation(string FrontendRoot, Dictionary<string,string> WebFiles, Dictionary<string,string> FrontendSourceFiles)
{
    internal const string Purpose = "device-runtime-browser-native-v1";
    /// <summary>明确的原浏览器入口核验完整Web集合与原源码，先于任何数据库或浏览器副作用。</summary>
    internal static DeviceRuntimeBrowserPreparation Read(JsonElement settings, string sourceRoot)
    {
        var proof = settings.GetProperty("BrowserPreparation").Deserialize<DeviceRuntimeBrowserPreparation>() ?? throw new InvalidOperationException("原浏览器固定Web证明缺失。");
        if (proof.FrontendRoot != Path.Combine(Directory.GetParent(sourceRoot)!.FullName, "frontend-project")) throw new InvalidOperationException("原浏览器固定Web根不符。");
        proof.Verify(sourceRoot); return proof;
    }
    /// <summary>未知产物或源码漂移均拒绝；原源目录和H5输出不能被逐页替代。</summary>
    internal void Verify(string sourceRoot)
    {
        var actual = PlatformBootstrapNativePreparation.CaptureHost(Path.Combine(FrontendRoot, "unpackage", "dist", "build", "web"));
        if (!actual.ContainsKey("index.html") || !actual.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(WebFiles.Keys) || WebFiles.Any(file => !actual.TryGetValue(file.Key, out var hash) || hash != file.Value) || FrontendSourceFiles.Count == 0) throw new InvalidOperationException("原浏览器Web完整产物发生漂移。");
        foreach (var file in FrontendSourceFiles)
        {
            if (!file.Key.StartsWith("frontend/metarill_uni_admin/", StringComparison.Ordinal) || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(CSharpGenerator.SafePath(sourceRoot, file.Key)))) != file.Value) throw new InvalidOperationException("原浏览器前端输入发生漂移。");
        }
    }
}
