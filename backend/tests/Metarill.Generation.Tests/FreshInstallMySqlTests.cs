using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

/// <summary>只有专属空库验收脚本提供的随机库配置才执行，普通 CI 不假称首次安装成功。</summary>
public sealed class FreshInstallMySqlFactAttribute : FactAttribute
{
    public FreshInstallMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")))
            Skip = "未通过专属脚本创建全新 Server-200 验收库，首次安装验收未执行。";
    }
}

/// <summary>两个场景使用各自独占的随机新库，不把已有基础表复用计作首次安装。</summary>
public sealed class FreshInstallMySqlTests(ITestOutputHelper output)
{
    /// <summary>普通启动和预览都保持0表，包内显式 apply 从零安装文件基础设施及业务表。</summary>
    [FreshInstallMySqlFact]
    public async Task Standalone_cli_installs_files_and_business_from_an_empty_database()
    {
        var options = FreshInstallConfiguration.Read("PackagedDatabase", "package");
        using (var initial = new DatabaseSession(options)) Assert.Equal(0L, await CountTablesAsync(initial));
        await using var fixture = await PackagedFilesFixture.CreateAsync(options);
        Assert.Empty(fixture.ExistingFileTables);
        await fixture.BuildAsync();
        await fixture.StartAsync(); Assert.Equal(0L, await CountTablesAsync(fixture.Session)); await fixture.StopAsync();
        await fixture.SchemaAsync("--schema-preview"); Assert.Equal(0L, await CountTablesAsync(fixture.Session));
        var filesBefore = await new SchemaService(fixture.Session).PreviewAsync(fixture.FileCatalog);
        Assert.Equal(3, filesBefore.Changes.Count(change => change.Kind == "createTable"));
        var securityBefore=await new SchemaService(fixture.Session).PreviewAsync(fixture.SecurityCatalog);
        Assert.Equal(6,securityBefore.Changes.Count(change=>change.Kind=="createTable"));
        await fixture.SchemaAsync("--schema-apply");
        Assert.Equal(10L, await CountTablesAsync(fixture.Session));
        Assert.Empty((await new SchemaService(fixture.Session).PreviewAsync(fixture.FileCatalog)).Changes);
        Assert.Empty((await new SchemaService(fixture.Session).PreviewAsync(fixture.Catalog)).Changes);
        Assert.Empty((await new SchemaService(fixture.Session).PreviewAsync(fixture.SecurityCatalog)).Changes);
        await fixture.SchemaAsync("--schema-apply"); Assert.Equal(10L, await CountTablesAsync(fixture.Session));
        await fixture.InitializeWebSecurityAsync();
        var credentials=await ScalarAsync(fixture.Session,"SELECT password FROM yz_manager WHERE name='files-probe'");
        await fixture.InitializeWebSecurityAsync();Assert.Equal(credentials,await ScalarAsync(fixture.Session,"SELECT password FROM yz_manager WHERE name='files-probe'"));
        await fixture.StartAsync(); await fixture.LoginAsync();
        var permissions=await fixture.SuccessAsync(HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");Assert.True(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());
        var menu=await fixture.SuccessAsync(HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu");Assert.NotEmpty(menu.GetProperty("menu").EnumerateArray());
        var bytes = Encoding.UTF8.GetBytes("全新随机库首次安装后的文件与业务验收");
        var file = await fixture.UploadAsync("fresh-install.txt", bytes);
        var added = await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/add", new { name = "首次安装业务", attachments = new[] { file.ToString() } });
        var record = long.Parse(added.GetProperty("data").GetProperty("id").ToString()); fixture.RecordIds.Add(record);
        await fixture.AssertReferenceAsync(record, file); await fixture.AssertContentAsync(file, bytes, publicly: false);
        output.WriteLine("独立应用：0表启动/预览均无DDL，显式CLI安装10表（业务1+文件3+安全6），二次无差异，真实Web初始化重复不重置、登录/权限/菜单及文件引用/下载成功。");
    }

    /// <summary>平台首次安装必须创建完整元数据表、内置元数据和管理员，重复初始化不重置密码。</summary>
    [FreshInstallMySqlFact]
    public async Task Platform_initialize_bootstraps_an_empty_database_and_is_idempotent()
    {
        var root = SourceRoot();
        var platform = FreshInstallConfiguration.Read("Database", "platform");
        var business = FreshInstallConfiguration.Read("BusinessDatabase", "business");
        using var platformSession = new DatabaseSession(platform); using var businessSession = new DatabaseSession(business);
        Assert.Equal(0L, await CountTablesAsync(platformSession)); Assert.Equal(0L, await CountTablesAsync(businessSession));
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "metarill-fresh-bootstrap");
        var directory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var password = Guid.NewGuid().ToString("N"); var alternatePassword = Guid.NewGuid().ToString("N");
        var settings = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new { Metarill = new { Database = platform, BusinessDatabase = business, WorkerId = 854, Bootstrap = new { Username = "fresh-admin", Password = password } } }));
        var assembly = Path.Combine(root, "backend/src/Metarill.Api/bin/Debug/net10.0/Metarill.Api.dll");
        Assert.True(File.Exists(assembly), "请使用 fresh-mysql.ps1 先构建当前平台宿主。");
        ProcessStartInfo StartInfo(string? operation = null)
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = root, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(assembly); if (operation is not null) start.ArgumentList.Add(operation);
            start.Environment["METARILL_SETTINGS"] = settings; start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            return start;
        }
        try
        {
            await using (var ordinary = await FreshHost.StartAsync(StartInfo(), "/api/runtime/health/check"))
            {
                Assert.Equal(0L, await CountTablesAsync(platformSession)); Assert.Equal(0L, await CountTablesAsync(businessSession));
            }
            var catalog = await CatalogAsync(Path.Combine(root, "backend/metadata/platform.json"));
            var demo = await CatalogAsync(Path.Combine(root, "backend/metadata/demo.json"));
            var businessCatalog = MetadataCatalogComposition.MergePhysical(demo, await CatalogAsync(Path.Combine(root, "backend/metadata/files.json")));
            var schema = new SchemaService(platformSession); var initial = await schema.PreviewAsync(catalog);
            Assert.Equal(catalog.ConfigTables.Count, initial.Changes.Count(change => change.Kind == "createTable"));
            Assert.Equal(0L, await CountTablesAsync(platformSession));
            await RunAsync(StartInfo("--initialize"));
            Assert.Equal(catalog.ConfigTables.Count, await CountTablesAsync(platformSession));
            Assert.Equal(businessCatalog.ConfigTables.Count, await CountTablesAsync(businessSession));
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            Assert.Empty((await new SchemaService(businessSession).PreviewAsync(businessCatalog)).Changes);
            var expected = new Dictionary<string, int> { ["config_table"] = catalog.ConfigTables.Count + demo.ConfigTables.Count, ["config_column"] = catalog.ConfigColumns.Count + demo.ConfigColumns.Count, ["config_page"] = catalog.ConfigPages.Count + demo.ConfigPages.Count, ["config_page_column"] = catalog.ConfigPageColumns.Count + demo.ConfigPageColumns.Count };
            foreach (var (table, count) in expected) Assert.Equal(count, Convert.ToInt32(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `" + table + "` WHERE `delete_no` IS NULL")));
            Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `yz_project` WHERE `delete_no` IS NULL")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `yz_manager` WHERE `delete_no` IS NULL")));
            var hash = await ScalarAsync(platformSession, "SELECT `password` FROM `yz_manager` WHERE `id`=1");
            var repeated = StartInfo("--initialize"); repeated.Environment["Metarill__Bootstrap__Password"] = alternatePassword;
            await RunAsync(repeated);
            Assert.Equal(hash, await ScalarAsync(platformSession, "SELECT `password` FROM `yz_manager` WHERE `id`=1"));
            foreach (var (table, count) in expected) Assert.Equal(count, Convert.ToInt32(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `" + table + "` WHERE `delete_no` IS NULL")));
            await using var host = await FreshHost.StartAsync(StartInfo(), "/api/runtime/health/check");
            using var login = await host.Client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["username"] = "fresh-admin", ["password"] = password }));
            var identity = await login.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(200, identity.GetProperty("code").GetInt32());
            host.Client.DefaultRequestHeaders.Add("loginStateId", identity.GetProperty("map").GetProperty("loginStateId").GetString());
            var permissions = await host.Client.GetFromJsonAsync<JsonElement>("/api/admin/sys/yz_manager/getCurrentPermissions");
            Assert.Equal(200, permissions.GetProperty("code").GetInt32()); Assert.True(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());
            var menu = await host.Client.GetFromJsonAsync<JsonElement>("/api/admin/sys/yz_manager/getMenu"); Assert.Equal(200, menu.GetProperty("code").GetInt32()); Assert.NotEmpty(menu.GetProperty("menu").EnumerateArray());
            output.WriteLine($"平台：普通启动保持0表，显式初始化创建{catalog.ConfigTables.Count}表并播种元数据，业务库创建{businessCatalog.ConfigTables.Count}表；重复初始化无重复且密码未重置，真实登录/权限/菜单通过。");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, temporaryRoot); }
    }

    /// <summary>只读完整数据库表数，证明不存在隐藏复用的基础表。</summary>
    private static async Task<long> CountTablesAsync(DatabaseSession session) => Convert.ToInt64(await ScalarAsync(session, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
    /// <summary>只接受测试内固定SQL；查询前由DatabaseSession核验目标库。</summary>
    private static async Task<object?> ScalarAsync(DatabaseSession session, string sql)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
    /// <summary>验收结构读取当前提交元数据，不手写任何产品表DDL。</summary>
    private static async Task<MetadataCatalog> CatalogAsync(string path) => JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>只消费子进程日志，避免错误信息携带环境配置。</summary>
    private static async Task RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        await stdout; await stderr; Assert.True(process.ExitCode == 0, "首次初始化命令失败，日志不回显配置。");
    }
    /// <summary>默认使用本普通克隆作为源码根。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}

/// <summary>凭据只存在于脚本的临时私有文件，名称、账号和目标服务器必须严格匹配本轮编号。</summary>
internal static class FreshInstallConfiguration
{
    /// <summary>从专属验收配置读取并验证一个随机库。</summary>
    public static DatabaseOptions Read(string property, string suffix)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var options = config.RootElement.GetProperty(property).Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Validate(options, suffix); return options;
    }
    /// <summary>禁止误连固定业务库、生产服务器或高权限账号。</summary>
    public static void Validate(DatabaseOptions options, string suffix)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var run = config.RootElement.GetProperty("RunId").GetString()!;
        if (!Regex.IsMatch(run, "^[a-f0-9]{16}$") || options.Provider != "mysql" || options.ExpectedDatabase != "metarill_fresh_" + run + "_" + suffix) throw new InvalidOperationException("首次安装只允许本轮随机新库。");
        var builder = new DbConnectionStringBuilder { ConnectionString = options.ConnectionString };
        if (builder["server"].ToString() is not ("deve-server.yinzon.com" or "192.168.31.200") || builder["user id"].ToString() != "mtf_" + run) throw new InvalidOperationException("首次安装只允许Server-200临时受限账号。");
        DatabaseProviders.Resolve(options.Provider).Validate(options);
    }
}

/// <summary>仅拥有本 fixture 创建的随机回环宿主，不影响用户开发服务。</summary>
internal sealed class FreshHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    public HttpClient Client { get; }
    private FreshHost(Process process, Uri address) { this.process = process; stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync(); Client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) }; }
    /// <summary>取得临时端口并启动实际平台二进制，等待健康响应。</summary>
    public static async Task<FreshHost> StartAsync(ProcessStartInfo info, string health)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var address = new Uri($"http://127.0.0.1:{port}"); info.ArgumentList.Add("--urls"); info.ArgumentList.Add(address.ToString());
        var host = new FreshHost(Process.Start(info)!, address);
        for (var attempt = 0; attempt < 150 && !host.process.HasExited; attempt++)
        {
            try { using var response = await host.Client.GetAsync(health); if (response.IsSuccessStatusCode) return host; } catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        await host.DisposeAsync(); throw new InvalidOperationException("首次安装验收宿主未就绪，日志不回显配置。");
    }
    /// <summary>退出宿主后等待日志管道结束，释放DLL占用。</summary>
    public async ValueTask DisposeAsync() { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await stdout; await stderr; process.Dispose(); Client.Dispose(); }
}
