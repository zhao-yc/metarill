using Metarill.Runtime.Exports;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;
namespace Metarill.Api.Controllers;

/// <summary>导出票据仍需真实身份；不能凭查询参数取得他人导出内容。</summary>
[ApiController,Route("api/admin/sys/export")]
public sealed class ExportController(ExcelPageExportService exports,HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Download([FromQuery]string exportId,CancellationToken token)
    {
        var content=await exports.OpenAsync(exportId,authorization.RequireActor(),token);
        Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
        return File(content.Stream,content.ContentType,content.FileName,enableRangeProcessing:true);
    }
}
