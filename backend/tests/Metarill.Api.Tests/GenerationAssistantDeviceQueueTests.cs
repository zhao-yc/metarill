using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Api.GenerationAssistant;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>真实队列文件验证设备关联；Scope是明确合成的服务端事实，不冒充数据库归属或真实grant。</summary>
public sealed class GenerationAssistantDeviceQueueTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"metarill-device-queue",Guid.NewGuid().ToString("N"));
    private readonly AssistantOwner owner=new(1,"1",9);
    private readonly string binding=Guid.NewGuid().ToString("D");
    private static string H(char value)=>new(value,64);
    private GenerationAssistantQueue Queue()=>new(new(root));
    private AssistantDeviceScope Scope()=>new(1,"11","1","21",H('2'),H('3'),new(true,true),H('4'),H('5'));
    private AssistantDeviceBindRequest Request()=>new(binding,"11",H('4'),H('5'),2,H('2'),H('3'));
    /// <summary>绑定默认只允许本测试sync-runtime，其他模式用各自用例明确覆盖。</summary>
    private void Bind(GenerationAssistantQueue queue,string? id=null,string mode="apply",string[]? actions=null)=>queue.Bind(owner,new(id??binding,"Example.Device",H('1'),mode,actions??["sync-runtime"]));
    /// <summary>重启回读及同内容重复保持原状态字节；核心v1绑定也完全不改。</summary>
    [Fact]
    public void Device_association_is_idempotent_durable_and_does_not_rewrite_core_binding()
    {
        var queue=Queue();Bind(queue);var core=Path.Combine(root,"binding-"+binding+".json");var original=File.ReadAllBytes(core);
        Assert.Null(queue.FindDeviceBinding(new(1,"1"),Scope()));var first=queue.AssociateDevice(owner,Request(),Scope());
        var file=Path.Combine(root,"device-1-11-1.json");var bytes=File.ReadAllBytes(file);
        Assert.Equal(first,Queue().AssociateDevice(owner,Request(),Scope()));Assert.Equal(bytes,File.ReadAllBytes(file));
        Assert.Equal(first,Queue().DeviceBinding(new(1,"1"),Scope()));Assert.Equal(original,File.ReadAllBytes(core));
        var view=JsonSerializer.Serialize(first);Assert.DoesNotContain("localBinding",view,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("token",view,StringComparison.OrdinalIgnoreCase);Assert.True(first.MatchesCurrentConfiguration);
    }
    /// <summary>新目录或设备重置后旧关联失效，但拥有者仍可取得旧ETag以明确重新绑定。</summary>
    [Theory]
    [InlineData("device")][InlineData("configuration")][InlineData("parts")]
    public void Changed_configuration_returns_stale_etag_without_authorizing_old_association(string kind)
    {
        var queue=Queue();Bind(queue);var first=queue.AssociateDevice(owner,Request(),Scope());
        var current=kind switch{"device"=>Scope() with{DeviceSha256=H('6')},"configuration"=>Scope() with{ConfigurationSha256=H('6'),BackendPathSha256=H('7')},_=>Scope() with{Parts=new(true,false),FrontendPathSha256=null}};
        Assert.Throws<UnauthorizedAccessException>(()=>queue.DeviceBinding(new(1,"1"),current));var stale=queue.FindDeviceBinding(new(1,"1"),current)!;
        Assert.False(stale.MatchesCurrentConfiguration);Assert.Equal(first.AssociationSha256,stale.AssociationSha256);
        var request=Request() with{BackendPathSha256=current.BackendPathSha256!,ExpectedDeviceSha256=current.DeviceSha256,ExpectedConfigurationSha256=current.ConfigurationSha256,ExpectedAssociationSha256=stale.AssociationSha256};
        var bound=queue.AssociateDevice(owner,request,current);Assert.True(bound.MatchesCurrentConfiguration);Assert.NotEqual(first.AssociationSha256,bound.AssociationSha256);Assert.Equal(bound,Queue().DeviceBinding(new(1,"1"),current));
    }
    /// <summary>切换核心绑定必须明确旧摘要，不由新的有效grant或单个同项目绑定自动接管。</summary>
    [Fact]
    public void Replacing_binding_requires_exact_old_association_hash()
    {
        var queue=Queue();Bind(queue);var first=queue.AssociateDevice(owner,Request(),Scope());var id=Guid.NewGuid().ToString("D");Bind(queue,id);
        var replacement=Request() with{BindingId=id};Assert.Throws<InvalidOperationException>(()=>queue.AssociateDevice(owner,replacement,Scope()));
        Assert.Throws<InvalidOperationException>(()=>queue.AssociateDevice(owner,replacement with{ExpectedAssociationSha256=H('9')},Scope()));
        Assert.Equal(first,queue.DeviceBinding(new(1,"1"),Scope()));var changed=queue.AssociateDevice(owner,replacement with{ExpectedAssociationSha256=first.AssociationSha256},Scope());Assert.Equal(id,changed.BindingId);
    }
    /// <summary>拒绝跨账号/项目/grant、错误设备/路径/协议及只预览绑定，原文件保持。</summary>
    [Theory]
    [InlineData("account")][InlineData("project")][InlineData("grant")][InlineData("device")][InlineData("backend")][InlineData("frontend")][InlineData("protocol")][InlineData("web")][InlineData("preview")][InlineData("no-runtime")][InlineData("stale-device")][InlineData("stale-config")]
    public void Device_association_rejects_unauthorized_or_mismatched_registration(string kind)
    {
        var queue=Queue();Bind(queue);var first=queue.AssociateDevice(owner,Request(),Scope());var bytes=File.ReadAllBytes(Path.Combine(root,"device-1-11-1.json"));var actor=owner;var request=Request();
        if(kind=="account")actor=owner with{AccountId=2};if(kind=="project")actor=owner with{ProjectId="2"};if(kind=="grant")actor=owner with{GrantId=10};if(kind=="web")actor=owner with{GrantId=null};
        if(kind=="stale-device")request=request with{ExpectedDeviceSha256=H('7')};if(kind=="stale-config")request=request with{ExpectedConfigurationSha256=H('8')};
        if(kind=="device")request=request with{DeviceId="12"};if(kind=="backend")request=request with{BackendPathSha256=H('6')};if(kind=="frontend")request=request with{FrontendPathSha256=H('6')};if(kind=="protocol")request=request with{RuntimePartsProtocol=1};
        if(kind is "preview" or "no-runtime"){var id=Guid.NewGuid().ToString("D");Bind(queue,id,kind=="preview"?"preview":"apply",kind=="preview"?[]:["generate-project-config"]);request=request with{BindingId=id};}
        var error=Record.Exception(()=>queue.AssociateDevice(actor,request,Scope()));Assert.True(error is UnauthorizedAccessException or InvalidOperationException);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(root,"device-1-11-1.json")));Assert.Equal(first,queue.DeviceBinding(new(1,"1"),Scope()));
    }
    /// <summary>即使状态文件被改，缺失/摘要损坏也不自动重建；未知请求字段不能上传目录或假Scope。</summary>
    [Theory]
    [InlineData("digest")][InlineData("unknown-body")][InlineData("duplicate-body")][InlineData("id")][InlineData("empty-parts")]
    public void Device_association_rejects_corruption_and_unsafe_shapes(string kind)
    {
        var queue=Queue();Bind(queue);queue.AssociateDevice(owner,Request(),Scope());
        if(kind=="digest"){var file=Path.Combine(root,"device-1-11-1.json");var node=JsonNode.Parse(File.ReadAllText(file))!;node["scope"]!["configurationSha256"]=H('8');File.WriteAllText(file,node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>queue.DeviceBinding(new(1,"1"),Scope()));}
        else if(kind is "unknown-body" or "duplicate-body")
        {var text=kind=="unknown-body"?"{\"deviceId\":\"11\",\"currentScope\":{},\"target\":\"E:/private\"}":"{\"deviceId\":\"11\",\"deviceId\":\"12\"}";using var body=JsonDocument.Parse(text);Assert.ThrowsAny<Exception>(()=>GenerationAssistantQueue.Parse<AssistantDeviceLookupRequest>(body.RootElement));}
        else Assert.Throws<InvalidOperationException>(()=>queue.FindDeviceBinding(new(1,"1"),kind=="id"?Scope() with{DeviceId="../11"}:Scope() with{Parts=new(false,false)}));
    }
    /// <summary>设备所有权变更后不同账号拥有独立关联槽，不读取或接管旧账号核心绑定。</summary>
    [Fact]
    public void Device_association_slots_are_separate_per_account()
    {
        var queue=Queue();Bind(queue);var old=queue.AssociateDevice(owner,Request(),Scope());
        var nextOwner=new AssistantOwner(2,"1",10);var nextScope=Scope() with{AccountId=2,DeviceSha256=H('7'),ConfigurationSha256=H('8')};var nextId=Guid.NewGuid().ToString("D");
        queue.Bind(nextOwner,new(nextId,"Example.NextOwner",H('9'),"apply",["sync-runtime"]));Assert.Null(queue.FindDeviceBinding(nextOwner,nextScope));
        var next=queue.AssociateDevice(nextOwner,Request() with{BindingId=nextId,ExpectedDeviceSha256=nextScope.DeviceSha256,ExpectedConfigurationSha256=nextScope.ConfigurationSha256},nextScope);Assert.Equal(nextId,next.BindingId);Assert.Equal(old,queue.DeviceBinding(new(1,"1"),Scope()));Assert.Equal(2,Directory.GetFiles(root,"device-*.json").Length);
    }

    /// <summary>仅清理本测试在固定临时前缀下创建的唯一普通目录。</summary>
    public void Dispose()
    {
        var boundary=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"metarill-device-queue"))+Path.DirectorySeparatorChar;var full=Path.GetFullPath(root);
        if(!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("测试清理越界。");
        if(Directory.Exists(full)){if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("测试目录不能被替换为链接。");Directory.Delete(full,true);}
    }
}
