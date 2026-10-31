namespace Metarill.Api.GenerationAssistant;

public sealed partial class GenerationAssistantQueue
{
    private sealed record RuntimeBatch(int SchemaVersion,AssistantRuntimeBatchOwner Owner,string BatchId,string DeviceId,DateTimeOffset CreatedAt,DateTimeOffset ExpiresAt,AssistantDeviceProjectSelection[] Projects,string IntegritySha256);
    private sealed record RuntimeBatchItem(int SchemaVersion,string BatchId,int Index,string DefinitionSha256,AssistantRuntimeBatchIntent? Intent,string? ErrorCode,string IntegritySha256);

    /// <summary>只读寻找原批次；同UUID跨会话/设备或损坏状态不能悄悄生成新清单。</summary>
    public AssistantRuntimeBatchDefinition? FindRuntimeBatch(AssistantRuntimeBatchOwner owner,string batchId)=>Locked<AssistantRuntimeBatchDefinition?>(()=>
    {
        RequireBatchOwner(owner);Uuid(batchId);return File.Exists(Safe("batch-"+batchId+".json"))?BatchDefinition(RequireBatch(owner,batchId)):null;
    });
    /// <summary>在任何项目入队前先刷盘保存整批发现；重复同UUID固定第一次清单，不吸纳新项目。</summary>
    public AssistantRuntimeBatchDefinition BeginRuntimeBatch(AssistantRuntimeBatchOwner owner,string batchId,AssistantDeviceProjectDiscovery discovery)=>Locked(()=>
    {
        RequireBatchOwner(owner);Uuid(batchId);DeviceId(discovery.DeviceId);
        if(File.Exists(Safe("batch-"+batchId+".json")))
        {
            var old=RequireBatch(owner,batchId);if(old.DeviceId!=discovery.DeviceId)throw new InvalidOperationException("原批次不能切换设备。");return BatchDefinition(old);
        }
        if(Directory.GetFiles(options.RootDirectory,"batch-*.json").Length>=options.MaxTasks)throw new InvalidOperationException("批次记录达到保留上限。");
        var now=clock.GetUtcNow();var batch=new RuntimeBatch(1,owner,batchId,discovery.DeviceId,now,now.AddSeconds(options.RequestTtlSeconds),discovery.Projects,"");ValidateBatch(batch);SaveBatch(batch);return BatchDefinition(batch);
    });
    /// <summary>协调器只读原意图，恢复不重新生成snapshot或改用新绑定；每项目快照独立有界文件避免整批8MiB上限。</summary>
    public AssistantRuntimeBatchIntent? RuntimeBatchIntent(AssistantRuntimeBatchOwner owner,string batchId,int index)=>Locked(()=>
    {
        var batch=RequireBatch(owner,batchId);_=BatchSelection(batch,index);return ReadBatchItem(batch,index)?.Intent;
    });
    /// <summary>准备先固定本次快照和实际任务指向，再允许入队；命中其它活动任务时先保存别名，崩溃后不重新选取。</summary>
    public AssistantRuntimeBatchIntent PrepareRuntimeBatchItem(AssistantRuntimeBatchOwner owner,string batchId,int index,AssistantOwner target,string bindingId,Metarill.Generation.GenerationUpdateRequest snapshot,AssistantRuntimeOrigin origin,AssistantDeviceScope current)=>Locked(()=>
    {
        var batch=RequireBatch(owner,batchId);RequireBatchFresh(batch);var selection=BatchSelection(batch,index);RequireBatchTarget(batch,selection,target);var binding=ReadBinding(bindingId);RequireScope(binding,target);
        if(selection.Status!="configured"||selection.Scope!=current||origin.Device!=selection.Scope)throw new UnauthorizedAccessException("本批次设备配置已变化，不能采用新的范围。");
        Metarill.Generation.LocalGenerationUpdater.ValidateRequest(snapshot);ValidateRuntimeSnapshot(target,snapshot,origin);if(snapshot.ProjectId!=target.ProjectId)throw new UnauthorizedAccessException("批次快照项目不符。");RequireCurrentRuntimeOrigin(target,binding,origin,current);
        var old=ReadBatchItem(batch,index);if(old is not null)
        {
            if(old.Intent is not{} intent||intent.BindingId!=bindingId||intent.Snapshot.RequestSha256!=snapshot.RequestSha256||intent.RuntimeOrigin!=origin)throw new InvalidOperationException("原批次意图已固定，不能替换快照或绑定。");return intent;
        }
        var requestId=GenerationAssistantBatchPlanning.StableRequestId(batchId,target.ProjectId);var existing=false;
        if(File.Exists(Safe("task-"+requestId+".json")))
        {
            var exact=ReadTask(requestId);RequireBatchTask(target,bindingId,snapshot.RequestSha256,origin,exact);existing=true;
        }
        else
        {
            var duplicate=Directory.GetFiles(options.RootDirectory,"task-*.json").Select(path=>Expire(ReadTask(Path.GetFileName(path)[5..^5])))
                .Where(task=>task.BindingId==bindingId&&task.Snapshot.RequestSha256==snapshot.RequestSha256&&task.RuntimeOrigin==origin&&task.Status is "queued" or "leased" or "preview_ready" or "dependencies_required")
                .OrderBy(task=>task.CreatedAt).ThenBy(task=>task.RequestId,StringComparer.Ordinal).FirstOrDefault(task=>View(task).Application is null);
            if(duplicate is not null){requestId=duplicate.RequestId;existing=true;}
        }
        var prepared=new AssistantRuntimeBatchIntent(bindingId,requestId,existing,snapshot,origin);SaveBatchItem(batch,new(1,batchId,index,batch.IntegritySha256,prepared,null,""));return prepared;
    });
    /// <summary>原意图已落盘后只写精确任务ID；即使别名任务后来完成也不产生替代任务。</summary>
    public AssistantTaskView EnqueueRuntimeBatchItem(AssistantRuntimeBatchOwner owner,string batchId,int index,AssistantOwner target,AssistantDeviceScope current)=>Locked(()=>
    {
        var batch=RequireBatch(owner,batchId);RequireBatchFresh(batch);RequireBatchTarget(batch,BatchSelection(batch,index),target);var item=ReadBatchItem(batch,index)?.Intent??throw new InvalidOperationException("批次项目尚未固定入队意图。");
        if(item.ExistingTask&&!File.Exists(Safe("task-"+item.AssignedRequestId+".json")))throw new InvalidOperationException("原关联任务已丢失，不能创建替代任务。");
        return EnqueueCore(target,item.AssignedRequestId,item.BindingId,item.Snapshot,item.RuntimeOrigin,current,false,batch.ExpiresAt);
    });
    /// <summary>当前目标项目Web身份通过后只读原任务结果；已过期批次仍可查历史，不创建任务或lease。</summary>
    public AssistantRuntimeBatchEnqueueItem RuntimeBatchItemStatus(AssistantRuntimeBatchOwner owner,string batchId,int index,AssistantOwner target)=>Locked(()=>
    {
        var batch=RequireBatch(owner,batchId);var selection=BatchSelection(batch,index);RequireBatchTarget(batch,selection,target);var item=ReadBatchItem(batch,index);
        if(selection.Status!="configured")return new(selection.ProjectId,selection.ProjectName,selection.Status,selection.ErrorCode,null);
        if(item?.ErrorCode is{} code)return new(selection.ProjectId,selection.ProjectName,"failed",code,null);
        if(item?.Intent is not{} intent)return new(selection.ProjectId,selection.ProjectName,clock.GetUtcNow()>=batch.ExpiresAt?"expired":"pending",null,null);
        if(!File.Exists(Safe("task-"+intent.AssignedRequestId+".json")))
        {
            if(intent.ExistingTask)throw new InvalidOperationException("原关联任务丢失，保留批次现场。");
            return new(selection.ProjectId,selection.ProjectName,clock.GetUtcNow()>=batch.ExpiresAt?"expired":"prepared",null,intent.AssignedRequestId);
        }
        var task=ReadTask(intent.AssignedRequestId);RequireBatchTask(target,intent.BindingId,intent.Snapshot.RequestSha256,intent.RuntimeOrigin,task);// 状态查询按当前时间投影过期，不调用会落盘的Expire，也不取得lease。
        if(task.Status is "queued" or "leased"&&(clock.GetUtcNow()>=task.ExpiresAt||task.LeaseExpiresAt is{} lease&&clock.GetUtcNow()>=lease))task=task with{Status="expired"};
        return GenerationAssistantBatchPlanning.FromTask(target.ProjectId,selection.ProjectName,View(task));
    });
    /// <summary>只有尚未准备的项目可记明确失败；未知磁盘/网络错误或已固定意图不覆写成其它失败记录。</summary>
    public void RejectRuntimeBatchItem(AssistantRuntimeBatchOwner owner,string batchId,int index,string errorCode)=>Locked(()=>
    {
        var batch=RequireBatch(owner,batchId);_=BatchSelection(batch,index);
        if(errorCode is not("approved_runtime_binding_missing" or "target-project-reauthentication-required" or "runtime_enqueue_rejected"))throw new InvalidOperationException("批次错误代码无效。");
        if(ReadBatchItem(batch,index) is null)SaveBatchItem(batch,new(1,batchId,index,batch.IntegritySha256,null,errorCode,""));return true;
    });
    /// <summary>原发现清单与身份必须结构有效，校验和用于漂移检测，不当作授予权限的签名。</summary>
    private static void ValidateBatch(RuntimeBatch batch)
    {
        RequireBatchOwner(batch.Owner);Uuid(batch.BatchId);DeviceId(batch.DeviceId);
        if(batch.SchemaVersion!=1||batch.ExpiresAt<=batch.CreatedAt||batch.Projects is null||batch.Projects.Length>1000)throw new InvalidOperationException("批次结构无效。");
        var projects=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in batch.Projects)
        {
            if(item is null||item.Status is not("configured" or "skipped" or "failed")||item.ProjectName is null)throw new InvalidOperationException("批次项目结构无效。");
            if(item.ProjectId is{} project){DeviceId(project);if(!projects.Add(project))throw new InvalidOperationException("批次不能重复同项目。");}
            if(item.Status=="configured")
            {
                if(item.Scope is not{} scope||item.ProjectId!=scope.ProjectId||item.ConfigurationId!=scope.ConfigurationId||scope.DeviceId!=batch.DeviceId)throw new InvalidOperationException("批次来源不符。");DeviceScope(new(batch.Owner.AccountId,scope.ProjectId),scope);
            }
            else if(item.Scope is not null)throw new InvalidOperationException("未就绪项目不能带可执行来源。");
        }
    }
    /// <summary>批次只属于实际Web会话，不能从Agent或跨账号/工作项目读取。</summary>
    private static void RequireBatchOwner(AssistantRuntimeBatchOwner owner){RequireOwner(new(owner.AccountId,owner.ProjectId),false);if(owner.SessionId<=0)throw new UnauthorizedAccessException("批次需要真实Web会话。");}
    private RuntimeBatch RequireBatch(AssistantRuntimeBatchOwner owner,string id)
    {
        RequireBatchOwner(owner);Uuid(id);var batch=Read<RuntimeBatch>("batch-"+id+".json");ValidateBatch(batch);
        if(batch.BatchId!=id||batch.IntegritySha256!=Hash(batch with{IntegritySha256=""}))throw new InvalidOperationException("批次持久记录损坏。");if(batch.Owner!=owner)throw new UnauthorizedAccessException("批次不属于当前Web会话。");return batch;
    }
    /// <summary>快照期限不因网络重试延长；状态查询仍可读，不能再执行新入队。</summary>
    private void RequireBatchFresh(RuntimeBatch batch){if(clock.GetUtcNow()>=batch.ExpiresAt)throw new InvalidOperationException("原批次已过期，不能继续入队。");}
    private static AssistantDeviceProjectSelection BatchSelection(RuntimeBatch batch,int index)=>index>=0&&index<batch.Projects.Length?batch.Projects[index]:throw new InvalidOperationException("批次项目序号无效。");
    private static void RequireBatchTarget(RuntimeBatch batch,AssistantDeviceProjectSelection selection,AssistantOwner target){RequireOwner(target,false);if(target.AccountId!=batch.Owner.AccountId||target.ProjectId!=selection.ProjectId)throw new UnauthorizedAccessException("目标身份与批次项目不一致。");}
    private static void RequireBatchTask(AssistantOwner target,string binding,string hash,AssistantRuntimeOrigin origin,AssistantTask task){RequireScope(task,target);if(task.BindingId!=binding||task.Snapshot.RequestSha256!=hash||task.RuntimeOrigin!=origin)throw new InvalidOperationException("原批次任务已绑定其它内容。");}
    private RuntimeBatchItem? ReadBatchItem(RuntimeBatch batch,int index)
    {
        var name=BatchItemName(batch.BatchId,index);if(!File.Exists(Safe(name)))return null;var item=Read<RuntimeBatchItem>(name);
        if(item.SchemaVersion!=1||item.BatchId!=batch.BatchId||item.Index!=index||item.DefinitionSha256!=batch.IntegritySha256||item.IntegritySha256!=Hash(item with{IntegritySha256=""})||(item.Intent is null)==(item.ErrorCode is null))throw new InvalidOperationException("批次项目持久记录损坏。");
        if(item.Intent is{} intent)
        {
            var selection=BatchSelection(batch,index);Uuid(intent.BindingId);Uuid(intent.AssignedRequestId);var target=new AssistantOwner(batch.Owner.AccountId,selection.ProjectId!);
            Metarill.Generation.LocalGenerationUpdater.ValidateRequest(intent.Snapshot);ValidateRuntimeSnapshot(target,intent.Snapshot,intent.RuntimeOrigin);
            if(intent.Snapshot.ProjectId!=target.ProjectId||intent.RuntimeOrigin.Device!=selection.Scope)throw new InvalidOperationException("批次项目来源损坏。");
        }
        else if(item.ErrorCode is not("approved_runtime_binding_missing" or "target-project-reauthentication-required" or "runtime_enqueue_rejected"))throw new InvalidOperationException("持久批次错误代码无效。");
        return item;
    }
    private static string BatchItemName(string id,int index)=>"batchitem-"+id+"-"+index.ToString("D4",System.Globalization.CultureInfo.InvariantCulture)+".json";
    private static AssistantRuntimeBatchDefinition BatchDefinition(RuntimeBatch batch)=>new(batch.BatchId,batch.DeviceId,batch.ExpiresAt,batch.Projects);
    private void SaveBatch(RuntimeBatch batch)=>Save("batch-"+batch.BatchId+".json",batch with{IntegritySha256=Hash(batch with{IntegritySha256=""})});
    private void SaveBatchItem(RuntimeBatch batch,RuntimeBatchItem item)=>Save(BatchItemName(batch.BatchId,item.Index),item with{IntegritySha256=Hash(item with{IntegritySha256=""})});
}
