using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>真实平台ASP.NET管线的HTTP/日志测试；数据库/ORM全部硬拒绝，不读取实库或模拟数据库成功。</summary>
public sealed class PageImportErrorHttpTests
{
    [Fact]
    public async Task Platform_keeps_original_http_envelope_and_logs_only_execution_failures()
    {
        var logs = new CaptureLogs(); var database = new NoDatabase();
        var temporary = Path.Combine(Path.GetTempPath(), "metarill-import-platform", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var settings = Path.Combine(temporary, "settings.json"); var previousSettings = Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        // 平台在CreateBuilder后立即读取该文件，不能依赖稍后才执行的Factory配置；其中只有虚构离线配置。
        await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new { Metarill = new { Database = new { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;" }, BusinessDatabase = new { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;" } } }));
        Environment.SetEnvironmentVariable("METARILL_SETTINGS", settings);
        try
        {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Metarill:Database:ExpectedDatabase"] = "offline", ["Metarill:Database:ConnectionString"] = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;",
                ["Metarill:BusinessDatabase:ExpectedDatabase"] = "offline", ["Metarill:BusinessDatabase:ConnectionString"] = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DatabaseSession>(); services.AddSingleton(new DatabaseSession(new(), database.Orm, database));
                services.AddControllers().AddApplicationPart(typeof(ImportErrorHttpProbeController).Assembly);
            });
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs));
        });
        using var client = factory.CreateClient();
        foreach (var (mode, message) in new[]
        {
            ("input", "第2行，列「金额」：应填写有效数字。"), ("unknown", "第2行：存在未开放的导入列，请核对模板。"),
            ("execution", "操作失败，请查看服务端日志。"), ("unexpected", "操作失败，请查看服务端日志。"), ("denied", "无权执行此操作。")
        })
        {
            using var response = await client.GetAsync("/__import-error-probe/" + mode); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(500, body.GetProperty("code").GetInt32()); Assert.Equal(message, body.GetProperty("msg").GetString());
            Assert.DoesNotContain("SYNTHETIC", body.ToString()); Assert.DoesNotContain("NO-ECHO", body.ToString());
        }
        var errors = logs.Entries.Where(entry => entry.Level == LogLevel.Error).ToArray(); Assert.Equal(2, errors.Length);
        Assert.Contains(errors, entry => entry.Message.Contains("platform-import-execution", StringComparison.Ordinal) && entry.Error is PageImportException { IsInputError: false, InnerException: InvalidOperationException });
        Assert.Contains(errors, entry => entry.Message.Contains("platform-import-unexpected", StringComparison.Ordinal) && entry.Error is IOException);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("platform-import-input", StringComparison.Ordinal) || entry.Error is PageImportException { IsInputError: true });
        Assert.Equal(0, database.Accesses);
        }
        finally { Environment.SetEnvironmentVariable("METARILL_SETTINGS", previousSettings); File.Delete(settings); Directory.Delete(temporary); }
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        internal ConcurrentQueue<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();
        public ILogger CreateLogger(string _) => new Capture(Entries); public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<(LogLevel Level, string Message, Exception? Error)> entries) : ILogger
        { public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel _) => true; public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => entries.Enqueue((level, formatter(state, error), error)); }
    }
    private sealed class NoDatabase : IDatabaseProvider
    {
        internal int Accesses; internal IFreeSql Orm { get; }
        internal NoDatabase() { var proxy = DispatchProxy.Create<IFreeSql, NoQueryProxy>(); ((NoQueryProxy)(object)proxy).Access = () => Accesses++; Orm = proxy; }
        public string Name => "offline"; public DatabaseCapabilities Capabilities => new("offline", false, false); public void Validate(DatabaseOptions _) { }
        public IFreeSql CreateOrm(DatabaseOptions _) => throw new InvalidOperationException("禁止创建数据库ORM。");
        public DbConnection CreateConnection(DatabaseOptions _) { Accesses++; throw new InvalidOperationException("本测试禁止数据库连接。"); }
        public string QuoteIdentifier(string _) => throw new NotSupportedException(); public string ParameterPlaceholder(string _) => throw new NotSupportedException();
    }
    public class NoQueryProxy : DispatchProxy
    { internal Action Access = null!; protected override object? Invoke(MethodInfo? method, object?[]? args) { if (method!.Name == "Dispose") return null; Access(); throw new InvalidOperationException("本测试禁止ORM访问。"); } }
}

/// <summary>只在测试程序集由WebApplicationFactory加载，不属于平台产品路由。</summary>
[ApiController, Route("__import-error-probe")]
public sealed class ImportErrorHttpProbeController : ControllerBase
{
    [HttpGet("{mode}")]
    public object Probe(string mode)
    {
        HttpContext.TraceIdentifier = "platform-import-" + mode;
        throw mode switch
        {
            "input" => PageImportException.Input(PageImportError.Number, 2, "金额", new JsonException("INPUT-NO-LOG-MARKER")),
            "unknown" => PageImportException.Input(PageImportError.UnknownHeader, 2, "CLIENT-NO-ECHO"),
            "execution" => PageImportException.Execution(2, new InvalidOperationException("SYNTHETIC-INNER-SQL-PATH-CONNECTION")),
            "unexpected" => new IOException("SYNTHETIC-PATH-CONNECTION"),
            "denied" => new UnauthorizedAccessException("SYNTHETIC-AUTH-NO-ECHO"),
            _ => new InvalidOperationException("未知测试探针")
        };
    }
}
