using Metarill.GlobalSample.Business;
using Metarill.Runtime.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Metarill.GlobalSample.Host;

/// <summary>只在显式安装该手写样板时注册业务；不修改受管启动或认证实现。</summary>
public static partial class HostCustomization
{
    /// <summary>使用既有宿主手写partial钩子，生成项目更新不能覆盖这里。</summary>
    static partial void ConfigureServices(IServiceCollection services,IConfiguration configuration,IHostEnvironment environment)
    {
        services.AddScoped<SampleContractAgentBusiness>();
        foreach(var button in new[]{"86000","86001","86002"})
            services.AddScoped<IOriginalBusinessButtonHandler>(provider=>new SampleContractButtonHandler(button,provider.GetRequiredService<SampleContractAgentBusiness>(),provider.GetRequiredService<Metarill.Core.Metadata.MetadataCatalog>()));
    }
}
