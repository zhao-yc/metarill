using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Example.McpFixture.Generated.DTOs;
using Metarill.Core.Security;
using Metarill.Runtime.Mcp;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Metarill.Mcp.Tests;

/// <summary>真实SDK Streamable HTTP与工具装配测试；身份/Service仅为离线合同夹具，不证明正式Web/OAuth或业务库。</summary>
public sealed class McpTransportTests
{
    /// <summary>Schema不接受服务/身份参数，真实工具调用保留DTO、ID及逐次授权，匿名在工具协议之前拒绝。</summary>
    [Fact]
    public async Task Real_sdk_transport_uses_service_injection_and_the_same_safe_dispatch()
    {
        using var f=new PublishedPageInvokerTests.Fixture();var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.Logging.ClearProviders();builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(f.Invoker);builder.Services.AddSingleton(f.Service);builder.Services.AddSingleton<IRuntimeAuthorization>(f.Access);builder.Services.AddSingleton<ISecurityIdentityAccessor>(f.Access);
        builder.Services.AddMcpServer().WithHttpTransport(options=>options.SessionMode=HttpServerSessionMode.Stateless).WithTools<GeneratedPageMcpTools>();
        await using var app=builder.Build();
        // 本测试只核协议及分派；这不是产品认证实现，也不读取或写入任何数据库。
        app.Use(async(context,next)=>{if(context.Request.Headers["loginStateId"]!="unit-mcp-session"){context.Response.StatusCode=401;return;}await next(context);});app.MapMcp("/api/mcp");await app.StartAsync();
        try
        {
            using var anonymous=app.GetTestClient();using(var denied=await anonymous.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=1,method="tools/list",@params=new{}}))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
            using var http=app.GetTestClient();http.DefaultRequestHeaders.Add("loginStateId","unit-mcp-session");
            await using var transport=new HttpClientTransport(new(){Endpoint=new Uri(http.BaseAddress!,"api/mcp"),TransportMode=HttpTransportMode.StreamableHttp},http,ownsHttpClient:false);await using var client=await McpClient.CreateAsync(transport);
            var tools=await client.ListToolsAsync();Assert.Equal(new[]{"call_generated_page","describe_generated_page","list_generated_pages"},tools.Select(tool=>tool.Name).Order(StringComparer.Ordinal));
            var call=Assert.Single(tools,tool=>tool.Name=="call_generated_page");Assert.Equal(new[]{"confirmed","data","operation","pageId","parentId"},call.JsonSchema.GetProperty("properties").EnumerateObject().Select(property=>property.Name).Order(StringComparer.Ordinal));
            var describe=Assert.Single(tools,tool=>tool.Name=="describe_generated_page");Assert.Equal(new[]{"operation","pageId"},describe.JsonSchema.GetProperty("properties").EnumerateObject().Select(property=>property.Name).Order(StringComparer.Ordinal));
            var described=await client.CallToolAsync("describe_generated_page",new Dictionary<string,object?>{["pageId"]=PublishedPageInvokerTests.Page,["operation"]="update"});Assert.NotEqual(true,described.IsError);Assert.False(Text(described).GetProperty("dataSchema").GetProperty("additionalProperties").GetBoolean());Assert.Empty(f.Service.Calls);
            var listed=await client.CallToolAsync("list_generated_pages",new Dictionary<string,object?>());Assert.NotEqual(true,listed.IsError);Assert.Equal(PublishedPageInvokerTests.Page,Text(listed)[0].GetProperty("id").GetString());
            Dictionary<string,object?> Arguments(string operation,bool confirmed)=>new(){["pageId"]=PublishedPageInvokerTests.Page,["operation"]=operation,["data"]=new{name="SDK测试"},["confirmed"]=confirmed,["parentId"]="original-parent"};
            var unconfirmed=await client.CallToolAsync("call_generated_page",Arguments("create",false));Assert.True(unconfirmed.IsError);Assert.Empty(f.Service.Calls);
            var created=await client.CallToolAsync("call_generated_page",Arguments("create",true));Assert.NotEqual(true,created.IsError);Assert.Equal("9007199254740993",Text(created).GetProperty("id").GetString());Assert.False(Assert.IsType<DemoCustomerPage2200000000000100020CreateRequest>(f.Service.Request).Notes.IsSpecified);Assert.Equal("original-parent",f.Service.Parent);
            f.Access.Identity=f.Access.Identity with{GrantId=70};var before=f.Service.Calls.Count;var agentWrite=await client.CallToolAsync("call_generated_page",Arguments("create",true));Assert.True(agentWrite.IsError);Assert.Equal(before,f.Service.Calls.Count);
            var agentRead=await client.CallToolAsync("call_generated_page",new Dictionary<string,object?>{["pageId"]=PublishedPageInvokerTests.Page,["operation"]="list",["data"]=new{},["confirmed"]=false});Assert.NotEqual(true,agentRead.IsError);Assert.Equal("9007199254740993",Text(agentRead).GetProperty("records")[0].GetProperty("id").GetString());
            var unavailableSchema=await client.CallToolAsync("describe_generated_page",new Dictionary<string,object?>{["pageId"]=PublishedPageInvokerTests.Page,["operation"]="create"});Assert.True(unavailableSchema.IsError);
            f.Access.Denied.Add("list");before=f.Service.Calls.Count;var revoked=await client.CallToolAsync("call_generated_page",new Dictionary<string,object?>{["pageId"]=PublishedPageInvokerTests.Page,["operation"]="list",["data"]=new{},["confirmed"]=false});Assert.True(revoked.IsError);Assert.Equal(before,f.Service.Calls.Count);
            var revokedSchema=await client.CallToolAsync("describe_generated_page",new Dictionary<string,object?>{["pageId"]=PublishedPageInvokerTests.Page,["operation"]="list"});Assert.True(revokedSchema.IsError);Assert.Equal(before,f.Service.Calls.Count);
        }
        finally{await app.StopAsync();}
    }
    /// <summary>检查MCP真实TextContent JSON，不能只在工具对象的中间结果上断言。</summary>
    private static JsonElement Text(CallToolResult result)=>JsonSerializer.Deserialize<JsonElement>(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
}
