using System.Reflection;
using Metarill.Api.Controllers;
using Metarill.Core.Security;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>真实MVC类型和方法的专用通道登记；合成身份仅验中间件分流，不代替真实OAuth。</summary>
public sealed class AgentDeviceControlGateTests
{
    /// <summary>只允许批准的三方法到控制器，不能提前解析普通能力目录或创建业务账本。</summary>
    [Theory][InlineData("Bind")][InlineData("Lookup")][InlineData("Configuration")]
    public async Task Actual_device_actions_reach_controller_without_business_catalog(string name)
    {
        using var services=new ServiceCollection().BuildServiceProvider();var context=Context(services,typeof(GenerationAssistantDeviceController),name);var calls=0;
        await new AgentTaskMiddleware(_=>{calls++;return Task.CompletedTask;}).InvokeAsync(context);Assert.Equal(1,calls);
    }
    /// <summary>同URL、未知动作、大小写变化、相似类型和没有MVC描述符均不能获得专用通道。</summary>
    [Theory][InlineData("unknown")][InlineData("case")][InlineData("otherController")][InlineData("noDescriptor")]
    public async Task Anything_outside_the_exact_type_and_action_stays_in_business_gate(string invalid)
    {
        using var services=new ServiceCollection().BuildServiceProvider();var context=Context(services,invalid=="otherController"?typeof(SimilarDeviceController):typeof(GenerationAssistantDeviceController),"Bind");
        if(invalid=="noDescriptor")context.SetEndpoint(null);
        else if(invalid is "unknown" or "case")context.GetEndpoint()!.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName=invalid=="unknown"?"BindOther":"bind";
        var calls=0;var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new AgentTaskMiddleware(_=>{calls++;return Task.CompletedTask;}).InvokeAsync(context));
        Assert.Contains(typeof(AgentCapabilityCatalog).FullName!,error.Message);Assert.Equal(0,calls);
    }
    /// <summary>描述符使用实际控制器反射方法；没有数据库或目录服务，误走业务分支立即失败。</summary>
    private static DefaultHttpContext Context(IServiceProvider services,Type controller,string name)
    {
        var context=new DefaultHttpContext{RequestServices=services};context.Request.Method="POST";context.Request.Path="/api/admin/sys/generation-assistant/helper/bind-device";
        var identity=new AuthenticatedIdentity(new RuntimeActor(1,"合成通道核验",true,"2200000000000100000","1"),new HashSet<string>(),1,9);context.Items[typeof(AgentRequestIdentity)]=new AgentRequestIdentity(identity,"agent",9);
        var method=controller.GetMethod(name)!;context.SetEndpoint(new Endpoint(null,new EndpointMetadataCollection(new ControllerActionDescriptor{ControllerTypeInfo=controller.GetTypeInfo(),MethodInfo=method,ActionName=name}),"实际动作核验"));return context;
    }
    /// <summary>相同方法名的其他实际类型不能被登记；并非生产控制器。</summary>
    private sealed class SimilarDeviceController:ControllerBase
    {
        /// <summary>仅供负例反射，实际不会被调用。</summary>
        public void Bind(){}
    }
}
