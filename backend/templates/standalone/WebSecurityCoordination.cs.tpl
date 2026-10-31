using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>正式Web只使用当前持久身份及固定目录协调；不注册平台配置守卫或新的HTTP入口。</summary>
internal static class ApplicationWebSecurityCoordination
{
    /// <summary>身份与授权别名共用同一个Scoped实例；开发内存身份不能进入持久安全写入协调。</summary>
    internal static void Add(IServiceCollection services,ApplicationAuthenticationMode mode)
    {
        ArgumentNullException.ThrowIfNull(services);ArgumentNullException.ThrowIfNull(mode);
        if(mode.Development)throw new InvalidOperationException("开发内存认证不能注册正式Web安全协调。");
        services.AddScoped<ApplicationWebAuthorization>();
        services.AddScoped<IRuntimeAuthorization>(provider=>provider.GetRequiredService<ApplicationWebAuthorization>());
        services.AddScoped<ISecurityIdentityAccessor>(provider=>provider.GetRequiredService<ApplicationWebAuthorization>());
        services.AddScoped<ISecurityCurrentSessionPolicy,ApplicationSecurityCurrentSessionPolicy>();
        services.AddScoped<SecurityRealmCurrentReader>();
        services.AddScoped<SecurityRealmMutationGuard>();
        services.AddScoped<IRecordMutationGuard>(provider=>provider.GetRequiredService<SecurityRealmMutationGuard>());
        services.AddScoped<RolePermissionBusiness>();
    }
}
