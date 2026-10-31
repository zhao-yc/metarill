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
        var discovery=await scopes.DiscoverAsync(current.Actor,deviceId,token);var items=new List<AssistantRuntimeBatchEnqueueItem>();
        foreach(var project in discovery.Projects)
        {
            token.ThrowIfCancellationRequested();if(project.Status!="configured"||project.ProjectId is null){items.Add(new(project.ProjectId,project.ProjectName,project.Status,project.ErrorCode,null));continue;}
            var requestId=GenerationAssistantBatchPlanning.StableRequestId(batchId,project.ProjectId);
            try
            {
                var target=await AuthenticateTargetAsync(current,project.ProjectId,loginStateId,authorizationHeader,token);var scope=await scopes.LoadAsync(target.Actor,deviceId,token);var owner=new AssistantOwner(target.Actor.AccountId,target.Actor.ProjectId);var binding=await FindBindingAsync(owner,target,scope,token);
                if(binding is null){items.Add(new(project.ProjectId,project.ProjectName,"failed","approved_runtime_binding_missing",null));continue;}
                var snapshot=LocalGenerationUpdater.CreateRequest(await metadata.LoadProjectAsync(target.Actor.ProjectId,token),"sync-runtime",scope.Parts);var task=queue.EnqueueRuntime(owner,requestId,binding.Value.Binding.BindingId,snapshot,new AssistantRuntimeOrigin(scope,binding.Value.Association.AssociationSha256),scope);
                items.Add(GenerationAssistantBatchPlanning.FromTask(project.ProjectId,project.ProjectName,task));
            }
            catch(UnauthorizedAccessException){items.Add(new(project.ProjectId,project.ProjectName,"failed","target-project-reauthentication-required",null));}
            catch(InvalidOperationException){items.Add(new(project.ProjectId,project.ProjectName,"failed","runtime_enqueue_rejected",null));}
        }
        return new(deviceId,items.ToArray(),items.Any(item=>item.Status=="queued"));
    }
    /// <summary>目标项目切换只接受同账号真实Web会话；当前项目复用已认证身份。</summary>
    private async Task<AuthenticatedIdentity> AuthenticateTargetAsync(AuthenticatedIdentity current,string projectId,string? login,string? bearer,CancellationToken token)
    {
        if(current.Actor.ProjectId==projectId)return current;
        if(string.IsNullOrEmpty(login)==string.IsNullOrEmpty(bearer))throw new UnauthorizedAccessException("目标项目需要重新登录。");
        var raw=login??bearer!;if(bearer is not null){if(!raw.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("目标项目需要重新登录。");raw=raw[7..];}
        var identity=await authentication.AuthenticateAsync(raw,"web",projectId,token);if(identity.Identity.Actor.AccountId!=current.Actor.AccountId||!identity.Identity.Actor.IsAdministrator)throw new UnauthorizedAccessException("目标项目身份不匹配。");return identity.Identity;
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
