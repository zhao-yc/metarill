using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Metadata;
using Metarill.Generation;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>真实文件队列验证固定分端来源；Scope及身份均显式合成，不证明真实数据库/OAuth或本机源码已应用。</summary>
public sealed class GenerationAssistantRuntimeOriginTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"metarill-runtime-origin",Guid.NewGuid().ToString("N"));
    private readonly MetadataCatalog metadata=Catalog();
    private readonly string binding=Guid.NewGuid().ToString("D");
    private AssistantOwner Agent=>new(1,metadata.YzProjectId,9);
    private AssistantOwner Web=>new(1,metadata.YzProjectId);
    private static string H(char value)=>new(value,64);
    private AssistantDeviceScope Scope()=>new(1,"11",metadata.YzProjectId,"21",H('2'),H('3'),new(true,false),H('4'),null);
    private GenerationAssistantQueue Queue()=>new(new(root));
    private AssistantRuntimeOrigin Setup(GenerationAssistantQueue queue)
    {
        queue.Bind(Agent,new(binding,"Example.RuntimeOrigin",H('1'),"apply",["sync-runtime","generate-project-config","generate-all-page"]));
        var association=queue.AssociateDevice(Agent,new(binding,"11",H('4'),H('5'),2,H('2'),H('3')),Scope());return new(Scope(),association.AssociationSha256);
    }
    private GenerationUpdateRequest Snapshot()=>LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime",Scope().Parts);
    private AssistantAuthorizeRequest Authorization(string id,GenerationUpdateRequest snapshot)=>new(binding,Guid.NewGuid().ToString("D"),snapshot.MetadataSha256,[id],["sync-runtime"],H('6'));

    /// <summary>普通入口不能接分端快照；新入口固定源后重启/同UUID回读，领取无证明不能获得lease。</summary>
    [Fact]
    public void Runtime_origin_is_durable_idempotent_and_required_before_claim()
    {
        var q=Queue();var origin=Setup(q);var snapshot=Snapshot();var id=Guid.NewGuid().ToString("D");
        Assert.Throws<InvalidOperationException>(()=>q.Enqueue(Web,id,binding,snapshot));Assert.Empty(Directory.GetFiles(root,"task-*.json"));
        var first=q.EnqueueRuntime(Web,id,binding,snapshot,origin,Scope());Assert.False(first.Applied);Assert.Equal("not_applied",first.Outcome);
        var file=Path.Combine(root,"task-"+id+".json");var bytes=File.ReadAllBytes(file);Assert.Equal(2,JsonNode.Parse(bytes)!["schemaVersion"]!.GetValue<int>());
        Assert.Equal(first,Queue().EnqueueRuntime(Web,id,binding,snapshot,origin,Scope()));Assert.Equal(bytes,File.ReadAllBytes(file));
        Assert.Equal(new AssistantPendingRuntimeOrigin(id,origin),Queue().PendingClaimOrigin(Agent,binding));
        Assert.Throws<UnauthorizedAccessException>(()=>q.Claim(Agent,binding));Assert.Equal(bytes,File.ReadAllBytes(file));
        var work=Queue().Claim(Agent,binding,new(id,Scope()))!;Assert.Equal(origin,work.RuntimeOrigin);Assert.Equal(2,work.Snapshot.SchemaVersion);Assert.Equal(Scope().Parts,work.Snapshot.RuntimeParts);
        Assert.Equal(work.LeaseId,Queue().Claim(Agent,binding,new(id,Scope()))!.LeaseId);
    }

    /// <summary>入队前必须同时满足快照范围、真实当前配置、固定关联和原本机策略，拒绝时不创建任务。</summary>
    [Theory]
    [InlineData("v1")][InlineData("other-action")][InlineData("other-parts")][InlineData("association")][InlineData("device")][InlineData("configuration")][InlineData("parts")][InlineData("owner")][InlineData("project")][InlineData("binding")]
    public void Runtime_origin_rejects_invalid_enqueue_before_persisting(string kind)
    {
        var q=Queue();var origin=Setup(q);var snapshot=Snapshot();var current=Scope();var selected=binding;
        if(kind=="v1")snapshot=LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime");if(kind=="other-action")snapshot=LocalGenerationUpdater.CreateRequest(metadata,"generate-all-page");if(kind=="other-parts")snapshot=LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime",new(false,true));
        if(kind=="association")origin=origin with{AssociationSha256=H('8')};if(kind=="device")current=current with{DeviceSha256=H('8')};if(kind=="configuration")current=current with{ConfigurationSha256=H('8')};
        if(kind=="parts")current=current with{Parts=new(false,true),BackendPathSha256=null,FrontendPathSha256=H('5')};if(kind=="owner")current=current with{AccountId=2};if(kind=="project")current=current with{ProjectId="2"};
        if(kind=="binding"){selected=Guid.NewGuid().ToString("D");q.Bind(Agent,new(selected,"Example.RuntimeOrigin",H('9'),"apply",["sync-runtime"]));}
        var error=Record.Exception(()=>q.EnqueueRuntime(Web,Guid.NewGuid().ToString("D"),selected,snapshot,origin,current));Assert.True(error is InvalidOperationException or UnauthorizedAccessException);Assert.Empty(Directory.GetFiles(root,"task-*.json"));
    }

    /// <summary>领取按本次精确请求ID复核，设备重置/改路径/改开关都不能沿旧Scope领取；原任务字节保留。</summary>
    [Theory]
    [InlineData("request")][InlineData("device")][InlineData("configuration")][InlineData("parts")]
    public void Runtime_origin_rejects_stale_or_mismatched_claim_proof(string kind)
    {
        var q=Queue();var origin=Setup(q);var id=Guid.NewGuid().ToString("D");q.EnqueueRuntime(Web,id,binding,Snapshot(),origin,Scope());var path=Path.Combine(root,"task-"+id+".json");var bytes=File.ReadAllBytes(path);
        var current=Scope();if(kind=="device")current=current with{DeviceSha256=H('8')};if(kind=="configuration")current=current with{ConfigurationSha256=H('8')};if(kind=="parts")current=current with{Parts=new(true,true),FrontendPathSha256=H('5')};
        Assert.Throws<UnauthorizedAccessException>(()=>Queue().Claim(Agent,binding,new(kind=="request"?Guid.NewGuid().ToString("D"):id,current)));Assert.Equal(bytes,File.ReadAllBytes(path));
    }

    /// <summary>分端任务不进入旧按动作聚合的确认目录；无论同来源或混合v1，均不能合并成扩大范围的组。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Runtime_origin_is_excluded_from_v1_approval_merging(bool anotherScoped)
    {
        var q=Queue();var origin=Setup(q);var snapshot=Snapshot();var id=Guid.NewGuid().ToString("D");q.EnqueueRuntime(Web,id,binding,snapshot,origin,Scope());
        Assert.Empty(q.Approvals(Agent,new(binding,snapshot.MetadataSha256)));var other=Guid.NewGuid().ToString("D");
        if(anotherScoped){var duplicate=q.EnqueueRuntime(Web,other,binding,snapshot,origin,Scope());Assert.Equal(id,duplicate.RequestId);Assert.Single(Directory.GetFiles(root,"task-*.json"));}
        else q.Enqueue(Web,other,binding,LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime"));
        var approvals=q.Approvals(Agent,new(binding,snapshot.MetadataSha256));if(anotherScoped)Assert.Empty(approvals);else Assert.Equal(other,Assert.Single(approvals).RequestId);
        var request=Authorization(id,snapshot) with{RequestIds=[id,other]};Assert.Throws<InvalidOperationException>(()=>q.PendingAuthorizeOrigin(Agent,request));if(anotherScoped)Assert.Throws<InvalidOperationException>(()=>q.Authorize(Agent,request,snapshot.MetadataSha256,new(id,Scope())));else Assert.Throws<UnauthorizedAccessException>(()=>q.Authorize(Agent,request,snapshot.MetadataSha256,new(id,Scope())));Assert.Empty(Directory.GetFiles(root,"group-*.json"));
    }

    /// <summary>每次批准及ticket重验都要求本次当前Scope；重复短ticket回读仍复核来源，不沿用签发时的旧证明。</summary>
    [Fact]
    public void Runtime_origin_is_bound_into_ticket_and_rechecked_on_every_authorization()
    {
        var q=Queue();var origin=Setup(q);var snapshot=Snapshot();var id=Guid.NewGuid().ToString("D");q.EnqueueRuntime(Web,id,binding,snapshot,origin,Scope());var request=Authorization(id,snapshot);
        Assert.Equal(new(id,origin),q.PendingAuthorizeOrigin(Agent,request));Assert.Throws<UnauthorizedAccessException>(()=>q.Authorize(Agent,request,snapshot.MetadataSha256));
        var ticket=q.Authorize(Agent,request,snapshot.MetadataSha256,new(id,Scope()));Assert.Equal(origin,ticket.RuntimeOrigin);SameTicket(ticket,Queue().Authorize(Agent,request,snapshot.MetadataSha256,new(id,Scope())));
        Assert.Throws<UnauthorizedAccessException>(()=>Queue().Authorize(Agent,request,snapshot.MetadataSha256,new(id,Scope() with{DeviceSha256=H('7')})));
        var check=new AssistantTicketRequest(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256);Assert.Equal(new(id,origin),Queue().PendingTicketOrigin(Agent,check));
        Assert.Throws<UnauthorizedAccessException>(()=>Queue().VerifyTicket(Agent,check,snapshot.MetadataSha256));SameTicket(ticket,Queue().VerifyTicket(Agent,check,snapshot.MetadataSha256,new(id,Scope())));
        Assert.Throws<UnauthorizedAccessException>(()=>Queue().VerifyTicket(Agent,check,snapshot.MetadataSha256,new(id,Scope() with{ConfigurationSha256=H('7')})));
    }

    /// <summary>换绑后旧ticket不能再应用，但真实旧结果仍可按原ticket回传留证，不把历史记录当当前授权。</summary>
    [Theory]
    [InlineData("scope")][InlineData("binding")]
    public void Runtime_origin_rebinding_blocks_old_application_without_erasing_old_receipt(string kind)
    {
        var q=Queue();var origin=Setup(q);var snapshot=Snapshot();var id=Guid.NewGuid().ToString("D");q.EnqueueRuntime(Web,id,binding,snapshot,origin,Scope());var ticket=q.Authorize(Agent,Authorization(id,snapshot),snapshot.MetadataSha256,new(id,Scope()));
        var next=kind=="scope"?Scope() with{ConfigurationSha256=H('7'),BackendPathSha256=H('8')}:Scope();var nextBinding=binding;
        if(kind=="binding"){nextBinding=Guid.NewGuid().ToString("D");q.Bind(Agent,new(nextBinding,"Example.OtherBinding",H('9'),"apply",["sync-runtime"]));}
        q.AssociateDevice(Agent,new(nextBinding,"11",next.BackendPathSha256!,H('5'),2,next.DeviceSha256,next.ConfigurationSha256,origin.AssociationSha256),next);
        Assert.Throws<UnauthorizedAccessException>(()=>Queue().VerifyTicket(Agent,new(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256),snapshot.MetadataSha256,new(id,next)));
        var receipt=new AssistantApplicationReceipt(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256,"applied",1,0,2);Assert.Equal(receipt,Queue().ReportApplication(Agent,receipt));Assert.True(Queue().Get(Web,id).Applied);
    }

    /// <summary>改写持久版本、移除来源或扩大端范围后，即使重算外层校验和，也不能重新领取。</summary>
    [Theory]
    [InlineData("version")][InlineData("remove")][InlineData("parts")]
    public void Runtime_origin_structural_scope_survives_rehashed_file_tampering(string kind)
    {
        var q=Queue();var origin=Setup(q);var id=Guid.NewGuid().ToString("D");q.EnqueueRuntime(Web,id,binding,Snapshot(),origin,Scope());var path=Path.Combine(root,"task-"+id+".json");var json=JsonNode.Parse(File.ReadAllText(path))!;
        if(kind=="version")json["schemaVersion"]=1;if(kind=="remove"){json.AsObject().Remove("runtimeOrigin");json["schemaVersion"]=1;}if(kind=="parts")json["runtimeOrigin"]!["device"]!["parts"]!["frontend"]=true;
        json["integritySha256"]="";json["integritySha256"]=Hash(json);File.WriteAllText(path,json.ToJsonString(Json)+"\n");Assert.Throws<InvalidOperationException>(()=>Queue().PendingClaimOrigin(Agent,binding));
    }

    /// <summary>原v1工作及ticket封套仍无新增null字段；独立旧ticket字段顺序的摘要保持，旧组可直接重启回读。</summary>
    [Fact]
    public void Runtime_origin_absent_keeps_v1_ticket_hash_and_work_shape()
    {
        var q=Queue();_=Setup(q);var snapshot=LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime");var id=Guid.NewGuid().ToString("D");q.Enqueue(Web,id,binding,snapshot);
        var work=q.Claim(Agent,binding)!;Assert.Null(work.RuntimeOrigin);Assert.DoesNotContain("runtimeOrigin",JsonSerializer.Serialize(work,Json),StringComparison.Ordinal);
        var ticket=q.Authorize(Agent,Authorization(id,snapshot),snapshot.MetadataSha256);Assert.Null(ticket.RuntimeOrigin);
        var old=new{ticket.GroupId,ticket.BindingId,ticket.MetadataSha256,ticket.Approvals,ticket.Actions,ticket.PlanSha256,ticket.IssuedAt,ticket.ExpiresAt,TicketSha256=""};Assert.Equal(Hash(old),ticket.TicketSha256);
        Assert.DoesNotContain("runtimeOrigin",File.ReadAllText(Path.Combine(root,"task-"+id+".json")),StringComparison.Ordinal);Assert.DoesNotContain("runtimeOrigin",File.ReadAllText(Path.Combine(root,"group-"+ticket.GroupId+".json")),StringComparison.Ordinal);
        SameTicket(ticket,Queue().VerifyTicket(Agent,new(binding,ticket.GroupId,ticket.TicketSha256,ticket.PlanSha256),snapshot.MetadataSha256));
    }

    /// <summary>record包含数组，重启后数组实例不同；独立比较完整协议字节，不按引用相等放宽任何字段。</summary>
    private static void SameTicket(AssistantApplyTicket expected,AssistantApplyTicket actual)=>Assert.Equal(JsonSerializer.Serialize(expected,Json),JsonSerializer.Serialize(actual,Json));
    /// <summary>客户端不能向原HTTP模型注入设备来源或当前配置证明，只有实际服务器读取器能提供proof。</summary>
    [Theory]
    [InlineData("enqueue")][InlineData("claim")][InlineData("authorize")][InlineData("ticket")]
    public void Runtime_origin_current_proof_is_not_a_client_request_field(string kind)
    {
        using var document=JsonDocument.Parse("{\"bindingId\":\""+binding+"\",\"proof\":{\"requestId\":\""+Guid.NewGuid().ToString("D")+"\"}}");
        var body=document.RootElement;
        Assert.Throws<JsonException>(()=>{switch(kind){case "enqueue":_=GenerationAssistantQueue.Parse<AssistantEnqueueRequest>(body);break;case "claim":_=GenerationAssistantQueue.Parse<AssistantClaimRequest>(body);break;case "authorize":_=GenerationAssistantQueue.Parse<AssistantAuthorizeRequest>(body);break;default:_=GenerationAssistantQueue.Parse<AssistantTicketRequest>(body);break;}});
    }

    /// <summary>v2查库前不领lease，v1仍直接取得原工作；空队列不构造假来源或工作。</summary>
    [Fact]
    public void Runtime_origin_claim_preparation_returns_v1_work_and_holds_v2_lease_until_verified()
    {
        var q=Queue();var origin=Setup(q);var scoped=Guid.NewGuid().ToString("D");var snapshot=Snapshot();q.EnqueueRuntime(Web,scoped,binding,snapshot,origin,Scope());
        var file=Path.Combine(root,"task-"+scoped+".json");var bytes=File.ReadAllBytes(file);var prepared=q.PrepareClaim(Agent,binding);
        Assert.Null(prepared.Work);Assert.Equal(new(scoped,origin),prepared.PendingOrigin);Assert.Equal(bytes,File.ReadAllBytes(file));
        var work=q.Claim(Agent,binding,new(scoped,Scope()))!;q.Report(Agent,new(binding,scoped,work.LeaseId,snapshot.RequestSha256,"failed",null,[],0,0,0,0,"prepare_failed"));
        var normal=Guid.NewGuid().ToString("D");var original=LocalGenerationUpdater.CreateRequest(metadata,"sync-runtime");q.Enqueue(Web,normal,binding,original);
        var legacy=q.PrepareClaim(Agent,binding);Assert.Null(legacy.PendingOrigin);Assert.Equal(normal,legacy.Work!.RequestId);Assert.Equal(legacy.Work.LeaseId,q.PrepareClaim(Agent,binding).Work!.LeaseId);
        q.Report(Agent,new(binding,normal,legacy.Work.LeaseId,original.RequestSha256,"failed",null,[],0,0,0,0,"prepare_failed"));Assert.Equal(new(null,null),q.PrepareClaim(Agent,binding));
    }

    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    /// <summary>独立使用已发布UTF8/LF协议，仅计算合成测试封套摘要，不打印磁盘内容。</summary>
    private static string Hash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,Json)+"\n")));
    /// <summary>元数据只取公开样例，真实连接配置不进入本测试。</summary>
    private static MetadataCatalog Catalog([CallerFilePath]string file="")=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../../backend/metadata/demo.json"))),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>只清理本用例在固定临时前缀下创建的唯一普通目录。</summary>
    public void Dispose()
    {
        var boundary=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"metarill-runtime-origin"))+Path.DirectorySeparatorChar;var full=Path.GetFullPath(root);
        if(!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("测试清理越界。");
        if(Directory.Exists(full)){if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("测试目录不能被替换为链接。");Directory.Delete(full,true);}
    }
}
