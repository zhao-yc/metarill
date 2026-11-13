using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>原页面固定提交第三个验证码字段；开关关闭时仍须抵达密码认证，不按未知字段拒绝。</summary>
    [Theory]
    [InlineData(false, "")][InlineData(true, "")]
    [InlineData(false, "client-proof-is-not-identity")][InlineData(true, "client-proof-is-not-identity")]
    public async Task Original_login_client_payload_accepts_optional_captcha_field(bool json,string captcha)
    {
        var context=new DefaultHttpContext();context.Request.ContentType=json?"application/json":"application/x-www-form-urlencoded";
        var body=json?JsonSerializer.Serialize(new{username=Username,password=Password,captchaVerification=captcha}):
            "username="+Uri.EscapeDataString(Username)+"&password="+Uri.EscapeDataString(Password)+"&captchaVerification="+Uri.EscapeDataString(captcha);
        context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(body));
        var parsed=await package.TopLevel("ReadLogin",context.Request);
        Assert.Equal(Username,package.Property<string>(parsed!,"Username"));Assert.Equal(Password,package.Property<string>(parsed!,"Password"));Assert.Equal(captcha,package.Property<string>(parsed!,"CaptchaVerification"));
    }

    /// <summary>原可选字段不能放开重复值、大小写变体、身份上下文或非字符串JSON。</summary>
    [Theory]
    [InlineData("form-duplicate")][InlineData("form-context")][InlineData("form-case")][InlineData("form-missing-password")]
    [InlineData("json-duplicate")][InlineData("json-context")][InlineData("json-null")][InlineData("json-object")][InlineData("json-missing-username")]
    public async Task Original_login_optional_field_preserves_strict_request_boundaries(string kind)
    {
        var body=kind switch
        {
            "form-duplicate"=>"username=a&password=b&captchaVerification=&captchaVerification=x",
            "form-context"=>"username=a&password=b&captchaVerification=&projectId=1",
            "form-case"=>"username=a&password=b&CaptchaVerification=",
            "form-missing-password"=>"username=a&captchaVerification=",
            "json-duplicate"=>"{\"username\":\"a\",\"password\":\"b\",\"captchaVerification\":\"\",\"captchaVerification\":\"x\"}",
            "json-context"=>"{\"username\":\"a\",\"password\":\"b\",\"captchaVerification\":\"\",\"projectId\":\"1\"}",
            "json-null"=>"{\"username\":\"a\",\"password\":\"b\",\"captchaVerification\":null}",
            "json-object"=>"{\"username\":\"a\",\"password\":\"b\",\"captchaVerification\":{\"enabled\":false}}",
            _=>"{\"password\":\"b\",\"captchaVerification\":\"\"}"
        };
        var context=new DefaultHttpContext();context.Request.ContentType=kind.StartsWith("form",StringComparison.Ordinal)?"application/x-www-form-urlencoded":"application/json";
        context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(body));
        await Assert.ThrowsAsync<ArgumentException>(()=>package.TopLevel("ReadLogin",context.Request));
    }

    /// <summary>同时执行原平台与生成包Vue方法取得三平台真实请求，再发送给Production/Web宿主；无库只接受认证拒绝401。</summary>
    [Fact]
    public async Task Original_login_client_payload_reaches_authentication_in_the_real_generated_host()
    {
        var host=package.HostDirectory;
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";
        start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
        start.Environment["Database__ConnectionString"]="";start.Environment["Database__ExpectedDatabase"]="";
        start.Environment["Logging__LogLevel__Default"]="Warning";
        await using var server=await FreshHost.StartProbeAsync(start,"/health");
        using var cap=await server.Client.GetAsync(CapConfigurationPath);Assert.Equal(HttpStatusCode.OK,cap.StatusCode);
        var configuration=await cap.Content.ReadFromJsonAsync<JsonElement>();Assert.False(configuration.GetProperty("data").GetProperty("enabled").GetBoolean());
        var payloads=await OriginalLoginPayloads(Path.GetFullPath(Path.Combine(host,"../../frontend/pages/login.vue")),configuration.GetProperty("data"));
        Assert.Equal(new[]{"original:H5","original:APP-PLUS","original:MP-WEIXIN","generated:H5","generated:APP-PLUS","generated:MP-WEIXIN"},payloads.Select(value=>value.GetProperty("client").GetString()+":"+value.GetProperty("platform").GetString()));
        foreach(var payload in payloads)
        {
            Assert.True(payload.GetProperty("submittingReset").GetBoolean());Assert.Equal("POST",payload.GetProperty("method").GetString());
            var original=payload.GetProperty("client").GetString()=="original";var fields=payload.GetProperty("data");
            Assert.False(payload.GetProperty("sendSessionId").GetBoolean());
            Assert.Equal(new[]{"captchaVerification","password","username"},fields.EnumerateObject().Select(value=>value.Name).OrderBy(value=>value,StringComparer.Ordinal));
            Assert.Equal("application/x-www-form-urlencoded",payload.GetProperty("contentType").GetString());Assert.Equal("",fields.GetProperty("captchaVerification").GetString());
            using HttpContent body=original?new FormUrlEncodedContent(fields.EnumerateObject().Select(value=>new KeyValuePair<string,string>(value.Name,value.Value.GetString()!))):JsonContent.Create(fields);
            using var response=await server.Client.PostAsync("/api"+payload.GetProperty("url").GetString(),body);
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
            var result=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(401,result.GetProperty("code").GetInt32());
            Assert.False(result.TryGetProperty("map",out var map)&&map.TryGetProperty("loginStateId",out _));
        }
        // Cap关闭时携带任意兼容字符串也不能成为密码/身份凭据。
        using var forged=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="original-client-offline",password="offline-invalid",captchaVerification="client-proof-is-not-identity"});
        Assert.Equal(HttpStatusCode.Unauthorized,forged.StatusCode);
    }

    /// <summary>真实宿主401回执进入两份原Vue逻辑；生成页不能静默吞掉错误，模态反馈与原页保持。</summary>
    [Fact]
    public async Task Original_generated_login_page_preserves_original_auth_error_feedback()
    {
        var host=package.HostDirectory;var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";
        start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
        start.Environment["Database__ConnectionString"]="";start.Environment["Database__ExpectedDatabase"]="";start.Environment["Logging__LogLevel__Default"]="Warning";
        await using var server=await FreshHost.StartProbeAsync(start,"/health");
        using var response=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="original-client-offline",password="offline-invalid"});
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);var rejected=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(401,rejected.GetProperty("code").GetInt32());
        var payloads=await OriginalLoginPayloads(Path.GetFullPath(Path.Combine(host,"../../frontend/pages/login.vue")),JsonSerializer.SerializeToElement(new{enabled=false,apiEndpoint=""}),rejected);
        Assert.Equal(6,payloads.Length);foreach(var payload in payloads)
        {
            Assert.True(payload.GetProperty("submittingReset").GetBoolean());
            Assert.Equal(new[]{rejected.GetProperty("msg").GetString()},payload.GetProperty("modals").EnumerateArray().Select(value=>value.GetString()));
        }
    }

    /// <summary>Node只执行两份真实登录页逻辑；HTTP回执由调用方提供，探针自身不访问网络或替代认证。</summary>
    private static async Task<JsonElement[]> OriginalLoginPayloads(string frontend,JsonElement configuration,JsonElement? response=null,[CallerFilePath]string caller="")
    {
        var start=new ProcessStartInfo("node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(caller)!,"StandaloneOriginalLoginProbe.mjs"));start.ArgumentList.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!,"../../../frontend/metarill_uni_admin/pages/login.vue")));start.ArgumentList.Add(configuration.GetRawText());start.ArgumentList.Add(frontend);start.ArgumentList.Add(response?.GetRawText()??"{\"code\":401,\"msg\":\"offline-probe-rejection\"}");
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(deadline.Token);}catch(Exception interrupted){await SharedSourceBuildGate.StopOwnProcessAsync(process,output,error,interrupted);throw;}
        Assert.True(process.ExitCode==0,await output+await error);return JsonSerializer.Deserialize<JsonElement[]>(await output)!;
    }
}
