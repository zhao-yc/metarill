using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Metarill.Runtime.Files;

/// <summary>提供本地和S3的对象操作；数据库权限、状态、引用与事务仍由原文件Service管理。</summary>
public sealed partial class FileObjectStorage : IDisposable
{
    private sealed record Profile(S3FileStorageProfile Options, string Location, Lazy<IAmazonS3> Client);
    private readonly Dictionary<string, Profile> profiles = new(StringComparer.Ordinal);
    private readonly string defaultProfile;
    private readonly long maximumBytes;
    private bool disposed;

    [GeneratedRegex("^s3:([a-z][a-z0-9_-]{0,15}):([a-f0-9]{32}):([a-f0-9]{32}\\.blob)$", RegexOptions.CultureInvariant)]
    private static partial Regex LocatorPattern();

    /// <summary>宿主注册为单例，客户连接懒创建并复用；仅本地配置不会连接任何云端。</summary>
    public FileObjectStorage(FileLibraryOptions options) : this(options, CreateClient) { }

    /// <summary>测试只替换SDK传输，不替换定位、边界和对象校验规则。</summary>
    internal FileObjectStorage(FileLibraryOptions options, Func<S3FileStorageProfile, IAmazonS3> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(clientFactory);
        maximumBytes = 100L * 1024 * 1024;
        if (options.MaxFileBytes is <= 0 or > 100L * 1024 * 1024 || options.Storage is null) throw new InvalidOperationException("文件存储大小上限必须在100MiB以内。");
        defaultProfile = options.Storage.DefaultProfile;
        if (string.IsNullOrEmpty(defaultProfile) || options.Storage.S3Profiles is null) throw new InvalidOperationException("文件存储配置未发布。");
        foreach (var (name, value) in options.Storage.S3Profiles)
        {
            if (name == "local" || !Regex.IsMatch(name, "^[a-z][a-z0-9_-]{0,15}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("S3配置别名无效。");
            var snapshot = (value ?? throw new InvalidOperationException("S3私有配置不能为空。")).Snapshot();
            profiles.Add(name, new(snapshot, snapshot.LocationId(), new(() => clientFactory(snapshot))));
        }
        if (defaultProfile != "local" && !profiles.ContainsKey(defaultProfile)) throw new InvalidOperationException("默认文件存储配置未发布。");
    }

    /// <summary>数据库失效前只验证固定定位；配置错误不得先删除行再回退到其它供应商。</summary>
    public void ValidateKey(string key, string root)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ProviderCode(key) == "local") _ = FileValidation.StoragePath(root, key); else _ = Resolve(key);
    }

