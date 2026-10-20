using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定版本目录在编译、实际生成程序集和真实无DB宿主间保持同一合同。</summary>
public sealed class TemplateVersionCatalogTests
{
    private const string PageId = "6186303424888832";
    private const string Declaration = "{\"catalog\":\"metarill-csharp-templates/v1\",\"nameField\":\"name\"}";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>空历史版本只读兼容；任何显式非C#版本都拒绝，绝不默写或映射Java版本。</summary>
    [Theory]
    [InlineData(null, true)] [InlineData("", true)] [InlineData("2300000000000000001", true)]
    [InlineData("2104907095303213057", false)] [InlineData("0", false)] [InlineData("2300000000000000002", false)] [InlineData("1e3", false)]
    public void Stored_project_version_keeps_null_but_rejects_explicit_legacy(string? value, bool accepted)
    {
        if (accepted) TemplateVersionCatalog.ValidateStoredProjectVersion(value);
        else Assert.Throws<InvalidOperationException>(() => TemplateVersionCatalog.ValidateStoredProjectVersion(value));
        Assert.Null(TemplateVersionCatalog.DisplayName(null));
        Assert.Equal(TemplateVersionCatalog.CurrentName, TemplateVersionCatalog.DisplayName(TemplateVersionCatalog.CurrentValue));
    }

    /// <summary>候选白名单拒绝动态目录、SQL、重复键以及浮点/对象ID，旧formParameters不成为授权。</summary>
    [Theory]
    [InlineData("{\"sql\":\"select 1\"}")] [InlineData("{\"catalog\":\"evil\"}")]
    [InlineData("{\"id\":\"2104907095303213057\"}")] [InlineData("{\"id\":1.0}")]
    [InlineData("{\"id\":{}} ")] [InlineData("{\"id\":null,\"id\":\"2300000000000000001\"}")]
    [InlineData("{\"targetParentId\":\"1\"}")] [InlineData("{\"formParameters\":[]}")]
    public void Candidate_input_rejects_unknown_or_malformed_fields(string text) => Assert.Throws<ArgumentException>(() => TemplateVersionCatalog.ReadOptions(Parse(text)));

