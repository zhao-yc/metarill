using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Api.GenerationAssistant;
using Metarill.Generation;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Xunit;
namespace Metarill.Api.Tests;

public sealed partial class GenerationAssistantRuntimeOriginTests
{
    private AssistantRuntimeBatchOwner BatchOwner=>new(1,metadata.YzProjectId,31);
    private AssistantDeviceProjectDiscovery Discovery()=>new("11",[new(metadata.YzProjectId,"样例","21","configured",null,Scope())]);
    /// <summary>首批完整清单先落盘，原UUID重试不吸纳新项目；状态与意图只读不生成任务。</summary>
    [Fact]
    public void Runtime_batch_restart_freezes_original_discovery_without_creating_tasks()
    {
        var queue=Queue();var batchId=Guid.NewGuid().ToString("D");var first=queue.BeginRuntimeBatch(BatchOwner,batchId,Discovery());var rootFile=Path.Combine(root,"batch-"+batchId+".json");var bytes=File.ReadAllBytes(rootFile);
        var changed=Discovery() with{Projects=[Discovery().Projects[0],new("2","新项目",null,"skipped","generation_disabled",null)]};
        var retry=Queue().BeginRuntimeBatch(BatchOwner,batchId,changed);Assert.Single(retry.Projects);Assert.Equal(first.ExpiresAt,retry.ExpiresAt);Assert.Equal(bytes,File.ReadAllBytes(rootFile));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
        var before=BatchDisk();Assert.Equal("pending",Queue().RuntimeBatchItemStatus(BatchOwner,batchId,0,Web).Status);Assert.Null(Queue().RuntimeBatchIntent(BatchOwner,batchId,0));Assert.Equal(before,BatchDisk());
    }
    /// <summary>单项目快照和精确指向先刷盘；模拟重启后沿原意图创建任务，重复创建不重新排队。</summary>
    [Fact]
    public void Runtime_batch_prepared_intent_survives_restart_and_publishes_same_exact_task()
    {
        var queue=Queue();var origin=Setup(queue);var batchId=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,batchId,Discovery());var snapshot=Snapshot();var intent=queue.PrepareRuntimeBatchItem(BatchOwner,batchId,0,Web,binding,snapshot,origin,Scope());
        Assert.Empty(Directory.GetFiles(root,"task-*.json"));Assert.Equal("prepared",Queue().RuntimeBatchItemStatus(BatchOwner,batchId,0,Web).Status);Assert.Equal(intent.AssignedRequestId,GenerationAssistantBatchPlanning.StableRequestId(batchId,Web.ProjectId));
        var recovered=Queue().RuntimeBatchIntent(BatchOwner,batchId,0)!;Assert.Equal(intent.AssignedRequestId,recovered.AssignedRequestId);Assert.Equal(snapshot.RequestSha256,recovered.Snapshot.RequestSha256);Assert.Equal(origin,recovered.RuntimeOrigin);
        var task=Queue().EnqueueRuntimeBatchItem(BatchOwner,batchId,0,Web,Scope());Assert.Equal(intent.AssignedRequestId,task.RequestId);Assert.Equal(task.RequestId,Queue().EnqueueRuntimeBatchItem(BatchOwner,batchId,0,Web,Scope()).RequestId);Assert.Single(Directory.GetFiles(root,"task-*.json"));
        var before=BatchDisk();Assert.Equal(task.RequestId,Queue().RuntimeBatchItemStatus(BatchOwner,batchId,0,Web).RequestId);Assert.Equal(before,BatchDisk());
    }
    /// <summary>去重别名先落盘，原任务后来完成且有新活动项时仍沿原指向，不能重新选择或重建。</summary>
    [Fact]
    public void Runtime_batch_alias_keeps_original_task_after_completion_and_new_activity()
    {
        var queue=Queue();var origin=Setup(queue);var snapshot=Snapshot();var old=Guid.NewGuid().ToString("D");queue.EnqueueRuntime(Web,old,binding,snapshot,origin,Scope());var batchId=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,batchId,Discovery());var intent=queue.PrepareRuntimeBatchItem(BatchOwner,batchId,0,Web,binding,snapshot,origin,Scope());Assert.True(intent.ExistingTask);Assert.Equal(old,intent.AssignedRequestId);
        var ticket=queue.Authorize(Agent,Authorization(old,snapshot),snapshot.MetadataSha256,new(old,Scope()));queue.ReportApplication(Agent,new(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256,"applied",1,0,0));
        var next=Guid.NewGuid().ToString("D");queue.EnqueueRuntime(Web,next,binding,snapshot,origin,Scope());var recovered=Queue().EnqueueRuntimeBatchItem(BatchOwner,batchId,0,Web,Scope());Assert.Equal(old,recovered.RequestId);Assert.True(recovered.Applied);Assert.Equal("applied",Queue().RuntimeBatchItemStatus(BatchOwner,batchId,0,Web).Status);Assert.Equal(2,Directory.GetFiles(root,"task-*.json").Length);
    }
    /// <summary>已关联任务丢失属于现场异常，重启不得拿保存意图重新生成被删任务。</summary>
    [Fact]
    public void Runtime_batch_missing_alias_never_creates_replacement_task()
    {
        var queue=Queue();var origin=Setup(queue);var snapshot=Snapshot();var old=Guid.NewGuid().ToString("D");queue.EnqueueRuntime(Web,old,binding,snapshot,origin,Scope());var batchId=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,batchId,Discovery());queue.PrepareRuntimeBatchItem(BatchOwner,batchId,0,Web,binding,snapshot,origin,Scope());
        File.Delete(Path.Combine(root,"task-"+old+".json"));Assert.Throws<InvalidOperationException>(()=>Queue().EnqueueRuntimeBatchItem(BatchOwner,batchId,0,Web,Scope()));Assert.Throws<InvalidOperationException>(()=>Queue().RuntimeBatchItemStatus(BatchOwner,batchId,0,Web));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }
    /// <summary>批次绑定原账号、来源项目、Web会话；目标项目也单独核对，不把摘要当身份。</summary>
    [Theory]
    [InlineData("account")][InlineData("project")][InlineData("session")][InlineData("target-account")][InlineData("target-project")]
    public void Runtime_batch_rejects_foreign_owner_or_target(string kind)
    {
        var queue=Queue();var id=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,id,Discovery());var owner=BatchOwner;var target=Web;
        if(kind=="account")owner=owner with{AccountId=2};if(kind=="project")owner=owner with{ProjectId="2"};if(kind=="session")owner=owner with{SessionId=32};if(kind=="target-account")target=target with{AccountId=2};if(kind=="target-project")target=target with{ProjectId="2"};
        Assert.Throws<UnauthorizedAccessException>(()=>queue.RuntimeBatchItemStatus(owner,id,0,target));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }
    /// <summary>原意图不能采用新快照、配置、来源或绑定；拒绝后持久字节和任务数量保持。</summary>
    [Theory]
    [InlineData("metadata")][InlineData("configuration")][InlineData("binding")]
    public void Runtime_batch_fixed_intent_rejects_replacement(string kind)
    {
        var queue=Queue();var origin=Setup(queue);var id=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,id,Discovery());queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,binding,Snapshot(),origin,Scope());var selected=binding;var current=Scope();var snapshot=Snapshot();
        if(kind=="metadata"){metadata.YzProjectName="改后的元数据";snapshot=Snapshot();}if(kind=="configuration")current=current with{ConfigurationSha256=H('7')};if(kind=="binding"){selected=Guid.NewGuid().ToString("D");queue.Bind(Agent,new(selected,"Example.Other",H('9'),"apply",["sync-runtime"]));}
        var before=BatchDisk();var error=Record.Exception(()=>queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,selected,snapshot,origin,current));Assert.True(error is InvalidOperationException or UnauthorizedAccessException);Assert.Equal(before,BatchDisk());Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }
    /// <summary>空/跳过/失败项不生成任务；明确初始失败不能被重试准备悄悄改写成可执行项。</summary>
    [Fact]
    public void Runtime_batch_failure_marker_is_durable_without_overwriting_prepared_intent()
    {
        var queue=Queue();var origin=Setup(queue);var id=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,id,Discovery());queue.RejectRuntimeBatchItem(BatchOwner,id,0,"approved_runtime_binding_missing");Assert.Equal("failed",Queue().RuntimeBatchItemStatus(BatchOwner,id,0,Web).Status);
        Assert.Throws<InvalidOperationException>(()=>queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,binding,Snapshot(),origin,Scope()));
        var another=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,another,Discovery());queue.PrepareRuntimeBatchItem(BatchOwner,another,0,Web,binding,Snapshot(),origin,Scope());var before=BatchDisk();queue.RejectRuntimeBatchItem(BatchOwner,another,0,"runtime_enqueue_rejected");Assert.Equal(before,BatchDisk());Assert.NotNull(Queue().RuntimeBatchIntent(BatchOwner,another,0));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }
    /// <summary>批次读盘校验坏记录和未知字段，损坏文件不能覆盖成新批次或创建替代任务。</summary>
    [Theory]
    [InlineData("definition")][InlineData("item")]
    public void Runtime_batch_corruption_blocks_recovery_without_replacement(string kind)
    {
        var queue=Queue();var origin=Setup(queue);var id=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,id,Discovery());queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,binding,Snapshot(),origin,Scope());
        var file=Path.Combine(root,kind=="definition"?"batch-"+id+".json":"batchitem-"+id+"-0000.json");var json=JsonNode.Parse(File.ReadAllText(file))!;json["unknownCommand"]="不允许";File.WriteAllText(file,json.ToJsonString());
        Assert.Throws<JsonException>(()=>queue.RuntimeBatchIntent(BatchOwner,id,0));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }
    /// <summary>期限不会被重复批次延长，状态仍可读，过期准备不创建任务。</summary>
    [Fact]
    public void Runtime_batch_expiry_is_fixed_and_read_only_status_remains_available()
    {
        var clock=new RuntimeBatchClock();var queue=new GenerationAssistantQueue(new(root){RequestTtlSeconds=60,LeaseSeconds=20},clock);var origin=Setup(queue);var id=Guid.NewGuid().ToString("D");var first=queue.BeginRuntimeBatch(BatchOwner,id,Discovery());queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,binding,Snapshot(),origin,Scope());clock.Now=clock.Now.AddSeconds(61);
        Assert.Equal(first.ExpiresAt,queue.BeginRuntimeBatch(BatchOwner,id,Discovery()).ExpiresAt);Assert.Equal("expired",queue.RuntimeBatchItemStatus(BatchOwner,id,0,Web).Status);Assert.Throws<InvalidOperationException>(()=>queue.EnqueueRuntimeBatchItem(BatchOwner,id,0,Web,Scope()));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }
    /// <summary>有界批次数量拒绝不得影响原记录，也不访问真实项目或数据库。</summary>
    [Fact]
    public void Runtime_batch_capacity_rejection_keeps_original_record()
    {
        var queue=new GenerationAssistantQueue(new(root){MaxTasks=1});var id=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,id,Discovery());var before=BatchDisk();Assert.Throws<InvalidOperationException>(()=>queue.BeginRuntimeBatch(BatchOwner,Guid.NewGuid().ToString("D"),Discovery()));Assert.Equal(before,BatchDisk());
    }
    /// <summary>临近批次截止才创建的任务不能延长原确认期限，过期状态投影也不写磁盘。</summary>
    [Fact]
    public void Runtime_batch_delayed_task_keeps_batch_expiry_and_status_reads_never_write()
    {
        var clock=new RuntimeBatchClock();var queue=new GenerationAssistantQueue(new(root){RequestTtlSeconds=60,LeaseSeconds=20},clock);var origin=Setup(queue);var id=Guid.NewGuid().ToString("D");var batch=queue.BeginRuntimeBatch(BatchOwner,id,Discovery());queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,binding,Snapshot(),origin,Scope());clock.Now=clock.Now.AddSeconds(40);
        var task=queue.EnqueueRuntimeBatchItem(BatchOwner,id,0,Web,Scope());Assert.Equal(batch.ExpiresAt,task.ExpiresAt);clock.Now=clock.Now.AddSeconds(21);var before=BatchDisk();Assert.Equal("expired",queue.RuntimeBatchItemStatus(BatchOwner,id,0,Web).Status);Assert.Equal(before,BatchDisk());
    }
    /// <summary>实际协调器状态方法只读取原任务，不触发发现/元数据/授权写入，空依赖保证未误调它们。</summary>
    [Fact]
    public async Task Runtime_batch_service_status_reads_original_intent_without_preparing_or_enqueueing()
    {
        var queue=Queue();var origin=Setup(queue);var id=Guid.NewGuid().ToString("D");queue.BeginRuntimeBatch(BatchOwner,id,Discovery());var intent=queue.PrepareRuntimeBatchItem(BatchOwner,id,0,Web,binding,Snapshot(),origin,Scope());var before=BatchDisk();
        var current=new AuthenticatedIdentity(new RuntimeActor(1,"合成批次查询",true,Web.ProjectId,"1"),new HashSet<string>(),31,null);var service=new GenerationAssistantBatchEnqueuer(queue,null!,null!,null!,null!);
        var status=await service.StatusAsync(current,id,null,null);Assert.Equal(id,status.BatchId);Assert.Equal("prepared",Assert.Single(status.Items).Status);Assert.Equal(intent.AssignedRequestId,status.Items[0].RequestId);Assert.Equal(before,BatchDisk());Assert.Empty(Directory.GetFiles(root,"task-*.json"));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.StatusAsync(current,id,null,null,cancelled.Token));Assert.Equal(before,BatchDisk());
    }

    /// <summary>哈希快照只比较独占目录内的协议字节，不输出凭据、文件内容或本机设置。</summary>
    private string BatchDisk()=>string.Join("\n",Directory.GetFiles(root,"*.json").Order(StringComparer.Ordinal).Select(path=>Path.GetFileName(path)+":"+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));
    /// <summary>明确合成时间，验证重试不能延长原确认期限。</summary>
    private sealed class RuntimeBatchClock:TimeProvider{internal DateTimeOffset Now=new(2026,10,31,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
}
