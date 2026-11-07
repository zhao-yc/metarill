using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>索引聚合接口保留原UI形状；保存配置不代表物理数据库已经同步。</summary>
[ApiController]
[Route("api/admin/custom/config_table")]
public sealed class IndexWorkbenchController(IndexWorkbenchBusiness business) : ControllerBase
{
    /// <summary>快照同时在detail和map携带版本，不改变原有索引编辑交互。</summary>
    [HttpPost("index_config")]
    public async Task<R<IndexWorkbenchSnapshot>> Snapshot([FromBody] JsonElement request, CancellationToken cancellationToken) => Envelope(await business.SnapshotAsync(request, cancellationToken));
    /// <summary>请求头或payload必须携带读取版本，冲突保留旧草稿并要求刷新。</summary>
    [HttpPost("save_indexes")]
    public async Task<R<IndexWorkbenchSnapshot>> Save([FromBody] JsonElement request, [FromHeader(Name = "X-Metarill-Metadata-Revision")] string? revision, CancellationToken cancellationToken)
    { try { return Envelope(await business.SaveAsync(request, revision, cancellationToken)); } catch (MetadataRevisionConflictException exception) { return R<IndexWorkbenchSnapshot>.Error(exception.Message, 409); } }
    /// <summary>map兼容既有工作台的版本读取方式。</summary>
    private static R<IndexWorkbenchSnapshot> Envelope(IndexWorkbenchSnapshot snapshot)
    { var response = R<IndexWorkbenchSnapshot>.Success(snapshot); response.Map["revision"] = snapshot.Revision; return response; }
}
