using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Generation.Tests;

/// <summary>末段故障验收只消费本轮独立用途和固定生产宿主，不能复用已结束的成功初始化租约。</summary>
internal static class PlatformSeedRollbackNativePreparation
{
    internal const string Purpose = "platform-seed-rollback-native-v1";
    internal const string EnvironmentKey = "METARILL_PLATFORM_SEED_ROLLBACK_NATIVE_SETTINGS";

    /// <summary>专用配置须与随机双库配置完全相同；所有文件校验先于数据库连接或宿主启动。</summary>
    internal static PlatformBootstrapNativePreparation Read(string sourceRoot)
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentKey);
        if (string.IsNullOrWhiteSpace(path) || path != Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")) throw new InvalidOperationException("末段故障验收必须共用本轮唯一配置。");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return Read(json.RootElement, sourceRoot);
    }

    /// <summary>明确用途、一次租约与证据落点均验证，固定文件继续复用原生产宿主完整集合校验。</summary>
    internal static PlatformBootstrapNativePreparation Read(JsonElement settings, string sourceRoot)
    {
        string Text(string key) => settings.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var run = Text("RunId");
        if (Text("Purpose") != Purpose || Text("AuthorizationStatus") != "approved" || Text("LeaseStatus") != "running" || !Regex.IsMatch(run, "^[a-f0-9]{16}$")) throw new InvalidOperationException("末段故障正式验收未取得本轮运行租约。");
        var proof = settings.GetProperty("PlatformSeedRollbackPreparation").Deserialize<PlatformBootstrapNativePreparation>() ?? throw new InvalidOperationException("末段故障固定宿主证明缺失。");
        var expected = Path.Combine(Path.GetFullPath(sourceRoot), ".local", "platform-seed-rollback-native", run, "evidence");
        if (proof.EvidenceDirectory != expected || !Path.IsPathFullyQualified(proof.HostRoot) || Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.HostRoot)) != proof.HostRoot) throw new InvalidOperationException("末段故障验收目录不规范。");
        proof.Verify(sourceRoot);
        Directory.CreateDirectory(expected);
        return proof;
    }
}
