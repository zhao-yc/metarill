namespace Metarill.Runtime.Files;

public sealed partial class FileLibraryService
{
    // 构造时先校验服务端公开地址，错误配置不能在文件保存成功后才暴露。
    private readonly string? legacyPublicBaseUrl = LegacyUploadAddress.NormalizeBase(options.LegacyPublicBaseUrl);

    /// <summary>配置的路径前缀完整保留；配置存在时不叠加请求PathBase，也不读取Host或代理头。</summary>
    internal string LegacyPublicUrl(string route, string? pathBase) => LegacyUploadAddress.Build(legacyPublicBaseUrl, route, pathBase);

    /// <summary>旧图片/富文本入口按原协议导入公开文件，所有者和项目仍来自当前真实身份。</summary>
    public async Task<FileDescriptor> ImportLegacyPublicAsync(string originalName, long sizeBytes, Stream source, CancellationToken token = default)
    {
        authorization.RequireActor(); ArgumentNullException.ThrowIfNull(source); FileValidation.RequireName(originalName);
        var policy = FileValidation.Policy(null, options); var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (sizeBytes <= 0 || sizeBytes > policy.MaximumBytes) throw new InvalidOperationException("文件为空或超过服务端大小上限。");
        if (!policy.Extensions.Contains(extension)) throw new InvalidOperationException("文件扩展名不在允许范围。");
        var isImage = extension is ".jpg" or ".jpeg" or ".png";
        if (!isImage && FileValidation.ContentType(originalName).StartsWith("image/", StringComparison.Ordinal)) throw new NotSupportedException("旧上传接口仅处理JPEG/PNG图片，其他图片请使用文件库。");
        using var normalized = isImage ? await LegacyImageNormalizer.NormalizeAsync(originalName, sizeBytes, source, policy.MaximumBytes, token) : null;
        var content = normalized ?? source; var length = normalized?.Length ?? sizeBytes;
        // 兼容单步导入共用一个数据库事务，失败不留下客户端无从完成的计划记录。
        return await store.Session.InTransactionAsync(async () =>
        {
            var plan = await PlanUploadCoreAsync(new(originalName, null, length), true, token);
            await UploadAsync(plan.FileId, content, token); return await CompleteAsync(plan.FileId, token);
        }, token);
    }
}
