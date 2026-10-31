using Metarill.Generation;
using System.Text.Json.Serialization;

namespace Metarill.Api.GenerationAssistant;

/// <summary>服务器只选择自己的状态目录与有界时限，不从请求接收任何本机工程目录。</summary>
public sealed record GenerationAssistantOptions(string RootDirectory)
{
    public int RequestTtlSeconds { get; init; } = 900;
    public int LeaseSeconds { get; init; } = 300;
    public int MaxTasks { get; init; } = 1000;
    public int ApplyTicketSeconds { get; init; } = 90;
    public int MaxBindings { get; init; } = 128;
}
public sealed record AssistantOwner(long AccountId, string ProjectId, long? GrantId = null);
public sealed record AssistantBindRequest(string BindingId, string RootNamespace, string LocalBindingSha256, string Mode = "preview", string[]? AllowedActions = null);
public sealed record AssistantEnqueueRequest(string BindingId, string Action);
public sealed record AssistantClaimRequest(string BindingId);
/// <summary>预览回执只上传摘要和计数，没有绝对路径、命令输出或写入成功状态。</summary>
public sealed record AssistantPreviewReceipt(string BindingId, string RequestId, string LeaseId, string RequestSha256, string Status,
    string? PlanSha256, string[] RequiredActions, int CreatedFiles, int ReplacedFiles, int DeletedFiles, int PreservedFiles, string? ErrorCode = null);
public sealed record AssistantBindingView(string BindingId, string ProjectId, string RootNamespace, string LocalBindingSha256, string Mode, string[] AllowedActions);
public sealed record AssistantTaskView(string RequestId, string BindingId, string ProjectId, string Action, string Status, string RequestSha256,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, AssistantPreviewReceipt? Preview, bool Applied = false, string Outcome = "not_applied", AssistantApplicationReceipt? Application = null);
public sealed record AssistantWork(string RequestId, string BindingId, string ProjectId, string RootNamespace, string LocalBindingSha256,
    string LeaseId, DateTimeOffset LeaseExpiresAt, GenerationUpdateRequest Snapshot, bool Applied = false, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] AssistantRuntimeOrigin? RuntimeOrigin=null);
internal sealed record AssistantBinding(int SchemaVersion, long AccountId, string ProjectId, long GrantId, string BindingId,
    string RootNamespace, string LocalBindingSha256, string Mode, string[] AllowedActions, string IntegritySha256);
internal sealed record AssistantTask(int SchemaVersion, long AccountId, string ProjectId, long GrantId, string RequestId, string BindingId,
    string RootNamespace, string LocalBindingSha256, GenerationUpdateRequest Snapshot, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    string Status, string? LeaseId, DateTimeOffset? LeaseExpiresAt, AssistantPreviewReceipt? Preview, string IntegritySha256, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] AssistantRuntimeOrigin? RuntimeOrigin=null);

/// <summary>分组只包含同快照下用户分别确认的UUID，不包含本机路径或追加动作命令。</summary>
public sealed record AssistantApproval(string RequestId, string Action, string RequestSha256, string MetadataSha256);
public sealed record AssistantApprovalsRequest(string BindingId, string MetadataSha256);
public sealed record AssistantAuthorizeRequest(string BindingId, string GroupId, string MetadataSha256, string[] RequestIds, string[] Actions, string PlanSha256);
public sealed record AssistantTicketRequest(string BindingId, string GroupId, string TicketSha256, string PlanSha256);
public sealed record AssistantApplyTicket(string GroupId, string BindingId, string MetadataSha256, AssistantApproval[] Approvals, string[] Actions,
    string PlanSha256, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string TicketSha256, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] AssistantRuntimeOrigin? RuntimeOrigin=null);
/// <summary>应用回执只报告本机核验结果；unknown和恢复失败永远不映射成功。</summary>
public sealed record AssistantApplicationReceipt(string BindingId, string GroupId, string TicketSha256, string PlanSha256, string Status,
    int WrittenFiles, int DeletedFiles, int PreservedFiles, string? ErrorCode = null);
internal sealed record AssistantApplyGroup(int SchemaVersion, long AccountId, string ProjectId, long GrantId, string RootNamespace,
    string LocalBindingSha256, AssistantApplyTicket Ticket, AssistantApplicationReceipt? Receipt, string IntegritySha256);

/// <summary>仅来自服务端实际设备配置与既有助手关联，固定到本次分端任务及ticket，不接客户端目录。</summary>
public sealed record AssistantRuntimeOrigin(AssistantDeviceScope Device,string AssociationSha256);
/// <summary>Controller在本次真实Agent认证后重新查库的事实；不是HTTP请求字段。</summary>
public sealed record AssistantRuntimeOriginProof(string RequestId,AssistantDeviceScope Current);
public sealed record AssistantPendingRuntimeOrigin(string RequestId,AssistantRuntimeOrigin RuntimeOrigin);

/// <summary>v1在一次队列遍历中正常领取；v2只返回待复核来源，查库前不提前占用lease。</summary>
public sealed record AssistantClaimPreparation(AssistantWork? Work,AssistantPendingRuntimeOrigin? PendingOrigin);
