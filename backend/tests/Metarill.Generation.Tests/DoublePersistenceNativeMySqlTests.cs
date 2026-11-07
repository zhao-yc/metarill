using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>缺专属设置明确跳过；有设置仍须先验证批准、用途和固定包，不借通用实库开关。</summary>
public sealed class DoublePersistenceNativeFactAttribute : FactAttribute
{
    /// <summary>发现测试时只检查专属本地路径，不读取配置或建立连接。</summary>
    public DoublePersistenceNativeFactAttribute() { if (!File.Exists(Environment.GetEnvironmentVariable(DoublePersistenceNativeLease.EnvironmentKey))) Skip = "数值正式持久化未配置本轮独占新库授权。"; }
}

/// <summary>当前公开Full包的真实Web／MySQL数值闭环，未授权时不能创建工程、连接或启动宿主。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed partial class DoublePersistenceNativeMySqlTests
{
    private const int Worker = 971;
    private const long Project = 54, First = 9007199254751000;
    private const string Party = DoublePersistenceNativePreparation.Prefix + "_party", Source = DoublePersistenceNativePreparation.Prefix + "_order", Group = DoublePersistenceNativePreparation.Prefix + "_group";
    private const string PartyRoute = "/api/admin/auto/" + Party + "/201/", SourceRoute = "/api/admin/auto/" + Source + "/101/";

    /// <summary>显式安装／默认反查／原CRUD及数值事务；只消费固定新库租约，远端清理由外层协调器负责。</summary>
    [DoublePersistenceNativeFact]
    public async Task Production_web_double_defaults_crud_filters_statistics_excel_and_rollback_keep_binary_values()
    {
        var settingsPath = Environment.GetEnvironmentVariable(DoublePersistenceNativeLease.EnvironmentKey)!;
        Assert.Equal(Path.GetFullPath(settingsPath), Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath)); var settings = document.RootElement; var root = NonRedundantApplicationFixture.Root();
        var run = DoublePersistenceNativeLease.Validate(settings, CSharpGenerator.SafePath(root, DoublePersistenceNativePreparation.Root + "/prepared"));
        // 原SheetJS解析器必须在固定来源下准备；缺失时在复制、进程和连接前拒绝。
        if (!File.Exists(CSharpGenerator.SafePath(root, "frontend/metarill_uni_admin/node_modules/xlsx/xlsx.mjs"))) throw new InvalidOperationException("数值验收缺少本轮固定原SheetJS依赖。");
        var proof = settings.GetProperty("DoublePersistencePreparation").Deserialize<GlobalCachedPackageProof>()!;
        var catalog = DoublePersistenceNativePreparation.Verify(proof); var options = FreshInstallConfiguration.Read("AgentDatabase", "agent");
        var evidence = CSharpGenerator.SafePath(root, DoublePersistenceNativePreparation.Root + "/" + run + "/evidence"); if (Directory.Exists(evidence)) throw new InvalidOperationException("数值本轮目录已存在，不自动重跑。"); Directory.CreateDirectory(evidence);
        var copied = DoublePersistenceNativePreparation.CopyForExecution(proof, CSharpGenerator.SafePath(root, DoublePersistenceNativePreparation.Root + "/" + run + "/execution/application"));
        var hostDirectory = CSharpGenerator.SafePath(copied.PackageRoot, "backend/Application.Host"); var storageBoundary = Path.Combine(evidence, "storage"); var storage = Path.Combine(storageBoundary, Guid.NewGuid().ToString("N")); var hooks = Path.Combine(evidence, "transaction-hooks.jsonl");
        using var session = new DatabaseSession(options); await Pin(session, settings.GetProperty("InstancePin")); var store = new SqlRowStore(session, catalog);
        var securityCatalog = await Model("security"); var security = new SqlRowStore(session, securityCatalog); var fileCatalog = await Model("files");
        var physical = MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(catalog, securityCatalog), fileCatalog), AgentFoundationCatalog.ForApplication(catalog));
        var ownerName = "double-native-owner"; var ownerPassword = Guid.NewGuid().ToString("N"); var readerPassword = Guid.NewGuid().ToString("N");
        FreshHost? host = null; HttpClient? client = null; var passed = false; var stage = "空库启动"; string? token = null;
        // 秘密只进入本轮真实宿主环境；普通启动不自动DDL，结构动作必须显式传入。
        ProcessStartInfo Info(string? command = null)
        {
            var info = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory); info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory, "bin/Release/net10.0/Application.Host.dll")); if (command is not null) info.ArgumentList.Add(command);
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; info.Environment["DOTNET_ENVIRONMENT"] = "Production"; info.Environment["Logging__LogLevel__Default"] = "Warning";
            info.Environment["Database__Provider"] = "mysql"; info.Environment["Database__ExpectedDatabase"] = options.ExpectedDatabase; info.Environment["Database__ConnectionString"] = options.ConnectionString;
            info.Environment["Authentication__Mode"] = "Web"; info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false"; info.Environment["Authentication__Bootstrap__Username"] = ownerName; info.Environment["Authentication__Bootstrap__Password"] = ownerPassword;
            info.Environment["Ids__WorkerId"] = Worker.ToString(CultureInfo.InvariantCulture); info.Environment["Agent__VerificationUri"] = "https://agent.example/#/pages/custom/agent_authorize";
            info.Environment["Files__RootDirectory"] = Path.Combine(storage, "uploads"); info.Environment["Exports__RootDirectory"] = Path.Combine(storage, "exports"); info.Environment["Files__LegacyPublicBaseUrl"] = "";
            info.Environment["METARILL_DOUBLE_PERSISTENCE_HOOK_RECEIPT"] = hooks; info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = evidence; return info;
        }
        try
        {
            Assert.Equal(0, await TableCount(session)); host = await FreshHost.StartAsync(Info(), "/health"); Assert.Equal(0, await TableCount(session)); await host.DisposeAsync(); host = null;
            stage = "显式预览／重复安装"; await Run("--schema-preview"); Assert.Equal(0, await TableCount(session)); await Run("--schema-apply"); await Run("--schema-apply"); Assert.Equal(15, FreshInstallConfiguration.ExpectedFullTableCount(catalog)); Assert.Equal(15, await TableCount(session)); Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);
            stage = "真实默认值及反查"; await VerifyPhysicalDefaults(session, catalog, store);
            stage = "真实账号及独立导航许可"; await Run("--initialize-security"); var passwordHash = SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager", Row("name", ownerName))), "password"); Assert.True(PasswordHasher.Verify(ownerPassword, passwordHash)); await Run("--initialize-security"); Assert.Equal(passwordHash, SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager", Row("name", ownerName))), "password"));
            var ids = new SnowflakeIdGenerator(Worker + 1); var role = ids.NextId(); var manager = ids.NextId(); var pageGrants = new Dictionary<string, long>(StringComparer.Ordinal);
            await session.InTransactionAsync(async () =>
            {
                await security.ReadForUpdateAsync("yz_project", Row("id", Project));
                await security.InsertAsync("yz_role", Row("id", role, "yz_project_id", Project, "yz_admin_type_id", 5L, "name", "double-native-reader", "is_super", 0, "is_built_in", 0));
                await security.InsertAsync("yz_manager", Row("id", manager, "yz_project_id", Project, "yz_admin_type_id", 5L, "name", "double-native-reader", "password", PasswordHasher.Hash(readerPassword), "is_disabled", 0, "yz_role_ids", JsonSerializer.Serialize(new[] { Id(role) })));
                foreach (var page in catalog.ConfigPages) { var id = ids.NextId(); pageGrants.Add(page.Id, id); await security.InsertAsync("yz_role_permission", Row("id", id, "yz_project_id", Project, "yz_role_id", role, "config_page_id", long.Parse(page.Id, CultureInfo.InvariantCulture), "permission_values", "[\"is_all\"]")); }
                foreach (var menu in catalog.ConfigMenus) await security.InsertAsync("yz_role_permission", Row("id", ids.NextId(), "yz_project_id", Project, "yz_role_id", role, "config_menu_id", long.Parse(menu.Id, CultureInfo.InvariantCulture), "permission_values", "[]"));
                return true;
            });
            host = await FreshHost.StartAsync(Info(), "/health"); await Login(host.Client, ownerName, ownerPassword); client = new() { BaseAddress = host.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(90) }; token = await Login(client, "double-native-reader", readerPassword);
            var menuResponse = await Send(client, HttpMethod.Get, "/api/admin/sys/yz_manager/getMenu"); Assert.False(menuResponse.GetProperty("permissions").GetProperty("isSuper").GetBoolean()); var menus = menuResponse.GetProperty("menu").EnumerateArray().ToArray(); Assert.Equal(3, menus.Length); Assert.All(menus, menu => { Assert.Equal("54", menu.GetProperty("projectId").GetString()); Assert.Equal("5", menu.GetProperty("adminTypeId").GetString()); });
            stage = "普通及非冗余数值闭环"; await Exercise(client, session, store, catalog, security, pageGrants, hooks, evidence);
            stage = "同地址重启及注销"; var address = host.Client.BaseAddress; await host.DisposeAsync(); host = null; host = await FreshHost.StartAsync(Info(), "/health", address); host.Client.DefaultRequestHeaders.Add("loginStateId", token);
            await Send(host.Client, HttpMethod.Post, PartyRoute + "page", new PageRequest { Filters = new() { ["name"] = JsonSerializer.SerializeToElement("ordinary-default") } });
            await Send(host.Client, HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login"); await Send(host.Client, HttpMethod.Post, PartyRoute + "page", new { }, HttpStatusCode.Unauthorized);
            Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes); DoublePersistenceNativePreparation.Verify(copied, true); DoublePersistenceNativePreparation.Verify(proof);
            foreach (var table in catalog.ConfigTables) Assert.InRange((await store.ReadIncludingDeletedAsync(table.TableName)).Count, 0, DoublePersistenceNativeLease.MaximumBusinessRows);
            passed = true; await Receipt("verified", new { run, passed, productionWeb = true, ordinaryAccount = true, originalFormThreeFields = true, persistentSession = true, tableCount = 14, publicFullApplication = true, fixedAuthorizationInjected = false, browserExecuted = false, performanceExecuted = false });
        }
        catch (Exception error) { await Receipt("failure", new { run, stage, passed = false, errorType = error.GetType().Name, error.StackTrace, databaseCleanupByLease = true }); throw; }
        finally
        {
            client?.Dispose(); if (host is not null) await host.DisposeAsync();
            if (Directory.Exists(storage)) await TestDirectoryCleanup.DeleteAsync(storage, storageBoundary);
            await Receipt("local-cleanup", new { run, stopped = true, storageRemoved = !Directory.Exists(storage), databaseCleanupByLease = true });
        }
        async Task<MetadataCatalog> Model(string name) => JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(copied.PackageRoot, "backend/metadata/" + name + ".json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Task Run(string command) => RunOwned(Info(command), Path.Combine(evidence, command.TrimStart('-') + "-" + Guid.NewGuid().ToString("N") + ".log"), options.ConnectionString, ownerPassword, readerPassword);
        Task Receipt(string name, object value) => File.WriteAllTextAsync(Path.Combine(evidence, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>数值请求用当前公开JSON选项编码，防止测试客户端默认格式本身先合并相邻值。</summary>
    private static Task<JsonElement> Send(HttpClient client, HttpMethod method, string route, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        => FreshWebHttp.SendAsync(client, method, route, body is null or HttpContent ? body : new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json"), expected);
    /// <summary>成功HTTP与R双重核对后才取业务data。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string route, object body) => (await Send(client, HttpMethod.Post, route, body)).GetProperty("data");
    /// <summary>真正原三字段登录，不以生成测试Actor替代账号身份。</summary>
    private static async Task<string> Login(HttpClient client, string name, string password)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = name, ["password"] = password, ["captchaVerification"] = "" });
        var result = await Send(client, HttpMethod.Post, "/api/admin/sys/yz_manager/login", form); var token = result.GetProperty("map").GetProperty("loginStateId").GetString()!; Assert.Equal(64, token.Length); client.DefaultRequestHeaders.Remove("loginStateId"); client.DefaultRequestHeaders.Add("loginStateId", token); return token;
    }
    /// <summary>实例核验仅SELECT系统变量，不读取其它业务记录。</summary>
    private static async Task Pin(DatabaseSession session, JsonElement pin)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var query = connection.CreateCommand(); query.CommandText = "SELECT DATABASE(),@@hostname,@@server_uuid"; await using var reader = await query.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal(session.Options.ExpectedDatabase, reader.GetString(0)); Assert.Equal(pin.GetProperty("hostname").GetString(), reader.GetString(1)); Assert.Equal(pin.GetProperty("serverUuid").GetString(), reader.GetString(2));
    }
    /// <summary>仅统计当前独占库，不能遍历其它业务库。</summary>
    private static async Task<int> TableCount(DatabaseSession session) { await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var query = connection.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"; return Convert.ToInt32(await query.ExecuteScalarAsync(), CultureInfo.InvariantCulture); }
    /// <summary>显式CLI命令单次执行，日志只保存脱敏结果，超时不重放。</summary>
    private static async Task RunOwned(ProcessStartInfo start, string path, params string[] secrets)
    {
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var expired = false;
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { expired = true; if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
        var text = await stdout + "\n" + await stderr; var parsed = new MySqlConnectionStringBuilder(start.Environment["Database__ConnectionString"]!);
        foreach (var secret in secrets.Append(parsed.Password).Where(value => !string.IsNullOrEmpty(value)).OrderByDescending(value => value.Length)) text = text.Replace(secret, "[已脱敏]", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, text); if (expired) throw new TimeoutException("数值显式命令超时，首因脱敏日志已保留，不自动重放。"); Assert.True(process.ExitCode == 0, "数值显式命令失败，脱敏证据保留。");
    }
    /// <summary>只构造固定字段的参数行，不拼接外部SQL。</summary>
    private static Dictionary<string, object?> Row(params object?[] values) { var row = new Dictionary<string, object?>(StringComparer.Ordinal); for (var index = 0; index < values.Length; index += 2) row.Add((string)values[index]!, values[index + 1]); return row; }
    /// <summary>业务ID始终按不变区域转为原字符串协议。</summary>
    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
    /// <summary>逐位比较，不以近似误差掩盖相邻有限数合并。</summary>
    private static void Bits(double expected, object? actual) => Assert.Equal(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(actual)));
}