using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>受管结构只读反查入口；响应始终 executable=false，不能提交为执行计划。</summary>
[ApiController, Route("api/admin/sys/database/inspect")]
public sealed class SchemaInspectionController(ProjectSchemaInspectionBusiness business) : ControllerBase
{
    /// <summary>只接收空对象或当前项目的元数据tableId，不接受SQL、连接或客户端项目覆盖。</summary>
    [HttpPost, RequestSizeLimit(4096)]
    public async Task<R<ProjectSchemaInspection>> Inspect([FromBody] JsonElement request, CancellationToken token) =>
        R<ProjectSchemaInspection>.Success(await business.InspectAsync(request, token));
}
