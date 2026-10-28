using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace {{NAMESPACE}}.Host;

/// <summary>Agent模块从同一已核目录取得资源；不修改Web基础身份或创建平台权限。</summary>
internal sealed class ApplicationAgentHostResources(VerifiedPackageSecurityCatalog catalog,ApplicationSecurityRegistry resources,IServiceProvider services):IAgentHostResources,ISecuritySessionPolicy
{
    public AgentHostCatalog AgentCatalog {get;}=AgentHostCatalog.ForApplication(catalog.Clone());
    public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>resources.PublishedCatalogs;
    /// <summary>普通应用永远不产生平台身份。</summary>
    public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
    /// <summary>认证与锁后复核使用同一固定签发域；客户端选择不能扩充后台。</summary>
    public bool IsAgentRealm(string projectId,string adminTypeId)=>AgentCatalog.OwnsIssuerRealm(projectId,adminTypeId);
    /// <summary>匹配字段与Web使用同一冻结目录，不读取客户端或外部元数据。</summary>
    public string? ExactDataManageManagerField(string projectId,string adminTypeId)=>resources.ExactDataManageManagerField(projectId,adminTypeId);
    /// <summary>只有认证遇到实际Agent会话才解析数据库证明，匿名请求不创建连接。</summary>
    public Task<bool> ValidateAgentSessionAsync(SecuritySessionBinding binding,CancellationToken cancellationToken)=>
        services.GetRequiredService<AgentSecuritySessionPolicy>().ValidateAgentSessionAsync(binding,cancellationToken);
}

/// <summary>完整包显式装配共享Agent；Web基础模板单独使用时继续默认拒绝Agent。</summary>
internal static class ApplicationAgentIntegration
{
    /// <summary>在Web协调注册后替换两类会话策略，目录与账本都只使用应用同库。</summary>
    internal static void Add(IServiceCollection services,IConfiguration configuration,ApplicationAuthenticationMode mode)
    {
        services.AddSingleton<ApplicationAgentHostResources>();
        services.AddSingleton<IAgentHostResources>(provider=>provider.GetRequiredService<ApplicationAgentHostResources>());
        services.Replace(ServiceDescriptor.Singleton<ISecurityResourceRegistry>(provider=>provider.GetRequiredService<ApplicationAgentHostResources>()));
        services.Replace(ServiceDescriptor.Singleton<ISecuritySessionPolicy>(provider=>provider.GetRequiredService<ApplicationAgentHostResources>()));
        services.AddSingleton<AgentSecuritySessionPolicy>();
        if(!mode.Development)services.Replace(ServiceDescriptor.Scoped<ISecurityCurrentSessionPolicy,AgentSecurityCurrentSessionPolicy>());
        // 网址只来自部署者可信配置，不从请求Host、页面或客户端回调推导。
        services.AddSingleton(configuration.GetSection("Agent").Get<AgentOAuthOptions>()??new AgentOAuthOptions{VerificationUri=""});
        services.AddSingleton<AgentOAuthService>();services.AddScoped<AgentAuthenticationService>();services.AddSingleton<AgentPublicRateLimiter>();
        services.AddSingleton<GeneratedCapabilityIndex>();services.AddScoped<AgentCapabilityCatalog>();
        services.AddSingleton(configuration.GetSection("Agent:Tasks").Get<AgentTaskOptions>()??new AgentTaskOptions());services.AddSingleton<AgentTaskStore>();
    }
}
