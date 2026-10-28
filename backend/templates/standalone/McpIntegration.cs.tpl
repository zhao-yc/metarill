using Metarill.Runtime.Mcp;
using ModelContextProtocol.AspNetCore;

namespace {{NAMESPACE}}.Host;

/// <summary>完整应用MCP仅绑定同一已核目录与实际生成程序集；不接平台设计器或外部元数据。</summary>
internal static class ApplicationMcpIntegration
{
    /// <summary>正式Web注册公共工具，显式开发身份没有持久会话证明，不发布MCP工具。</summary>
    internal static void Add(IServiceCollection services,VerifiedPackageSecurityCatalog catalog,ApplicationAuthenticationMode mode)
    {
        if(mode.Development)return;
        services.AddSingleton(new PublishedPageInvoker([new(catalog.Clone(),"{{NAMESPACE}}",typeof(ApplicationMcpIntegration).Assembly)]));
        services.AddMcpServer().WithHttpTransport(options=>options.SessionMode=HttpServerSessionMode.Stateless).WithTools<GeneratedPageMcpTools>();
    }
    /// <summary>端点位于原严格Web/Agent认证管线之后；Agent写工具由公共执行器继续拒绝。</summary>
    internal static void Map(WebApplication app,ApplicationAuthenticationMode mode)
    {
        if(!mode.Development)app.MapMcp("/api/mcp");
    }
}
