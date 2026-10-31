using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;
namespace Metarill.Api.Controllers;

public sealed record RoleSnapshotRequest(string RoleId);
/// <summary>沿用前端独立授权工作台协议，新库直接使用资源授权模式。</summary>
[ApiController,Route("api/admin/sys/yzp_permission")]
public sealed class RolePermissionController(RolePermissionBusiness business):ControllerBase
{
    [HttpGet("roles")]public async Task<R<object>> Roles(CancellationToken token)=>R<object>.Success(await business.RolesAsync(token));
    [HttpPost("snapshot")]public async Task<R<object>> Snapshot(RoleSnapshotRequest request,CancellationToken token)=>R<object>.Success(await business.SnapshotAsync(request.RoleId,token));
    [HttpPost("save")]public async Task<R<object>> Save(RolePermissionSave request,CancellationToken token)=>R<object>.Success(await business.SaveAsync(request,token));
}
