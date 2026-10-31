using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>实际平台Program绑定部署侧云配置，平台/业务文件槽复用共享连接，原拒绝入口不改。</summary>
public sealed class PlatformCloudStorageWiringTests
{
    /// <summary>不可达数据库与合成S3配置；只解析DI/随机定位并验证匿名拒绝，不执行SQL、S3或初始化。</summary>
    [Fact]
    public async Task Actual_program_binds_private_cloud_profiles_and_keeps_original_anonymous_file_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "metarill-cloud-wiring", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json"); var previous = Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        var database = new { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1;" };
        var profile = new S3FileStorageProfile { Endpoint = "https://s3.invalid", Bucket = "metarill-test", AccessKey = "synthetic-key", SecretKey = "synthetic-secret" };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            Metarill = new { Database = database, BusinessDatabase = database,
                Files = new FileLibraryOptions { RootDirectory = "客户端不能覆盖既定平台根", Storage = new() { DefaultProfile = "test", S3Profiles = new() { ["test"] = profile } } } }
        }));
        Environment.SetEnvironmentVariable("METARILL_SETTINGS", path);
        try
        {
            await using var factory = new WebApplicationFactory<Program>(); using var client = factory.CreateClient();
            var options = factory.Services.GetRequiredService<FileLibraryOptions>();
            Assert.Equal("test", options.Storage.DefaultProfile); Assert.True(Path.IsPathFullyQualified(options.RootDirectory));
            Assert.EndsWith(Path.Combine(".local", "files"), options.RootDirectory);
            var storage = factory.Services.GetRequiredService<FileObjectStorage>(); Assert.Same(storage, factory.Services.GetRequiredService<FileObjectStorage>());
            Assert.StartsWith("s3:test:", storage.CreateKey());
            var metadata = factory.Services.GetRequiredService<MetadataRegistry>(); var runtimes = factory.Services.GetRequiredService<ProjectFileRuntimes>();
            foreach (var project in new[] { metadata.Platform.YzProjectId, metadata.Demo.YzProjectId })
                Assert.Equal("test", runtimes.Resolve(project).Options.Storage.DefaultProfile);
            foreach (var (route, body) in new (string, object)[]
            {
                ("/api/admin/sys/file-library/upload-plan", new { originalName = "sample.txt", sizeBytes = 1 }),
                ("/api/admin/sys/file-library/complete", new { fileId = "7" })
            })
            {
                using var response = await client.PostAsJsonAsync(route, body); response.EnsureSuccessStatusCode();
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(500, result.RootElement.GetProperty("code").GetInt32()); Assert.Equal("登录超时", result.RootElement.GetProperty("msg").GetString());
            }
        }
        finally { Environment.SetEnvironmentVariable("METARILL_SETTINGS", previous); File.Delete(path); Directory.Delete(root); }
    }
}
