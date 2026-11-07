using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: HostingStartup(typeof(Metarill.Paired.SqlObservation.SqlObservationHostingStartup))]
namespace Metarill.Paired.SqlObservation;

/// <summary>仅可信验收宿主显式加载本程序集并启用配置；产品Program、接口及模板无需添加分支。</summary>
public sealed class SqlObservationHostingStartup:IHostingStartup
{
    /// <summary>默认不注册服务、不读文件；开启后才在原认证之前接请求采集。</summary>
    public void Configure(IWebHostBuilder builder)=>builder.ConfigureServices((context,services)=>
    {
        var section=context.Configuration.GetSection("Metarill:Paired:SqlObservation");if(!section.GetValue<bool>("Enabled"))return;
        // 测试宿主或宿主构造器可能重放配置回调，同一服务集合只接一次采集。
        services.TryAddSingleton(_=>new SqlObservationHost(section));services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter,ObservationStartupFilter>());
    });
    private sealed class ObservationStartupFilter(SqlObservationHost host):IStartupFilter
    {
        /// <summary>沿同一原RequestDelegate建立scope，原链执行一次，不增路由、返回头或授权。</summary>
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use((context,continuation)=>host.CaptureAsync(context,()=>continuation(context)));next(app);};
    }
}
