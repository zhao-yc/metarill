using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>完整平台生产宿主、真实数据库／OAuth／CLI及两个本机工程；门禁替身或CI跳过不是正式通过。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class DeviceRuntimeNativeMySqlTests
{
    private const string Batch = "/api/admin/sys/generation-assistant/runtime-batch/", Legacy = "/api/admin/custom/index/sync_runtime_all_projects?parentId=undefined";

    /// <summary>两项目原按钮批次逐项应用、未绑定失败、禁用跳过，以及固定地址重启恢复原批次。</summary>
    [DeviceRuntimeNativeMySqlFact]
    public Task Original_batch_uses_real_device_bindings_two_projects_and_restart_recovery() => RunAsync(false);

    /// <summary>同一真实初始化／绑定基础上由原页面实际操作，代理只观察或注入运输故障，不制造业务成功。</summary>
    [DeviceRuntimeBrowserNativeFact]
    public Task Original_browser_drives_real_batch_and_unknown_refresh_recovery() => RunAsync(true);

    /// <summary>共用真实平台、数据库和本机工程准备，浏览器与纯HTTP只在明确专属入口处分流。</summary>
    private async Task RunAsync(bool browserMode)
    {
        var nativeSettings = Environment.GetEnvironmentVariable(browserMode ? "METARILL_DEVICE_RUNTIME_BROWSER_SETTINGS" : "METARILL_DEVICE_RUNTIME_NATIVE_SETTINGS")!;
        Assert.Equal(nativeSettings, Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS"));
        var source = Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? NonRedundantApplicationFixture.Root());
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(nativeSettings));
        var (run, proof) = DeviceRuntimeNativePreparation.Read(settings.RootElement, source, browserMode);
        var browserPreparation = browserMode ? DeviceRuntimeBrowserPreparation.Read(settings.RootElement, source) : null;
        var platform = FreshInstallConfiguration.Read("Database", "platform"); var business = FreshInstallConfiguration.Read("BusinessDatabase", "business");
        using var session = new DatabaseSession(platform); using var businessSession = new DatabaseSession(business);
        Assert.Equal(0L, await Scalar(session, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
        Assert.Equal(0L, await Scalar(businessSession, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
        var evidence = proof.Host.EvidenceDirectory; Directory.CreateDirectory(evidence); Directory.CreateDirectory(proof.ExecutionRoot);
        var credentialsRoot = Path.Combine(evidence, "credentials"); var credentials = Path.Combine(credentialsRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(credentials);
        var password = Guid.NewGuid().ToString("N"); var username = "device-runtime-owner"; var webToken = ""; var stage = "显式平台初始化"; var passed = false; FreshHost? host = null; Exception? primaryFailure = null;
        var realWeb = false; var bindings = 0; var applied = new HashSet<string>(StringComparer.Ordinal); var queueRejectedSuccess = false; var unboundFailed = false; var disabledSkipped = false; var runtimeWrites = false; var frontendPreserved = false; var restartBatch = false;
        var hostSettings = Path.Combine(credentials, "host.json");
        await File.WriteAllTextAsync(hostSettings, JsonSerializer.Serialize(new { Metarill = new { Database = platform, BusinessDatabase = business, WorkerId = 881, Bootstrap = new { Username = username, Password = password } } }));
        ProcessStartInfo Info(string? operation = null)
        {
            var info = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", source); info.ArgumentList.Add(proof.Host.AssemblyPath); if (operation is not null) info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; info.Environment["METARILL_SETTINGS"] = hostSettings;
            info.Environment["Metarill__Database__ConnectionString"] = platform.ConnectionString; info.Environment["Metarill__BusinessDatabase__ConnectionString"] = business.ConnectionString; info.Environment["Metarill__Bootstrap__Password"] = password;
            info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = evidence; return info;
        }
        async Task<string> Process(ProcessStartInfo info, string name)
        {
            using var child = System.Diagnostics.Process.Start(info)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            try { await child.WaitForExitAsync(timeout.Token); } catch { if (!child.HasExited) child.Kill(true); await child.WaitForExitAsync(); throw; }
            string Redact(string text) { foreach (var secret in new[] { password, webToken, platform.ConnectionString, business.ConnectionString }.Where(secret => secret.Length > 0)) text = text.Replace(secret, "[已脱敏]", StringComparison.Ordinal); return text; }
            var text = await stdout; await File.WriteAllTextAsync(Path.Combine(evidence, name + ".stdout.log"), Redact(text)); await File.WriteAllTextAsync(Path.Combine(evidence, name + ".stderr.log"), Redact(await stderr));
            Assert.True(child.ExitCode == 0, "设备批量固定阶段失败：" + name + "；见本轮脱敏日志。"); return text;
        }
        try
        {
            await Process(Info("--initialize"), "initialize");
            var registry = new MetadataRegistry(Path.Combine(source, "backend/metadata/platform.json"), Path.Combine(source, "backend/metadata/demo.json")); var store = new SqlRowStore(session, registry.Platform); var metadata = new MetadataRepository(store, registry);
            var second = JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(registry.Demo, JsonDefaults.Options).Replace("220000000000010", "220000000000020", StringComparison.Ordinal), JsonDefaults.Options)!;
            second.YzProjectName = "助手第二项目";
            foreach (var project in new[] { (DeviceRuntimeNativePreparation.SecondProject, second.YzProjectName), (DeviceRuntimeNativePreparation.UnboundProject, "未绑定项目"), (DeviceRuntimeNativePreparation.DisabledProject, "禁用项目") })
                await store.InsertAsync("yz_project", new Dictionary<string, object?> { ["id"] = long.Parse(project.Item1, CultureInfo.InvariantCulture), ["name"] = project.Item2, ["yz_yzp_version_id"] = TemplateVersionCatalog.CurrentValue, ["db_type"] = 2, ["db_name"] = business.ExpectedDatabase });
            await metadata.SeedAsync(second);
            stage = "从数据库元数据创建两个旧运行时完整工程";
            var projects = new[] { registry.Demo.YzProjectId, second.YzProjectId }; var targets = projects.Select((_, index) => Path.Combine(proof.ExecutionRoot, "target-" + index)).ToArray(); var works = projects.Select((_, index) => Path.Combine(proof.ExecutionRoot, "work-" + index)).ToArray();
            for (var index = 0; index < projects.Length; index++)
            {
                var catalog = await metadata.LoadProjectAsync(projects[index]); new ApplicationPackager().Create(catalog, targets[index], "Metarill.DeviceRuntime" + index, proof.BaselineSourceRoot);
                await File.WriteAllTextAsync(Path.Combine(targets[index], "业务手写保留.txt"), "本轮手写文件必须保持原样");
            }
            var before = targets.Select(Snapshot).ToArray();
            var configId = 880000000000000100L;
            foreach (var project in projects.Concat([DeviceRuntimeNativePreparation.UnboundProject, DeviceRuntimeNativePreparation.DisabledProject]))
            {
                var index = Array.IndexOf(projects, project); var enabled = project != DeviceRuntimeNativePreparation.DisabledProject; var target = index >= 0 ? targets[index] : Path.Combine(proof.ExecutionRoot, "unbound");
                await store.InsertAsync("yz_manager_devices_project", new Dictionary<string, object?> { ["id"] = ++configId, ["yz_manager_id"] = 1L, ["yz_project_id"] = long.Parse(project, CultureInfo.InvariantCulture), ["yz_manager_devices_id"] = 1L, ["is_java"] = enabled ? 1 : 0, ["java_path"] = target, ["is_uniapp"] = index == 0 ? 1 : 0, ["uniapp_path"] = index == 0 ? Path.Combine(target, "frontend") : null });
            }
            var unboundConfig = configId - 1;
            var input = Path.Combine(evidence, "input.json");
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new { deviceId = "1", config = Path.Combine(credentials, "cli.json"), cli = Path.Combine(proof.CliRoot, "Metarill.Cli.dll"), dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"), projects = projects.Select((projectId, index) => new { projectId, target = targets[index], work = works[index] }).ToArray() }));
            host = await FreshHost.StartAsync(Info(), "/api/runtime/health/check"); webToken = await FreshWebHttp.LoginAsync(host.Client, username, password, form: true); realWeb = true;
            void Headers(HttpClient client) { client.DefaultRequestHeaders.Remove("loginStateId"); client.DefaultRequestHeaders.Add("loginStateId", webToken); client.DefaultRequestHeaders.Remove("sysParams"); client.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = registry.Platform.YzProjectId, yzManagerDevicesId = "1" }))); }
            Headers(host.Client);
            async Task<JsonElement> Probe(string mode, int index)
            {
                var info = NonRedundantApplicationFixture.Start("node", source); foreach (var argument in new[] { Path.Combine(source, "backend/tests/Metarill.Generation.Tests/DeviceRuntimeBatchCliLiveProbe.mjs"), mode, host!.Client.BaseAddress!.ToString().TrimEnd('/') + "/api", source, evidence, input, index.ToString(CultureInfo.InvariantCulture) }) info.ArgumentList.Add(argument);
                info.Environment["METARILL_AGENT_WEB_TOKEN"] = webToken; using var response = JsonDocument.Parse(await Process(info, "cli-" + mode + "-" + index + "-" + Guid.NewGuid().ToString("N"))); return response.RootElement.Clone();
            }
            async Task<JsonElement> Send(string route, object body, string? batch = null, int code = 200)
            {
                using var request = DeviceRuntimeNativeHttp.Create(route, body, batch);
                using var response = await host!.Client.SendAsync(request); var value = await response.Content.ReadFromJsonAsync<JsonElement>();
                // 只留传输／包装形状，不输出正文、消息、账号或令牌；断言失败前先留下首因。
                await File.WriteAllTextAsync(Path.Combine(evidence, "http-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new { stage, route = route.Split('?')[0], httpStatus = (int)response.StatusCode, code = value.TryGetProperty("code", out var actual) && actual.ValueKind == JsonValueKind.Number && actual.TryGetInt32(out var number) ? (int?)number : null, dataKind = value.TryGetProperty("data", out var data) ? data.ValueKind.ToString() : "missing" }));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(code, value.GetProperty("code").GetInt32()); return value.GetProperty("data").Clone();
            }
            stage = "真实OAuth与两个原设备关联"; for (var index = 0; index < 2; index++) { var bound = await Probe("bind", index); Assert.True(bound.GetProperty("deviceBound").GetBoolean()); bindings++; Assert.Equal(before[index], Snapshot(targets[index])); }
            if (browserPreparation is not null)
            {
                stage = "原浏览器真实页面及未知刷新恢复";
                await DeviceRuntimeBrowserScenario.RunAsync(source, run, browserPreparation, proof, host, credentials, username, password, projects, Probe,
                    async () => { await store.UpdateAsync("yz_manager_devices_project", unboundConfig, new Dictionary<string, object?> { ["delete_no"] = new DateTime(2026, 11, 1) }); });
                await AuditUpdatedTargetsAsync(source, targets, before);
                foreach (var project in projects) applied.Add(project);
                queueRejectedSuccess = true; unboundFailed = true; disabledSkipped = true; runtimeWrites = true; frontendPreserved = true;
                stage = "原浏览器收尾及真实CLI注销";
                for (var index = 0; index < 2; index++) Assert.True((await Probe("logout", index)).GetProperty("loggedOut").GetBoolean());
                proof.Verify(source); browserPreparation.Verify(source); passed = true; return;
            }
            stage = "原批量按钮入队不得假成功"; var firstBatch = Guid.NewGuid().ToString("D"); var first = await Send(Legacy, new { }, firstBatch, 500); AssertItems(first, projects, "queued", includeUnbound: true); queueRejectedSuccess = true; unboundFailed = true; disabledSkipped = true;
            var ids = first.GetProperty("items").EnumerateArray().Where(item => projects.Contains(item.GetProperty("projectId").GetString())).ToDictionary(item => item.GetProperty("projectId").GetString()!, item => item.GetProperty("requestId").GetString()); Assert.Equal(2, ids.Values.Distinct().Count());
            for (var index = 0; index < 2; index++) { var result = await Probe("poll", index); Assert.Equal(ids[projects[index]], result.GetProperty("requestId").GetString()); applied.Add(projects[index]); }
            var complete = await Send(Batch + "status", new { batchId = firstBatch }); AssertItems(complete, projects, "applied", includeUnbound: true);
            stage = "核对实际运行时写入与未选前端";
            await AuditUpdatedTargetsAsync(source, targets, before);
            runtimeWrites = true; frontendPreserved = true;
            stage = "重启恢复原批次及相同任务UUID";
            await store.UpdateAsync("yz_manager_devices_project", unboundConfig, new Dictionary<string, object?> { ["delete_no"] = new DateTime(2026, 11, 1) });
            var secondBatch = Guid.NewGuid().ToString("D"); var queued = await Send(Legacy, new { }, secondBatch, 500); AssertItems(queued, projects, "queued", includeUnbound: false);
            var address = host.Client.BaseAddress!; await host.DisposeAsync(); host = null; host = await FreshHost.StartAsync(Info(), "/api/runtime/health/check", address); Headers(host.Client);
            var recovered = await Send(Batch + "status", new { batchId = secondBatch }); Assert.Equal(queued.GetRawText(), recovered.GetRawText());
            for (var index = 0; index < 2; index++) await Probe("poll", index);
            var final = await Send(Batch + "status", new { batchId = secondBatch }); AssertItems(final, projects, "applied", includeUnbound: false); var repeated = await Send(Batch + "status", new { batchId = secondBatch }); Assert.Equal(final.GetRawText(), repeated.GetRawText()); restartBatch = true;
            stage = "注销两个真实CLI授权"; for (var index = 0; index < 2; index++) Assert.True((await Probe("logout", index)).GetProperty("loggedOut").GetBoolean());
            proof.Verify(source); passed = true;
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            // 收尾独立记录，不允许清理异常遮住原始HTTP／断言故障；成功也必须满足清理完成。
            var cleanupErrors = new List<string>();
            try { if (host is not null) await host.DisposeAsync(); } catch (Exception error) { cleanupErrors.Add("宿主停止：" + error.GetType().Name); }
            try { await TestDirectoryCleanup.DeleteAsync(credentials, credentialsRoot); } catch (Exception error) { cleanupErrors.Add("本轮凭据：" + error.GetType().Name); }
            try
            {
                await File.WriteAllTextAsync(Path.Combine(evidence, "verified.json"), JsonSerializer.Serialize(new { passed = passed && cleanupErrors.Count == 0, stage, primaryErrorType = primaryFailure?.GetType().Name, cleanupErrors, realWeb, realOAuth = bindings > 0, realCli = bindings > 0, deviceBindings = bindings, appliedProjects = applied.Count, originalBatchRoute = queueRejectedSuccess, actualRuntimeWrites = runtimeWrites, backendOnlyPreservedFrontend = frontendPreserved, queuedWasNotSuccess = queueRejectedSuccess, unboundFailed, disabledSkipped, restartOriginalBatch = restartBatch, browserExecuted = browserMode && passed && cleanupErrors.Count == 0, hardwareExecuted = false, performanceExecuted = false }));
            }
            catch when (primaryFailure is not null) { /* 原始故障仍由TRX保留；外层固定目录清理继续执行。 */ }
            if (primaryFailure is null && cleanupErrors.Count != 0) throw new InvalidOperationException("设备批量收尾未完成，见本轮脱敏记录。");
        }
    }

    /// <summary>HTTP与真实浏览器共用逐字文件核验，不能以服务器applied标签代替本机真实写入。</summary>
    private static async Task AuditUpdatedTargetsAsync(string source, string[] targets, SortedDictionary<string, string>[] before)
    {
        for (var index = 0; index < 2; index++)
        {
            var after = Snapshot(targets[index]); const string runtimePath = "src/Metarill.Security/AdminAccountInitializationBusiness.cs";
            Assert.NotEqual(before[index][runtimePath], after[runtimePath]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(source, "backend", runtimePath)))), after[runtimePath]); Assert.Equal("本轮手写文件必须保持原样", await File.ReadAllTextAsync(Path.Combine(targets[index], "业务手写保留.txt")));
            if (index == 1) Assert.Equal(before[index].Where(file => file.Key.StartsWith("frontend/", StringComparison.Ordinal)).ToArray(), after.Where(file => file.Key.StartsWith("frontend/", StringComparison.Ordinal)).ToArray());
        }
    }

    /// <summary>固定项目逐项核验；不能把排队、未绑定或禁用状态合并成整体成功。</summary>
    internal static void AssertItems(JsonElement result, string[] projects, string status, bool includeUnbound)
    {
        var items = result.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(includeUnbound ? 4 : 3, items.Length);
        foreach (var project in projects) Assert.Equal(status, Assert.Single(items, item => item.GetProperty("projectId").GetString() == project).GetProperty("status").GetString());
        Assert.Equal("skipped", Assert.Single(items, item => item.GetProperty("projectId").GetString() == DeviceRuntimeNativePreparation.DisabledProject).GetProperty("status").GetString());
        if (includeUnbound) { var unbound = Assert.Single(items, item => item.GetProperty("projectId").GetString() == DeviceRuntimeNativePreparation.UnboundProject); Assert.Equal("failed", unbound.GetProperty("status").GetString()); Assert.Equal("approved_runtime_binding_missing", unbound.GetProperty("errorCode").GetString()); Assert.Null(unbound.GetProperty("requestId").GetString()); }
    }

    /// <summary>目标工程按相对路径固定排序，仅在本机比较摘要，不输出源码或配置。</summary>
    private static SortedDictionary<string, string> Snapshot(string root) => new(Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))) , StringComparer.Ordinal);

    /// <summary>只读固定计数SQL，随机库及账号已经由专用配置验证。</summary>
    private static async Task<long> Scalar(DatabaseSession session, string sql)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
