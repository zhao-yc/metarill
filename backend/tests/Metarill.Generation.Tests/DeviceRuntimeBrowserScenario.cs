using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实原页面负责登录、原按钮和刷新恢复；后台只观察、运行既有CLI并独立核验，不代点或伪造响应。</summary>
internal static class DeviceRuntimeBrowserScenario
{
    /// <summary>分三轮验证部分失败、完整成功及未知刷新；浏览器回执必须附本轮真实截图且与服务端批次一致。</summary>
    internal static async Task RunAsync(string source, string run, DeviceRuntimeBrowserPreparation browser, DeviceRuntimeNativePreparation proof, FreshHost host, string credentials, string username, string password, string[] projects, Func<string,int,Task<JsonElement>> probe, Func<Task> removeUnbound)
    {
        var evidence = proof.Host.EvidenceDirectory; var controls = Path.Combine(evidence, "browser-control"); Directory.CreateDirectory(controls);
        await File.WriteAllTextAsync(Path.Combine(credentials, "browser-login.json"), JsonSerializer.Serialize(new { username, password }));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var address = $"http://metarill-runtime-{run}.localhost:{port}/";
        var config = Path.Combine(controls, "preview.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { runId = run, leaseId = Guid.NewGuid().ToString("N"), expiresAt = DateTimeOffset.UtcNow.AddMinutes(28), frontend = browser.FrontendRoot, frontendAddress = $"http://127.0.0.1:{port}/", backendAddress = host.Client.BaseAddress!.ToString(), browserAddress = address, controlDirectory = controls, credentialDirectory = credentials }));
        await File.WriteAllTextAsync(Path.Combine(controls, "phase.json"), "{\"phase\":\"partial\"}");
        var info = NonRedundantApplicationFixture.Start("node", source); info.ArgumentList.Add(Path.Combine(source, "tools/acceptance/serve-runtime-browser.mjs")); info.ArgumentList.Add(config);
        using var gateway = Process.Start(info)!; var stdout = gateway.StandardOutput.ReadToEndAsync(); var stderr = gateway.StandardError.ReadToEndAsync();
        var seen = new HashSet<string>(StringComparer.Ordinal); var ui = new List<object>(); var stage = "原Web门面启动"; var passed = false; Exception? primaryFailure = null;
        async Task Stage(string value)
        {
            stage = value; await File.WriteAllTextAsync(Path.Combine(controls, "browser-stage.json"), JsonSerializer.Serialize(new { runId = run, stage }));
        }
        async Task<JsonElement> WaitJson(string file)
        {
            var deadline = DateTime.UtcNow.AddMinutes(8);
            while (DateTime.UtcNow < deadline)
            {
                if (gateway.HasExited) throw new InvalidOperationException("原浏览器门面提前退出。");
                if (File.Exists(file))
                {
                    try { using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file)); return document.RootElement.Clone(); }
                    catch (Exception error) when (error is JsonException or IOException) { /* 文件可能仍在原子发布之前；不扩大路径或重发业务。 */ }
                }
                await Task.Delay(200);
            }
            throw new TimeoutException("等待本轮原浏览器步骤超时：" + Path.GetFileName(file));
        }
        async Task<JsonElement> NewBatch(string phase)
        {
            var deadline = DateTime.UtcNow.AddMinutes(8);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var file in Directory.GetFiles(controls, "enqueued-*.json").Order(StringComparer.Ordinal))
                {
                    var name = Path.GetFileNameWithoutExtension(file)["enqueued-".Length..]; if (seen.Contains(name)) continue;
                    var value = await WaitJson(file); Assert.Equal(phase, value.GetProperty("phase").GetString()); Assert.Equal(name, value.GetProperty("batchId").GetString()); Assert.Equal(name, value.GetProperty("requestId").GetString()); Assert.True(value.GetProperty("confirmed").GetBoolean()); Assert.True(value.GetProperty("sysParamsPresent").GetBoolean()); Assert.Equal(500, value.GetProperty("code").GetInt32()); seen.Add(name); return value;
                }
                if (gateway.HasExited) throw new InvalidOperationException("原浏览器门面提前退出。"); await Task.Delay(200);
            }
            throw new TimeoutException("等待原页面真实批量按钮超时。");
        }
        async Task Acknowledge(string file, string scenario, string batch)
        {
            var value = await WaitJson(Path.Combine(controls, file + ".json")); Assert.Equal(run, value.GetProperty("runId").GetString()); Assert.Equal(scenario, value.GetProperty("scenario").GetString()); Assert.True(value.GetProperty("verified").GetBoolean()); Assert.Equal(batch, value.GetProperty("batchId").GetString()); Assert.True(value.GetProperty("spaceId").GetInt32() > 0);
            var screenshot = Path.GetFullPath(value.GetProperty("screenshot").GetString()!); Assert.StartsWith(Path.GetFullPath(evidence) + Path.DirectorySeparatorChar, screenshot); Assert.True(File.Exists(screenshot)); Assert.True(new FileInfo(screenshot).Length > 0);
            ui.Add(new { scenario, batchId = batch, screenshot, sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(screenshot))) });
        }
        async Task<JsonElement> Status(string batch)
        {
            var session = await WaitJson(Path.Combine(credentials, "browser-session.json")); var context = await WaitJson(Path.Combine(credentials, "browser-context.json"));
            using var client = new HttpClient { BaseAddress = host.Client.BaseAddress }; client.DefaultRequestHeaders.Add("loginStateId", session.GetProperty("loginStateId").GetString()); client.DefaultRequestHeaders.Add("sysParams", context.GetProperty("sysParams").GetString());
            using var request = DeviceRuntimeNativeHttp.Create("/api/admin/sys/generation-assistant/runtime-batch/status", new { batchId = batch }); using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var value = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(200, value.GetProperty("code").GetInt32()); return value.GetProperty("data").Clone();
        }
        async Task Ready(string phase)
        {
            await File.WriteAllTextAsync(Path.Combine(controls, "ready.json"), JsonSerializer.Serialize(new { runId = run, phase, address, credentialFile = Path.Combine(credentials, "browser-login.json"), evidence, controls }));
        }
        try
        {
            using (var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") })
            {
                client.DefaultRequestHeaders.Host = new Uri(address).Authority; var ready = false;
                for (var attempt = 0; attempt < 100 && !gateway.HasExited; attempt++)
                {
                    try { using var response = await client.GetAsync("/"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { }
                    await Task.Delay(100);
                }
                Assert.True(ready, "原浏览器Web门面未就绪。");
            }
            for (var index = 0; index < 3; index++)
            {
                var phase = index == 0 ? "partial" : index == 1 ? "complete" : "unknown";
                await Stage("等待原页面：" + phase); await File.WriteAllTextAsync(Path.Combine(controls, "phase.json"), JsonSerializer.Serialize(new { phase })); await Ready(phase);
                var incoming = await NewBatch(phase); var batch = incoming.GetProperty("batchId").GetString()!; DeviceRuntimeNativeMySqlTests.AssertItems(incoming, projects, "queued", index == 0);
                long restoreSequence = 0;
                if (index == 2)
                {
                    await Acknowledge("unknown-ui", "unknown", batch); await Acknowledge("reloaded-ui", "reloaded", batch);
                    restoreSequence = (await File.ReadAllLinesAsync(Path.Combine(controls, "gateway-trace.jsonl"))).Select(line => JsonDocument.Parse(line)).Max(document => { using (document) return document.RootElement.GetProperty("sequence").GetInt64(); });
                }
                await Stage("真实本机原计划应用：" + phase);
                for (var project = 0; project < 2; project++) Assert.True((await probe("poll", project)).GetProperty("applied").GetBoolean());
                DeviceRuntimeNativeMySqlTests.AssertItems(await Status(batch), projects, "applied", index == 0);
                if (index == 2) { await File.WriteAllTextAsync(Path.Combine(controls, "phase.json"), "{\"phase\":\"restored\"}"); await Ready("restored"); }
                await Stage("等待原页面结果：" + phase); await Acknowledge(index == 0 ? "partial-ui" : index == 1 ? "complete-ui" : "restored-ui", index == 2 ? "restored" : phase, batch);
                if (index == 0) await removeUnbound();
                if (index == 2)
                {
                    var after = (await File.ReadAllLinesAsync(Path.Combine(controls, "gateway-trace.jsonl"))).Select(line => JsonDocument.Parse(line)).ToArray();
                    try
                    {
                        Assert.DoesNotContain(after, document => document.RootElement.GetProperty("sequence").GetInt64() > restoreSequence && document.RootElement.GetProperty("pathname").GetString() == "/api/admin/custom/index/sync_runtime_all_projects");
                        Assert.Contains(after, document => document.RootElement.GetProperty("sequence").GetInt64() > restoreSequence && document.RootElement.GetProperty("pathname").GetString()!.EndsWith("/runtime-batch/status", StringComparison.Ordinal) && document.RootElement.TryGetProperty("batchId", out var id) && id.GetString() == batch);
                    }
                    finally { foreach (var document in after) document.Dispose(); }
                }
            }
            await Stage("等待原浏览器退出及本轮origin清理"); await Ready("cleanup"); var cleaned = await WaitJson(Path.Combine(controls, "browser-cleaned.json")); Assert.Equal(run, cleaned.GetProperty("runId").GetString()); Assert.True(cleaned.GetProperty("loggedOut").GetBoolean()); Assert.True(cleaned.GetProperty("originCleared").GetBoolean()); Assert.True(cleaned.GetProperty("spaceFinished").GetBoolean()); Assert.Equal(3, seen.Count); browser.Verify(source); passed = true;
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            // 门面收尾失败单独记录，不能再次遮住原页面／运输／断言首因。
            var cleanupErrors = new List<string>();
            try { if (!gateway.HasExited) gateway.Kill(true); await gateway.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(controls, "gateway.stdout.log"), await stdout); await File.WriteAllTextAsync(Path.Combine(controls, "gateway.stderr.log"), await stderr); }
            catch (Exception error) { cleanupErrors.Add(error.GetType().Name); }
            try { await File.WriteAllTextAsync(Path.Combine(evidence, "browser-verified.json"), JsonSerializer.Serialize(new { passed = passed && cleanupErrors.Count == 0, stage, primaryErrorType = primaryFailure?.GetType().Name, cleanupErrors, runId = run, actualOriginalFrontend = passed, realBrowser = passed, responseBusinessStubbed = false, batches = seen.Order(StringComparer.Ordinal).ToArray(), ui, hardwareExecuted = false, performanceExecuted = false })); }
            catch when (primaryFailure is not null) { /* 外层TRX保持原始故障，固定目录凭据仍由外层清理。 */ }
            if (primaryFailure is null && cleanupErrors.Count != 0) throw new InvalidOperationException("原浏览器门面收尾未完成，见本轮记录。");
        }
    }
}
