using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>配置导入与DDL入口分开，确认只允许服务端既有计划，不接受任意元数据或SQL。</summary>
[ApiController, Route("api/admin/sys/database/import")]
public sealed class SchemaMetadataImportController(SchemaMetadataImportBusiness business) : ControllerBase
{
    /// <summary>生成可审核的短期计划，不保存配置。</summary>
    [HttpPost("prepare"), RequestSizeLimit(16384)]
    public async Task<R<SchemaMetadataImportPlan>> Prepare([FromBody] JsonElement request, CancellationToken token) => R<SchemaMetadataImportPlan>.Success(await business.PrepareAsync(request, token));
    /// <summary>确认仅保存配置；结构核验和元数据失败均终止，不执行DDL或业务行写入。</summary>
    [HttpPost("apply"), RequestSizeLimit(4096)]
    public async Task<R<SchemaMetadataImportResult>> Apply([FromBody] JsonElement request, CancellationToken token) => R<SchemaMetadataImportResult>.Success(await business.ApplyAsync(request, token));
    /// <summary>回执只读且同身份，未知不能作为未写入证明。</summary>
    [HttpPost("status"), RequestSizeLimit(4096)]
    public async Task<R<SchemaMetadataImportStatus>> Status([FromBody] JsonElement request, CancellationToken token) => R<SchemaMetadataImportStatus>.Success(await business.StatusAsync(request, token));
}
