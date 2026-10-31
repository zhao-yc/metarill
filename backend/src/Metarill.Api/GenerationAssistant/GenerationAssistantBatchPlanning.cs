namespace Metarill.Api.GenerationAssistant;

/// <summary>批量计划只汇总发现结果，不创建任务、不声称已授权或已应用；每个项目结果保持可审计。</summary>
public sealed record AssistantRuntimeBatchPlanRequest(string DeviceId);
public sealed record AssistantRuntimeBatchPlanItem(string? ProjectId,string ProjectName,string? ConfigurationId,string Status,string? ErrorCode,bool RequiresProjectAuthorization);
public sealed record AssistantRuntimeBatchPlan(string DeviceId,AssistantRuntimeBatchPlanItem[] Items,bool CanCreateTasks);

public static class GenerationAssistantBatchPlanning
{
    /// <summary>将设备发现映射为原按钮需要的逐项目结果；configured仍需后续目标项目的真实Web/grant授权。</summary>
    public static AssistantRuntimeBatchPlan Create(AssistantDeviceProjectDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        var items=discovery.Projects.Select(project=>project.Status switch
        {
            "skipped"=>new AssistantRuntimeBatchPlanItem(project.ProjectId,project.ProjectName,project.ConfigurationId,"skipped",project.ErrorCode,false),
            "failed"=>new AssistantRuntimeBatchPlanItem(project.ProjectId,project.ProjectName,project.ConfigurationId,"failed",project.ErrorCode,false),
            "configured" when project.Scope is not null=>new AssistantRuntimeBatchPlanItem(project.ProjectId,project.ProjectName,project.ConfigurationId,"needs-project-authorization",null,true),
            "configured"=>new AssistantRuntimeBatchPlanItem(project.ProjectId,project.ProjectName,project.ConfigurationId,"failed","scope_missing",false),
            _=>new AssistantRuntimeBatchPlanItem(project.ProjectId,project.ProjectName,project.ConfigurationId,"failed","unknown_discovery_status",false)
        }).ToArray();
        return new(discovery.DeviceId,items,false);
    }
}
