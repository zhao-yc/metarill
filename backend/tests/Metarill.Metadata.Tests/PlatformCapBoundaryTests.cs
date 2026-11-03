using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>直接执行链接的真实平台HTTP动作，验证码失败时不触及任何账号或数据库依赖。</summary>
public sealed class PlatformCapBoundaryTests
{
    /// <summary>启用配置只有公开站点地址；原form第三字段经实际动作进入共享二次校验。</summary>
    [Fact]
    public async Task Original_platform_cap_action_uses_private_configuration_and_verifies_before_account_lookup()
    {
        using var handler=new RejectingCap();using var client=new HttpClient(handler);var cap=new LoginCapVerifier(client,new(){Enabled=true,ServerUrl="https://cap.example.invalid",SiteKey="test-site",SecretKey="unit-only-platform-secret"});
        // 其它依赖若被触及会立即失败，不能用伪造账号／成功认证掩盖校验次序。
        var controller=new IdentityController(new AuthenticationService(null!,null!,null!,null!),null!,null!,null!,null!,cap);controller.ControllerContext=new(){HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext()};var config=controller.CapConfig();Assert.Equal(200,config.Code);Assert.DoesNotContain("unit-only-platform-secret",JsonSerializer.Serialize(config));
        var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>controller.Login("unit-only-user","unit-only-password",CancellationToken.None,"unit-only-proof"));
        Assert.Equal("安全验证失败，请重试",error.Message);Assert.Equal(1,handler.Calls);Assert.Equal("unit-only-proof",handler.Token);
    }
    /// <summary>缺少原字段保持原提示，连Cap传输也不发生，更不能提前读取密码。</summary>
    [Fact]
    public async Task Original_platform_missing_token_is_rejected_without_transport_or_identity_services()
    {
        using var handler=new RejectingCap();using var client=new HttpClient(handler);var controller=new IdentityController(new AuthenticationService(null!,null!,null!,null!),null!,null!,null!,null!,new(client,new(){Enabled=true}));
        controller.ControllerContext=new(){HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext()};
        var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>controller.Login("unit-only-user","unit-only-password",CancellationToken.None));Assert.Equal("请先完成安全验证",error.Message);Assert.Equal(0,handler.Calls);
    }
    private sealed class RejectingCap:HttpMessageHandler
    {
        internal int Calls;internal string? Token;
        /// <summary>只解释合成请求，不打开套接字或记录私钥正文。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));Token=json.RootElement.GetProperty("response").GetString();return new(HttpStatusCode.OK){Content=new StringContent("{\"success\":false}",Encoding.UTF8,"application/json")};
        }
    }
}
