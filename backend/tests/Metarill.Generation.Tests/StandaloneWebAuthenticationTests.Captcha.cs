using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
        await using var server=await FreshHost.StartAsync(start,"/health");
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
}
