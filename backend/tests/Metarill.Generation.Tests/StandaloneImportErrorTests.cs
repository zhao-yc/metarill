using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>从真实模板装配并启动私有独立宿主；探针控制器只写临时包，不配置或连接数据库。</summary>
public sealed class StandaloneImportErrorTests
{
    [Fact]
    public async Task Actual_host_shows_only_safe_input_messages_and_logs_unexpected_errors_with_trace_and_inner_stack()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-import-error-http"); var root = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var output = Path.Combine(root, "application");
        try
        {
            new ApplicationPackager().Create(GeneratorTests.Catalog(), output, "Example.ImportErrorHttp", SourceRoot(), ApplicationPackageMode.BackendOnly);
            var host = Path.Combine(output, "backend/Application.Host");
            await File.WriteAllTextAsync(Path.Combine(host, "ImportErrorProbeController.cs"), """
using Metarill.Core.Contracts;
using Microsoft.AspNetCore.Mvc;
[ApiController, Route("api/test/import-error")]
public sealed class ImportErrorProbeController : ControllerBase
{
    [HttpGet("{mode}")]
    public object Probe(string mode)
    {
        HttpContext.TraceIdentifier = "import-error-probe-" + mode;
        throw mode switch
        {
            "input" => PageImportException.Input(PageImportError.Number, 2, "金额", new System.Text.Json.JsonException("INPUT-NO-LOG-MARKER")),
            "unknown" => PageImportException.Input(PageImportError.UnknownHeader, 2, "CLIENT-UNKNOWN-NO-ECHO"),
            "execution" => PageImportException.Execution(2, new InvalidOperationException("SYNTHETIC-INNER-SQL-PATH-CONNECTION")),
            "unexpected" => new InvalidOperationException("SYNTHETIC-UNEXPECTED-SECRET"),
            "argument" => new ArgumentException("RAW-ARG-NO-ECHO"),
            "denied" => new UnauthorizedAccessException("RAW-AUTH-NO-ECHO"),
            "forbidden" => new System.Security.SecurityException("RAW-PERMISSION-NO-ECHO"),
            _ => new InvalidOperationException("未知测试探针")
        };
    }
}
""");
            var build = Start(output, "build", "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet");
            using (var process = Process.Start(build)!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
                Assert.True(process.ExitCode == 0, await stdout + await stderr);
            }
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var start = Start(host, Path.Combine(host, "bin/Debug/net10.0/Application.Host.dll"), "--urls", "http://127.0.0.1:" + port);
            var password = Guid.NewGuid().ToString("N"); start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "true"; start.Environment["Authentication__DevelopmentBootstrap__Username"] = "import-error-probe"; start.Environment["Authentication__DevelopmentBootstrap__Password"] = password;
            start.Environment["Logging__LogLevel__Default"] = "Warning"; start.Environment["Exports__RootDirectory"] = Path.Combine(root, "exports");
            // 故意没有Database配置；所有私有探针都不注入DatabaseSession、Mapper或Service。
            using var server = Process.Start(start)!; var serverOut = server.StandardOutput.ReadToEndAsync(); var serverErr = server.StandardError.ReadToEndAsync();
            try
            {
                using var client = new HttpClient { BaseAddress = new("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(5) };
                var ready = false; for (var attempt = 0; attempt < 100 && !server.HasExited; attempt++)
                { try { using var health = await client.GetAsync("/health"); if (health.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } await Task.Delay(100); }
                Assert.True(ready, "独立无数据库错误探针宿主未就绪。");
                using var login = await client.PostAsJsonAsync("/api/admin/sys/yz_manager/login", new { username = "import-error-probe", password });
                Assert.Equal(HttpStatusCode.OK, login.StatusCode); var json = await login.Content.ReadFromJsonAsync<JsonElement>(); client.DefaultRequestHeaders.Add("loginStateId", json.GetProperty("map").GetProperty("loginStateId").GetString());
                foreach (var (mode, status, message) in new[]
                {
                    ("input", HttpStatusCode.BadRequest, "第2行，列「金额」：应填写有效数字。"),
                    ("unknown", HttpStatusCode.BadRequest, "第2行：存在未开放的导入列，请核对模板。"),
                    ("execution", HttpStatusCode.InternalServerError, "操作失败，请由应用管理员核查服务端状态。"),
                    ("unexpected", HttpStatusCode.InternalServerError, "操作失败，请由应用管理员核查服务端状态。"),
                    ("argument", HttpStatusCode.BadRequest, "请求参数不符合页面定义。"),
                    ("denied", HttpStatusCode.Unauthorized, "请先登录；开发认证仅在 Development 且显式配置后可用。"),
                    ("forbidden", HttpStatusCode.Forbidden, "没有当前操作权限。")
                })
                {
                    using var response = await client.GetAsync("/api/test/import-error/" + mode); Assert.Equal(status, response.StatusCode);
                    var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal((int)status, body.GetProperty("code").GetInt32()); Assert.Equal(message, body.GetProperty("msg").GetString());
                    Assert.DoesNotContain("SYNTHETIC", body.ToString()); Assert.DoesNotContain("NO-ECHO", body.ToString()); Assert.DoesNotContain("INPUT-NO-LOG", body.ToString());
                }
            }
            finally { if (!server.HasExited) server.Kill(true); await server.WaitForExitAsync(); }
            var logs = await serverOut + await serverErr;
            Assert.Contains("import-error-probe-execution", logs); Assert.Contains("import-error-probe-unexpected", logs); Assert.Contains("SYNTHETIC-INNER-SQL-PATH-CONNECTION", logs); Assert.Contains("SYNTHETIC-UNEXPECTED-SECRET", logs);
            Assert.Contains("PageImportException", logs); Assert.Contains("ImportErrorProbeController.Probe", logs); Assert.DoesNotContain("INPUT-NO-LOG-MARKER", logs); Assert.DoesNotContain("import-error-probe-input", logs);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root, boundary); }
    }

    /// <summary>仅运行当前临时包自己的SDK项目与DLL，输出持续重定向且不打印认证凭据。</summary>
    private static ProcessStartInfo Start(string working, params string[] arguments)
    { var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = working, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 }; foreach (var argument in arguments) start.ArgumentList.Add(argument); return start; }
    private static string SourceRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}
