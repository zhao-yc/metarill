using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Metadata;
using Metarill.Generation;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>真实临时文件验证队列重启、lease和预览终态；不访问数据库或本机工程。</summary>
public sealed class GenerationAssistantQueueTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "metarill-assistant-queue-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();
    private readonly AssistantOwner web = new(1, "2200000000000100000");
    private readonly AssistantOwner agent = new(1, "2200000000000100000", 11);
    private readonly string binding = Guid.NewGuid().ToString("D");
    private GenerationAssistantQueue Queue() => new(new GenerationAssistantOptions(root) { RequestTtlSeconds = 60, LeaseSeconds = 20 }, clock);
    private void Bind(GenerationAssistantQueue queue) => queue.Bind(agent, new(binding, "Example.Assistant", new string('a', 64)));
    private static GenerationUpdateRequest Snapshot(string action = "sync-runtime", [CallerFilePath] string file = "") => LocalGenerationUpdater.CreateRequest(JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../../backend/metadata/demo.json"))), new JsonSerializerOptions(JsonSerializerDefaults.Web))!, action);

    /// <summary>重启后同UUID仍指向原快照，claim重读同lease，不会创建第二份工作。</summary>
    [Fact]
    public void Restart_retains_binding_snapshot_and_exact_single_lease()
    {
        var queue = Queue(); Bind(queue); var id = Guid.NewGuid().ToString("D"); var snapshot = Snapshot(); queue.Enqueue(web, id, binding, snapshot);
        queue = Queue(); var work = queue.Claim(agent, binding)!; Assert.Equal(id, work.RequestId); Assert.Equal(snapshot.RequestSha256, work.Snapshot.RequestSha256); Assert.False(work.Applied);
        var copies = Enumerable.Range(0, 8).AsParallel().Select(_ => queue.Claim(agent, binding)!.LeaseId).ToArray(); Assert.All(copies, lease => Assert.Equal(work.LeaseId, lease)); Assert.Single(Directory.GetFiles(root, "task-*.json"));
        Assert.Equal("leased", Queue().Get(web, id).Status); Assert.Equal(snapshot.RequestSha256, Queue().Enqueue(web, id, binding, snapshot).RequestSha256);
        Assert.Throws<InvalidOperationException>(() => Queue().Enqueue(web, id, binding, Snapshot("generate-all-page")));
    }

    /// <summary>授权scope来自账号/项目/grant三元组；跨scope不能读取、领取或覆盖注册。</summary>
    [Theory]
    [InlineData("account")] [InlineData("project")] [InlineData("grant")]
    public void Another_scope_cannot_claim_or_rebind(string kind)
    {
        var queue = Queue(); Bind(queue); var id = Guid.NewGuid().ToString("D"); queue.Enqueue(web, id, binding, Snapshot());
        var other = kind == "account" ? agent with { AccountId = 2 } : kind == "project" ? agent with { ProjectId = "999" } : agent with { GrantId = 12 };
        Assert.Throws<UnauthorizedAccessException>(() => queue.Claim(other, binding)); Assert.Throws<UnauthorizedAccessException>(() => queue.Bind(other, new(binding, "Example.Assistant", new string('a', 64))));
        if (kind != "grant") Assert.Throws<UnauthorizedAccessException>(() => queue.Get(other with { GrantId = null }, id)); Assert.Equal("queued", queue.Get(web, id).Status);
    }

    /// <summary>lease过期和排队TTL过期均持久终结，重启不会自动重排或显示已应用。</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Expired_work_is_not_requeued_after_restart(bool claimed)
    {
        var queue = Queue(); Bind(queue); var id = Guid.NewGuid().ToString("D"); queue.Enqueue(web, id, binding, Snapshot()); if (claimed) queue.Claim(agent, binding); clock.Advance(claimed ? 21 : 61);
        var status = Queue().Get(web, id); Assert.Equal("expired", status.Status); Assert.False(status.Applied); Assert.Equal("not_applied", status.Outcome); Assert.Null(Queue().Claim(agent, binding));
    }

    /// <summary>预览回执必须匹配lease/hash；重复相同回执幂等，不能改成应用成功或覆盖其他摘要。</summary>
    [Fact]
    public void Preview_receipt_is_durable_idempotent_and_never_application_success()
    {
        var queue = Queue(); Bind(queue); var id = Guid.NewGuid().ToString("D"); queue.Enqueue(web, id, binding, Snapshot()); var work = queue.Claim(agent, binding)!;
        var receipt = new AssistantPreviewReceipt(binding, id, work.LeaseId, work.Snapshot.RequestSha256, "dependencies-required", new string('b', 64), ["generate-project-config"], 1, 2, 0, 10);
        Assert.Throws<UnauthorizedAccessException>(() => queue.Report(agent, receipt with { LeaseId = Guid.NewGuid().ToString("D") })); Assert.Throws<InvalidOperationException>(() => queue.Report(agent, receipt with { Status = "succeeded" }));
        var status = queue.Report(agent, receipt); Assert.Equal("dependencies_required", status.Status); Assert.False(status.Applied); Assert.Equal("not_applied", status.Outcome);
        var repeated = Queue().Report(agent, receipt); Assert.Equal(status.Status, repeated.Status); Assert.Equal(status.Preview!.PlanSha256, repeated.Preview!.PlanSha256); Assert.Equal(status.Preview.RequiredActions, repeated.Preview.RequiredActions); Assert.Throws<InvalidOperationException>(() => Queue().Report(agent, receipt with { PlanSha256 = new string('c', 64) })); Assert.Null(Queue().Claim(agent, binding));
    }

    /// <summary>状态损坏被拒绝并保持原字节，不用新的任务替代已有UUID。</summary>
    [Fact]
    public void Corrupted_persistent_snapshot_blocks_instead_of_replacing_work()
    {
        var queue = Queue(); Bind(queue); var id = Guid.NewGuid().ToString("D"); queue.Enqueue(web, id, binding, Snapshot()); var path = Path.Combine(root, "task-" + id + ".json");
        var json = JsonNode.Parse(File.ReadAllText(path))!; json["snapshot"]!["metadata"]!["yzProjectName"] = "篡改快照"; File.WriteAllText(path, json.ToJsonString()); var bytes = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => Queue().Claim(agent, binding)); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>HTTP协议严格拒绝未知目录/SQL、重复键，不能让字段覆盖改变控制合同。</summary>
    [Theory]
    [InlineData("{\"bindingId\":\"x\",\"action\":\"sync-runtime\",\"target\":\"C:/private\"}")]
    [InlineData("{\"bindingId\":\"x\",\"bindingId\":\"y\",\"action\":\"sync-runtime\"}")]
    [InlineData("{\"bindingId\":\"x\",\"action\":\"sync-runtime\",\"sql\":\"select 1\"}")]
    public void Http_contract_rejects_unknown_and_duplicate_fields(string input)
    { using var json = JsonDocument.Parse(input); Assert.ThrowsAny<Exception>(() => GenerationAssistantQueue.Parse<AssistantEnqueueRequest>(json.RootElement)); }

    /// <summary>实际目录联接不能把服务器状态写到队列根之外，拒绝后外部目录仍为空。</summary>
    [Fact]
    public void Linked_server_state_root_is_rejected_without_external_writes()
    {
        var outside = root + "-outside"; Directory.CreateDirectory(outside);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if ((root + outside).IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n']) >= 0) throw new InvalidOperationException("测试路径包含shell字符。");
                using var process = Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = "/c mklink /J \"" + root + "\" \"" + outside + "\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(root, outside);
            Assert.Throws<InvalidOperationException>(() => Queue()); Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { if (Directory.Exists(root)) { Assert.True((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0); Directory.Delete(root, false); } Directory.Delete(outside, false); }
    }

    /// <summary>默认preview即使已确认动作也没有应用权限，不能由网络升级本机策略。</summary>
    [Fact]
    public void Preview_binding_rejects_application_ticket()
    {
        var queue = Queue(); Bind(queue); var id = Guid.NewGuid().ToString("D"); var snapshot = Snapshot(); queue.Enqueue(web,id,binding,snapshot);
        Assert.Throws<UnauthorizedAccessException>(() => queue.Authorize(agent,new(binding,Guid.NewGuid().ToString("D"),snapshot.MetadataSha256,[id],[snapshot.Action],new string('b',64)),snapshot.MetadataSha256));
        Assert.Empty(Directory.GetFiles(root,"group-*.json")); Assert.Equal("queued",queue.Get(web,id).Status);
    }
    /// <summary>只把同快照下分别确认的UUID合组，短ticket重启幂等；applied只能来自独立应用回执。</summary>
    [Fact]
    public void Exact_same_snapshot_confirmation_group_is_durable_and_reports_real_application_separately()
    {
        var queue=Queue(); queue.Bind(agent,new(binding,"Example.Assistant",new string('a',64),"apply",["sync-runtime","generate-project-config"]));
        var one=Guid.NewGuid().ToString("D"); var two=Guid.NewGuid().ToString("D"); var first=Snapshot(); var second=Snapshot("generate-project-config");
        queue.Enqueue(web,one,binding,first); queue.Enqueue(web,two,binding,second); Assert.Equal(2,queue.Approvals(agent,new(binding,first.MetadataSha256)).Length);
        var request=new AssistantAuthorizeRequest(binding,Guid.NewGuid().ToString("D"),first.MetadataSha256,[one,two],["sync-runtime","generate-project-config"],new string('b',64));
        Assert.Throws<UnauthorizedAccessException>(()=>queue.Authorize(agent,request with { Actions=["sync-runtime","generate-project-config","generate-all-page"] },first.MetadataSha256));
        Assert.Throws<InvalidOperationException>(()=>queue.Authorize(agent,request,new string('c',64)));
        var ticket=queue.Authorize(agent,request,first.MetadataSha256); Assert.Equal(ticket.TicketSha256,Queue().Authorize(agent,request,first.MetadataSha256).TicketSha256);
        Assert.Null(Queue().Claim(agent,binding)); var status=Queue().Get(web,one); Assert.False(status.Applied); Assert.Equal("unknown",status.Outcome); Assert.Equal("awaiting_application",status.Status);
        var check=new AssistantTicketRequest(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256); Assert.Equal(ticket.TicketSha256,Queue().VerifyTicket(agent,check,first.MetadataSha256).TicketSha256);
        Assert.Throws<UnauthorizedAccessException>(()=>Queue().VerifyTicket(agent with { GrantId=12 },check,first.MetadataSha256));
        Assert.Throws<InvalidOperationException>(()=>Queue().VerifyTicket(agent,check,new string('c',64)));
        var applied=new AssistantApplicationReceipt(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256,"applied",3,0,10);
        Assert.Equal(applied,Queue().ReportApplication(agent,applied)); Assert.Equal(applied,Queue().ReportApplication(agent,applied));
        Assert.True(Queue().Get(web,one).Applied); Assert.True(Queue().Get(web,two).Applied); Assert.Equal("applied",Queue().Get(web,two).Outcome);
        Assert.Throws<InvalidOperationException>(()=>Queue().ReportApplication(agent,applied with { Status="manual-recovery",WrittenFiles=0,PreservedFiles=0,ErrorCode="application_unknown" }));
    }
    /// <summary>过期ticket不能写；未知应用组不重新派发，也不伪造未执行证明。</summary>
    [Fact]
    public void Expired_ticket_is_not_reissued_and_keeps_unknown_application_state()
    {
        var queue=Queue(); queue.Bind(agent,new(binding,"Example.Assistant",new string('a',64),"apply",["sync-runtime"])); var id=Guid.NewGuid().ToString("D"); var snapshot=Snapshot(); queue.Enqueue(web,id,binding,snapshot);
        var request=new AssistantAuthorizeRequest(binding,Guid.NewGuid().ToString("D"),snapshot.MetadataSha256,[id],["sync-runtime"],new string('b',64)); var ticket=queue.Authorize(agent,request,snapshot.MetadataSha256); clock.Advance(61);
        Assert.Throws<InvalidOperationException>(()=>Queue().VerifyTicket(agent,new(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256),snapshot.MetadataSha256));
        Assert.Throws<InvalidOperationException>(()=>Queue().Authorize(agent,request,snapshot.MetadataSha256)); Assert.Null(Queue().Claim(agent,binding)); Assert.Equal("unknown",Queue().Get(web,id).Outcome);
    }
    /// <summary>跨快照或他人确认UUID不能参与同一组，不能把未点击动作追加为all。</summary>
    [Fact]
    public void Mixed_snapshot_or_account_intent_cannot_form_application_group()
    {
        var queue=Queue(); queue.Bind(agent,new(binding,"Example.Assistant",new string('a',64),"apply",["sync-runtime","generate-project-config"])); var one=Guid.NewGuid().ToString("D"); var two=Guid.NewGuid().ToString("D"); var snapshot=Snapshot(); var other=Snapshot("generate-project-config"); other.Metadata.YzProjectName="另一快照"; other=LocalGenerationUpdater.CreateRequest(other.Metadata,other.Action);
        queue.Enqueue(web,one,binding,snapshot); queue.Enqueue(web,two,binding,other);
        Assert.Throws<InvalidOperationException>(()=>queue.Authorize(agent,new(binding,Guid.NewGuid().ToString("D"),snapshot.MetadataSha256,[one,two],["sync-runtime","generate-project-config"],new string('b',64)),snapshot.MetadataSha256));
        Assert.Empty(Directory.GetFiles(root,"group-*.json"));
    }

    /// <summary>阶段一旧preview记录只读兼容，不阻断新profile绑定；不写旧文件、不静默增加apply策略。</summary>
    [Fact]
    public void Legacy_preview_binding_remains_readonly_without_blocking_new_profile()
    {
        var queue=Queue(); Bind(queue); var id=Guid.NewGuid().ToString("D"); queue.Enqueue(web,id,binding,Snapshot()); var path=Path.Combine(root,"binding-"+binding+".json");
        var node=JsonNode.Parse(File.ReadAllText(path))!.AsObject(); node.Remove("mode"); node.Remove("allowedActions"); node["integritySha256"]="";
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"}; var text=node.ToJsonString(options)+"\n"; node["integritySha256"]=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))); File.WriteAllText(path,node.ToJsonString(options)+"\n"); var bytes=File.ReadAllBytes(path);
        Assert.Equal("preview",Queue().Bindings(web).Single().Mode); Assert.Empty(Queue().Bindings(web).Single().AllowedActions); Assert.NotNull(Queue().Claim(agent,binding));
        Queue().Bind(agent,new(Guid.NewGuid().ToString("D"),"Example.Assistant",new string('c',64),"apply",["sync-runtime"])); Assert.Equal(2,Queue().Bindings(web).Length); Assert.Equal(bytes,File.ReadAllBytes(path));
    }

    public void Dispose() { if (Directory.Exists(root)) { Assert.StartsWith(Path.GetTempPath(), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); Directory.Delete(root, true); } }
    private sealed class Clock : TimeProvider { private DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance(int seconds) => now = now.AddSeconds(seconds); }
}
