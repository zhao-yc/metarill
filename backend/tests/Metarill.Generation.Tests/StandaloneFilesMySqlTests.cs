using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

/// <summary>仅在显式提供业务隔离库配置时执行独立文件库真实验收。</summary>
public sealed class StandaloneFilesMySqlFactAttribute : FactAttribute
{
    public StandaloneFilesMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")))
            Skip = "未提供隔离业务库配置，独立文件库 HTTP/MySQL 验收未执行。";
    }
}

/// <summary>从真实独立包 HTTP 入口验证文件与生成业务记录的原子引用，不伪装多账号权限验收。</summary>
public sealed class StandaloneFilesMySqlTests(ITestOutputHelper output)
{
    /// <summary>串联显式结构、实际文件流、生成记录事务与注销状态。</summary>
    [StandaloneFilesMySqlFact]
    public async Task Packaged_files_and_generated_records_share_real_mysql_transactions()
    {
        await using var fixture = await PackagedFilesFixture.CreateAsync();
        output.WriteLine("显式 schema 前已存在的文件基础表：" + string.Join("、", fixture.ExistingFileTables));
        await fixture.BuildAsync();
        // 正常 Web 启动不能隐式建业务表；建表只能来自包内显式 schema 命令。
        await fixture.StartAsync();
        Assert.Empty((await new SchemaService(fixture.Session).InspectAsync(fixture.Catalog)).Tables);
        await fixture.StopAsync();
        await fixture.SchemaAsync("--schema-preview");
        Assert.Empty((await new SchemaService(fixture.Session).InspectAsync(fixture.Catalog)).Tables);
        await fixture.SchemaAsync("--schema-apply");
        Assert.Empty((await new SchemaService(fixture.Session).PreviewAsync(fixture.Catalog)).Changes);
        Assert.Empty((await new SchemaService(fixture.Session).PreviewAsync(fixture.FileCatalog)).Changes);
        await fixture.StartAsync();
        await fixture.LoginAsync();

        // 包内真实旧上传入口必须能加载native依赖并返回可匿名读取的重新编码图片。
        var legacyPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAYAAAC56t6BAAAAEUlEQVR4nGP4z8DwH4QZMBgAoXkL9U3EmgcAAAAASUVORK5CYII=");
        using (var legacyForm = new MultipartFormDataContent())
        {
            legacyForm.Add(new ByteArrayContent(legacyPng), "file", "standalone-legacy.png");
            using var legacyResponse = await fixture.Client.PostAsync("/api/admin/sys/upload/uploadImage", legacyForm);
            legacyResponse.EnsureSuccessStatusCode(); var legacy = await legacyResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(200, legacy.GetProperty("code").GetInt32()); var url = legacy.GetProperty("location").GetString()!;
            Assert.Equal(url, legacy.GetProperty("data").GetProperty("url").GetString());
            var legacyId = long.Parse(Regex.Match(url, "public-content/([0-9]+)").Groups[1].Value); fixture.FileIds.Add(legacyId);
            using var publicImage = await fixture.Anonymous.GetAsync(url); publicImage.EnsureSuccessStatusCode();
            Assert.Equal("image/png", publicImage.Content.Headers.ContentType?.MediaType);
            var processed = await publicImage.Content.ReadAsByteArrayAsync(); Assert.Equal(legacyPng[..8], processed[..8]);
        }

        var bytes = Encoding.UTF8.GetBytes("独立包真实文件下载与事务验收");
        var privateFile = await fixture.UploadAsync("private.txt", bytes);
        var publicFile = await fixture.UploadAsync("public.txt", bytes, publicly: true);
        await fixture.AssertContentAsync(privateFile, bytes, publicly: false);
        await fixture.AssertContentAsync(publicFile, bytes, publicly: true);
        using (var denied = await fixture.Anonymous.GetAsync(fixture.ContentPath(privateFile, false)))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var added = await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/add", new
        {
            name = "独立文件引用记录", notes = "初始值", attachments = new[] { new { fileId = privateFile.ToString() } }
        });
        var record = long.Parse(added.GetProperty("data").GetProperty("id").ToString());
        fixture.RecordIds.Add(record);
        await fixture.AssertReferenceAsync(record, privateFile);
        using (var referenced = await fixture.Client.DeleteAsync(fixture.FileRoute + "/" + privateFile))
            Assert.Equal(HttpStatusCode.InternalServerError, referenced.StatusCode);
        Assert.Equal("ready", await fixture.ScalarAsync("SELECT `status` FROM `yz_file_library` WHERE `id`=@id", privateFile));

