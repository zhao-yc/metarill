using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class SampleContractActionPackageTests
{
    /// <summary>只读条件写入元数据后真实完整打包／严格编译，不在编译后补改目录来冒充可生成。</summary>
    [Fact]
    public async Task Actual_full_package_readonly_condition_rejects_web_and_agent_before_business()
    {
        var prepared=new SampleContractActionPackageFixture{ApplicationAssemblyName="Application.Host.ReadOnly."+Guid.NewGuid().ToString("N")};
        prepared.Metadata.ConfigPages.Single(page=>page.Id=="81002").Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("amount");
        for(var i=0;i<prepared.Metadata.ConfigButtons.Count;i++)
        {
            var button=JsonNode.Parse(prepared.Metadata.ConfigButtons[i].GetRawText())!;
            if(button["id"]!.GetValue<string>()!="86000")continue;
            button["visibleScheme"]="VisibleWhenWritable";prepared.Metadata.ConfigButtons[i]=JsonSerializer.SerializeToElement(button);
        }
        try
        {
            await prepared.InitializeAsync();var helper=new SampleContractActionPackageTests(prepared);using var run=prepared.Run();run.Authorization.AllowUpdate=true;
            var definitions=new ServiceCollection();helper.RegisterButtonServices(definitions,run);definitions.AddSingleton<GeneratedCapabilityIndex>();definitions.AddSingleton<AgentCapabilityCatalog>();using var services=definitions.BuildServiceProvider();
            var registered=services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(value=>value.MethodInfo.GetCustomAttribute<AgentActionAttribute>()?.Id=="sample-contract-preview");
            var context=new DefaultHttpContext{RequestServices=services};context.Request.Method="POST";context.Request.ContentType="application/json";context.Request.Path="/"+registered.AttributeRouteInfo!.Template;context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(registered),"原真实动作"));
            var executor=services.GetRequiredService<OriginalBusinessButtonExecutor>();var catalog=services.GetRequiredService<AgentCapabilityCatalog>();var gate=Assert.IsType<ButtonExecutionGuard>(services.GetRequiredService<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>());
            var request=new OriginalBusinessButtonRequest("86000","7",JsonSerializer.SerializeToElement(new{id="7",amount=0}),"Normal","");
            await Assert.ThrowsAsync<BusinessRuleException>(()=>executor.ExecuteAsync(request,context));await Assert.ThrowsAsync<BusinessRuleException>(()=>catalog.ValidateActionInvocationAsync("action:sample-contract-preview",context,Encoding.UTF8.GetBytes("{\"id\":\"7\"}")));
            Assert.Equal(0,gate.Calls);Assert.DoesNotContain("begin",run.Trace);Assert.DoesNotContain("write",run.Trace);Assert.Empty(run.Observer.Contexts);
            run.Set("Amount",0m);await catalog.ValidateActionInvocationAsync("action:sample-contract-preview",context,Encoding.UTF8.GetBytes("{\"id\":\"7\"}"));
            var result=Assert.IsType<ObjectResult>(await executor.ExecuteAsync(request with{Entity=JsonSerializer.SerializeToElement(new{id="7",amount=99})},context));Assert.Null(result.StatusCode);Assert.Equal(1,gate.Calls);Assert.Equal(1,gate.Releases);Assert.DoesNotContain("begin",run.Trace);Assert.True(prepared.HandwrittenProtected);
            // 普通记录写通过真实生成控制器标记绑定相同Service，客户端amount回显不能替代原记录。
            var update=services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(value=>value.ControllerTypeInfo.Namespace?.Contains(".Generated.Controllers",StringComparison.Ordinal)==true&&value.AttributeRouteInfo?.Template?.EndsWith("/sample_contract/81002/update",StringComparison.Ordinal)==true);
            var updateContext=new DefaultHttpContext{RequestServices=services};updateContext.Request.Method="POST";updateContext.Request.ContentType="application/json";updateContext.Request.Path="/"+update.AttributeRouteInfo!.Template;updateContext.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(update),"原生成更新"));
            Assert.Equal("update",catalog.Describe("page:81002:update").Operation);
            var taskGateCalls=gate.Calls;run.Set("Amount",10m);
            var denied=await Assert.ThrowsAsync<AgentTaskException>(()=>catalog.ValidateRecordWriteInvocationAsync("page:81002:update",updateContext,Encoding.UTF8.GetBytes("{\"id\":\"7\",\"amount\":0,\"sampleCustomerName\":\"原回显\"}")));
            Assert.Equal("not_applied",denied.Outcome);Assert.Equal("YZP_CAPABILITY_DENIED",denied.Code);Assert.Equal(taskGateCalls,gate.Calls);Assert.DoesNotContain("begin",run.Trace);
            run.Set("Amount",0m);await catalog.ValidateRecordWriteInvocationAsync("page:81002:update",updateContext,Encoding.UTF8.GetBytes("{\"id\":\"7\",\"amount\":99,\"sampleCustomerName\":\"原回显\"}"));
            Assert.Equal(taskGateCalls,gate.Calls);Assert.DoesNotContain("write",run.Trace);Assert.Empty(run.Observer.Contexts);
            // 使用实际包控制器验证原JSON/R边界；身份与ORM仍是明确离线夹具，不声称真实登录或数据库。
            var builder=WebApplication.CreateBuilder(Array.Empty<string>());builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");helper.RegisterButtonServices(builder.Services,run);
            await using var app=builder.Build();app.Use(async(current,next)=>{try{await next();}catch(BusinessRuleException error){current.Response.StatusCode=200;await current.Response.WriteAsJsonAsync(R<object>.Error(error.Message));}});app.MapControllers();await app.StartAsync();
            try
            {
                using var client=new HttpClient{BaseAddress=new Uri(app.Urls.Single()),Timeout=TimeSpan.FromSeconds(20)};var httpGate=Assert.IsType<ButtonExecutionGuard>(app.Services.GetRequiredService<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>());
                run.Set("Amount",10m);using var blocked=await client.PostAsJsonAsync("/api/admin/sys/config_button/execute",request);Assert.Equal(HttpStatusCode.OK,blocked.StatusCode);using var blockedBody=JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());Assert.Equal(500,blockedBody.RootElement.GetProperty("code").GetInt32());Assert.Contains("只读",blockedBody.RootElement.GetProperty("msg").GetString());Assert.Equal(0,httpGate.Calls);
                run.Set("Amount",0m);using var allowed=await client.PostAsJsonAsync("/api/admin/sys/config_button/execute",request);Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);using var allowedBody=JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());Assert.Equal(200,allowedBody.RootElement.GetProperty("code").GetInt32());Assert.Equal(1,httpGate.Calls);Assert.Equal(1,httpGate.Releases);Assert.DoesNotContain("begin",run.Trace);Assert.Empty(run.Observer.Contexts);
            }
            finally{await app.StopAsync();}

        }
        finally{await prepared.DisposeAsync();}
    }
}
