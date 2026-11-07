using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>实际平台Program必须注册原工具及新布局聚合；匿名仍在协议前拒绝，不依赖替代宿主证明接线。</summary>
public sealed class MetadataWorkbenchMcpRegistrationTests
{
    /// <summary>私有合成配置指向不可达端口，只解析真实DI工具表和匿名响应，不初始化或执行SQL。</summary>
    [Fact]
    public async Task Actual_program_registers_original_layout_tools_without_exposing_services_or_identity_inputs()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-layout-mcp-program",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"settings.json");var previous=Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        var database=new{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1"};
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Metarill=new{Database=database,BusinessDatabase=database}}));Environment.SetEnvironmentVariable("METARILL_SETTINGS",path);
        try
        {
            await using var factory=new WebApplicationFactory<Program>();using var client=factory.CreateClient();
            var tools=factory.Services.GetServices<McpServerTool>().Select(tool=>tool.ProtocolTool).ToArray();
            Assert.Contains(tools,tool=>tool.Name=="get_project_metadata");Assert.Contains(tools,tool=>tool.Name=="call_generated_page");
            var snapshot=Assert.Single(tools,tool=>tool.Name=="getPageDetailLayout");var save=Assert.Single(tools,tool=>tool.Name=="savePageDetailLayout");
            Assert.True(snapshot.Annotations?.ReadOnlyHint);Assert.NotEqual(true,save.Annotations?.ReadOnlyHint);
            Assert.Equal(new[]{"pageId"},snapshot.InputSchema.GetProperty("properties").EnumerateObject().Select(item=>item.Name));
            Assert.Equal(new[]{"request"},save.InputSchema.GetProperty("properties").EnumerateObject().Select(item=>item.Name));
            foreach(var name in new[]{"getPageDetailLayout","savePageDetailLayout"})
            {
                using var result=await client.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=1,method="tools/call",@params=new{name,arguments=new{}}});
                Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
            }
        }
        finally{Environment.SetEnvironmentVariable("METARILL_SETTINGS",previous);File.Delete(path);Directory.Delete(root);}
    }
}
