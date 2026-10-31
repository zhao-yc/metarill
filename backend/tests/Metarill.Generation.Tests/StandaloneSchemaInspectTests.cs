using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>缺少独立测试配置时明确跳过真实结构反查，不能把离线命令拒绝算作数据库验收。</summary>
public sealed class StandaloneSchemaInspectMySqlFactAttribute : FactAttribute
{
    public StandaloneSchemaInspectMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS"))) Skip = "未提供隔离MySQL配置，独立包结构反查实证未执行。";
    }
}

/// <summary>真实独立命令只读包内受管表；不复用协调者UI表，也不创建或清理固定文件表。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class StandaloneSchemaInspectTests : IAsyncDisposable
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "metarill-schema-command", Guid.NewGuid().ToString("N"));

    /// <summary>即使没有数据库配置，混合/重复/赋值及未知结构flags都须在构造宿主前明确拒绝并退出。</summary>
    [Fact]
    public async Task Invalid_schema_command_combinations_exit_without_starting_a_host()
    {
        var output = Path.Combine(temporary, "application");
        new ApplicationPackager().Create(GeneratorTests.Catalog(), output, "Example.SchemaArguments", SourceRoot(), ApplicationPackageMode.BackendOnly);
        await BuildAsync(output);
        var host = Path.Combine(output, "backend/Application.Host"); var metadata = MetadataSnapshot(output);
        foreach (var arguments in new[] {
            new[] { "--schema-inspect", "--schema-preview" }, new[] { "--schema-inspect", "--schema-apply" }, new[] { "--schema-preview", "--schema-apply" },
            new[] { "--schema-inspect", "--schema-inspect" }, new[] { "--schema-preview", "--schema-preview" }, new[] { "--schema-apply", "--schema-apply" },
            new[] { "--schema-inspect=true" }, new[] { "--schema-unknown" } })
        {
            var result = await CommandAsync(host, null, arguments);
            Assert.Equal(2, result.Code); Assert.Empty(result.Output); Assert.True(result.Error.Contains("必须单独使用", StringComparison.Ordinal));
            Assert.Equal(metadata, MetadataSnapshot(output)); Assert.False(Directory.Exists(Path.Combine(host, ".local")));
        }
    }

    /// <summary>通过包内命令实际读取Server-200随机表，逐项证明无DDL、无行改动、无元数据或执行历史改写。</summary>
    [StandaloneSchemaInspectMySqlFact]
    public async Task Inspect_returns_actual_snapshot_without_plans_or_side_effects()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var configured = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var connection = new MySqlConnector.MySqlConnectionStringBuilder(configured.ConnectionString);
        Assert.True(configured.Provider == "mysql" && configured.ExpectedDatabase == "metarill_business_test" && connection.Server is "deve-server.yinzon.com" or "192.168.31.200", "结构只读验收只允许200服务器的独立业务测试库。");
        await using var fixture = await PackagedFilesFixture.CreateAsync();
        Assert.Equal(3, fixture.ExistingFileTables.Length); // 固定基础表仅反查复用，本测试不为它们执行DDL。
        await fixture.BuildAsync();
        var physical = Metarill.Core.Metadata.MetadataCatalogComposition.MergePhysical(Metarill.Core.Metadata.MetadataCatalogComposition.MergePhysical(fixture.Catalog, fixture.FileCatalog),fixture.SecurityCatalog);
        var schema = new SchemaService(fixture.Session); var metadata = MetadataSnapshot(fixture.Output);
        var journal = Path.Combine(fixture.HostDirectory, ".local/schema-history.jsonl");
        var before = await CommandAsync(fixture.HostDirectory, fixture.Session.Options, "--schema-inspect");
        Assert.Equal(0, before.Code); using (var json = JsonDocument.Parse(before.Output))
        {
            VerifySnapshot(json.RootElement, await schema.InspectSnapshotAsync(physical));
            Assert.DoesNotContain(json.RootElement.GetProperty("databaseSchema").GetProperty("tables").EnumerateArray(), table => table.GetProperty("name").GetString() == fixture.Table);
        }
        Assert.Empty((await schema.InspectAsync(fixture.Catalog)).Tables); Assert.Empty(schema.History);
        Assert.False(File.Exists(journal)); Assert.False(Directory.Exists(Path.Combine(fixture.HostDirectory, ".local/exports"))); Assert.Equal(metadata, MetadataSnapshot(fixture.Output));
        // 正常Web启动只显式提供health，不会为了命令功能读取或创建业务表。
        await fixture.StartAsync(); Assert.Empty((await schema.InspectAsync(fixture.Catalog)).Tables); await fixture.StopAsync();
        var plan = await schema.PreviewAsync(fixture.Catalog); Assert.Equal(fixture.Table, Assert.Single(plan.Changes).Table);
        Assert.True((await schema.ApplyAsync(plan)).Succeeded); // 表结构始终从当前测试元数据生成。
        await fixture.StartAsync(); await fixture.LoginAsync();
        var added = await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/add", new { name = "只读反查前记录", notes = "应完整保留", amount = 12.34 });
        var id = long.Parse(added.GetProperty("data").GetProperty("id").GetString()!); fixture.RecordIds.Add(id); await fixture.StopAsync();
        var rows = await RowsSnapshotAsync(fixture); var historyCount = schema.History.Count;
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!); await File.WriteAllTextAsync(journal, "已有执行历史不得被反查改写\n");
        var journalBytes = await File.ReadAllBytesAsync(journal);
        string? fingerprint = null;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var inspected = await CommandAsync(fixture.HostDirectory, fixture.Session.Options, "--schema-inspect"); Assert.Equal(0, inspected.Code);
            using var json = JsonDocument.Parse(inspected.Output); var expected = await schema.InspectSnapshotAsync(physical); VerifySnapshot(json.RootElement, expected);
            Assert.Contains(json.RootElement.GetProperty("databaseSchema").GetProperty("tables").EnumerateArray(), table => table.GetProperty("name").GetString() == fixture.Table);
            fingerprint ??= expected.Fingerprint; Assert.Equal(fingerprint, expected.Fingerprint);
            Assert.Equal(rows, await RowsSnapshotAsync(fixture)); Assert.Equal(metadata, MetadataSnapshot(fixture.Output)); Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journal)); Assert.Equal(historyCount, schema.History.Count);
        }
        // 预期库与真实连接串目标不同必须拒绝，不回退、不生成计划或启动服务。
        var wrong = await CommandAsync(fixture.HostDirectory, fixture.Session.Options with { ExpectedDatabase = "metarill_business_test_mismatch" }, "--schema-inspect");
        Assert.NotEqual(0, wrong.Code); Assert.True(wrong.Error.Contains("连接串数据库必须与 ExpectedDatabase 完全一致", StringComparison.Ordinal));
        foreach (var command in new[] { "--schema-preview", "--schema-apply", "--schema-inspect" })
        {
            var invalid = await CommandAsync(fixture.HostDirectory, fixture.Session.Options, "--schema-inspect", command); Assert.Equal(2, invalid.Code);
        }
        Assert.Equal(rows, await RowsSnapshotAsync(fixture)); Assert.Equal(metadata, MetadataSnapshot(fixture.Output)); Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journal)); Assert.Equal(historyCount, schema.History.Count);
    }

    /// <summary>输出保持单一JSON快照，不能暗含可执行plan或Changes。</summary>
    private static void VerifySnapshot(JsonElement actual, SchemaInspectionSnapshot expected)
    {
        Assert.False(actual.GetProperty("executable").GetBoolean()); Assert.Equal(expected.Provider, actual.GetProperty("provider").GetString()); Assert.Equal(expected.Fingerprint, actual.GetProperty("fingerprint").GetString());
        var schema = actual.GetProperty("databaseSchema").Deserialize<DatabaseSchema>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(JsonSerializer.Serialize(expected.Schema), JsonSerializer.Serialize(schema));
        Assert.False(actual.TryGetProperty("planId", out _)); Assert.False(actual.TryGetProperty("id", out _)); Assert.False(actual.TryGetProperty("changes", out _));
    }

    /// <summary>只读完整随机表的全部列与行，摘要同时覆盖审计字段、软删状态和业务值。</summary>
    private static async Task<string> RowsSnapshotAsync(PackagedFilesFixture fixture)
    {
        await using var connection = fixture.Session.Provider.CreateConnection(fixture.Session.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM " + fixture.Session.Provider.QuoteIdentifier(fixture.Table) + " ORDER BY `id`";
        await using var reader = await command.ExecuteReaderAsync(); var rows = new List<object?[]>();
        while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
        Assert.Single(rows); return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    /// <summary>源码元数据与构建时复制的元数据同时比较，返回只含路径和摘要的确定性清单。</summary>
    private static string MetadataSnapshot(string output) => string.Join("\n", Directory.EnumerateFiles(Path.Combine(output, "backend"), "*.json", SearchOption.AllDirectories)
        .Where(file => Path.GetDirectoryName(file)!.EndsWith(Path.DirectorySeparatorChar + "metadata", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal).Select(file => Path.GetRelativePath(output, file) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))));

    /// <summary>真实restore/build留在独立包，编译失败只给安全提示，不输出私有配置。</summary>
    private static async Task BuildAsync(string output)
    {
        foreach (var operation in new[] { "restore", "build" })
        {
            var info = StartInfo(output); foreach (var argument in new[] { operation, "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet" }) info.ArgumentList.Add(argument);
            if (operation == "build") info.ArgumentList.Add("--no-restore"); var result = await RunAsync(info); Assert.True(result.Code == 0, "独立结构命令包构建失败，私有日志未输出。");
        }
    }

    /// <summary>只经进程环境传递私有Database配置，Production命令无需开发账号或Web认证。</summary>
    private static Task<CommandResult> CommandAsync(string host, DatabaseOptions? options, params string[] arguments)
    {
        var info = StartInfo(host); info.ArgumentList.Add(Path.Combine(host, "bin/Debug/net10.0/Application.Host.dll")); foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
        foreach (var key in new[] { "Database__Provider", "Database__ExpectedDatabase", "Database__ConnectionString" }) info.Environment.Remove(key);
        if (options is not null) { info.Environment["Database__Provider"] = options.Provider; info.Environment["Database__ExpectedDatabase"] = options.ExpectedDatabase; info.Environment["Database__ConnectionString"] = options.ConnectionString; }
        return RunAsync(info);
    }

    /// <summary>命令必须自行结束；如果误启动HTTP服务会超时失败并仅终止本次子进程。</summary>
    private static async Task<CommandResult> RunAsync(ProcessStartInfo info)
    {
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw new InvalidOperationException("独立结构命令未按时退出，已终止本次测试子进程。"); }
        return new(process.ExitCode, await output.WaitAsync(timeout.Token), await error.WaitAsync(timeout.Token));
    }
    private sealed record CommandResult(int Code, string Output, string Error);
    /// <summary>隐藏子进程窗口并按UTF8读取命令JSON；日志只在测试内安全判断。</summary>
    private static ProcessStartInfo StartInfo(string directory) => new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
    /// <summary>按当前普通克隆定位可信源码，可由CI显式指定固定源码根。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    /// <summary>只清理本测试根下的GUID临时包，真实随机表由隔离数据库夹具负责收尾。</summary>
    public async ValueTask DisposeAsync() => await TestDirectoryCleanup.DeleteAsync(temporary, Path.Combine(Path.GetTempPath(), "metarill-schema-command"));
}