        await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/update", new { id = record.ToString(), notes = "省略附件保持引用" });
        await fixture.AssertReferenceAsync(record, privateFile);
        var original = await fixture.RowSnapshotAsync(record);
        var pending = await fixture.PlanAsync("pending.txt", bytes);
        var crossProject = await fixture.UploadAsync("other-project.txt", bytes);
        // 只修改本 fixture 创建的独占文件行，验证真实跨项目数据拒绝；不使用请求头伪造身份。
        await fixture.ExecuteAsync("UPDATE `yz_file_library` SET `yz_project_id`=999999 WHERE `id`=@id", crossProject);
        foreach (var invalidFile in new[] { pending, crossProject, publicFile, long.MaxValue })
        {
            using var invalid = await fixture.Client.PostAsJsonAsync(fixture.RecordRoute + "/update", new
            {
                id = record.ToString(), notes = "此值必须回滚", attachments = new[] { invalidFile.ToString() }
            });
            Assert.False(invalid.IsSuccessStatusCode);
            Assert.Equal(original, await fixture.RowSnapshotAsync(record));
            await fixture.AssertReferenceAsync(record, privateFile);
        }
        await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/update", new { id = record.ToString(), attachments = (object?)null });
        Assert.Equal(0L, await fixture.ActiveReferencesAsync(record));
        Assert.Null(await fixture.ScalarAsync($"SELECT `attachments` FROM `{fixture.Table}` WHERE `id`=@id", record));
        await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/update", new { id = record.ToString(), attachments = new[] { privateFile.ToString() } });
        await fixture.AssertReferenceAsync(record, privateFile);
        await fixture.SuccessAsync(HttpMethod.Delete, fixture.RecordRoute, new { id = record.ToString() });
        Assert.NotNull(await fixture.ScalarAsync($"SELECT `delete_no` FROM `{fixture.Table}` WHERE `id`=@id", record));
        Assert.Equal(0L, await fixture.ActiveReferencesAsync(record));
        Assert.Equal("ready", await fixture.ScalarAsync("SELECT `status` FROM `yz_file_library` WHERE `id`=@id", privateFile));
        var deleted = await fixture.SuccessAsync(HttpMethod.Delete, fixture.FileRoute + "/" + privateFile);
        Assert.True(deleted.GetProperty("data").GetProperty("storageRemoved").GetBoolean());
        using var unavailable = await fixture.Client.GetAsync(fixture.ContentPath(privateFile, false));
        Assert.Equal(HttpStatusCode.Unauthorized, unavailable.StatusCode);
        var logoutProbe = await fixture.UploadAsync("logout-private.txt", bytes);
        await fixture.AssertContentAsync(logoutProbe, bytes, publicly: false);
        await fixture.SuccessAsync(HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login", new { });
        using (var revokedList = await fixture.Client.PostAsJsonAsync(fixture.FileRoute + "/list", new { }))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedList.StatusCode);
        using (var revokedPlan = await fixture.Client.PostAsJsonAsync(fixture.FileRoute + "/upload-plan", new { originalName = "revoked.txt", sizeBytes = 1, configColumnId = fixture.Catalog.ConfigColumns.Single(c => c.ColumnName == "attachments").Id }))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedPlan.StatusCode);
        using (var revokedPrivate = await fixture.Client.GetAsync(fixture.ContentPath(logoutProbe, false)))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedPrivate.StatusCode);
        await fixture.AssertContentAsync(publicFile, bytes, publicly: true);
    }
}

