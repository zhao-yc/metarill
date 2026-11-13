using System.Diagnostics;
using System.Net;
using System.Text;
using Metarill.Cli;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>真实CLI子进程连接Kestrel平台，使用原网页登录身份和真实MySQL；没有进程内HTTP替身。</summary>
    [MySqlApiFact]
    public async Task Platform_schema_cli_process_uses_real_web_host_and_rejects_invalid_sessions()
    {
        var (factory, client) = await Login(factory => { factory.UseKestrel(0); return factory; });
        await using var host = factory; using var http = client;
        var server = factory.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        var session = Assert.Single(client.DefaultRequestHeaders.GetValues("loginStateId"));
        foreach (var command in new[] { "catalog", "inspect" })
        {
            var result = await RunPlatformSchemaCli(address, command, Project, session);
            Assert.True(result.Code == 0, result.Error); Assert.Equal("", result.Error);
            var data = Json(result.Output); Assert.Equal(Project, data.GetProperty("projectId").GetString()); Assert.False(data.GetProperty("executable").GetBoolean());
            Assert.DoesNotContain(session, result.Output);
        }
        var denied = await RunPlatformSchemaCli(address, "catalog", Project, new string('x', 64));
        Assert.Equal(1, denied.Code); Assert.Equal("", denied.Output); Assert.Contains("平台结构查询失败", denied.Error); Assert.DoesNotContain(session, denied.Error);
        var wrongProject = await RunPlatformSchemaCli(address, "catalog", "9223372036854775807", session);
        Assert.Equal(1, wrongProject.Code); Assert.Equal("", wrongProject.Output);
    }

    /// <summary>真实默认HTTP传输遇重定向即拒绝，不能把loginStateId转发至另一端点。</summary>
    [Fact]
    public async Task Platform_schema_cli_process_does_not_follow_redirects_or_print_server_errors()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var host = builder.Build();
        var received = 0;
        host.MapPost("/api/admin/sys/database/catalog", context => { context.Response.Redirect("/capture"); return Task.CompletedTask; });
        host.MapMethods("/capture", ["GET", "POST"], () => { Interlocked.Increment(ref received); return Results.Text("不应转发会话"); });
        host.MapPost("/api/admin/sys/database/inspect", () => Results.Json(new { code = 500, msg = "测试私密连接字符串" }));
        await host.StartAsync();
        var address = Assert.Single(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        foreach (var command in new[] { "catalog", "inspect" })
        {
            var result = await RunPlatformSchemaCli(address, command, Project, new string('x', 64));
            Assert.Equal(1, result.Code); Assert.Equal("", result.Output); Assert.DoesNotContain("测试私密", result.Error); Assert.DoesNotContain(new string('x', 64), result.Error);
        }
        Assert.Equal(0, received);
        await host.StopAsync();
    }

    /// <summary>收到响应头后仍持续约束总时长，防止流式响应挂住CLI；不依赖HttpClient仅到响应头的超时。</summary>
    [Fact]
    public async Task Platform_schema_cli_process_bounds_stream_read_time()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var host = builder.Build();
        host.MapPost("/api/admin/sys/database/inspect", async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        await host.StartAsync();
        var address = Assert.Single(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        var result = await RunPlatformSchemaCli(address, "inspect", Project, new string('x', 64));
        Assert.Equal(1, result.Code); Assert.Equal("", result.Output); Assert.Contains("平台结构查询失败", result.Error);
        await host.StopAsync();
    }

    /// <summary>只通过子进程私有环境传会话；参数不拼shell，超时只终止本测试拥有的进程。</summary>
    private static async Task<(int Code, string Output, string Error)> RunPlatformSchemaCli(string address, string command, string project, string session, params string[] extra)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { typeof(PlatformSchemaCommand).Assembly.Location, "schema", command, "--server", address, "--project-id", project, "--token-env", "METARILL_CLI_TEST_SESSION" }.Concat(extra)) info.ArgumentList.Add(argument);
        info.Environment["METARILL_CLI_TEST_SESSION"] = session;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); await Task.WhenAll(output, error); throw; }
        return (process.ExitCode, await output, await error);
    }
}
