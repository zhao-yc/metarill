using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>在真实打包宿主中验证共享导出接线；测试投影只存于临时包，完全不连接数据库。</summary>
internal static class StandaloneExportHttpProbe
{
    /// <summary>仅向测试包写入固定投影控制器，产品包不会包含该入口。</summary>
    public static void WriteFixture(string output) => File.WriteAllText(Path.Combine(output, "backend/Application.Host/ExportProbeController.cs"), """
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Mvc;
[ApiController, Route("api/test/export")]
public sealed class ExportProbeController(IPageExportService exports, IRuntimeAuthorization authorization) : ControllerBase
{
    /// <summary>经真实认证和读取前门禁提交固定投影，不访问任何数据库。</summary>
    [HttpPost]
    public async Task<R<string>> Create(CancellationToken token) => R<string>.Success(await exports.CreateAsync(authorization.RequireActor(), (maxRows, _) =>
    {
        if (maxRows != 1) throw new InvalidOperationException("未读取独立包导出限额。");
        return Task.FromResult(new PageExportRequest("101", "独立包导出", ["名称", "长编号", "安全文本"], [new object?[] { "中文验收", "9223372036854775807", "=1+1" }]));
    }, token));
}
""");

    /// <summary>使用随机空闲回环端口启动包内 DLL，验证登录、XLSX、Range、拒绝匿名和票据限额。</summary>
    public static async Task VerifyAsync(string output)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var hostDirectory = Path.Combine(output, "backend/Application.Host");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { WorkingDirectory = hostDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Debug/net10.0/Application.Host.dll"));
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "true";
        start.Environment["Authentication__DevelopmentBootstrap__Username"] = "package-probe";
        start.Environment["Authentication__DevelopmentBootstrap__Password"] = "package-probe-secret";
        start.Environment["Exports__RootDirectory"] = Path.Combine(output, ".local/exports");
        start.Environment["Exports__MaxRows"] = "1";
        start.Environment["Exports__MaxActiveTickets"] = "1";
        using var process = Process.Start(start)!;
        // 持续消费日志防止子进程输出缓冲区阻塞，不向测试输出打印登录凭据或票据。
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var ready = false;
            for (var i = 0; i < 100 && !process.HasExited; i++)
            {
                try { using var health = await client.GetAsync("/health"); if (health.IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Assert.True(ready, "独立包测试宿主未就绪。");
            using var anonymous = await client.GetAsync("/api/admin/sys/export/export?exportId=unknown");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var login = await client.PostAsJsonAsync("/api/admin/sys/yz_manager/login", new { username = "package-probe", password = "package-probe-secret" });
            login.EnsureSuccessStatusCode();
            var loginJson = await login.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Add("loginStateId", loginJson.GetProperty("map").GetProperty("loginStateId").GetString());
            using var create = await client.PostAsJsonAsync("/api/test/export", new { }); create.EnsureSuccessStatusCode();
            var ticket = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetString();
            var downloadPath = "/api/admin/sys/export/export?exportId=" + ticket;
            using var download = await client.GetAsync(downloadPath); download.EnsureSuccessStatusCode();
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", download.Content.Headers.ContentType?.MediaType);
            Assert.True(download.Headers.CacheControl?.Private); Assert.True(download.Headers.CacheControl?.NoStore);
            Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
            using var workbook = new ZipArchive(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));
            using var strings = workbook.GetEntry("xl/sharedStrings.xml")!.Open();
            var xml = await XDocument.LoadAsync(strings, LoadOptions.None, CancellationToken.None);
            Assert.Contains("中文验收", xml.ToString()); Assert.Contains("9223372036854775807", xml.ToString()); Assert.Contains("=1+1", xml.ToString());
            using var worksheet = workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = await XDocument.LoadAsync(worksheet, LoadOptions.None, CancellationToken.None);
            Assert.DoesNotContain(sheet.Descendants(), element => element.Name.LocalName == "f");
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, downloadPath);
            rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
            using var range = await client.SendAsync(rangeRequest); Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
            Assert.Equal(10, (await range.Content.ReadAsByteArrayAsync()).Length);
            using var full = await client.PostAsJsonAsync("/api/test/export", new { });
            Assert.Equal(HttpStatusCode.InternalServerError, full.StatusCode);
            Assert.Single(Directory.GetFiles(Path.Combine(output, ".local/exports")));
            using var logout = await client.PostAsJsonAsync("/api/admin/sys/yz_manager/exit_login", new { }); logout.EnsureSuccessStatusCode();
            using var revoked = await client.GetAsync(downloadPath); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(); await stdout; await stderr;
        }
    }
}