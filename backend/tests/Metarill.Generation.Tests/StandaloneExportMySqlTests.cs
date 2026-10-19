using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>仅在显式提供隔离业务库配置时运行真实独立包验收。</summary>
public sealed class StandaloneExportMySqlFactAttribute : FactAttribute
{
    public StandaloneExportMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")))
            Skip = "未提供 METARILL_TEST_SETTINGS，独立包真实 MySQL 导出尚未执行。";
    }
}

/// <summary>只操作随机独占表，从真实生成页新增、筛选并导出，不使用固定投影替身。</summary>
public sealed class StandaloneExportMySqlTests
{
    [StandaloneExportMySqlFact]
    public async Task Packaged_generated_page_exports_filtered_mysql_rows_and_rejects_overflow()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase")
            .Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test")
            throw new InvalidOperationException("独立包导出测试仅允许 MySQL 的 metarill_business_test 库。");
        // DatabaseSession 还会核对连接串实际 Database，SchemaService 再核对服务器返回的目标库。
        using var session = new DatabaseSession(options);
        var root = SourceRoot();
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(root, "backend/metadata/demo.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var table = "mt_test_export_" + Guid.NewGuid().ToString("N");
        Assert.Single(catalog.ConfigTables).TableName = table;
        var page = Assert.Single(catalog.ConfigPages);
        var temporary = Path.Combine(Path.GetTempPath(), "metarill-mysql-export", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(temporary, "application");
        var cleanupTable = false;
        Process? host = null; Task<string>? stdout = null; Task<string>? stderr = null;
        try
        {
            var schema = new SchemaService(session);
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            cleanupTable = true;
            var plan = await schema.PreviewAsync(catalog);
            Assert.Equal("metarill_business_test", plan.Database);
            Assert.Single(plan.Changes);
            Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            new ApplicationPackager().Create(catalog, output, "Example.DatabaseExport", root);
            Assert.False(output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            // 独立包真实 restore/build，不向原仓库项目引用回退。
            foreach (var operation in new[] { "restore", "build" })
            {
                var build = StartInfo(output);
                foreach (var argument in new[] { operation, "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet" }) build.ArgumentList.Add(argument);
                if (operation == "build") build.ArgumentList.Add("--no-restore");
                using var process = Process.Start(build)!;
                var buildOut = process.StandardOutput.ReadToEndAsync(); var buildError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
                await buildOut; await buildError;
                Assert.True(process.ExitCode == 0, "独立包 restore/build 失败，未输出进程日志以保护环境信息。");
            }
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var hostDirectory = Path.Combine(output, "backend/Application.Host");
            var start = StartInfo(hostDirectory);
            start.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Debug/net10.0/Application.Host.dll"));
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["Database__Provider"] = "mysql";
            start.Environment["Database__ExpectedDatabase"] = options.ExpectedDatabase;
            start.Environment["Database__ConnectionString"] = options.ConnectionString;
            start.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "true";
            start.Environment["Authentication__DevelopmentBootstrap__Username"] = "mysql-export-probe";
            var password = Guid.NewGuid().ToString("N");
            start.Environment["Authentication__DevelopmentBootstrap__Password"] = password;
            var exportRoot = Path.Combine(output, ".local/exports");
            start.Environment["Exports__RootDirectory"] = exportRoot;
            start.Environment["Exports__MaxRows"] = "1";
            host = Process.Start(start)!;
            stdout = host.StandardOutput.ReadToEndAsync(); stderr = host.StandardError.ReadToEndAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
            var ready = false;
            for (var attempt = 0; attempt < 150 && !host.HasExited; attempt++)
            {
                try { using var health = await client.GetAsync("/health"); if (health.IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Assert.True(ready, "独立包真实数据库宿主未就绪。");
            using var login = await client.PostAsJsonAsync("/api/admin/sys/yz_manager/login", new { username = "mysql-export-probe", password });
            login.EnsureSuccessStatusCode();
            var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
            var sessionId = loginBody.GetProperty("map").GetProperty("loginStateId").GetString()!;
            Assert.False(sessionId.Contains('.')); // 开发认证沿用不透明会话，不能把票据当 JWT 使用。
            client.DefaultRequestHeaders.Add("loginStateId", sessionId);
            var route = $"/api/admin/auto/{table}/{page.Id}";
            foreach (var name in new[] { "独立导出保留记录", "独立导出排除记录" })
            {
                using var added = await client.PostAsJsonAsync(route + "/add", new { name, notes = "真实数据库验收", amount = 12.34, isEnabled = 1 });
                added.EnsureSuccessStatusCode();
                Assert.Equal(200, (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetInt32());
            }
            using var export = await client.PostAsJsonAsync(route + "/initExport", new { filters = new { name = "独立导出保留记录" } });
            export.EnsureSuccessStatusCode();
            var exportBody = await export.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(200, exportBody.GetProperty("code").GetInt32());
            var downloadPath = "/api/admin/sys/export/export?exportId=" + exportBody.GetProperty("data").GetString();
            using var download = await client.GetAsync(downloadPath); download.EnsureSuccessStatusCode();
            using (var archive = new ZipArchive(new MemoryStream(await download.Content.ReadAsByteArrayAsync())))
            {
                using var stringStream = archive.GetEntry("xl/sharedStrings.xml")!.Open();
                var strings = await XDocument.LoadAsync(stringStream, LoadOptions.None, CancellationToken.None);
                var values = strings.Descendants().Where(element => element.Name.LocalName == "si").Select(element => string.Concat(element.Descendants().Where(child => child.Name.LocalName == "t").Select(child => child.Value))).ToArray();
                Assert.Contains("名称", values); Assert.Contains("项目", values); Assert.Contains("独立导出保留记录", values);
                Assert.DoesNotContain("独立导出排除记录", values);
                var projectIndex = Array.IndexOf(values, catalog.YzProjectId);
                Assert.True(projectIndex >= 0 && catalog.YzProjectId.Length > 15, "项目长编号必须作为完整文本导出。");
                using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                var sheet = await XDocument.LoadAsync(sheetStream, LoadOptions.None, CancellationToken.None);
                Assert.Equal(2, sheet.Descendants().Count(element => element.Name.LocalName == "row"));
                Assert.Contains(sheet.Descendants(), element => element.Name.LocalName == "c" && (string?)element.Attribute("t") == "s" && element.Elements().Any(child => child.Name.LocalName == "v" && child.Value == projectIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            var before = Directory.GetFiles(exportRoot).Order().ToArray();
            Assert.Single(before);
            using var overflow = await client.PostAsJsonAsync(route + "/initExport", new { filters = new { } });
            Assert.Equal(HttpStatusCode.InternalServerError, overflow.StatusCode);
            Assert.Equal(before, Directory.GetFiles(exportRoot).Order().ToArray());
            using var anonymous = new HttpClient { BaseAddress = client.BaseAddress };
            using var denied = await anonymous.GetAsync(downloadPath); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var logout = await client.PostAsJsonAsync("/api/admin/sys/yz_manager/exit_login", new { }); logout.EnsureSuccessStatusCode();
            using var revoked = await client.GetAsync(downloadPath); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        finally
        {
            // 先停止持有数据库连接的宿主，再且只清理本测试确认不存在过的唯一随机表。
            try
            {
                if (host is not null) { if (!host.HasExited) host.Kill(true); await host.WaitForExitAsync(); if (stdout is not null) await stdout; if (stderr is not null) await stderr; host.Dispose(); }
            }
            finally
            {
                try
                {
                    if (cleanupTable)
                    {
                        if (options.ExpectedDatabase != "metarill_business_test" || !System.Text.RegularExpressions.Regex.IsMatch(table, "^mt_test_export_[a-f0-9]{32}$")) throw new InvalidOperationException("拒绝清理非当前隔离测试表。");
                        await using var connection = session.Provider.CreateConnection(options); await connection.OpenAsync();
                        await using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE IF EXISTS " + session.Provider.QuoteIdentifier(table); await command.ExecuteNonQueryAsync();
                    }
                }
                finally { await TestDirectoryCleanup.DeleteAsync(temporary, Path.Combine(Path.GetTempPath(), "metarill-mysql-export")); }
            }
        }
    }

    /// <summary>测试进程隐藏运行并消费日志，数据库凭据仅通过宿主环境传递。</summary>
    private static ProcessStartInfo StartInfo(string directory) => new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    /// <summary>允许 CI 显式指定可信源码根，默认按测试文件位置定位普通克隆。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}
