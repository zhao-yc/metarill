using Metarill.Generation;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;

namespace Metarill.Api.GenerationAssistant;

/// <summary>旧页面批量按钮的兼容协调器：服务器逐项目重新认证和查关联，保持页面无需改操作。</summary>
public sealed class GenerationAssistantBatchEnqueuer(GenerationAssistantQueue queue,GenerationAssistantDeviceScopeReader scopes,MetadataRepository metadata,AgentOAuthService oauth,AgentAuthenticationService authentication)
{
    /// <summary>批量入口只接受当前网页登录的同账号目标项目，返回逐项稳定状态；不返回凭据或路径。</summary>
    public async Task<AssistantRuntimeBatchEnqueueResult> EnqueueAsync(AuthenticatedIdentity current,string deviceId,string batchId,string? loginStateId,string? authorizationHeader,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var owner=BatchOwner(current);var batch=queue.FindRuntimeBatch(owner,batchId);
        if(batch is null)batch=queue.BeginRuntimeBatch(owner,batchId,await scopes.DiscoverAsync(current.Actor,deviceId,token));
        if(batch.DeviceId!=deviceId)throw new InvalidOperationException("原批次不能切换设备。");
        var items=new List<AssistantRuntimeBatchEnqueueItem>();
        for(var index=0;index<batch.Projects.Length;index++)
        {
            var project=batch.Projects[index];token.ThrowIfCancellationRequested();
            if(project.Status!="configured"||project.ProjectId is null){items.Add(new(project.ProjectId,project.ProjectName,project.Status,project.ErrorCode,null));continue;}
            try
            {
                var target=await AuthenticateTargetAsync(current,project.ProjectId,loginStateId,authorizationHeader,token);var targetOwner=new AssistantOwner(target.Actor.AccountId,target.Actor.ProjectId);
                var status=queue.RuntimeBatchItemStatus(owner,batchId,index,targetOwner);
                // 原任务已创建或原项已明确失败时只回读，不再次批准/排队；撤销grant不抹掉真实历史回执。
                if(status.Status is not("pending" or "prepared")){items.Add(status);continue;}
                var scope=await scopes.LoadAsync(target.Actor,batch.DeviceId,token);
                if(scope!=project.Scope)throw new InvalidOperationException("原批次设备配置已变化，不能重排新范围。");
                var intent=queue.RuntimeBatchIntent(owner,batchId,index);
                if(intent is null)
                {
                    var binding=await FindBindingAsync(targetOwner,target,scope,token);
                    if(binding is null){queue.RejectRuntimeBatchItem(owner,batchId,index,"approved_runtime_binding_missing");items.Add(new(project.ProjectId,project.ProjectName,"failed","approved_runtime_binding_missing",null));continue;}
                    var snapshot=LocalGenerationUpdater.CreateRequest(await metadata.LoadProjectAsync(target.Actor.ProjectId,token),"sync-runtime",scope.Parts);
                    intent=queue.PrepareRuntimeBatchItem(owner,batchId,index,targetOwner,binding.Value.Binding.BindingId,snapshot,new(scope,binding.Value.Association.AssociationSha256),scope);
                }
                else
                {
                    await oauth.RequireApprovedGrantAsync(queue.RequireBinding(targetOwner,intent.BindingId),target,token);
                    var currentSnapshot=LocalGenerationUpdater.CreateRequest(await metadata.LoadProjectAsync(target.Actor.ProjectId,token),"sync-runtime",scope.Parts);
                    if(currentSnapshot.RequestSha256!=intent.Snapshot.RequestSha256)throw new InvalidOperationException("原批次元数据已变化，不能替换固定快照。");
                }
                var task=queue.EnqueueRuntimeBatchItem(owner,batchId,index,targetOwner,scope);items.Add(GenerationAssistantBatchPlanning.FromTask(project.ProjectId,project.ProjectName,task));
            }
            catch(UnauthorizedAccessException){queue.RejectRuntimeBatchItem(owner,batchId,index,"target-project-reauthentication-required");items.Add(new(project.ProjectId,project.ProjectName,"failed","target-project-reauthentication-required",null));}
            catch(InvalidOperationException)
            {
                // 原意图已落盘后的异常可能发生在任务发布之后，保持未知并让调用者只查原批次，不伪装明确失败。
                if(queue.RuntimeBatchIntent(owner,batchId,index) is not null)throw;
                queue.RejectRuntimeBatchItem(owner,batchId,index,"runtime_enqueue_rejected");items.Add(new(project.ProjectId,project.ProjectName,"failed","runtime_enqueue_rejected",null));
            }
        }
        return new(batch.DeviceId,items.ToArray(),items.Any(item=>item.Status=="queued"),batchId);
    }
    /// <summary>只读原批次、原项目、原任务；每项目用当前真实Web重新认证，不启动发现、prepare、claim或入队。</summary>
    public async Task<AssistantRuntimeBatchEnqueueResult> StatusAsync(AuthenticatedIdentity current,string batchId,string? loginStateId,string? authorizationHeader,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var owner=BatchOwner(current);var batch=queue.FindRuntimeBatch(owner,batchId)??throw new InvalidOperationException("原批次不存在，请保留原编号核查。");var items=new List<AssistantRuntimeBatchEnqueueItem>();
        for(var index=0;index<batch.Projects.Length;index++)
        {
            var project=batch.Projects[index];token.ThrowIfCancellationRequested();
            if(project.ProjectId is null||project.Status!="configured"){items.Add(new(project.ProjectId,project.ProjectName,project.Status,project.ErrorCode,null));continue;}
            try{var target=await AuthenticateTargetAsync(current,project.ProjectId,loginStateId,authorizationHeader,token);items.Add(queue.RuntimeBatchItemStatus(owner,batchId,index,new(target.Actor.AccountId,target.Actor.ProjectId)));}
            catch(UnauthorizedAccessException){items.Add(new(project.ProjectId,project.ProjectName,"failed","target-project-reauthentication-required",null));}
        }
        return new(batch.DeviceId,items.ToArray(),items.Any(item=>item.Status=="queued"),batchId);
    }
    /// <summary>批次必须来自实际Web管理员会话；会话ID只进私有状态，不输出到HTTP。</summary>
    private static AssistantRuntimeBatchOwner BatchOwner(AuthenticatedIdentity identity)
    {
        if(!identity.Actor.IsAdministrator||identity.GrantId is not null||identity.SessionId<=0)throw new UnauthorizedAccessException("批次需要当前Web管理员会话。");return new(identity.Actor.AccountId,identity.Actor.ProjectId,identity.SessionId);
    }
    /// <summary>目标项目切换只接受同账号真实Web会话；当前项目复用已认证身份。</summary>
    private async Task<AuthenticatedIdentity> AuthenticateTargetAsync(AuthenticatedIdentity current,string projectId,string? login,string? bearer,CancellationToken token)
    {
        if(current.Actor.ProjectId==projectId)return current;
        if(string.IsNullOrEmpty(login)==string.IsNullOrEmpty(bearer))throw new UnauthorizedAccessException("目标项目需要重新登录。");
        var raw=login??bearer!;if(bearer is not null){if(!raw.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("目标项目需要重新登录。");raw=raw[7..];}
        var identity=await authentication.AuthenticateAsync(raw,"web",projectId,token);if(identity.Kind!="web"||identity.Identity.GrantId is not null||identity.Identity.SessionId!=current.SessionId||identity.Identity.Actor.ProjectId!=projectId||identity.Identity.Actor.AccountId!=current.Actor.AccountId||!identity.Identity.Actor.IsAdministrator)throw new UnauthorizedAccessException("目标项目身份不匹配。");return identity.Identity;
    }
    /// <summary>关联必须属于当前目标配置且对应已批准grant的绑定。</summary>
    private async Task<(AssistantBindingView Binding,AssistantDeviceBindingView Association)?> FindBindingAsync(AssistantOwner owner,AuthenticatedIdentity identity,AssistantDeviceScope scope,CancellationToken token)
    {
        foreach(var binding in queue.Bindings(owner))
        {
            try{await oauth.RequireApprovedGrantAsync(queue.RequireBinding(owner,binding.BindingId),identity,token);var association=queue.FindDeviceBinding(owner,scope);if(association is {MatchesCurrentConfiguration:true,BindingId:var id}&&id==binding.BindingId)return(binding,association);}catch(UnauthorizedAccessException){}
        }
        return null;
    }
}
