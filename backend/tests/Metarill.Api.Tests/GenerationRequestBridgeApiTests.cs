using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Security;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>权限和协议拒绝在仓储读取之前完成，不用测试替身宣称真实数据库成功。</summary>
public sealed class GenerationRequestBoundaryTests
{
    /// <summary>匿名、Agent和普通账号不能借首页GET取得开发工程生成请求。</summary>
    [Theory]
    [InlineData("anonymous", true)] [InlineData("agent", true)] [InlineData("web", false)]
    public async Task Non_web_administrator_is_rejected_before_metadata_read(string kind, bool administrator)
    { var controller = Controller(kind, administrator); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Runtime(default)); }

    /// <summary>所有query和GET正文被拒，选择参数不能变为项目、路径或命令授权。</summary>
    [Theory]
    [InlineData("?projectId=999", false)] [InlineData("?target=C:/arbitrary", false)] [InlineData("?namespace=Bad", false)] [InlineData("?action=all", false)] [InlineData("", true)]
    public async Task Unknown_parameters_are_rejected_before_metadata_read(string query, bool body)
    { var controller = Controller(); controller.Request.QueryString = new QueryString(query); if (body) controller.Request.ContentLength = 2; await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ProjectConfiguration(default)); }

    /// <summary>平台显式BackendOnly不能被三个首页动作包装成成功ZIP或错误的完整业务工程。</summary>
    [Fact]
    public async Task Platform_backend_only_is_explicitly_rejected()
    {
        var controller = Controller(project: "1608097412978577409");
        foreach (var call in new Func<Task>[] { () => controller.ProjectConfiguration(default), () => controller.Runtime(default), () => controller.Pages(default) }) { var error = await Assert.ThrowsAsync<InvalidOperationException>(call); Assert.Contains("BackendOnly", error.Message); Assert.Contains("未写入", error.Message); }
    }

    /// <summary>认证上下文明确绑定为测试身份；null仓储确保所有拒绝发生于读取前。</summary>
    private static GenerationRequestController Controller(string kind = "web", bool administrator = true, string project = "2200000000000100000", [CallerFilePath] string file = "")
    {
        var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..")); var registry = new MetadataRegistry(Path.Combine(source, "backend/metadata/platform.json"), Path.Combine(source, "backend/metadata/demo.json"));
        var context = new DefaultHttpContext(); context.Request.Headers["X-Yzp-Agent-Confirm"]="true"; context.Request.Headers["X-Yzp-Agent-Request-Id"]=Guid.NewGuid().ToString("D"); context.Request.Headers["X-Metarill-Assistant-Binding"]=Guid.NewGuid().ToString("D");
        if (kind != "anonymous") { var identity = new AuthenticatedIdentity(new RuntimeActor(1, "测试身份", administrator, project, "1"), new HashSet<string>(), 1, kind == "agent" ? 1 : null); context.Items[typeof(AuthenticatedIdentity)] = identity; context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(identity, kind, identity.GrantId); }
        return new GenerationRequestController(null!, registry, new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = context }, registry), null!, null!) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}

public sealed partial class ApiAcceptanceTests
{
    /// <summary>真实HTTP原三GET必须有绑定/确认/UUID，普通读取不会触发本机队列或结构执行。</summary>
    [MySqlApiFact]
    public async Task Generation_three_gets_require_explicit_confirmation_binding_without_schema_execution()
    {
        var (factory,client)=await Login(); await using var host=factory; using var http=client; var schemas=factory.Services.GetRequiredService<ProjectSchemaService>(); var before=schemas.Resolve(Project).History.Count;
        try
        {
            foreach(var route in new[]{"generate_project_config","sync_runtime","generate_all_page"})
            {
                Assert.Equal(500,(await Body(await client.GetAsync("/api/admin/custom/index/"+route))).GetProperty("code").GetInt32());
                Assert.Equal(500,(await Body(await client.GetAsync("/api/admin/custom/index/"+route+"?projectId=999"))).GetProperty("code").GetInt32());
            }
            Assert.Equal(before,schemas.Resolve(Project).History.Count);
        }
        finally
        {
            // 仅退出本次HTTP客户端的会话，不批量处理测试管理员其他会话。
            Assert.Equal(200,(await Body(await client.PostAsync("/api/admin/sys/yz_manager/exit_login",null))).GetProperty("code").GetInt32());
        }
    }
}
