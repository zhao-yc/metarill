using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>设备批量正式验收专用批准，普通CI或其它实库配置不能启用。</summary>
public sealed class DeviceRuntimeNativeMySqlFactAttribute : FactAttribute
{
    public DeviceRuntimeNativeMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_DEVICE_RUNTIME_NATIVE_SETTINGS"))) Skip = "未提供本轮设备关联与批量运行时专属租约，真实验收未执行。";
    }
}

/// <summary>当前生产宿主、固定CLI及旧发布源码共同约束本轮本机写入；不自行创建或批准租约。</summary>
internal sealed record DeviceRuntimeNativePreparation(PlatformBootstrapNativePreparation Host, string CliRoot, Dictionary<string,string> CliFiles, string BaselineSourceRoot, Dictionary<string,string> BaselineFiles, string ExecutionRoot)
{
    internal const string Purpose = "device-runtime-batch-native-v1";
    internal const string BaselineCommit = "1e57bb94acbb8f74538be9d4b10647bca1d884f3";
    internal const string SecondProject = "2200000000000200000", UnboundProject = "2200000000000300000", DisabledProject = "2200000000000400000";

    /// <summary>专用用途和running状态必须先于读连接、生成目标、启动进程及任何SQL。</summary>
    internal static (string Run, DeviceRuntimeNativePreparation Proof) Read(JsonElement settings, string sourceRoot, bool browser = false)
    {
        string Text(string key) => settings.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var run = Text("RunId");
        if (Text("Purpose") != (browser ? DeviceRuntimeBrowserPreparation.Purpose : Purpose) || Text("AuthorizationStatus") != "approved" || Text("LeaseStatus") != "running" || !Regex.IsMatch(run, "^[a-f0-9]{16}$")) throw new InvalidOperationException("设备批量正式验收未取得本轮精确运行租约。");
        var proof = settings.GetProperty("DeviceRuntimePreparation").Deserialize<DeviceRuntimeNativePreparation>() ?? throw new InvalidOperationException("设备批量固定来源缺失。");
        var parent = Directory.GetParent(Path.GetFullPath(sourceRoot))!.FullName;
        if (proof.Host.HostRoot != Path.Combine(parent, "production-host") || proof.CliRoot != Path.Combine(parent, "fixed-cli") || proof.ExecutionRoot != Path.Combine(parent, "execution", run) || Directory.Exists(proof.ExecutionRoot) || proof.BaselineSourceRoot != Path.Combine(parent, "baseline-source") || proof.Host.EvidenceDirectory != Path.Combine(sourceRoot, ".local", "device-runtime-native", run, "evidence")) throw new InvalidOperationException("设备批量正式目录不规范或已消费。");
        proof.Verify(sourceRoot);
        return (run, proof);
    }

    /// <summary>旧源只用于创建真实旧运行时目标；当前固定源负责预览与应用，禁止改受管摘要制造升级。</summary>
    internal void Verify(string sourceRoot)
    {
        Host.Verify(sourceRoot);
        var files = PlatformBootstrapNativePreparation.CaptureHost(CliRoot);
        if (!files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(CliFiles.Keys) || CliFiles.Any(file => !files.TryGetValue(file.Key, out var digest) || digest != file.Value) || !CliFiles.ContainsKey("Metarill.Cli.dll")) throw new InvalidOperationException("设备批量固定CLI发生漂移。");
        if (File.ReadAllText(Path.Combine(BaselineSourceRoot, ".git", "HEAD")).Trim() != BaselineCommit || BaselineFiles.Count == 0) throw new InvalidOperationException("旧发布来源提交不符。");
        foreach (var file in BaselineFiles)
            if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(CSharpGenerator.SafePath(BaselineSourceRoot, file.Key)))) != file.Value) throw new InvalidOperationException("旧发布源码发生漂移。");
    }
}