/// <summary>凭据仅通过子进程环境传递，固定文件基础表只复用，不 DROP 或批量清理。</summary>
internal sealed class PackagedFilesFixture : IAsyncDisposable
{
    private const string TemporaryRootName = "metarill-mysql-files";
    private readonly string temporary = Path.Combine(Path.GetTempPath(), TemporaryRootName, Guid.NewGuid().ToString("N"));
    private readonly string password = Guid.NewGuid().ToString("N");
    private Process? host;
    private Task<string>? stdout;
    private Task<string>? stderr;
    private bool cleanupTable;
    private string root = "";
    public DatabaseSession Session { get; private set; } = null!;
    public MetadataCatalog Catalog { get; private set; } = null!;
    public MetadataCatalog FileCatalog { get; private set; } = null!;
    public string Table { get; private set; } = "";
    public string RecordRoute => $"/api/admin/auto/{Table}/{Catalog.ConfigPages.Single().Id}";
    public string FileRoute => "/api/admin/sys/file-library";
    public string Output => Path.Combine(temporary, "application");
    public string HostDirectory => Path.Combine(Output, "backend/Application.Host");
    public HttpClient Client { get; private set; } = null!;
    public HttpClient Anonymous { get; private set; } = null!;
    public List<long> FileIds { get; } = [];
    public string[] ExistingFileTables { get; private set; } = [];
    public List<long> RecordIds { get; } = [];
    private string ProtectedColumn => Catalog.ConfigColumns.Single(c => c.ColumnName == "attachments").Id;
    private string PublicColumn => Catalog.ConfigColumns.Single(c => c.ColumnName == "public_files").Id;

