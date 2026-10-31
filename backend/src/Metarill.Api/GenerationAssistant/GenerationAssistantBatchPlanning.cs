using System.Security.Cryptography;
using System.Text;

namespace Metarill.Api.GenerationAssistant;

/// <summary>批量计划只汇总发现结果，不创建任务、不声称已授权或已应用；每个项目结果保持可审计。</summary>
public sealed record AssistantRuntimeBatchPlanRequest(string DeviceId);
public sealed record AssistantRuntimeBatchPlanItem(string? ProjectId,string ProjectName,string? ConfigurationId,string Status,string? ErrorCode,bool RequiresProjectAuthorization);
public sealed record AssistantRuntimeBatchPlan(string DeviceId,AssistantRuntimeBatchPlanItem[] Items,bool CanCreateTasks);

public static class GenerationAssistantBatchPlanning
{
    /// <summary>批次与项目组合生成稳定标准UUID，重复确认回到同一任务而不复用客户端项目编号。</summary>
    public static string StableRequestId(string batchId,string projectId)
    {
        if(!Guid.TryParseExact(batchId,"D",out var batch)||batch.ToString("D")!=batchId||string.IsNullOrWhiteSpace(projectId))throw new ArgumentException("批次或项目标识无效。");
        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes("metarill-runtime-batch-v1\n"+batchId+"\n"+projectId));bytes[6]=(byte)((bytes[6]&15)|80);bytes[8]=(byte)((bytes[8]&63)|128);return new Guid(bytes[..16]).ToString("D");
    }
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

/// <summary>授权预检只返回逐项目状态；不返回token、路径或grant原始凭据。</summary>
public sealed record AssistantRuntimeBatchAuthorizationItem(string? ProjectId,string ProjectName,string Status,string? ErrorCode,string? BindingId);
public sealed record AssistantRuntimeBatchAuthorizationPlan(string DeviceId,AssistantRuntimeBatchAuthorizationItem[] Items,bool CanCreateTasks);

/// <summary>批量入队回执只报告逐项目任务编号和稳定状态，不返回路径、令牌或应用成功。</summary>
public sealed record AssistantRuntimeBatchEnqueueRequest(string DeviceId);
public sealed record AssistantRuntimeBatchEnqueueItem(string? ProjectId,string ProjectName,string Status,string? ErrorCode,string? RequestId);
public sealed record AssistantRuntimeBatchEnqueueResult(string DeviceId,AssistantRuntimeBatchEnqueueItem[] Items,bool AnyQueued);