    /// <summary>新键绑定固定供应商位置，长度不超过现有100字符列；旧裸随机键保持本地语义。</summary>
    public string CreateKey()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = Guid.NewGuid().ToString("N") + ".blob";
        return defaultProfile == "local" ? key : "s3:" + defaultProfile + ":" + profiles[defaultProfile].Location + ":" + key;
    }

    /// <summary>只读取固定供应商标记，完整键及位置在对象操作时独立验证。</summary>
    public static string ProviderCode(string key) => key.StartsWith("s3:", StringComparison.Ordinal) ? "s3" : "local";

    /// <summary>调用方已经计算大小与摘要；S3不可覆盖写，未知提交结果重试只接受逐字相同的实际对象。</summary>
    public async Task CommitAsync(string key, string temporaryPath, string root, long length, string sha256, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); RequireBounds(length, sha256);
        if (ProviderCode(key) == "local") { File.Move(temporaryPath, FileValidation.StoragePath(root, key), true); return; }
        var (profile, objectKey) = Resolve(key);
        await using var input = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != length) throw new InvalidOperationException("S3暂存对象大小与计划不一致。");
        try
        {
            await profile.Client.Value.PutObjectAsync(new PutObjectRequest
            {
                BucketName = profile.Options.Bucket, Key = objectKey, InputStream = input, AutoCloseStream = false,
                ContentType = "application/octet-stream", IfNoneMatch = "*",
                ChecksumSHA256 = Convert.ToBase64String(Convert.FromHexString(sha256))
            }, token);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // 仅以真实有界GET和数据库已核对摘要证明相同；不能信任远端元数据或单独ETag。
            await using var existing = await OpenAsync(key, root, length, sha256, token);
        }
        catch (AmazonS3Exception error) { throw new InvalidOperationException("S3对象保存失败，请稍后核验或重试。", error); }
    }

    /// <summary>本地按原路径读取；S3先有界下载并核对实际摘要，再返回可寻址临时流，保留原Range读取能力。</summary>
    public async Task<Stream> OpenAsync(string key, string root, long length, string sha256, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ProviderCode(key) == "local")
        {
            if (length < 0) throw new InvalidOperationException("文件对象大小无效。");
            var local = new FileStream(FileValidation.StoragePath(root, key), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (local.Length == length) return local;
            await local.DisposeAsync(); throw new InvalidOperationException("文件对象大小异常。");
        }
        RequireBounds(length, sha256);
        var (profile, objectKey) = Resolve(key);
        FileValidation.RequireStorageRoot(root); Directory.CreateDirectory(root); FileValidation.RequireStorageRoot(root);
        var path = FileValidation.StoragePath(root, Guid.NewGuid().ToString("N") + ".blob");
        FileStream? output = null;
        try
        {
            using var response = await profile.Client.Value.GetObjectAsync(new GetObjectRequest { BucketName = profile.Options.Bucket, Key = objectKey }, token);
            if (response.ContentLength != length) throw new InvalidOperationException("S3对象大小与文件记录不一致。");
            output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long actual = 0;
            var buffer = new byte[65536];
            while (true)
            {
                var read = await response.ResponseStream.ReadAsync(buffer, token);
                if (read == 0) break;
                actual += read; if (actual > length || actual > maximumBytes) throw new InvalidOperationException("S3实际对象超过已确认大小。");
                digest.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (actual != length || Convert.ToHexStringLower(digest.GetHashAndReset()) != sha256) throw new InvalidOperationException("S3对象内容与文件记录不一致。");
            await output.FlushAsync(token); output.Position = 0;
            var result = output; output = null; return new ObjectReadStream(result);
        }
        catch (AmazonS3Exception error) { throw new InvalidOperationException("S3对象读取失败。", error); }
        finally { if (output is not null) await output.DisposeAsync(); }
    }

    /// <summary>只删除固定随机对象；失败返回false，不能恢复已经失效的数据库记录或扩大到目录/桶。</summary>
    public async Task<bool> DeleteAsync(string key, string root, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ProviderCode(key) == "local")
        {
            var path = FileValidation.StoragePath(root, key);
            try { File.Delete(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
        var (profile, objectKey) = Resolve(key);
        try
        {
            var response = await profile.Client.Value.DeleteObjectAsync(new DeleteObjectRequest { BucketName = profile.Options.Bucket, Key = objectKey }, token);
            return (int)response.HttpStatusCode is >= 200 and < 300;
        }
        catch (AmazonS3Exception) { return false; }
    }

    /// <summary>对象只允许已发布别名及完全相同的位置摘要，不解析URL、不回退到新桶或本地路径。</summary>
    private (Profile Profile, string ObjectKey) Resolve(string key)
    {
        var match = LocatorPattern().Match(key);
        if (!match.Success || !profiles.TryGetValue(match.Groups[1].Value, out var profile) || profile.Location != match.Groups[2].Value)
            throw new InvalidOperationException("S3对象定位与已发布存储配置不一致。");
        return (profile, (profile.Options.Prefix.Length == 0 ? "" : profile.Options.Prefix + "/") + match.Groups[3].Value);
    }

    /// <summary>大小和摘要来自受权文件行；异常不能触发云端或本地对象读取。</summary>
    private void RequireBounds(long length, string sha256)
    {
        if (length < 0 || length > maximumBytes || FileValidation.Digest(sha256) != sha256 || sha256.Length == 0)
            throw new InvalidOperationException("文件对象的大小或摘要无效。");
    }

    /// <summary>只使用当前私有配置的显式凭据和端点，不走环境默认账号链。</summary>
    private static IAmazonS3 CreateClient(S3FileStorageProfile profile)
    {
        AWSCredentials credentials = string.IsNullOrEmpty(profile.SessionToken)
            ? new BasicAWSCredentials(profile.AccessKey, profile.SecretKey)
            : new SessionAWSCredentials(profile.AccessKey, profile.SecretKey, profile.SessionToken);
        return new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = profile.Endpoint, AuthenticationRegion = profile.Region, ForcePathStyle = profile.ForcePathStyle,
            MaxErrorRetry = 2, Timeout = TimeSpan.FromSeconds(60)
        });
    }


    /// <summary>S3缓存仅作为原只读内容流暴露；保留Seek/Length与异步读取，关闭后删除本次临时对象。</summary>
    private sealed class ObjectReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => inner.ReadAsync(buffer, offset, count, token);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException("文件内容流只读。");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("文件内容流只读。");
        /// <summary>同步关闭同样把临时文件的生命周期交回原控制器。</summary>
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        /// <summary>异步取消/关闭不保留缓存或远端响应句柄。</summary>
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    }

    /// <summary>宿主退出只释放已经打开的连接，不因清理而初始化其它供应商。</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var profile in profiles.Values) if (profile.Client.IsValueCreated) profile.Client.Value.Dispose();
    }
}
