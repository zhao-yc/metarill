using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>只验证正式入口的文件与批准边界；合成文件从不启动宿主或连接数据库。</summary>
public sealed class PlatformBootstrapNativePreparationTests
{
    /// <summary>其它用途、pending、旧租约及错误编号必须在创建证据目录前拒绝。</summary>
    [Theory]
    [InlineData("agent", "approved", "running", "0123456789abcdef")]
    [InlineData(PlatformBootstrapNativePreparation.Purpose, "pending", "running", "0123456789abcdef")]
    [InlineData(PlatformBootstrapNativePreparation.Purpose, "approved", "not-started", "0123456789abcdef")]
    [InlineData(PlatformBootstrapNativePreparation.Purpose, "approved", "ended", "0123456789abcdef")]
    [InlineData(PlatformBootstrapNativePreparation.Purpose, "approved", "running", "bad-run")]
    public void Invalid_lease_rejects_before_files_or_connections(string purpose, string authorization, string lease, string run)
    {
        var settings = JsonSerializer.SerializeToElement(new { Purpose = purpose, AuthorizationStatus = authorization, LeaseStatus = lease, RunId = run });
        Assert.Throws<InvalidOperationException>(() => PlatformBootstrapNativePreparation.Read(settings, Path.GetTempPath()));
    }

    /// <summary>同一生产校验器逐字检查集合、程序集及元数据，不把测试占位字节当作实际编译通过。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fixed_file_gate_rejects_changed_binary_extra_file_and_metadata(bool sharePlatformDatabase)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-platform-bootstrap-proof"); var root = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var host = Path.Combine(root, "host"); var metadata = Path.Combine(root, "backend", "metadata"); Directory.CreateDirectory(host); Directory.CreateDirectory(metadata);
        try
        {
            foreach (var name in new[] { "Metarill.Api.dll", "Metarill.Api.deps.json", "Metarill.Api.runtimeconfig.json" }) await File.WriteAllTextAsync(Path.Combine(host, name), "仅用于摘要拒绝测试，不可执行");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in new[] { "platform", "demo", "files", "security" })
            {
                var path = Path.Combine(metadata, name + ".json"); await File.WriteAllTextAsync(path, "{}"); hashes.Add("backend/metadata/" + name + ".json", Hash(path));
            }
            var evidence = Path.Combine(root, ".local", "platform-bootstrap-native", "0123456789abcdef", "evidence");
            var proof = new PlatformBootstrapNativePreparation(host, PlatformBootstrapNativePreparation.CaptureHost(host), hashes, evidence) { SharePlatformDatabase = sharePlatformDatabase, VerifyPublishedBusinessPermissions = sharePlatformDatabase };
            proof.Verify(root);
            var settings = JsonSerializer.SerializeToElement(new { Purpose = PlatformBootstrapNativePreparation.Purpose, AuthorizationStatus = "approved", LeaseStatus = "running", RunId = "0123456789abcdef", PlatformBootstrapPreparation = proof });
            var loaded = PlatformBootstrapNativePreparation.Read(settings, root); Assert.Equal(proof.AssemblyPath, loaded.AssemblyPath); Assert.Equal(sharePlatformDatabase, loaded.SharePlatformDatabase); Assert.Equal(sharePlatformDatabase, loaded.VerifyPublishedBusinessPermissions); Assert.True(Directory.Exists(evidence));
            var assembly = proof.AssemblyPath; var original = await File.ReadAllBytesAsync(assembly); await File.AppendAllTextAsync(assembly, "漂移"); Assert.Throws<InvalidOperationException>(() => proof.Verify(root)); await File.WriteAllBytesAsync(assembly, original);
            var extra = Path.Combine(host, "unknown.txt"); await File.WriteAllTextAsync(extra, "未知输出"); Assert.Throws<InvalidOperationException>(() => proof.Verify(root)); File.Delete(extra);
            await File.AppendAllTextAsync(Path.Combine(metadata, "demo.json"), "漂移"); Assert.Throws<InvalidOperationException>(() => proof.Verify(root));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root, boundary); }
    }

    /// <summary>配置必须来自本轮租约文件，拒绝空值、错误格式及非 DB0，错误不暴露合成凭据。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unit-only-redis:6379,defaultDatabase=2,password=unit-only-secret")]
    [InlineData("unit-only-redis:6379,defaultDatabase=broken,password=unit-only-secret")]
    public void Platform_login_redis_rejects_missing_or_invalid_scope(string? connection)
    {
        var settings = JsonSerializer.SerializeToElement(new { LoginFailureRedisConnectionString = connection });
        var error = Assert.Throws<InvalidOperationException>(() => FreshInstallConfiguration.ParseLoginRedis(settings));
        Assert.Equal("平台正式空库验收需要本轮隔离 Redis DB0 配置。", error.Message);
        Assert.Null(error.InnerException);
    }

    /// <summary>保留完整 Redis 连接配置，平台宿主使用它执行真实计数检查。</summary>
    [Fact]
    public void Platform_login_redis_keeps_the_explicit_private_connection()
    {
        const string connection = "127.0.0.1:6380,defaultDatabase=0,password=unit-only-secret";
        var settings = JsonSerializer.SerializeToElement(new { LoginFailureRedisConnectionString = connection });
        Assert.Equal(connection, FreshInstallConfiguration.ParseLoginRedis(settings));
    }

    /// <summary>只验证协调器实际传入的私有路径，缺路径不能通过正式预检。</summary>
    [CoordinatorRedisSettingsFact]
    public void Coordinator_redis_settings_preflight()
    {
        _ = FreshInstallConfiguration.ReadLoginRedisFile(Environment.GetEnvironmentVariable("METARILL_FRESH_LOGIN_REDIS_SETTINGS"));
    }

    /// <summary>普通 CI 没有本轮私有配置，显式跳过协调器文件验证。</summary>
    private sealed class CoordinatorRedisSettingsFactAttribute : FactAttribute
    {
        public CoordinatorRedisSettingsFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_FRESH_LOGIN_REDIS_SETTINGS")))
                Skip = "未提供本轮隔离 Redis 配置路径，协调器预检未执行。";
        }
    }

    /// <summary>只固定测试字节，不输出其内容。</summary>
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
