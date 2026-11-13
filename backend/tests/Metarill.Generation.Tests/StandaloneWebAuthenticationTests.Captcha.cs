using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class StandaloneWebAuthenticationTests
{
    private const string CapConfigurationPath="/api/admin/sys/yz_manager/cap-config";

    /// <summary>执行实际编译宿主的认证边界；只公开原GET配置，不开放相似路径或其它方法。</summary>
    [Theory]
    [InlineData("GET",CapConfigurationPath,false)]
    [InlineData("POST",CapConfigurationPath,true)]
    [InlineData("PUT",CapConfigurationPath,true)]
    [InlineData("HEAD",CapConfigurationPath,true)]
    [InlineData("GET",CapConfigurationPath+"-extra",true)]
    [InlineData("GET",CapConfigurationPath+"/secret",true)]
    [InlineData("GET","/api/admin/sys/yz_manager/getCurrentPermissions",true)]
    [InlineData("GET","/api/admin/sys/yz_manager/edit_password",true)]
    public async Task Original_cap_config_has_only_the_exact_public_get_boundary(string method,string path,bool required)
    {
        var context=new DefaultHttpContext();context.Request.Method=method;context.Request.Path=path;
        Assert.Equal(required,await package.TopLevel("RequiresIdentity",context));
    }

    /// <summary>原启用开关必须真实生效，缺少token在任何数据库身份查询前按原R500拒绝。</summary>
    [Fact]
    public async Task Enabled_cap_keeps_original_public_configuration_and_missing_token_rejection()
    {
        var host=package.HostDirectory;var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";
        start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
        start.Environment["Database__ConnectionString"]="";start.Environment["Database__ExpectedDatabase"]="";
        start.Environment["Authentication__Cap__Enabled"]="true";start.Environment["Authentication__Cap__ServerUrl"]="https://cap.example.invalid";
        start.Environment["Authentication__Cap__SiteKey"]="test-site";start.Environment["Authentication__Cap__SecretKey"]="unit-only-private-cap-value";
        start.Environment["Logging__LogLevel__Default"]="Warning";
        await using var server=await FreshHost.StartProbeAsync(start,"/health");
        using var config=await server.Client.GetAsync(CapConfigurationPath);var body=await config.Content.ReadAsStringAsync();var value=JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(HttpStatusCode.OK,config.StatusCode);Assert.Equal(200,value.GetProperty("code").GetInt32());Assert.True(value.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal("https://cap.example.invalid/test-site/",value.GetProperty("data").GetProperty("apiEndpoint").GetString());Assert.DoesNotContain("unit-only-private-cap-value",body);
        using var response=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="no-database-user",password="unit-only-password",captchaVerification=""});
        var rejected=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(500,rejected.GetProperty("code").GetInt32());Assert.Equal("请先完成安全验证",rejected.GetProperty("msg").GetString());
        Assert.False(rejected.TryGetProperty("map",out var map)&&map.TryGetProperty("loginStateId",out _));
    }

    /// <summary>真实宿主把原第三字段转给本机合成Cap；通过才抵达无库拒绝，重定向不能泄出私钥。</summary>
    [Theory][InlineData("accepted",false)][InlineData("accepted",true)][InlineData("rejected",false)][InlineData("rejected",true)][InlineData("redirect",false)][InlineData("redirect",true)]
    public async Task Enabled_cap_in_real_host_uses_original_payload_and_never_follows_redirect(string scenario,bool json)
    {
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var fake=builder.Build();
        var hits=0;var redirected=0;string? secret=null;string? proof=null;string? query=null;
        fake.MapPost("/test-site/siteverify",async(HttpContext context)=>
        {
            Interlocked.Increment(ref hits);using var body=await JsonDocument.ParseAsync(context.Request.Body);secret=body.RootElement.GetProperty("secret").GetString();proof=body.RootElement.GetProperty("response").GetString();query=context.Request.QueryString.Value;
            if(scenario=="redirect"){context.Response.StatusCode=302;context.Response.Headers.Location="/redirect-target";return;}
            await context.Response.WriteAsJsonAsync(new{success=scenario=="accepted"});
        });
        fake.MapMethods("/redirect-target",new[]{"GET","POST"},()=>{Interlocked.Increment(ref redirected);return new{success=true};});await fake.StartAsync();
        var host=package.HostDirectory;var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
        start.Environment["Database__ConnectionString"]="";start.Environment["Database__ExpectedDatabase"]="";
        start.Environment["Authentication__Cap__Enabled"]="true";start.Environment["Authentication__Cap__ServerUrl"]=fake.Urls.Single();start.Environment["Authentication__Cap__SiteKey"]="test-site";start.Environment["Authentication__Cap__SecretKey"]="unit-only-wire-secret";start.Environment["Logging__LogLevel__Default"]="Warning";
        await using var server=await FreshHost.StartProbeAsync(start,"/health");
        using HttpContent content=json?JsonContent.Create(new{username="no-database-user",password="unit-only-password",captchaVerification="wire-proof"}):new FormUrlEncodedContent(new Dictionary<string,string>{{"username","no-database-user"},{"password","unit-only-password"},{"captchaVerification","wire-proof"}});
        using var response=await server.Client.PostAsync("/api/admin/sys/yz_manager/login",content);var value=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(1,hits);Assert.Equal(0,redirected);Assert.Equal("unit-only-wire-secret",secret);Assert.Equal("wire-proof",proof);Assert.True(string.IsNullOrEmpty(query));
        Assert.DoesNotContain("unit-only-wire-secret",value.GetRawText());Assert.False(value.TryGetProperty("map",out var map)&&map.TryGetProperty("loginStateId",out _));
        if(scenario=="accepted"){Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Assert.Equal(401,value.GetProperty("code").GetInt32());}
        else{Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(500,value.GetProperty("code").GetInt32());Assert.Equal(scenario=="redirect"?"安全验证暂不可用，请稍后再试":"安全验证失败，请重试",value.GetProperty("msg").GetString());}
    }

    /// <summary>真实Production/Web进程在无数据库、无身份时仍提供原登录前配置；保护接口继续401。</summary>
    [Fact]
    public async Task Original_cap_config_is_readable_before_login_in_the_real_generated_web_host()
    {
        var host=package.HostDirectory;
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";
        start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
        // 显式清空继承配置；此用例没有数据库连接，不注入假身份、令牌或认证服务。
        start.Environment["Database__ConnectionString"]="";start.Environment["Database__ExpectedDatabase"]="";
        start.Environment["Files__RootDirectory"]=Path.Combine(host,".local/cap-public-files");
        start.Environment["Exports__RootDirectory"]=Path.Combine(host,".local/cap-public-exports");
        start.Environment["Logging__LogLevel__Default"]="Warning";
        await using var server=await FreshHost.StartProbeAsync(start,"/health");
        foreach(var stale in new[]{false,true})
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,CapConfigurationPath);
            if(stale)request.Headers.Add("loginStateId",new string('a',64));
            using var response=await server.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var result=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(200,result.GetProperty("code").GetInt32());
            var data=result.GetProperty("data");Assert.Equal(2,data.EnumerateObject().Count());
            Assert.False(data.GetProperty("enabled").GetBoolean());Assert.Equal("",data.GetProperty("apiEndpoint").GetString());
        }
        foreach(var (method,path) in new[]{(HttpMethod.Post,CapConfigurationPath),(HttpMethod.Head,CapConfigurationPath),
            (HttpMethod.Get,CapConfigurationPath+"-extra"),(HttpMethod.Get,CapConfigurationPath+"/secret"),
            (HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions"),(HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu"),
            (HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login"),(HttpMethod.Post,"/api/admin/sys/yz_manager/edit_password")})
        {
            using var request=new HttpRequestMessage(method,path);using var response=await server.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        }
        using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="not-initialized",password="not-a-real-password"});
        Assert.Equal(HttpStatusCode.Unauthorized,login.StatusCode);
    }
    /// <summary>配置数据库的正式宿主缺Redis时，在Cap和数据库查询前拒绝，不退回开发身份。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Required_failure_store_is_checked_before_cap_and_credentials_in_the_real_host(bool json)
    {
        var host=package.HostDirectory;var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
        start.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;User ID=unit_only;Database=unit_only;Connection Timeout=1";start.Environment["Database__ExpectedDatabase"]="unit_only";
        start.Environment["Authentication__LoginFailures__ConnectionString"]="";start.Environment["Authentication__Cap__Enabled"]="true";start.Environment["Authentication__Cap__ServerUrl"]="https://cap.example.invalid";start.Environment["Authentication__Cap__SiteKey"]="unit-only-site";start.Environment["Authentication__Cap__SecretKey"]="unit-only-secret";start.Environment["Logging__LogLevel__Default"]="Warning";
        await using var server=await FreshHost.StartProbeAsync(start,"/health");using var body=json?(HttpContent)JsonContent.Create(new{username="unit-only-user",password="unit-only-password",captchaVerification=""}):new FormUrlEncodedContent(new Dictionary<string,string>{{"username","unit-only-user"},{"password","unit-only-password"},{"captchaVerification",""}});
        using var response=await server.Client.PostAsync("/api/admin/sys/yz_manager/login",body);var value=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(500,value.GetProperty("code").GetInt32());Assert.Equal("登录安全校验暂不可用，请稍后再试",value.GetProperty("msg").GetString());
        Assert.False(value.TryGetProperty("map",out var map)&&map.TryGetProperty("loginStateId",out _));Assert.DoesNotContain("unit-only-secret",value.ToString());
    }
}
