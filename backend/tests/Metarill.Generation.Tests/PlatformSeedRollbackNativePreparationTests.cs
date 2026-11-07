using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>仅验证独立故障用途和固定文件门禁，合成文件不启动进程、不创建连接。</summary>
public sealed class PlatformSeedRollbackNativePreparationTests
{
    /// <summary>旧成功安装、助手、pending、未运行及结束的租约均在文件读取前拒绝。</summary>
    [Theory]
    [InlineData(PlatformBootstrapNativePreparation.Purpose, "approved", "running", "0123456789abcdef")]
    [InlineData(DeviceRuntimeNativePreparation.Purpose, "approved", "running", "0123456789abcdef")]
    [InlineData(PlatformSeedRollbackNativePreparation.Purpose, "pending", "running", "0123456789abcdef")]
    [InlineData(PlatformSeedRollbackNativePreparation.Purpose, "approved", "not-started", "0123456789abcdef")]
    [InlineData(PlatformSeedRollbackNativePreparation.Purpose, "approved", "ended", "0123456789abcdef")]
    [InlineData(PlatformSeedRollbackNativePreparation.Purpose, "approved", "running", "bad-run")]
    public void Invalid_or_other_purpose_lease_rejects_before_files(string purpose, string authorization, string lease, string run)
    {
        var settings = JsonSerializer.SerializeToElement(new { Purpose = purpose, AuthorizationStatus = authorization, LeaseStatus = lease, RunId = run });
        Assert.Throws<InvalidOperationException>(() => PlatformSeedRollbackNativePreparation.Read(settings, Path.GetTempPath()));
    }

    /// <summary>本用途固定落点必须与旧空库用途隔离；通过后共享宿主校验继续拒绝元数据漂移。</summary>
    [Fact]
    public async Task Dedicated_evidence_and_current_files_are_required()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-seed-rollback-proof"); var root = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var host = Path.Combine(root, "host"); var metadata = Path.Combine(root, "backend", "metadata"); Directory.CreateDirectory(host); Directory.CreateDirectory(metadata);
        try
        {
            foreach (var name in new[] { "Metarill.Api.dll", "Metarill.Api.deps.json", "Metarill.Api.runtimeconfig.json" }) await File.WriteAllTextAsync(Path.Combine(host, name), "仅用于故障租约文件校验，不可执行");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in new[] { "platform", "demo", "files", "security" })
            {
                var path = Path.Combine(metadata, name + ".json"); await File.WriteAllTextAsync(path, "{}"); hashes.Add("backend/metadata/" + name + ".json", Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))));
            }
            var expected = Path.Combine(root, ".local", "platform-seed-rollback-native", "0123456789abcdef", "evidence");
            var proof = new PlatformBootstrapNativePreparation(host, PlatformBootstrapNativePreparation.CaptureHost(host), hashes, expected);
            JsonElement Settings(PlatformBootstrapNativePreparation value) => JsonSerializer.SerializeToElement(new { Purpose = PlatformSeedRollbackNativePreparation.Purpose, AuthorizationStatus = "approved", LeaseStatus = "running", RunId = "0123456789abcdef", PlatformSeedRollbackPreparation = value });
            Assert.Throws<InvalidOperationException>(() => PlatformSeedRollbackNativePreparation.Read(Settings(proof with { EvidenceDirectory = Path.Combine(root, ".local", "platform-bootstrap-native", "0123456789abcdef", "evidence") }), root));
            Assert.False(Directory.Exists(expected));
            Assert.Equal(expected, PlatformSeedRollbackNativePreparation.Read(Settings(proof), root).EvidenceDirectory);
            await File.AppendAllTextAsync(Path.Combine(metadata, "demo.json"), "漂移");
            Assert.Throws<InvalidOperationException>(() => PlatformSeedRollbackNativePreparation.Read(Settings(proof), root));
            Assert.Throws<InvalidOperationException>(() => PlatformBootstrapNativePreparation.Read(Settings(proof), root));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root, boundary); }
    }

    /// <summary>以真实发布目录验证最后故障点，不能把没有索引的示例目录误当末索引表失败。</summary>
    [Fact]
    public async Task Failure_checkpoints_are_last_real_seed_rows_in_both_published_catalogs()
    {
        var root = NonRedundantApplicationFixture.Root();
        async Task<MetadataCatalog> Read(string name) => JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(root, "backend", "metadata", name + ".json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var platform = await Read("platform"); var demo = await Read("demo");
        var first = PlatformSeedRollbackCheckpoint.For(platform); var second = PlatformSeedRollbackCheckpoint.For(demo);
        Assert.Equal("config_index_column", first.Table); Assert.Equal(1505, first.PriorRows); Assert.Equal(platform.ConfigIndexColumns[^1].GetProperty("id").ToString(), first.LastId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("config_menu", second.Table); Assert.Equal(24, second.PriorRows); Assert.Equal(demo.ConfigMenus[^1].Id, second.LastId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1531, first.PriorRows + second.PriorRows + 2);
        Assert.DoesNotContain(PlatformSeedRollbackCheckpoint.Rows(platform).SelectMany(x => x.Ids), id => id == second.LastId);
    }
}
