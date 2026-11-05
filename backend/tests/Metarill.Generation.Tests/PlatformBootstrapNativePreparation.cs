using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Generation.Tests;

/// <summary>当前平台生产宿主的固定来源；验收批准由外层单次执行器记录，不接受旧Agent租约。</summary>
internal sealed record PlatformBootstrapNativePreparation(string HostRoot, Dictionary<string, string> Files, Dictionary<string, string> MetadataFiles, string EvidenceDirectory)
{
    internal const string Purpose = "platform-bootstrap-native-v1";
    // 同库配置仅由冻结的正式验收方案提供，原开发验收仍默认分库。
    public bool SharePlatformDatabase { get; init; }
    // 授权和业务夹具只能由冻结的独占空库执行方案显式启用。
    public bool VerifyPublishedBusinessPermissions { get; init; }
    internal string AssemblyPath => Path.Combine(HostRoot, "Metarill.Api.dll");

    /// <summary>只有明确选择本轮正式配置才启用固定宿主，验证先于数据库连接和子进程。</summary>
    internal static PlatformBootstrapNativePreparation? ReadIfRequested(string sourceRoot)
    {
        var path = Environment.GetEnvironmentVariable("METARILL_PLATFORM_BOOTSTRAP_NATIVE_SETTINGS");
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path != Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")) throw new InvalidOperationException("平台正式验收必须共用本轮唯一配置。");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return Read(document.RootElement, sourceRoot);
    }

    /// <summary>验证用途、批准状态及固定发布目录，不创建资源或解释空值为授权。</summary>
    internal static PlatformBootstrapNativePreparation Read(JsonElement settings, string sourceRoot)
    {
        string Text(string key) => settings.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var run = Text("RunId");
        if (Text("Purpose") != Purpose || Text("AuthorizationStatus") != "approved" || Text("LeaseStatus") != "running" || !Regex.IsMatch(run, "^[a-f0-9]{16}$")) throw new InvalidOperationException("平台空库正式验收未取得本轮运行租约。");
        var proof = settings.GetProperty("PlatformBootstrapPreparation").Deserialize<PlatformBootstrapNativePreparation>() ?? throw new InvalidOperationException("平台固定宿主证明缺失。");
        var expected = Path.Combine(Path.GetFullPath(sourceRoot), ".local", "platform-bootstrap-native", run, "evidence");
        if (proof.EvidenceDirectory != expected || !Path.IsPathFullyQualified(proof.HostRoot) || Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.HostRoot)) != proof.HostRoot) throw new InvalidOperationException("平台正式验收目录不规范。");
        proof.Verify(sourceRoot);
        Directory.CreateDirectory(expected);
        return proof;
    }

    /// <summary>核对完整发布输出及四份实际目录；未知文件、链接、程序集或元数据漂移全部拒绝。</summary>
    internal void Verify(string sourceRoot)
    {
        var actual = CaptureHost(HostRoot);
        if (!actual.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Files.Keys) || Files.Any(file => !actual.TryGetValue(file.Key, out var digest) || digest != file.Value)) throw new InvalidOperationException("平台生产宿主完整集合或字节发生漂移。");
        foreach (var required in new[] { "Metarill.Api.dll", "Metarill.Api.deps.json", "Metarill.Api.runtimeconfig.json" })
            if (!Files.ContainsKey(required)) throw new InvalidOperationException("平台生产宿主缺少正式入口或依赖说明。");
        var metadata = new[] { "platform", "demo", "files", "security" }.ToDictionary(name => "backend/metadata/" + name + ".json", name => Hash(CSharpGenerator.SafePath(sourceRoot, "backend/metadata/" + name + ".json")), StringComparer.Ordinal);
        if (!metadata.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(MetadataFiles.Keys) || metadata.Any(file => !MetadataFiles.TryGetValue(file.Key, out var digest) || digest != file.Value)) throw new InvalidOperationException("平台正式验收元数据发生漂移。");
    }

    /// <summary>固定根与子文件均拒绝链接，不跟随目录到受审来源之外。</summary>
    internal static Dictionary<string, string> CaptureHost(string root)
    {
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("平台生产宿主不存在或为链接。");
        var result = new Dictionary<string, string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("平台生产宿主不能包含链接。");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(path));
            }
        return result;
    }

    /// <summary>仅记录摘要，不回显源码或配置秘密。</summary>
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
