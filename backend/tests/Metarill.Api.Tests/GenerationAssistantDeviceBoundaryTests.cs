using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Security;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
namespace Metarill.Api.Tests;

public sealed class GenerationAssistantDeviceBoundaryTests
{
    /// <summary>真实Agent/admin边界先于空仓储；合成身份只证明调用顺序，不作为真实OAuth验收。</summary>
    [Theory]
    [InlineData("anonymous",true)][InlineData("web",true)][InlineData("agent",false)]
    public async Task Device_helper_rejects_wrong_identity_before_any_device_lookup(string kind,bool administrator)
    {
        var controller=Controller(kind,administrator);using var body=JsonDocument.Parse("{\"deviceId\":\"11\"}");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>controller.Lookup(body.RootElement,default));
    }
    /// <summary>平台BackendOnly、query和未知/重复正文不能选择他人的项目或传本机目录。</summary>
    [Theory]
    [InlineData("platform")][InlineData("query")][InlineData("unknown")][InlineData("duplicate")]
    public async Task Device_helper_rejects_unpublished_scope_before_lookup(string kind)
    {
        var controller=Controller(project:kind=="platform"?"1608097412978577409":"2200000000000100000");
        if(kind=="query")controller.Request.QueryString=new("?projectId=2");
        var json=kind switch{"unknown"=>"{\"deviceId\":\"11\",\"projectId\":\"2\",\"target\":\"E:/private\"}","duplicate"=>"{\"deviceId\":\"11\",\"deviceId\":\"12\"}",_=>"{\"deviceId\":\"11\"}"};
        using var body=JsonDocument.Parse(json);var error=await Record.ExceptionAsync(()=>controller.Lookup(body.RootElement,default));Assert.True(error is InvalidOperationException or JsonException);
    }
    /// <summary>故意不给真实设备读取器，所有拒绝都必须发生在访问它之前。</summary>
    private static GenerationAssistantDeviceController Controller(string kind="agent",bool administrator=true,string project="2200000000000100000",[CallerFilePath]string file="")
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));var registry=new MetadataRegistry(Path.Combine(root,"backend/metadata/platform.json"),Path.Combine(root,"backend/metadata/demo.json"));var http=new DefaultHttpContext();
        if(kind!="anonymous"){var identity=new AuthenticatedIdentity(new RuntimeActor(1,"合成设备测试",administrator,project,"1"),new HashSet<string>(),1,kind=="agent"?9:null);http.Items[typeof(AuthenticatedIdentity)]=identity;http.Items[typeof(AgentRequestIdentity)]=new AgentRequestIdentity(identity,kind,identity.GrantId);}
        return new(null!,null!,new(new HttpContextAccessor{HttpContext=http},registry),registry){ControllerContext=new ControllerContext{HttpContext=http}};
    }
}
