using Metarill.Core.Persistence;
using Metarill.Runtime.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Metarill.Runtime.Security;

/// <summary>平台安全写协调与旧配置准备共享固定注册顺序，测试和实际宿主消费同一入口。</summary>
public static class RuntimeSecurityServices
{
    /// <summary>安全表先取得完整项目/后台聚合锁，旧配置守卫之后才执行其余配置逻辑。</summary>
    public static IServiceCollection AddRealmWriteCoordination(this IServiceCollection services)
    {
        services.AddScoped<ISecurityIdentityAccessor>(provider=>provider.GetRequiredService<HttpRuntimeAuthorization>());
        services.AddScoped<PlatformSecurityCurrentResources>();
        services.AddScoped<ISecurityCurrentResources>(provider=>provider.GetRequiredService<PlatformSecurityCurrentResources>());
        services.AddScoped<IRolePermissionCatalog>(provider=>provider.GetRequiredService<PlatformSecurityCurrentResources>());
        services.AddScoped<ISecurityCurrentSessionPolicy,PlatformSecurityCurrentSessionPolicy>();
        services.AddScoped<SecurityRealmCurrentReader>(provider=>new(provider.GetRequiredService<Metarill.Runtime.Persistence.SqlRowStore>(),
            provider.GetRequiredService<MetadataRegistry>(),provider.GetRequiredService<ISecurityCurrentResources>(),provider.GetRequiredService<ISecurityCurrentSessionPolicy>()));
        services.AddScoped<SecurityRealmMutationGuard>(provider=>new(provider.GetRequiredService<Metarill.Runtime.Persistence.SqlRowStore>(),
            provider.GetRequiredService<MetadataRegistry>(),provider.GetRequiredService<ISecurityIdentityAccessor>(),provider.GetRequiredService<SecurityRealmCurrentReader>()));
        services.AddScoped<IRecordMutationGuard>(provider=>provider.GetRequiredService<SecurityRealmMutationGuard>());
        services.AddScoped<ConfigurationMutationGuard>();
        services.AddScoped<IRecordMutationGuard>(provider=>provider.GetRequiredService<ConfigurationMutationGuard>());
        return services;
    }
}
