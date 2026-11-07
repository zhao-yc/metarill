using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>实际平台Program的初始化工厂、目标绑定和原HTTP入口装配；只用不可达的离线连接，不执行账号写入。</summary>
public sealed class PlatformAdminInitializationWiringTests
{
    /// <summary>解析全部实际依赖并由匿名HTTP检查入口；初始化事务、物理身份及业务写入另有专项，不由此测试代替。</summary>
    [Fact]
    public async Task Actual_platform_program_resolves_original_initialization_and_denies_anonymous_requests()
    {
        var folder=Path.Combine(Path.GetTempPath(),"metarill-platform-init-wiring",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"settings.json");var previous=Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        var database=new{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1;"};
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Metarill=new{Database=database,BusinessDatabase=database}}));
        Environment.SetEnvironmentVariable("METARILL_SETTINGS",path);
        try
        {
            await using var factory=new WebApplicationFactory<Program>();using var client=factory.CreateClient();
            using var scope=factory.Services.CreateScope();var services=scope.ServiceProvider;
            Assert.IsType<PlatformAdminAccountInitializationBusiness>(services.GetRequiredService<IAdminAccountInitializer>());
            Assert.IsType<AgentAdminInitializationPolicy>(services.GetRequiredService<IAdminInitializationAgentPolicy>());
            Assert.NotNull(ActivatorUtilities.CreateInstance<CompatibilityButtonController>(services));
            var registry=services.GetRequiredService<MetadataRegistry>();
            var platformPhysical=services.GetRequiredService<ProjectPhysicalModels>().Compose(registry.Platform);
            Assert.NotEmpty(Metarill.Data.Schema.MySqlSchemaPlanner.Compile(platformPhysical,"offline").Tables);
            var targets=services.GetRequiredService<ProjectAdminInitializationTargets>();
            var sessions=services.GetRequiredService<TargetDatabaseSessions>();
            Assert.Same(sessions.Business,targets.Resolve(registry.Demo.YzProjectId).Session);
            Assert.Throws<UnauthorizedAccessException>(()=>targets.Resolve(registry.Platform.YzProjectId));
            Assert.Throws<InvalidOperationException>(()=>targets.Resolve("999999999999999999"));
            foreach(var route in new[]{"/api/admin/custom/index/init_admin?id=7","/api/admin/sys/config_button/execute"})
            {
                using var response=route.EndsWith("execute",StringComparison.Ordinal)
                    ?await client.PostAsJsonAsync(route,new{buttonId=AdminAccountInitializationBusiness.ButtonId,entityId="7"})
                    :await client.PostAsync(route,null);
                using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(500,body.RootElement.GetProperty("code").GetInt32());
                Assert.Equal("登录超时",body.RootElement.GetProperty("msg").GetString());
            }
            Assert.Null(sessions.Platform.CurrentTransaction);Assert.Null(sessions.Business.CurrentTransaction);
        }
        finally
        {
            Environment.SetEnvironmentVariable("METARILL_SETTINGS",previous);
            if(File.Exists(path))File.Delete(path);if(Directory.Exists(folder))Directory.Delete(folder);
        }
    }
}
