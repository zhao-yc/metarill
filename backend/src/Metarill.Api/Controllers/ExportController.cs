using Metarill.Runtime.Exports;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;
namespace Metarill.Api.Controllers;

/// <summary>固定导出成功后允许短时无头下载；显式认证头仍必须通过真实账号与项目校验。</summary>
[ApiController,Route("api/admin/sys/export")]
public sealed class ExportController(ExcelPageExportService exports,HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpGet("export"), HttpHead("export")]
    public async Task<IActionResult> Download([FromQuery]string exportId,CancellationToken token)
    {
        var content=ExportDownloadAccess.CanUseGrant(Request) ? await exports.OpenDownloadAsync(exportId,token) : await exports.OpenAsync(exportId,authorization.RequireActor(),token);
        Response.Headers["Referrer-Policy"]="no-referrer";Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
        return File(content.Stream,content.ContentType,content.FileName,enableRangeProcessing:true);
    }
}
