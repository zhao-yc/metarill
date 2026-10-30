using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Metarill.Runtime.Files;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>实际对象层与SDK请求合同；只替换云端传输，不连接桶或数据库。</summary>
public sealed class FileObjectStorageTests
{
    /// <summary>保持原本地裸键、目录隔离和旧行空摘要读取；切到云端后也不能误读本地对象。</summary>
    [Fact]
    public async Task Existing_local_keys_remain_local_and_keep_original_read_contract()
    {
        await InRoot(async root =>
        {
            var fake = new S3Transport(); using var storage = Create(fake);
            var bytes = Encoding.UTF8.GetBytes("原本地文件"); var key = Guid.NewGuid().ToString("N") + ".blob";
            await File.WriteAllBytesAsync(Path.Combine(root, key), bytes);
            await using (var read = await storage.OpenAsync(key, root, bytes.Length, ""))
            { Assert.False(read.CanWrite); Assert.Equal(bytes, await Read(read)); }
            Assert.Equal(0, fake.Gets);
            Assert.True(await storage.DeleteAsync(key, root)); Assert.False(File.Exists(Path.Combine(root, key)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync("../outside", root, 0, ""));
        });
    }

    /// <summary>云端实际字节校验后返回原可寻址只读流；流关闭仅清理本次缓存，不触碰上传暂存。</summary>
    [Fact]
    public async Task S3_upload_and_download_are_bounded_immutable_seekable_readonly_and_owned()
    {
        await InRoot(async root =>
        {
            var fake = new S3Transport(); using var storage = Create(fake);
            var bytes = Encoding.UTF8.GetBytes("S3字节%_"); var temporary = Path.Combine(root, "input.part"); await File.WriteAllBytesAsync(temporary, bytes);
            var key = storage.CreateKey(); Assert.InRange(key.Length, 1, 100); Assert.Equal("s3", FileObjectStorage.ProviderCode(key));
            await storage.CommitAsync(key, temporary, root, bytes.Length, Hash(bytes));
            Assert.NotNull(fake.Put); Assert.Equal("*", fake.Put.IfNoneMatch); Assert.Equal("metarill-test", fake.Put.BucketName);
            Assert.StartsWith("objects/", fake.Put.Key); Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), fake.Put.ChecksumSHA256);
            Assert.True(File.Exists(temporary));
            await using (var read = await storage.OpenAsync(key, root, bytes.Length, Hash(bytes)))
            {
                Assert.True(read.CanSeek); Assert.False(read.CanWrite); Assert.Equal(bytes.Length, read.Length);
                Assert.Equal(bytes, await Read(read)); read.Seek(1, SeekOrigin.Begin); Assert.Equal(bytes[1], read.ReadByte());
                Assert.Throws<NotSupportedException>(() => read.WriteByte(0));
            }
            Assert.Empty(Directory.GetFiles(root, "*.blob")); Assert.True(File.Exists(temporary));
            Assert.True(await storage.DeleteAsync(key, root)); Assert.Equal(fake.Put.Key, fake.DeletedKey);
        });
    }

    /// <summary>未知提交结果之后只读确认同一实际对象，重试不得覆盖不同内容。</summary>
    [Fact]
    public async Task Ambiguous_put_can_retry_only_identical_bytes_and_never_replace_an_existing_object()
    {
        await InRoot(async root =>
        {
            var fake = new S3Transport { FailAfterCommit = true }; using var storage = Create(fake);
            var bytes = Encoding.UTF8.GetBytes("same"); var path = Path.Combine(root, "input.part"); await File.WriteAllBytesAsync(path, bytes);
            var key = storage.CreateKey();
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.CommitAsync(key, path, root, bytes.Length, Hash(bytes)));
            fake.FailAfterCommit = false; await storage.CommitAsync(key, path, root, bytes.Length, Hash(bytes));
            Assert.Equal(1, fake.Gets);
            var other = Encoding.UTF8.GetBytes("evil"); await File.WriteAllBytesAsync(path, other);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.CommitAsync(key, path, root, other.Length, Hash(other)));
            Assert.Equal(bytes, fake.Bytes); Assert.Null(fake.DeletedKey); Assert.Empty(Directory.GetFiles(root, "*.blob"));
        });
    }

    /// <summary>远端头、短流、超长流和摘要均独立验证，任何失败都不能留下可读缓存。</summary>
    [Theory]
    [InlineData("header")][InlineData("short")][InlineData("long")][InlineData("digest")]
    public async Task Invalid_remote_object_fails_before_publication_and_cleans_temporary_copy(string failure)
    {
        await InRoot(async root =>
        {
            var expected = new byte[] { 1, 2, 3 }; var fake = new S3Transport { Bytes = expected, DeclaredLength = 3 };
            if (failure == "header") fake.DeclaredLength = 4;
            if (failure == "short") fake.Bytes = [1, 2];
            if (failure == "long") fake.Bytes = [1, 2, 3, 4];
            if (failure == "digest") fake.Bytes = [3, 2, 1];
            using var storage = Create(fake);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync(storage.CreateKey(), root, 3, Hash(expected)));
            Assert.Empty(Directory.GetFiles(root));
        });
    }

    /// <summary>零字节仍是有效文件，不能与缺少对象或空摘要混为一谈。</summary>
    [Fact]
    public async Task Zero_bytes_keep_valid_digest_and_missing_digest_never_reaches_s3()
    {
        await InRoot(async root =>
        {
            var fake = new S3Transport(); using var storage = Create(fake);
            var path = Path.Combine(root, "input.part"); await File.WriteAllBytesAsync(path, []);
            var key = storage.CreateKey(); await storage.CommitAsync(key, path, root, 0, Hash([]));
            await using (var read = await storage.OpenAsync(key, root, 0, Hash([]))) Assert.Equal(0, read.Length);
            var count = fake.Gets; await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync(key, root, 0, ""));
            Assert.Equal(count, fake.Gets);
        });
    }

    /// <summary>位置摘要与凭据分离，允许同位置轮换密钥，禁止静默切桶/前缀/寻址或解析任意URL。</summary>
    [Fact]
    public void Locator_pins_published_location_and_cannot_choose_endpoint_bucket_or_path()
    {
        var fake = new S3Transport(); using var initial = Create(fake); var key = initial.CreateKey();
        using var rotated = new FileObjectStorage(Options(Profile() with { SecretKey = "synthetic-rotated" }), _ => fake);
        rotated.ValidateKey(key, Path.GetTempPath());
        foreach (var profile in new[] { Profile() with { Bucket = "different-bucket" }, Profile() with { Prefix = "different" },
            Profile() with { Endpoint = "https://elsewhere.invalid" }, Profile() with { ForcePathStyle = false } })
        {
            using var changed = new FileObjectStorage(Options(profile), _ => throw new InvalidOperationException("不应连接"));
            Assert.Throws<InvalidOperationException>(() => changed.ValidateKey(key, Path.GetTempPath()));
        }
        foreach (var invalid in new[] { "s3:https://client.invalid/object", key + "/../other", key.Replace("s3:sample:", "s3:missing:", StringComparison.Ordinal), key.ToUpperInvariant() })
            Assert.Throws<InvalidOperationException>(() => initial.ValidateKey(invalid, Path.GetTempPath()));
        Assert.DoesNotContain("synthetic-secret", Profile().ToString());
    }

    /// <summary>配置无效在网络前拒绝，固定开发HTTP仅由部署方显式开启。</summary>
    [Theory]
    [InlineData("http://s3.invalid")][InlineData("https://user:pass@s3.invalid")][InlineData("https://s3.invalid/path")]
    [InlineData("https://s3.invalid/?x=1")][InlineData("https://s3.invalid/#x")][InlineData("file:///tmp")]
    public void Invalid_endpoint_never_creates_a_client(string endpoint)
        => Assert.Throws<InvalidOperationException>(() => new FileObjectStorage(Options(Profile() with { Endpoint = endpoint }), _ => throw new Exception("不应连接")));

    /// <summary>删除失败不能伪报成功；没有打开的备用供应商也不因Dispose而连接。</summary>
    [Fact]
    public async Task Delete_failure_is_explicit_and_dispose_does_not_start_unused_clients()
    {
        var calls = 0; var fake = new S3Transport { FailDelete = true };
        using var storage = new FileObjectStorage(Options(Profile()), _ => { calls++; return fake; });
        Assert.Equal(0, calls); Assert.False(await storage.DeleteAsync(storage.CreateKey(), Path.GetTempPath())); Assert.Equal(1, calls);
        using var unopened = new FileObjectStorage(Options(Profile()), _ => throw new Exception("未使用配置不应连接"));
        unopened.Dispose(); Assert.Throws<ObjectDisposedException>(() => unopened.CreateKey());
    }

    /// <summary>仅合成SDK账号，不读取开发环境凭据。</summary>
    private static S3FileStorageProfile Profile() => new()
    { Endpoint = "https://s3.invalid", Bucket = "metarill-test", Prefix = "objects", AccessKey = "synthetic-key", SecretKey = "synthetic-secret" };
    private static FileLibraryOptions Options(S3FileStorageProfile profile) => new()
    { Storage = new() { DefaultProfile = "sample", S3Profiles = new() { ["sample"] = profile } } };
    private static FileObjectStorage Create(S3Transport fake) => new(Options(Profile()), _ => fake);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<byte[]> Read(Stream stream) { using var output = new MemoryStream(); await stream.CopyToAsync(output); return output.ToArray(); }

    /// <summary>每项用例独占系统临时子目录；递归清理前再次确认直接父目录和随机名称。</summary>
    private static async Task InRoot(Func<string, Task> action)
    {
        var boundary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "metarill-object-storage-tests"));
        var root = Path.Combine(boundary, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { await action(root); }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != boundary || !Guid.TryParseExact(Path.GetFileName(root), "N", out _))
                throw new InvalidOperationException("测试目录越界，拒绝清理。");
            Directory.Delete(root, true);
        }
    }

    /// <summary>只替换AWS调用的网络边界，保存真实请求，模拟条件写与不明回执；不伪装正式桶验收。</summary>
    private sealed class S3Transport() : AmazonS3Client(new BasicAWSCredentials("synthetic-key", "synthetic-secret"),
        new AmazonS3Config { ServiceURL = "https://s3.invalid", AuthenticationRegion = "us-east-1" })
    {
        public PutObjectRequest? Put { get; private set; }
        public byte[]? Bytes { get; set; }
        public long? DeclaredLength { get; set; }
        public bool FailAfterCommit { get; set; }
        public bool FailDelete { get; set; }
        public int Gets { get; private set; }
        public string? DeletedKey { get; private set; }
        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken token = default)
        {
            Put = request;
            if (Bytes is not null) throw new AmazonS3Exception("合成已存在") { StatusCode = HttpStatusCode.PreconditionFailed };
            Bytes = await Read(request.InputStream);
            if (FailAfterCommit) throw new AmazonS3Exception("合成回执丢失") { StatusCode = HttpStatusCode.InternalServerError };
            return new() { HttpStatusCode = HttpStatusCode.OK };
        }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken token = default)
        {
            Gets++;
            return Task.FromResult(new GetObjectResponse
            { HttpStatusCode = HttpStatusCode.OK, ContentLength = DeclaredLength ?? Bytes!.Length, ResponseStream = new MemoryStream(Bytes!, false) });
        }
        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken token = default)
        {
            if (FailDelete) throw new AmazonS3Exception("合成删除失败") { StatusCode = HttpStatusCode.InternalServerError };
            DeletedKey = request.Key; Bytes = null; return Task.FromResult(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });
        }
    }
}
