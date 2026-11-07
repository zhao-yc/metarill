using Metarill.Runtime.Files;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留uni-file-picker/TinyMCE旧上传协议，实际内容进入当前项目文件库。</summary>
[ApiController, Route("api/admin/sys/upload")]
public sealed class LegacyUploadController(IFileLibraryResolver resolver) : ControllerBase
{
    /// <summary>先解析真实认证项目，再读取有界的单文件multipart；查询参数不能建立登录身份。</summary>
    [HttpPost("uploadImage"), RequestSizeLimit(105_906_176), RequestFormLimits(MultipartBodyLengthLimit = 105_906_176, ValueCountLimit = 16)]
    public async Task<object> UploadImage(CancellationToken token)
    {
        var files = resolver.RequireCurrent();
        if (!Request.HasFormContentType) throw new ArgumentException("请使用multipart文件上传。");
        var form = await Request.ReadFormAsync(token);
        if (form.Files.Count != 1 || form.Files[0].Name != "file") throw new ArgumentException("仅接受一个名为file的上传文件。");
        var upload = form.Files[0]; await using var stream = upload.OpenReadStream();
        var saved = await files.ImportLegacyPublicAsync(upload.FileName, upload.Length, stream, token);
        if (saved.Status != "ready" || saved.AccessMode != "PUBLIC" || saved.Url is null) throw new InvalidOperationException("文件未完成公开导入。");
        // 服务端固定API路由可直接供图片src使用，不根据客户端Host或任意URL生成地址。
        var url = files.LegacyPublicUrl(saved.Url, Request.PathBase.Value);
        return new { code = 200, data = new { url, name = saved.OriginalName, extname = Path.GetExtension(saved.OriginalName).TrimStart('.').ToLowerInvariant() }, location = url };
    }
}
