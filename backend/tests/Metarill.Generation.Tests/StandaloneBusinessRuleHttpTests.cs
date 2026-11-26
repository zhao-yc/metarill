using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>执行真实完整包及仅后端包的异常边界，探针不请求数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class StandaloneBusinessRuleHttpTests
{
    /// <summary>业务提示保持原HTTP200/R500；参数、权限及未知内部异常维持当前状态与脱敏。</summary>
    [Theory]
    [InlineData(ApplicationPackageMode.FullApplication)]
    [InlineData(ApplicationPackageMode.BackendOnly)]
    public async Task Actual_hosts_preserve_explicit_business_failure_without_exposing_internal_errors(ApplicationPackageMode mode)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-business-rule-http");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"app");FreshHost? host=null;try
        {
            new ApplicationPackager().Create(GlobalCachedSourceGenerationTests.Catalog(),package,"Example.BusinessRuleHttp",NonRedundantApplicationFixture.Root(),mode);
            await File.WriteAllTextAsync(Path.Combine(package,"backend/Application.Host/BusinessRuleProbeController.cs"),"""
using System.Security;
using Metarill.Core.Contracts;
using Metarill.Runtime.Files;
using Microsoft.AspNetCore.Mvc;
namespace Example.BusinessRuleHttp;
// 仅用于该测试包，实际宿主中间件负责转换异常和校验开发会话。
[ApiController,Route("contract-probe"),Route("api/contract-probe")]
public sealed class BusinessRuleProbeController:ControllerBase
{
    [HttpGet("{kind}")]
    public R<string> Get(string kind)=>kind switch
    {
        "business"=>throw new BusinessRuleException("合同金额不能小于零"),
        "argument"=>throw new ArgumentException("参数正文标记不得回显"),
        "required"=>throw BusinessRuleException.RequiredField("合同名称"),
        "upload-name"=>R<string>.Success(FileValidation.RequireName(null)),
        "upload-expired"=>UploadDeadline(DateTimeOffset.UnixEpoch.UtcDateTime),
        "upload-deadline-invalid"=>UploadDeadline("损坏期限标记不得回显"),
        "import-required"=>throw PageImportException.Input(PageImportError.Required,2,"合同名称"),
        "import-input"=>throw PageImportException.Input(PageImportError.Number,2,"数量"),
        "import-execution"=>throw PageImportException.Execution(2,new InvalidOperationException("导入内部错误不得回显")),
        "database"=>throw new InvalidOperationException("SQL连接正文标记不得回显"),
        "unauthorized"=>throw new UnauthorizedAccessException("身份正文标记不得回显"),
        "file-forbidden"=>throw new AuthenticatedFileReadDeniedException("文件内部路径标记不得回显"),
        "forbidden"=>throw new SecurityException("权限正文标记不得回显"),
        "unsupported"=>throw new NotSupportedException("内部路径标记不得回显"),
        _=>R<string>.Error("原控制器业务失败")
    };

    // 使用真实上传期限判定验证过期与坏数据不会落在同一种业务协议。
    private static R<string> UploadDeadline(object deadline)
    {
        FileUploadLifetime.RequireActive(new Dictionary<string, object?> { [FileUploadLifetime.DeadlineColumn] = deadline },
            DateTimeOffset.UnixEpoch.AddHours(1));
        return R<string>.Success("允许上传");
    }
}
""");
            foreach(var operation in new[]{"restore","build"})
                await NonRedundantApplicationFixture.BuildAsync(package,operation);
            var backendOnly=mode==ApplicationPackageMode.BackendOnly;
            var hostDirectory=Path.Combine(package,"backend/Application.Host");
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);
            start.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));
            start.Environment["ASPNETCORE_ENVIRONMENT"]=backendOnly?"Development":"Production";
            start.Environment["DOTNET_ENVIRONMENT"]=start.Environment["ASPNETCORE_ENVIRONMENT"];
            start.Environment["Authentication__Mode"]="Web";
            start.Environment["Authentication__DevelopmentBootstrap__Enabled"]=backendOnly?"true":"false";
            start.Environment.Remove("METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY");
            const string username="contract-test";
            var password=Guid.NewGuid().ToString("N");
            if(backendOnly)
            {
                start.Environment["Authentication__DevelopmentBootstrap__Username"]=username;
                start.Environment["Authentication__DevelopmentBootstrap__Password"]=password;
            }
            host=await FreshHost.StartAsync(start,"/health");
            var route=backendOnly?"/api/contract-probe/":"/contract-probe/";
            var unauthenticated=backendOnly?"请先登录；开发认证仅在 Development 且显式配置后可用。":"请先登录，或确认应用认证配置。";
            if(backendOnly)
            {
                // 走原开发登录端点建立会话，再从同一个 API 入口检查业务失败和其它异常。
                using var denied=await host.Client.GetAsync(route+"business");
                Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
                using var login=await host.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username,password});
                Assert.Equal(HttpStatusCode.OK,login.StatusCode);
                using var loginJson=JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                Assert.Equal(200,loginJson.RootElement.GetProperty("code").GetInt32());
                host.Client.DefaultRequestHeaders.Add("loginStateId",loginJson.RootElement.GetProperty("map").GetProperty("loginStateId").GetString()!);
            }
            const string internalError="操作失败，请由应用管理员核查服务端状态。";
            foreach(var (kind,status,code,message) in new[]
            {
                ("business",200,500,"合同金额不能小于零"),
                ("argument",400,400,"请求参数不符合页面定义。"),
                ("required",200,500,"合同名称不能为空。"),
                ("upload-name",200,500,"文件信息不能为空"),
                ("upload-expired",200,500,"上传计划已过期。"),
                ("upload-deadline-invalid",500,500,internalError),
                ("import-required",400,400,"第2行，列「合同名称」：不能为空。"),
                ("import-input",400,400,"第2行，列「数量」：应填写有效数字。"),
                ("import-execution",500,500,internalError),
                ("database",500,500,internalError),
                ("unauthorized",401,401,unauthenticated),
                ("forbidden",403,403,"没有当前操作权限。"),
                ("file-forbidden",403,403,"没有当前操作权限。"),
                ("unsupported",501,501,"此能力需要宿主实现，目前不可用。"),
                ("direct",200,500,"原控制器业务失败")
            })
            {
                using var response=await host.Client.GetAsync(route+kind);
                Assert.Equal((HttpStatusCode)status,response.StatusCode);
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(code,json.RootElement.GetProperty("code").GetInt32());
                Assert.Equal(message,json.RootElement.GetProperty("msg").GetString());
                Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("data").ValueKind);
                Assert.Empty(json.RootElement.GetProperty("map").EnumerateObject());
            }
        }
        finally{if(host is not null)await host.DisposeAsync();if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
