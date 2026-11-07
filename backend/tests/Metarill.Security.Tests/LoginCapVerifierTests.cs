using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>实际共享Cap实现使用受控HTTP传输，不连接真实站点或数据库。</summary>
public sealed class LoginCapVerifierTests
{
    private const string Secret="unit-only-cap-secret";
    /// <summary>所有配置只为本机合成用例，不包含真实站点或密钥。</summary>
    private static LoginCapOptions Options(bool enabled=true,string server="https://cap.example.invalid",string site="test-site",string secret=Secret)=>new(){Enabled=enabled,ServerUrl=server,SiteKey=site,SecretKey=secret};
    /// <summary>关闭时不发送请求，公开配置及选项序列化均不包含私钥。</summary>
    [Fact]
    public async Task Disabled_cap_never_sends_and_public_payload_never_contains_secret()
    {
        using var handler=new Transport();using var client=new HttpClient(handler);var options=Options(false);var verifier=new LoginCapVerifier(client,options);
        await verifier.VerifyAsync(null);Assert.Equal(0,handler.Calls);var value=JsonSerializer.SerializeToElement(verifier.PublicConfiguration(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(value.GetProperty("data").GetProperty("enabled").GetBoolean());Assert.Equal("",value.GetProperty("data").GetProperty("apiEndpoint").GetString());Assert.DoesNotContain(Secret,value.GetRawText());Assert.DoesNotContain(Secret,JsonSerializer.Serialize(options));Assert.DoesNotContain(Secret,options.ToString()!);
    }
    /// <summary>原缺token提示优先于配置与网络，防止在验证前查询账号。</summary>
    [Theory][InlineData(null)][InlineData("")][InlineData(" \t\r\n")]
    public async Task Enabled_cap_requires_token_before_any_transport(string? token)
    {
        using var handler=new Transport();using var client=new HttpClient(handler);var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>new LoginCapVerifier(client,Options()).VerifyAsync(token));Assert.Equal("请先完成安全验证",error.Message);Assert.Equal(0,handler.Calls);
    }
    /// <summary>缺配置、带凭据／查询的地址和路径回退均不调用传输，也不把开关改成false。</summary>
    [Theory][InlineData("secret")][InlineData("site")][InlineData("url")][InlineData("scheme")][InlineData("credentials")][InlineData("query")][InlineData("dot")]
    public async Task Invalid_configuration_fails_closed_without_secret_or_network(string kind)
    {
        var options=kind switch{"secret"=>Options(secret:""),"site"=>Options(site:""),"url"=>Options(server:""),"scheme"=>Options(server:"file:///unit-only"),"credentials"=>Options(server:"https://user:unit-only-cap-secret@cap.example.invalid"),"query"=>Options(server:"https://cap.example.invalid?secret=unit-only-cap-secret"),_=>Options(site:"..")};
        using var handler=new Transport();using var client=new HttpClient(handler);var verifier=new LoginCapVerifier(client,options);Assert.Equal(500,verifier.PublicConfiguration().Code);
        var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>verifier.VerifyAsync("unit-token"));Assert.Equal("安全验证暂不可用，请稍后再试",error.Message);Assert.Equal(0,handler.Calls);Assert.DoesNotContain(Secret,error.ToString());
    }
    /// <summary>通过原站点路径和JSON两字段验证，只有布尔true放行，token按原ASCII规则去边空白。</summary>
    [Fact]
    public async Task Real_verifier_uses_original_siteverify_request_and_true_response()
    {
        using var handler=new Transport(async(request,_)=>
        {
            Assert.Equal(HttpMethod.Post,request.Method);Assert.Equal("https://cap.example.invalid/base/test-site/siteverify",request.RequestUri!.AbsoluteUri);Assert.Equal("application/json",request.Content!.Headers.ContentType!.MediaType);
            using var body=JsonDocument.Parse(await request.Content.ReadAsStringAsync());Assert.Equal(new[]{"response","secret"},body.RootElement.EnumerateObject().Select(field=>field.Name).Order(StringComparer.Ordinal));Assert.Equal(Secret,body.RootElement.GetProperty("secret").GetString());Assert.Equal("unit-token",body.RootElement.GetProperty("response").GetString());return Response("{\"success\":true}");
        });
        using var client=new HttpClient(handler);var verifier=new LoginCapVerifier(client,Options(server:"https://cap.example.invalid/base/"));Assert.Equal(200,verifier.PublicConfiguration().Code);await verifier.VerifyAsync(" \tunit-token\r\n");Assert.Equal(1,handler.Calls);
    }
    /// <summary>缺success、错误布尔类型及重复键不应被JSON宽松转换视为成功。</summary>
    [Theory][InlineData("{}")] [InlineData("{\"success\":false}")] [InlineData("{\"success\":\"true\"}")] [InlineData("{\"success\":1}")] [InlineData("null")] [InlineData("[]")] [InlineData("{\"success\":false,\"success\":true}")]
    public async Task Invalid_responses_never_reach_password_validation(string body)
    {
        using var handler=new Transport((_,_)=>Task.FromResult(Response(body)));using var client=new HttpClient(handler);var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>new LoginCapVerifier(client,Options()).VerifyAsync("unit-token"));Assert.Equal("安全验证失败，请重试",error.Message);Assert.Equal(1,handler.Calls);
    }
    /// <summary>HTTP错误、异常、坏JSON及超预算响应只显示固定不可用提示，不附远端秘密正文。</summary>
    [Theory][InlineData("http")][InlineData("exception")][InlineData("json")][InlineData("oversized")][InlineData("timeout")][InlineData("redirect")]
    public async Task Transport_failures_are_closed_and_redacted(string kind)
    {
        using var handler=new Transport((_,_)=>kind switch
        {"exception"=>throw new HttpRequestException(Secret),"timeout"=>throw new TaskCanceledException(Secret),"http"=>Task.FromResult(Response(Secret,HttpStatusCode.BadGateway)),"redirect"=>Task.FromResult(Response("{\"success\":true}",HttpStatusCode.Redirect)),"json"=>Task.FromResult(Response(Secret)),_=>Task.FromResult(Response("{\"success\":true,\"padding\":\""+new string('x',4096)+"\"}"))});
        using var client=new HttpClient(handler);var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>new LoginCapVerifier(client,Options()).VerifyAsync("unit-token"));Assert.Equal("安全验证暂不可用，请稍后再试",error.Message);Assert.DoesNotContain(Secret,error.ToString());Assert.Null(error.InnerException);
    }
    /// <summary>调用方取消继续传播，不能被错误转成验证码成功或失败计数。</summary>
    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        using var handler=new Transport();using var client=new HttpClient(handler);using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new LoginCapVerifier(client,Options()).VerifyAsync("unit-token",cancellation.Token));Assert.Equal(0,handler.Calls);
    }
    /// <summary>所有响应是本测试合成JSON；处理器不持有网络套接字。</summary>
    private static HttpResponseMessage Response(string body,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    private sealed class Transport(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>? send=null):HttpMessageHandler
    {
        internal int Calls;
        /// <summary>仅记录调用次数，凭据正文不进入诊断输出。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Calls++;return send?.Invoke(request,cancellationToken)??Task.FromResult(Response("{\"success\":true}"));}
    }
}
