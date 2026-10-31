using System.Net;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>执行真实完整包的异常边界；探针不请求数据库或替代身份，不计实库业务通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class StandaloneBusinessRuleHttpTests
{
    /// <summary>业务提示保持原HTTP200/R500；参数、权限及未知内部异常维持当前状态与脱敏。</summary>
    [Fact]
    public async Task Actual_full_host_preserves_explicit_business_failure_without_exposing_internal_errors()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-business-rule-http");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"app");FreshHost? host=null;try
        {
            new ApplicationPackager().Create(GlobalCachedSourceGenerationTests.Catalog(),package,"Example.BusinessRuleHttp",NonRedundantApplicationFixture.Root());
            await File.WriteAllTextAsync(Path.Combine(package,"backend/Application.Host/BusinessRuleProbeController.cs"),"""
using System.Security;
using Metarill.Core.Contracts;
using Microsoft.AspNetCore.Mvc;
namespace Example.BusinessRuleHttp;
// 只为本次异常合同提供入口，避免数据库与身份替身掩盖真实宿主中间件。
[ApiController,Route("contract-probe")]
public sealed class BusinessRuleProbeController:ControllerBase
{
    [HttpGet("{kind}")]
    public R<string> Get(string kind)=>kind switch
    {
        "business"=>throw new BusinessRuleException("合同金额不能小于零"),
        "argument"=>throw new ArgumentException("参数正文标记不得回显"),
        "database"=>throw new InvalidOperationException("SQL连接正文标记不得回显"),
        "unauthorized"=>throw new UnauthorizedAccessException("身份正文标记不得回显"),
        "forbidden"=>throw new SecurityException("权限正文标记不得回显"),
        "unsupported"=>throw new NotSupportedException("内部路径标记不得回显"),
        _=>R<string>.Error("原控制器业务失败")
    };
}
""");
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(package,operation);var hostDirectory=Path.Combine(package,"backend/Application.Host");var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);start.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";start.Environment.Remove("METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY");host=await FreshHost.StartAsync(start,"/health");
            foreach(var (kind,status,code,message) in new[]{("business",200,500,"合同金额不能小于零"),("argument",400,400,"请求参数不符合页面定义。"),("database",500,500,"操作失败，请由应用管理员核查服务端状态。"),("unauthorized",401,401,"请先登录，或确认应用认证配置。"),("forbidden",403,403,"没有当前操作权限。"),("unsupported",501,501,"此能力需要宿主实现，目前不可用。"),("direct",200,500,"原控制器业务失败")})
            {
                using var response=await host.Client.GetAsync("/contract-probe/"+kind);Assert.Equal((HttpStatusCode)status,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(code,json.RootElement.GetProperty("code").GetInt32());Assert.Equal(message,json.RootElement.GetProperty("msg").GetString());
            }
        }
        finally{if(host is not null)await host.DisposeAsync();if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
