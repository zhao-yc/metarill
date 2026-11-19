using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace Metarill.Runtime.Files;

public sealed partial class FileObjectStorage
{
    private const string DirectSuffix = ":direct";
    private const string DirectOriginMetadata = "metarill-direct-origin";

    /// <summary>直传标记只决定上传方式，读取与删除仍解析同一正式对象。</summary>
    internal static bool IsDirectUpload(string key) =>
        key.StartsWith("s3:", StringComparison.Ordinal) && key.EndsWith(DirectSuffix, StringComparison.Ordinal);

    /// <summary>由 SDK 签发只允许一个暂存键、实际大小和类型的原表单计划。</summary>
    internal async Task<LocalUploadPlan?> TryPlanDirectAsync(string key, string contentType, long size,
        DateTimeOffset deadline)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsDirectUpload(key)) return null;
        var (profile, objectKey) = Resolve(key);
        var endpoint = profile.Options.DirectUploadEndpoint ?? throw new InvalidOperationException("S3直传入口未发布。");
        if (size < 0 || size > maximumBytes) throw new InvalidOperationException("直传文件大小无效。");
        var stagingKey = DirectStagingKey(profile, objectKey);
        var post = await profile.Client.Value.CreatePresignedPostAsync(new CreatePresignedPostRequest
        {
            BucketName = profile.Options.Bucket,
            Key = stagingKey,
            Expires = deadline.UtcDateTime,
            Fields = new() { ["Content-Type"] = contentType },
            Conditions = [S3PostCondition.ContentLengthRange(size, size)]
        });
        // POST 策略绑定桶和对象键。浏览器 action 由独立发布配置决定，避免将私有 SDK 端点交给浏览器。
        var url = endpoint + (profile.Options.DirectUploadPathStyle ? "/" + profile.Options.Bucket : "/");
        return new("s3", "DIRECT_FORM", url, new Dictionary<string, string>(), post.Fields, deadline);
    }

    /// <summary>先核对已封存对象。确认重试期间，暂存表单即使被重复提交也不能改写正式内容。</summary>
    internal async Task<string> CompleteDirectAsync(string key, string root, long size, string? requestedHash,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsDirectUpload(key) || size < 0 || size > maximumBytes)
            throw new InvalidOperationException("S3直传计划无效。");
        var (profile, objectKey) = Resolve(key);
        var stagingKey = DirectStagingKey(profile, objectKey);
        var expectedHash = FileValidation.Digest(requestedHash);
        try
        {
            var saved = await ReadS3Async(profile, objectKey, root, size, expectedHash, stagingKey, token);
            await saved.Content.DisposeAsync();
            await RemoveDirectStageAsync(profile, stagingKey, token);
            return saved.Sha256;
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound) { }
        catch (AmazonS3Exception error) { throw new InvalidOperationException("S3封存对象读取失败。", error); }

        try
        {
            var staged = await ReadS3Async(profile, stagingKey, root, size, expectedHash, null, token);
            await using var content = staged.Content;
            await PutS3Async(profile, objectKey, root, content, size, staged.Sha256, token, directOrigin: stagingKey);
            var sealedObject = await ReadS3Async(profile, objectKey, root, size, staged.Sha256, stagingKey, token);
            await sealedObject.Content.DisposeAsync();
            await RemoveDirectStageAsync(profile, stagingKey, token);
            return sealedObject.Sha256;
        }
        catch (AmazonS3Exception error) { throw new InvalidOperationException("S3直传暂存对象读取失败，请重新提交上传。", error); }
    }

    /// <summary>正式对象已核验才尝试移除暂存键，仍可提交的旧表单和清理失败也由桶的暂存过期规则回收。</summary>
    private static async Task<bool> RemoveDirectStageAsync(Profile profile, string stagingKey, CancellationToken token)
    {
        try
        {
            var response = await profile.Client.Value.DeleteObjectAsync(new DeleteObjectRequest
                { BucketName = profile.Options.Bucket, Key = stagingKey }, token);
            return (int)response.HttpStatusCode is >= 200 and < 300;
        }
        catch (AmazonS3Exception) { return false; }
    }

    /// <summary>签名暂存键不与正式随机键重合，客户端凭表单无法覆盖正式对象。</summary>
    private static string DirectStagingKey(Profile profile, string objectKey)
    {
        var prefix = profile.Options.Prefix;
        var leaf = objectKey[(objectKey.LastIndexOf('/') + 1)..];
        return (prefix.Length == 0 ? "" : prefix + "/") + "upload-staging/" + leaf;
    }
}