    /// <summary>完整包与用途IR使用相同目录证明，未知声明不因目标表不存在而被降级放行。</summary>
    [Theory]
    [InlineData("unknown")] [InlineData("sql")] [InlineData("table")] [InlineData("duplicate")] [InlineData("type")] [InlineData("overlay")] [InlineData("physical-alias")] [InlineData("virtual")] [InlineData("join")] [InlineData("link")] [InlineData("link-page")] [InlineData("logical")] [InlineData("name-field")]
    public void Bad_catalog_is_gap_and_full_package_produces_no_output(string scenario)
    {
        var catalog = Catalog(); var column = catalog.ConfigColumns.Single(item => item.Id == TemplateVersionCatalog.VersionColumnId);
        if (scenario == "unknown") column.ControlDetail = Declaration.Replace(TemplateVersionCatalog.CatalogId, "unknown-catalog");
        if (scenario == "sql") column.ControlDetail = Declaration[..^1] + ",\"whereSql\":\"1=1\"}";
        if (scenario == "table") column.ControlDetail = Declaration[..^1] + ",\"table\":\"yz_role\"}";
        if (scenario == "duplicate") column.ControlDetail = Declaration[..^1] + ",\"catalog\":\"metarill-csharp-templates/v1\"}";
        if (scenario == "type") column.LogicalType = "string";
        if (scenario == "overlay") catalog.ConfigPageColumns.Single(item => item.ConfigColumnId == column.Id).Extra["controlDetail"] = JsonSerializer.SerializeToElement(Declaration[..^1] + ",\"orderBy\":\"name\"}");
        if (scenario == "virtual") column.IsVirtualColumn = 1;
        if (scenario == "join") column.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        if (scenario == "link") column.LinkConfigTableId = "1692532858499969026";
        if (scenario == "link-page") column.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("1692532858529329154");
        if (scenario == "logical") column.LogicalType = "int32";
        if (scenario == "name-field") column.ControlDetail = Declaration.Replace("\"name\"", "\"password\"");
        if (scenario == "physical-alias") catalog.ConfigColumns.Add(new() { Id = "999", ConfigTableId = TemplateVersionCatalog.ProjectTableId, ColumnName = "yz_yzp_version_name", LogicalType = "string" });
        var model = GenerationModel.Create(catalog, "Example.Version"); var table = model.Tables.Single(); var page = table.Pages.Single();
        Assert.Contains(GenerationCapabilityAudit.Collect(model), gap => gap.Column == "yz_yzp_version_id");
        Assert.False(ReadProjectionCompilation.ForPage(model, table, page).Detail.CanCompile);
        var validation = ForeignValidationRenderer.Render(model, table, page);
        Assert.DoesNotContain("TemplateVersionCatalog.RequireSupported", validation);
        if (scenario == "virtual")
            Assert.DoesNotContain(page.Writable.Concat(page.Readable), candidate => candidate.Source.Id == TemplateVersionCatalog.VersionColumnId);
        else Assert.Contains(JsonSerializer.Serialize(FixedOptionCatalogCompilation.Resolve(model, page, table.Columns.Single(candidate => candidate.Source.Id == TemplateVersionCatalog.VersionColumnId)).Reason), validation);
        var output = Path.Combine(Path.GetTempPath(), "metarill-version-gap-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
        var rejected = Record.Exception(() => new ApplicationPackager().Create(catalog, output, "Example.Version", SourceRoot()));
        Assert.True(rejected is InvalidOperationException or ArgumentException);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>目录无需物理名称列；列表、详情与选择IR明确登记计算名称，原zxz结构与回调保持不变。</summary>
    [Fact]
    public async Task Real_generated_contract_preserves_original_ui_alias_and_strict_service()
    {
        var catalog = Catalog(); var model = GenerationModel.Create(catalog, "Example.Version"); var table = model.Tables.Single(); var page = table.Pages.Single();
        var ir = ReadProjectionCompilation.ForPage(model, table, page);
        foreach (var plan in new[] { ir.List, ir.Detail, ir.Selection })
        {
            Assert.True(plan.CanCompile, string.Join(";", plan.Gaps.Select(gap => gap.Reason)));
            Assert.Contains(plan.OutputFields, field => field.JsonName == "yzYzpVersionName" && field.Kind == ReadProjectionOutputKind.CatalogName);
            Assert.DoesNotContain(plan.PhysicalColumns, column => column.Source.ColumnName == "yz_yzp_version_name");
        }
        new FrontendPackager().Validate(catalog);
        var detail = OriginalUniappDetailRenderer.Render(model, table, page, []);
        Assert.Contains("<zxz-uni-data-select v-model='entity.yzYzpVersionId' :localdata='yz_yzp_version_list' filterable :disabled='yzYzpVersionIdReadOnly' dataKey='name' dataValue='id' @change='yz_yzp_versionSelectChange' />", detail);
        Assert.Contains("readonlyDisplayText(entity.yzYzpVersionName)", detail);
        Assert.Contains("this.entity.yzYzpVersionName=item ? item.name : ''", detail);
        Assert.Contains("findNameIdYzYzpVersion1669606903854936065", detail);
        Assert.Contains("formParameters:uni.getStorageSync(that.formParametersName)", detail);
        Assert.DoesNotContain("<uni-data-checkbox", detail);
        Assert.Contains("yzYzpVersionId:\"2300000000000000001\"", detail);
        var list = OriginalUniappListRenderer.Render(model, table, page);
        Assert.Contains("relationDisplayText(item.yzYzpVersionName)", list);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-version-contract"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.Version");
            await File.WriteAllTextAsync(Path.Combine(directory, "detail.vue"), detail);
            await File.WriteAllTextAsync(Path.Combine(directory, "catalog-options.json"), JsonSerializer.Serialize(TemplateVersionCatalog.ReadOptions(null), Json));
            await RunVueProbe(directory);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entityType = assembly.GetType("Example.Version.Generated.Entities.YzProject", true)!;
            var entity = Activator.CreateInstance(entityType)!; entityType.GetProperty("YzYzpVersionId")!.SetValue(entity, TemplateVersionCatalog.CurrentValue);
            var voType = assembly.GetType("Example.Version.Generated.VOs.YzProjectPage" + PageId + "Vo", true)!;
            Assert.Equal(TemplateVersionCatalog.CurrentName, voType.GetProperty("YzYzpVersionName")!.GetValue(voType.GetMethod("FromEntity")!.Invoke(null, [entity])));
            entityType.GetProperty("YzYzpVersionId")!.SetValue(entity, null);
            Assert.Null(voType.GetProperty("YzYzpVersionName")!.GetValue(voType.GetMethod("FromEntity")!.Invoke(null, [entity])));
            var controllerType = assembly.GetType("Example.Version.Generated.Controllers.YzProjectPage" + PageId + "Controller", true)!;
            var legacy = (LegacyPageWriteContract)controllerType.GetField("LegacyWrite", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var payload = legacy.Normalize(Parse("{\"id\":\"1\",\"yzYzpVersionId\":\"2300000000000000001\",\"yzYzpVersionName\":\"伪造名称\"}"), LegacyPageWriteOperation.Update, catalog.YzProjectId, null);
            Assert.False(payload.TryGetProperty("yzYzpVersionName", out _));
            Assert.Throws<ArgumentException>(() => legacy.Normalize(Parse("{\"id\":\"1\",\"arbitraryName\":\"伪造\"}"), LegacyPageWriteOperation.Update, catalog.YzProjectId, null));
            var deniedController = (Microsoft.AspNetCore.Mvc.ControllerBase)Activator.CreateInstance(controllerType, [null, new DeniedAuthorization()])!;
            deniedController.ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
            var denied = Assert.Throws<TargetInvocationException>(() => controllerType.GetMethod("findNameIdYzYzpVersion1669606903854936065")!.Invoke(deniedController, [null]));
            Assert.IsType<UnauthorizedAccessException>(denied.InnerException);
            var dtoType = assembly.GetType("Example.Version.Generated.DTOs.YzProjectPage" + PageId + "UpdateRequest", true)!;
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"id\":1,\"yzYzpVersionName\":\"伪造\"}", dtoType, Json));
            var serviceType = assembly.GetType("Example.Version.Generated.Services.YzProjectService", true)!;
            var service = Activator.CreateInstance(serviceType, [null, null, null, Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(), null])!;
            var validate = serviceType.GetMethod("ValidateRelations" + PageId + "Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var value in new long?[] { TemplateVersionCatalog.CurrentValue, 2104907095303213057L, null, 1L })
            {
                entityType.GetProperty("YzYzpVersionId")!.SetValue(entity, value);
                var task = (Task)validate.Invoke(service, [entity, new RuntimeActor(1, "离线测试", true, catalog.YzProjectId, "1"), false, new HashSet<string> { "yz_yzp_version_id" }, CancellationToken.None])!;
                if (value == TemplateVersionCatalog.CurrentValue) await task; else await Assert.ThrowsAsync<ArgumentException>(() => task);
            }
            // 省略字段不把历史null变成默认版本，也不会因无关更新而强制改值。
            entityType.GetProperty("YzYzpVersionId")!.SetValue(entity, null);
            await (Task)validate.Invoke(service, [entity, new RuntimeActor(1, "离线测试", true, catalog.YzProjectId, "1"), false, new HashSet<string>(), CancellationToken.None])!;
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>真实Full包启动与原POST候选不连接数据库；匿名/未知请求仍由实际HTTP边界拒绝。</summary>
    [Fact]
    public async Task Real_standalone_host_returns_original_candidates_without_database()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-version-http"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "application"); new ApplicationPackager().Create(Catalog(), output, "Example.VersionHttp", SourceRoot());
            await Build(output); var host = Path.Combine(output, "backend/Application.Host");
            await using var server = await Server.Start(host);
            var endpoint = "/api/admin/auto/yz_project/" + PageId + "/findNameIdYzYzpVersion1669606903854936065";
            using (var anonymous = await server.Client.PostAsJsonAsync(endpoint, new { formParameters = new { } })) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            var password = server.Password;
            using var login = await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login", new { username = "version-probe", password });
            using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync()); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            server.Client.DefaultRequestHeaders.Add("loginStateId", loginJson.RootElement.GetProperty("map").GetProperty("loginStateId").GetString());
            using var candidates = await server.Client.PostAsJsonAsync(endpoint, new { parentId = "", formParameters = new { ignored = "上下文不是权限" } });
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode); using var json = JsonDocument.Parse(await candidates.Content.ReadAsStringAsync());
            var option = Assert.Single(json.RootElement.GetProperty("data").EnumerateArray()); Assert.Equal(TemplateVersionCatalog.CurrentId, option.GetProperty("id").GetString()); Assert.Equal(TemplateVersionCatalog.CurrentName, option.GetProperty("name").GetString()); Assert.Equal(TemplateVersionCatalog.CurrentId, option.GetProperty("value").GetString());
            foreach (var body in new[] { "{\"sql\":\"select 1\"}", "{\"id\":\"2104907095303213057\"}", "{\"id\":null,\"id\":\"2300000000000000001\"}" })
            { using var bad = await server.Client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json")); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
            using (var query = await server.Client.PostAsJsonAsync(endpoint + "?projectId=1", new { })) Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
            using var empty = await server.Client.PostAsJsonAsync(endpoint, new { id = (string?)null }); using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()); Assert.Empty(emptyJson.RootElement.GetProperty("data").EnumerateArray());
            Assert.False(File.Exists(Path.Combine(host, ".local/schema-history.jsonl")));
            Assert.False(File.Exists(Path.Combine(host, ".local/development-auth/credentials.json")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>从真实平台模型裁出版本页，仅保留本轮所需物理字段；不新造版本表。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "backend/metadata/platform.json")), Json)!;
        catalog.ConfigTables = catalog.ConfigTables.Where(table => table.Id == TemplateVersionCatalog.ProjectTableId).ToList();
        catalog.ConfigColumns = catalog.ConfigColumns.Where(column => column.ConfigTableId == TemplateVersionCatalog.ProjectTableId && column.ColumnName is "id" or "name" or "yz_yzp_version_id" or "yz_project_id" or "delete_no").ToList();
        catalog.ConfigPages = catalog.ConfigPages.Where(page => page.Id == PageId).ToList();
        catalog.ConfigPageColumns = catalog.ConfigPageColumns.Where(field => field.ConfigPageId == PageId && catalog.ConfigColumns.Any(column => column.Id == field.ConfigColumnId)).ToList();
        catalog.ConfigPages[0].Extra.Remove("isRepeatPwd");
        catalog.ConfigMenus = []; catalog.ConfigButtons = []; catalog.ConfigChildren = []; catalog.ConfigCustomPages = []; catalog.ConfigIndexes = []; catalog.ConfigIndexColumns = [];
        return catalog;
    }
    private static JsonElement Parse(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    private static string SourceRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    /// <summary>实际Vue编译挂载原下拉并派发原change事件；此离线探针不冒充浏览器验收。</summary>
    private static async Task RunVueProbe(string directory, [CallerFilePath] string source = "")
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "TemplateVersionVueProbe.mjs")); info.ArgumentList.Add(directory);
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
    /// <summary>真实包构建将全部编译警告提升为错误，不依赖运行中的平台或数据库。</summary>
    private static async Task Build(string output)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = output, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "build", "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet", "-warnaserror" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
    /// <summary>证明候选入口先执行页面授权，不能只验证目录ID就对普通身份放行。</summary>
    private sealed class DeniedAuthorization : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => throw new InvalidOperationException("页面拒绝后不应建立Actor。");
        public void RequirePagePermission(string pageId, string operation) => throw new UnauthorizedAccessException("离线测试：没有版本页面权限。");
    }
    /// <summary>仅本轮随机loopback端口、显式开发账号和不可达数据库；不会启动结构命令。</summary>
    private sealed class Server(Process process, HttpClient client, string password, Task<string> stdout, Task<string> stderr) : IAsyncDisposable
    {
        internal HttpClient Client => client;
        internal string Password => password;
        internal static async Task<Server> Start(string host)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var password = Guid.NewGuid().ToString("N"); var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = host, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(Path.Combine(host, "bin/Debug/net10.0/Application.Host.dll")); info.ArgumentList.Add("--urls"); info.ArgumentList.Add("http://127.0.0.1:" + port);
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "true"; info.Environment["Authentication__DevelopmentBootstrap__Username"] = "version-probe"; info.Environment["Authentication__DevelopmentBootstrap__Password"] = password; info.Environment["Logging__LogLevel__Default"] = "Warning";
            info.Environment["Database__ExpectedDatabase"] = "metarill_version_probe"; info.Environment["Database__ConnectionString"] = "Server=127.0.0.1;Port=1;Database=metarill_version_probe;User ID=nonexistent;Password=offline-only;Connection Timeout=1;";
            var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); var client = new HttpClient { BaseAddress = new("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(5) }; var server = new Server(process, client, password, stdout, stderr);
            try { for (var attempt = 0; attempt < 100 && !process.HasExited; attempt++) { try { using var response = await client.GetAsync("/health"); if (response.IsSuccessStatusCode) return server; } catch (HttpRequestException) { } await Task.Delay(100); } throw new InvalidOperationException("临时版本候选宿主未启动。"); }
            catch { await server.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync() { client.Dispose(); if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await stdout; await stderr; process.Dispose(); }
    }
}
