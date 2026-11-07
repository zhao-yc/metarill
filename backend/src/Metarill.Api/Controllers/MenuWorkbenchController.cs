using Metarill.Core.Contracts;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;
public sealed record MenuScopeRequest(string ProjectId,string? AdminTypeId);
[ApiController,Route("api/admin/custom/menu_workbench")]
public sealed class MenuWorkbenchController(MenuWorkbenchBusiness business,SqlRowStore store,HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpPost("projects")]public async Task<R<object>> Projects(CancellationToken cancellationToken)
    {authorization.RequireAdministrator();return R<object>.Success((await store.ReadAsync("yz_project",cancellationToken:cancellationToken)).Select(p=>new{id=SqlRowStore.Text(p,"id"),name=SqlRowStore.Text(p,"name"),isV8=true}));}
    [HttpPost("snapshot")]public async Task<R<object>> Snapshot(MenuScopeRequest request,CancellationToken cancellationToken)=>R<object>.Success(await business.SnapshotAsync(request.ProjectId,request.AdminTypeId,cancellationToken));
    [HttpPost("save")]public async Task<R<object>> Save(MenuSaveRequest request,CancellationToken cancellationToken)=>R<object>.Success(await business.SaveAsync(request,cancellationToken:cancellationToken));
    [HttpPost("delete")]public async Task<R<object>> Delete(MenuSaveRequest request,CancellationToken cancellationToken)=>R<object>.Success(await business.SaveAsync(request,true,cancellationToken));
}
