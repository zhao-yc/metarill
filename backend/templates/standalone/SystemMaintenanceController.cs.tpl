using Metarill.Core.Contracts;
using Metarill.Data.Relations;
using Metarill.Runtime.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace Metarill.Api.Controllers;

public sealed record MaintenanceRequest(string? ConfigTableId);
/// <summary>保留原维护页POST、配置表ID和R响应，不接收连接／SQL／字段或项目覆盖。</summary>
[ApiController,Route("api/admin/sys/system_maintenance")]
public sealed class SystemMaintenanceController(SystemMaintenanceBusiness business):ControllerBase
{
    /// <summary>沿原可筛选下拉返回id/value/name/text/rawName/tableName。</summary>
    [HttpPost("tables")]
    public R<IReadOnlyList<MaintenanceTableOption>> Tables()=>R<IReadOnlyList<MaintenanceTableOption>>.Success(business.Tables());
    /// <summary>原确认按钮提交后执行整表计划，失败由宿主统一响应并整体回滚。</summary>
    [HttpPost("backfill_join_columns")]
    public async Task<R<MaintenanceResult>> Backfill([FromBody(EmptyBodyBehavior=EmptyBodyBehavior.Allow)]MaintenanceRequest? request,CancellationToken token)=>R<MaintenanceResult>.Success(await business.BackfillAsync(request?.ConfigTableId,token));
}
