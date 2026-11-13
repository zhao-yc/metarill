using Metarill.Generation;
namespace Metarill.Api.GenerationAssistant;

public sealed partial class GenerationAssistantQueue
{
    /// <summary>兼容原v1单次扫描领取；只有确需查库的v2才先返回来源，随后按精确请求ID再次持锁领取。</summary>
    public AssistantClaimPreparation PrepareClaim(AssistantOwner owner,string bindingId)=>Locked<AssistantClaimPreparation>(()=>
    {
        RequireOwner(owner,true);var binding=ReadBinding(bindingId);RequireScope(binding,owner);var task=NextClaimable(owner,bindingId);
        if(task is null)return new(null,null);
        return task.RuntimeOrigin is{} origin?new(null,new(task.RequestId,origin)):new(LeaseWork(task),null);
    });
    /// <summary>真实控制器先取得下一项固定设备来源；并发改变领取项时Claim会拒绝旧请求ID证明。</summary>
    public AssistantPendingRuntimeOrigin? PendingClaimOrigin(AssistantOwner owner,string bindingId)=>Locked<AssistantPendingRuntimeOrigin?>(()=>
    {
        RequireOwner(owner,true);var binding=ReadBinding(bindingId);RequireScope(binding,owner);var task=NextClaimable(owner,bindingId);
        return task?.RuntimeOrigin is{} origin?new(task.RequestId,origin):null;
    });
    /// <summary>只能读取本grant/绑定真实任务的来源；普通三动作返回null，分端确认不能与其他动作合并。</summary>
    public AssistantPendingRuntimeOrigin? PendingAuthorizeOrigin(AssistantOwner owner,AssistantAuthorizeRequest request)=>Locked<AssistantPendingRuntimeOrigin?>(()=>
    {
        RequireOwner(owner,true);var binding=ReadBinding(request.BindingId);RequireScope(binding,owner);
        if(request.RequestIds is null||request.RequestIds.Length is <1 or >3)throw new InvalidOperationException("确认集合无效。");
        var tasks=request.RequestIds.Select(ReadTask).ToArray();foreach(var task in tasks){RequireScope(task,owner);if(task.BindingId!=binding.BindingId)throw new UnauthorizedAccessException("任务不属于当前绑定。");}
        var selected=tasks.Where(task=>task.RuntimeOrigin is not null).ToArray();if(selected.Length==0)return null;
        if(tasks.Length!=1)throw new InvalidOperationException("分端运行时确认只能包含原单个任务。");
        return new(selected[0].RequestId,selected[0].RuntimeOrigin!);
    });
    /// <summary>回读已签ticket的原来源供本次实际查库；不从网络接受设备/配置摘要。</summary>
    public AssistantPendingRuntimeOrigin? PendingTicketOrigin(AssistantOwner owner,AssistantTicketRequest request)=>Locked<AssistantPendingRuntimeOrigin?>(()=>
    {
        RequireOwner(owner,true);var binding=ReadBinding(request.BindingId);RequireScope(binding,owner);var group=ReadGroup(request.GroupId);RequireGroup(group,owner,binding);
        return group.Ticket.RuntimeOrigin is{} origin?new(group.Ticket.Approvals.Single().RequestId,origin):null;
    });
    /// <summary>结构与摘要范围是持久协议的一部分；普通入口仅v1，分端只能v2运行时且与真实设备启用端一致。</summary>
    private static void ValidateRuntimeSnapshot(AssistantOwner owner,GenerationUpdateRequest snapshot,AssistantRuntimeOrigin? origin)
    {
        if(origin is null){if(snapshot.SchemaVersion!=1||snapshot.RuntimeParts is not null)throw new InvalidOperationException("分端任务缺少固定设备来源，不能走普通入口。");return;}
        ArgumentNullException.ThrowIfNull(origin.Device);DeviceScope(owner,origin.Device);RequireHash(origin.AssociationSha256);
        if(snapshot.SchemaVersion!=2||snapshot.Action!="sync-runtime"||snapshot.RuntimeParts!=origin.Device.Parts)throw new InvalidOperationException("分端任务范围与固定设备来源不符。");
    }
    /// <summary>只能以本次服务端实际读取的Scope及当前CAS关联证明授权，源配置/重置/改绑任一变化均拒绝。</summary>
    private void RequireCurrentRuntimeOrigin(AssistantOwner owner,AssistantBinding binding,AssistantRuntimeOrigin origin,AssistantDeviceScope current)
    {
        DeviceScope(owner,current);
        if(origin.Device!=current)throw new UnauthorizedAccessException("设备配置已变化，本次运行时任务未获批准。");
        var association=ReadDeviceAssociation(owner.AccountId,current.DeviceId,current.ProjectId);
        if(association.Scope!=current||association.IntegritySha256!=origin.AssociationSha256||association.BindingId!=binding.BindingId||association.GrantId!=binding.GrantId||association.LocalBindingSha256!=binding.LocalBindingSha256||binding.Mode!="apply"||!binding.AllowedActions.Contains("sync-runtime",StringComparer.Ordinal))
            throw new UnauthorizedAccessException("设备关联或本机允许范围已变化。");
    }
    /// <summary>v1确认保持旧行为；v2严格单任务、不借用同快照其他确认，来源证明绑定精确请求ID。</summary>
    private AssistantRuntimeOrigin? RequireRuntimeSet(AssistantOwner owner,AssistantBinding binding,AssistantTask[] tasks,AssistantRuntimeOriginProof? proof)
    {
        foreach(var task in tasks){RequireScope(task,owner);if(task.BindingId!=binding.BindingId)throw new UnauthorizedAccessException("任务不属于当前绑定。");}
        var scoped=tasks.Where(task=>task.RuntimeOrigin is not null).ToArray();
        if(scoped.Length==0){if(proof is not null)throw new InvalidOperationException("普通任务不接受分端来源证明。");return null;}
        if(tasks.Length!=1||proof is null||proof.RequestId!=tasks[0].RequestId)throw new UnauthorizedAccessException("分端任务缺少本次精确来源复核或混入其他确认。");
        RequireCurrentRuntimeOrigin(owner,binding,scoped[0].RuntimeOrigin!,proof.Current);return scoped[0].RuntimeOrigin;
    }
}