    /// <summary>内存元数据只增加本次业务字段，文件基础设施定义必须读取打包后的唯一来源。</summary>
    public static async Task<PackagedFilesFixture> CreateAsync(DatabaseOptions? freshOptions = null)
    {
        var fixture = new PackagedFilesFixture();
        try
        {
            DatabaseOptions options;
            if (freshOptions is null)
            {
                using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
                options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("独立文件验收仅允许 MySQL metarill_business_test。");
            }
            else { FreshInstallConfiguration.Validate(freshOptions, "package"); options = freshOptions; }
            fixture.Session = new DatabaseSession(options);
            fixture.root = SourceRoot();
            fixture.Catalog = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(fixture.root, "backend/metadata/demo.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var table = fixture.Catalog.ConfigTables.Single();
            table.TableName = fixture.Table = "mt_test_files_" + Guid.NewGuid().ToString("N");
            var page = fixture.Catalog.ConfigPages.Single();
            page.IsNew = page.IsSave = page.IsDelete = 1;
            foreach (var (name, id, access) in new[] { ("attachments", "2200000000000199101", "PROTECTED"), ("public_files", "2200000000000199102", "PUBLIC") })
            {
                fixture.Catalog.ConfigColumns.Add(new ColumnMetadata { Id = id, ConfigTableId = table.Id, YzProjectId = fixture.Catalog.YzProjectId, Name = name, ColumnName = name, LogicalType = "string", ColumnType = "string", ColumnLength = "max", ControlKind = "FileLibrary", ControlDetail = "{\"accessMode\":\"" + access + "\",\"maxCount\":3}" });
                fixture.Catalog.ConfigPageColumns.Add(new PageColumnMetadata { Id = (long.Parse(id) + 100).ToString(), ConfigPageId = page.Id, ConfigColumnId = id, ColumnName = name, YzProjectId = fixture.Catalog.YzProjectId, IsEdit = 1, IsShow = 1 });
            }
            Assert.Empty((await new SchemaService(fixture.Session).InspectAsync(fixture.Catalog)).Tables);
            fixture.cleanupTable = true;
            new ApplicationPackager().Create(fixture.Catalog, fixture.Output, "Example.FileAcceptance", fixture.root);
            Assert.False(fixture.Output.StartsWith(fixture.root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            fixture.FileCatalog = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(fixture.Output, "backend/metadata/files.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(new[] { "yz_file_library", "yz_file_library_folder", "yz_file_ref" }, fixture.FileCatalog.ConfigTables.Select(t => t.TableName).Order().ToArray());
            var schema = new SchemaService(fixture.Session);
            fixture.ExistingFileTables = (await schema.InspectAsync(fixture.FileCatalog)).Tables.Select(t => t.Name).Order().ToArray();
            var filePlan = await schema.PreviewAsync(fixture.FileCatalog);
            // 已存在的固定基础表只能验证复用，结构漂移时拒绝执行 DDL。
            Assert.DoesNotContain(filePlan.Changes, change => fixture.ExistingFileTables.Contains(change.Table));
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    /// <summary>独立包 restore/build 全部在包目录执行，禁止引用回原仓库。</summary>
    public async Task BuildAsync()
    {
        foreach (var operation in new[] { "restore", "build" })
        {
            var info = StartInfo(Output);
            foreach (var argument in new[] { operation, "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet" }) info.ArgumentList.Add(argument);
            if (operation == "build") info.ArgumentList.Add("--no-restore");
            await RunAsync(info);
        }
    }

    /// <summary>真实运行包内 schema CLI；不使用测试私有 SQL 创建文件基础表。</summary>
    public Task SchemaAsync(string operation)
    {
        var info = HostInfo(); info.ArgumentList.Add(operation); return RunAsync(info);
    }

    /// <summary>随机端口隐藏启动宿主，启动及错误日志不输出连接配置。</summary>
    public async Task StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var info = HostInfo(); info.ArgumentList.Add("--urls"); info.ArgumentList.Add($"http://127.0.0.1:{port}");
        host = Process.Start(info)!; stdout = host.StandardOutput.ReadToEndAsync(); stderr = host.StandardError.ReadToEndAsync();
        Client?.Dispose(); Anonymous?.Dispose();
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
        Anonymous = new HttpClient { BaseAddress = Client.BaseAddress, Timeout = Client.Timeout };
        for (var attempt = 0; attempt < 150 && !host.HasExited; attempt++)
        {
            try { using var response = await Anonymous.GetAsync("/health"); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        Assert.Fail("独立文件库宿主未就绪，进程日志未输出以保护环境信息。");
    }

    /// <summary>开发认证固定为服务端单项目账号，不声称覆盖跨 owner 权限。</summary>
    public async Task LoginAsync()
    {
        var login = await SuccessAsync(HttpMethod.Post, "/api/admin/sys/yz_manager/login", new { username = "files-probe", password });
        Client.DefaultRequestHeaders.Add("loginStateId", login.GetProperty("map").GetProperty("loginStateId").GetString());
    }

    /// <summary>捕获每个真实上传计划的 ID，便于故障后仍精确清理。</summary>
    public async Task<long> PlanAsync(string name, byte[] bytes, bool publicly = false)
    {
        var response = await SuccessAsync(HttpMethod.Post, FileRoute + "/upload-plan", new { originalName = name, mimeType = "text/html", sizeBytes = bytes.Length, configColumnId = publicly ? PublicColumn : ProtectedColumn, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
        var data = response.GetProperty("data"); var id = long.Parse(data.GetProperty("fileId").ToString()); FileIds.Add(id);
        Assert.Equal("local", data.GetProperty("plan").GetProperty("providerCode").GetString());
        Assert.Equal("planned", await ScalarAsync("SELECT `status` FROM `yz_file_library` WHERE `id`=@id", id));
        return id;
    }

    /// <summary>走实际 multipart 与 complete 两步，核对服务端摘要和 MIME，而非上传者声明。</summary>
    public async Task<long> UploadAsync(string name, byte[] bytes, bool publicly = false)
    {
        var id = await PlanAsync(name, bytes, publicly);
        using (var premature = await Client.PostAsJsonAsync(FileRoute + "/complete", new { fileId = id.ToString() }))
            Assert.Equal(HttpStatusCode.InternalServerError, premature.StatusCode);
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", name);
        using var uploaded = await Client.PostAsync(FileRoute + "/local-upload/" + id, form);
        uploaded.EnsureSuccessStatusCode();
        var upload = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("uploaded", upload.GetProperty("data").GetProperty("status").GetString());
        var completed = await SuccessAsync(HttpMethod.Post, FileRoute + "/complete", new { fileId = id.ToString() });
        var descriptor = completed.GetProperty("data");
        Assert.Equal("ready", descriptor.GetProperty("status").GetString());
        Assert.Equal("text/plain", descriptor.GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), descriptor.GetProperty("sha256").GetString());
        Assert.DoesNotContain(".blob", descriptor.GetRawText());
        Assert.DoesNotContain("storageKey", descriptor.GetRawText());
        return id;
    }

    /// <summary>只构造固定协议的数字文件标识路径。</summary>
    public string ContentPath(long id, bool publicly) => FileRoute + (publicly ? "/public-content/" : "/protected-content/") + id;

    /// <summary>公开流使用无会话客户端，分别核对 GET、HEAD 和字节范围。</summary>
    public async Task AssertContentAsync(long id, byte[] bytes, bool publicly)
    {
        var client = publicly ? Anonymous : Client;
        using var get = await client.GetAsync(ContentPath(id, publicly)); get.EnsureSuccessStatusCode();
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("text/plain", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", get.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(get.Headers.CacheControl?.NoStore);
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, ContentPath(id, publicly)));
        head.EnsureSuccessStatusCode(); Assert.Equal(bytes.Length, head.Content.Headers.ContentLength); Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        using var request = new HttpRequestMessage(HttpMethod.Get, ContentPath(id, publicly)); request.Headers.Range = new RangeHeaderValue(1, 4);
        using var range = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode); Assert.Equal(bytes[1..5], await range.Content.ReadAsByteArrayAsync());
        Assert.Equal(bytes.Length, range.Content.Headers.ContentRange?.Length);
    }

    /// <summary>成功响应必须同时满足 HTTP 和统一业务状态。</summary>
    public async Task<JsonElement> SuccessAsync(HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await Client.SendAsync(request); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(200, json.GetProperty("code").GetInt32()); return json;
    }

    /// <summary>独占记录快照包含原字段和审计时间，失败写入不能留下部分更新。</summary>
    public async Task<string> RowSnapshotAsync(long record)
    {
        await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT `notes`,`attachments`,`update_time` FROM `{Table}` WHERE `id`=@id"; Bind(command, record);
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        return JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray());
    }

    /// <summary>只统计当前独占业务记录的有效引用。</summary>
    public async Task<long> ActiveReferencesAsync(long record) => Convert.ToInt64(await ScalarAsync("SELECT COUNT(*) FROM `yz_file_ref` WHERE `record_id`=@id AND `delete_no` IS NULL", record));

    /// <summary>不仅计数，还验证引用所属页面、项目、字段和文件标识。</summary>
    public async Task AssertReferenceAsync(long record, long file)
    {
        Assert.Equal(1L, await ActiveReferencesAsync(record));
        var value = await ScalarAsync("SELECT CONCAT(`file_id`,'|',`page_id`,'|',`field_name`,'|',`yz_project_id`) FROM `yz_file_ref` WHERE `record_id`=@id AND `delete_no` IS NULL", record);
        Assert.Equal($"{file}|{Catalog.ConfigPages.Single().Id}|attachments|{Catalog.YzProjectId}", value);
    }

    /// <summary>查询只使用固定 SQL 和参数，业务表名来自本 fixture 随机命名。</summary>
    public async Task<object?> ScalarAsync(string sql, long id)
    {
        await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; Bind(command, id);
        var value = await command.ExecuteScalarAsync(); return value is DBNull ? null : value;
    }
    /// <summary>写入只用于独占负例播种和精确清理，所有值均参数化。</summary>
    public async Task ExecuteAsync(string sql, long id)
    {
        await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; Bind(command, id); await command.ExecuteNonQueryAsync();
    }
    /// <summary>固定参数名绑定数据库值。</summary>
    private static void Bind(DbCommand command, long id) { var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter); }

    /// <summary>子进程日志仅消费不打印，任何失败使用不含配置的固定提示。</summary>
    private static async Task RunAsync(ProcessStartInfo info)
    {
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        await output; await error; Assert.True(process.ExitCode == 0, "独立文件包命令失败，日志未输出以保护环境信息。");
    }
    /// <summary>宿主配置限定为隔离业务库、专属存储根与测试节点。</summary>
    private ProcessStartInfo HostInfo()
    {
        var info = StartInfo(HostDirectory); info.ArgumentList.Add(Path.Combine(HostDirectory, "bin/Debug/net10.0/Application.Host.dll"));
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["Database__Provider"] = "mysql"; info.Environment["Database__ExpectedDatabase"] = Session.Options.ExpectedDatabase; info.Environment["Database__ConnectionString"] = Session.Options.ConnectionString;
        info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "true"; info.Environment["Authentication__DevelopmentBootstrap__Username"] = "files-probe"; info.Environment["Authentication__DevelopmentBootstrap__Password"] = password;
        info.Environment["Ids__WorkerId"] = "852";
        info.Environment["Files__RootDirectory"] = Path.Combine(temporary, "objects"); info.Environment["Files__MaxFileBytes"] = "4096";
        return info;
    }
    /// <summary>所有子进程隐藏窗口，并消费标准输出避免阻塞。</summary>
    private static ProcessStartInfo StartInfo(string directory) => new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    /// <summary>默认定位本普通克隆，可由 CI 显式指定可信源目录。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    /// <summary>停止持有数据库连接的宿主后再清理，绝不停止用户的其他服务。</summary>
    public async Task StopAsync()
    {
        if (host is null) return;
        if (!host.HasExited) host.Kill(true); await host.WaitForExitAsync();
        if (stdout is not null) await stdout; if (stderr is not null) await stderr;
        host.Dispose(); host = null;
    }

    /// <summary>只清理捕获的文件/记录 ID 与本次随机业务表，固定基础设施表永远保留。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(); Client?.Dispose(); Anonymous?.Dispose();
            if (Session is not null)
            {
                if (Session.Options.ExpectedDatabase != "metarill_business_test") FreshInstallConfiguration.Validate(Session.Options, "package");
                foreach (var id in FileIds) { await ExecuteAsync("DELETE FROM `yz_file_ref` WHERE `file_id`=@id", id); await ExecuteAsync("DELETE FROM `yz_file_library` WHERE `id`=@id", id); }
                foreach (var id in RecordIds) await ExecuteAsync("DELETE FROM `yz_file_ref` WHERE `record_id`=@id", id);
                if (cleanupTable)
                {
                    if (!Regex.IsMatch(Table, "^mt_test_files_[a-f0-9]{32}$")) throw new InvalidOperationException("拒绝清理非当前随机表。");
                    await ExecuteAsync("DROP TABLE IF EXISTS " + Session.Provider.QuoteIdentifier(Table), 0);
                }
            }
        }
        finally { Session?.Dispose(); await TestDirectoryCleanup.DeleteAsync(temporary, Path.Combine(Path.GetTempPath(), TemporaryRootName)); }
    }
}
