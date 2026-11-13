using Metarill.Api.GenerationAssistant;
using Metarill.Generation;
using Xunit;
namespace Metarill.Api.Tests;

public sealed partial class GenerationAssistantRuntimeOriginTests
{
    /// <summary>确定UUID已被其它快照占用时先拒绝，不能被另一条同来源活动任务掩盖冲突。</summary>
    [Fact]
    public void Runtime_deduplication_checks_exact_request_id_before_aliasing_an_active_task()
    {
        var queue=Queue();var origin=Setup(queue);var old=Guid.NewGuid().ToString("D");queue.Enqueue(Web,old,binding,LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime"));
        var active=Guid.NewGuid().ToString("D");queue.EnqueueRuntime(Web,active,binding,Snapshot(),origin,Scope());
        var before=File.ReadAllBytes(Path.Combine(root,"task-"+old+".json"));
        Assert.Throws<InvalidOperationException>(()=>Queue().EnqueueRuntime(Web,old,binding,Snapshot(),origin,Scope()));Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"task-"+old+".json")));Assert.Equal(2,Directory.GetFiles(root,"task-*.json").Length);
    }

    /// <summary>已applied/回滚/未知的任务不再吞掉新批次，精确旧UUID仍返回旧终态，新的去重回执指向实际文件。</summary>
    [Theory]
    [InlineData("applied",null)][InlineData("rolled-back","application_rolled_back")][InlineData("manual-recovery","application_unknown")]
    public void Runtime_deduplication_releases_terminal_slots_and_preserves_exact_history(string status,string? error)
    {
        var queue=Queue();var origin=Setup(queue);var snapshot=Snapshot();var old=Guid.NewGuid().ToString("D");queue.EnqueueRuntime(Web,old,binding,snapshot,origin,Scope());
        var ticket=queue.Authorize(Agent,Authorization(old,snapshot),snapshot.MetadataSha256,new(old,Scope()));
        queue.ReportApplication(Agent,new(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256,status,status=="applied"?1:0,0,0,error));
        var next=Guid.NewGuid().ToString("D");var fresh=Queue().EnqueueRuntime(Web,next,binding,snapshot,origin,Scope());Assert.Equal(next,fresh.RequestId);Assert.Equal("queued",fresh.Status);
        var history=Queue().EnqueueRuntime(Web,old,binding,snapshot,origin,Scope());Assert.Equal(old,history.RequestId);Assert.Equal(status.Replace('-','_'),history.Status);
        var alias=Queue().EnqueueRuntime(Web,Guid.NewGuid().ToString("D"),binding,snapshot,origin,Scope());Assert.Equal(next,alias.RequestId);Assert.True(File.Exists(Path.Combine(root,"task-"+alias.RequestId+".json")));Assert.Equal(2,Directory.GetFiles(root,"task-*.json").Length);
        var item=GenerationAssistantBatchPlanning.FromTask(metadata.YzProjectId,"样例",alias);Assert.Equal(next,item.RequestId);Assert.Equal(alias.Status,item.Status);
    }

    /// <summary>领取中的活动任务去重必须返回原lease所属任务，不能回传尚未持久化的批次推导编号。</summary>
    [Fact]
    public void Runtime_deduplication_reports_actual_leased_id_and_status()
    {
        var queue=Queue();var origin=Setup(queue);var snapshot=Snapshot();var id=Guid.NewGuid().ToString("D");queue.EnqueueRuntime(Web,id,binding,snapshot,origin,Scope());queue.Claim(Agent,binding,new(id,Scope()));
        var alias=queue.EnqueueRuntime(Web,Guid.NewGuid().ToString("D"),binding,snapshot,origin,Scope());var item=GenerationAssistantBatchPlanning.FromTask(metadata.YzProjectId,"样例",alias);
        Assert.Equal(id,item.RequestId);Assert.Equal("leased",item.Status);Assert.Single(Directory.GetFiles(root,"task-*.json"));
    }
}
