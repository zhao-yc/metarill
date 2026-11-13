using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式验收前先验证原三字段登录传输；仅替换HTTP底层，不连接网络或伪造数据库验收。</summary>
public sealed class GlobalDerivedTargetNativeContractTests
{
    /// <summary>原空验证码字段和编码均保留，连续登录只保留最新令牌header。</summary>
    [Fact]
    public async Task Original_login_form_keeps_three_fields_and_replaces_previous_header()
    {
        var count=0;using var handler=new Capture(async request=>
        {
            Assert.Equal(HttpMethod.Post,request.Method);Assert.Equal("/api/admin/sys/yz_manager/login",request.RequestUri!.AbsolutePath);Assert.Equal("",request.RequestUri.Query);Assert.Equal("application/x-www-form-urlencoded",request.Content!.Headers.ContentType!.MediaType);
            var data=(await request.Content.ReadAsStringAsync()).Split('&').Select(value=>value.Split('=',2)).ToDictionary(pair=>Decode(pair[0]),pair=>Decode(pair[1]),StringComparer.Ordinal);
            Assert.Equal(new[]{"captchaVerification","password","username"},data.Keys.Order(StringComparer.Ordinal));Assert.Equal("",data["captchaVerification"]);Assert.Equal("合成账户+%",data["username"]);Assert.Equal("合成口令&+中文",data["password"]);count++;
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{code=200,map=new{loginStateId=new string(count==1?'a':'b',64)}}),Encoding.UTF8,"application/json")};
        });using var client=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:1")};
        Assert.Equal(new string('a',64),await GlobalDerivedTargetNativeHttp.LoginAsync(client,"合成账户+%","合成口令&+中文"));Assert.Equal(new string('b',64),await GlobalDerivedTargetNativeHttp.LoginAsync(client,"合成账户+%","合成口令&+中文"));Assert.Equal(2,count);Assert.Equal(new string('b',64),Assert.Single(client.DefaultRequestHeaders.GetValues("loginStateId")));
    }
    /// <summary>按form规则先还原空格再解码转义，合成数据不发往网络。</summary>
    private static string Decode(string value)=>Uri.UnescapeDataString(value.Replace('+',' '));
    private sealed class Capture(Func<HttpRequestMessage,Task<HttpResponseMessage>> reply):HttpMessageHandler
    {
        /// <summary>替换传输边界，上层表单构造和响应令牌解析保持实际实现。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>reply(request);
    }
}
