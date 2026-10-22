using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际运行包内宿主验证预检与认证顺序，不注册数据库或用源码字符串代替HTTP证据。</summary>
internal static class StandaloneCorsHttpProbe
{
    /// <summary>开发显式开启才允许回环HTTP(S)Origin，Production与关闭配置均不默认开放。</summary>
    public static async Task VerifyAsync(string output)
    {
        foreach (var (environment, enabled, allowed) in new[] { ("Development", true, true), ("Development", false, false), ("Production", true, false) })
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var host = Path.Combine(output, "backend/Application.Host");
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = host, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(Path.Combine(host, "bin/Debug/net10.0/Application.Host.dll")); info.ArgumentList.Add("--urls"); info.ArgumentList.Add($"http://127.0.0.1:{port}");
            info.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
            info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = enabled ? "true" : "false";
            info.Environment["Authentication__DevelopmentBootstrap__Username"] = "cors-probe";
            info.Environment["Authentication__DevelopmentBootstrap__Password"] = Guid.NewGuid().ToString("N");
            info.Environment["Exports__RootDirectory"] = Path.Combine(output, ".local/cors-exports", environment + "-" + enabled);
            using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
                await EnsureReadyAsync(client, process, stdout, stderr, TimeSpan.FromSeconds(30));
                foreach (var origin in new[] { "http://127.0.0.1:5190", "https://localhost:5191", "http://[::1]:5190" })
                {
                    using var request = Preflight(origin); using var response = await client.SendAsync(request);
                    if (allowed)
                    {
                        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
                        var headers = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).Split(',', StringSplitOptions.TrimEntries);
                        Assert.Contains("loginStateId", headers, StringComparer.OrdinalIgnoreCase); Assert.Contains("content-type", headers, StringComparer.OrdinalIgnoreCase);
                        Assert.Contains("POST", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")).Split(',', StringSplitOptions.TrimEntries));
                    }
                    else { Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.False(response.Headers.Contains("Access-Control-Allow-Origin")); }
                    Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
                }
                foreach (var origin in new[] { "https://evil.example", "http://localhost.evil.example", "file://localhost", "null", "http://127.0.0.1:5190/path", "http://user@localhost:5190", "http://localhost:5190?x=1", "http://localhost:5190#x" })
                {
                    using var request = Preflight(origin); using var response = await client.SendAsync(request); Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
                }
                // 有效开发Origin也不能代替登录；CORS应在401上仍附允许来源，浏览器才可处理真实错误。
                using var write = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sys/file-library/upload-plan") { Content = JsonContent.Create(new { }) };
                write.Headers.TryAddWithoutValidation("Origin", "http://127.0.0.1:5190");
                using var denied = await client.SendAsync(write); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                if (allowed) Assert.Equal("http://127.0.0.1:5190", Assert.Single(denied.Headers.GetValues("Access-Control-Allow-Origin")));
                else Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
                Assert.False(denied.Headers.Contains("Access-Control-Allow-Credentials"));
            }
            finally { await StopOwnedProcessAsync(process); await stdout; await stderr; }
        }
    }

    /// <summary>仅启动探测允许重试：每次HTTP超时可继续，总预算到期返回失败，外部取消必须原样传播。</summary>
    internal static async Task<bool> WaitUntilReadyAsync(HttpClient client, Func<bool> hasExited, TimeSpan budget, CancellationToken cancellationToken = default, TimeProvider? deadlineClock = null)
    {
        // 实际探测继续使用系统时钟；单元场景可控制预算时钟，独立于CI线程调度耗时。
        using var budgetDeadline = new CancellationTokenSource(budget, deadlineClock ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budgetDeadline.Token);
        while (!hasExited())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) return false;
            try
            {
                using var health = await client.GetAsync("/health", deadline.Token);
                cancellationToken.ThrowIfCancellationRequested();
                if (deadline.IsCancellationRequested) return false;
                if (health.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { /* 子进程尚未监听时继续在同一总预算内探测。 */ }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (deadline.IsCancellationRequested) return false;
                // HttpClient自身的单次5秒期限不等价于整个宿主启动失败。
            }
            try { await Task.Delay(100, deadline.Token); }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>启动失败后只结束本探针创建的进程，再收集其完整输出，避免ReadToEnd等待活进程而挂起。</summary>
    internal static async Task EnsureReadyAsync(HttpClient client, Process process, Task<string> stdout, Task<string> stderr, TimeSpan budget, CancellationToken cancellationToken = default)
    {
        if (await WaitUntilReadyAsync(client, () => process.HasExited, budget, cancellationToken)) return;
        await StopOwnedProcessAsync(process);
        throw new Xunit.Sdk.XunitException($"独立CORS验收宿主在{budget.TotalSeconds}秒启动总预算内未就绪；退出码：{process.ExitCode}。\n标准输出：\n{await stdout}\n标准错误：\n{await stderr}");
    }

    /// <summary>仅处理调用者创建的临时子进程，容许它在HasExited检查与Kill之间自行退出。</summary>
    private static async Task StopOwnedProcessAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) when (process.HasExited) { }
        await process.WaitForExitAsync();
    }

    /// <summary>预检包含真实uni-app请求的会话与JSON头，目标选受认证的文件写入口证明中间件顺序。</summary>
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/sys/file-library/upload-plan");
        request.Headers.TryAddWithoutValidation("Origin", origin); request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST"); request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "loginStateId,content-type"); return request;
    }
}
