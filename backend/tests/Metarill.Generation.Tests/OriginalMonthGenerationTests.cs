using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>验证原年月的发布门禁、实际Vue事件和生成API日期形状。</summary>
public sealed class OriginalMonthGenerationTests
{
    /// <summary>同页保存日期月与字符串月，生成包仍使用原详情和查询控件。</summary>
    [Fact]
    public async Task Original_month_round_trips_through_the_full_application()
    {
        var catalog = CreateCatalog();
        var source = NonRedundantApplicationFixture.Root();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-month");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, "Example.MonthApplication", source).FrontendIncluded);
            await RunProbe(output);
            foreach (var command in new[] { "restore", "build" })
                await NonRedundantApplicationFixture.BuildAsync(output, command);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>已有日期月仍用原日期文本返回，表单清空与省略按 Optional 分别处理。</summary>
    [Fact]
    public async Task Generated_month_dates_keep_the_legacy_http_contract()
    {
        var catalog = CreateCatalog();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-month-contract");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.GuardContract");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var requestType = assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101UpdateRequest", true)!;
            var json = JsonDefaults.Create();
            json.Converters.Clear();
            JsonDefaults.ConfigureOriginalCalendarInput(json);
            var request = JsonSerializer.Deserialize("""{"id":1,"billingMonth":"2024-02-01","periodCode":"2024-02"}""",
                requestType, json)!;
            var month = requestType.GetProperty("BillingMonth")!;
            Assert.Equal(new DateTime(2024, 2, 1), ((Metarill.Core.Contracts.Optional<DateTime?>)month.GetValue(request)!).Value);
            var omitted = JsonSerializer.Deserialize("""{"id":1}""", requestType, json)!;
            Assert.False(((Metarill.Core.Contracts.Optional<DateTime?>)month.GetValue(omitted)!).IsSpecified);
            var cleared = JsonSerializer.Deserialize("""{"id":1,"billingMonth":""}""", requestType, json)!;
            var field = (Metarill.Core.Contracts.Optional<DateTime?>)month.GetValue(cleared)!;
            Assert.True(field.IsSpecified);
            Assert.Null(field.Value);

            // 执行实际生成Service。仅存储连接使用离线替身，年月仍走统一保存和范围编译。
            using var run = new GeneratedMutationGuardRun(assembly);
            var service = run.Service([]);
            var created = await ClockTimeGenerationTests.Invoke(service, "Create101Async",
                run.Request(Metarill.Core.Persistence.RecordMutationOperation.Create,
                    """{"name":"年月样本","billingMonth":"2024-02-01","periodCode":"2024-02"}"""),
                null, run.Actor, CancellationToken.None);
            Assert.Equal(new DateTime(2024, 2, 1), created!.GetType().GetProperty("BillingMonth")!.GetValue(created));
            Assert.Equal("2024-02", created.GetType().GetProperty("PeriodCode")!.GetValue(created));
            Assert.Contains("write:Insert", run.Trace);
            // 真实ORM只翻译查询；任何试图建立连接的操作都会立即失败。
            using var orm = new FreeSqlBuilder().UseConnectionFactory(DataType.MySql,
                () => throw new InvalidOperationException("离线年月回归禁止数据库连接。"))
                .UseAutoSyncStructure(false).Build();
            using var session = new DatabaseSession(new() { Provider = "mysql", ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Database=offline;User ID=unused" }, orm);
            var mapper = Activator.CreateInstance(assembly.GetType("Example.GuardContract.Generated.Mappers.CustomerMapper", true)!, session)!;
            var serviceType = service.GetType();
            var queryService = Activator.CreateInstance(serviceType, session, mapper, new SnowflakeIdGenerator(842),
                Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>())!;
            var query = serviceType.GetMethod("Query101", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var selection = query.Invoke(queryService, [new PageRequest
            {
                Filters = new()
                {
                    ["billingMonth"] = JsonSerializer.SerializeToElement(new[] { "2024-02-01", "2024-02-29" }),
                    ["periodCode"] = JsonSerializer.SerializeToElement("2024-02")
                }
            }, run.Actor])!;
            var entity = assembly.GetType("Example.GuardContract.Generated.Entities.Customer", true)!;
            var sql = (string)typeof(OriginalMonthGenerationTests).GetMethod(nameof(RenderSql), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(entity).Invoke(null, [selection])!;
            Assert.Contains("`billing_month` >=", sql);
            Assert.Contains("`billing_month` < ", sql);
            Assert.Contains("`period_code`", sql);
            Assert.Contains(" LIKE ", sql, StringComparison.OrdinalIgnoreCase);

            var voType = assembly.GetType("Example.GuardContract.Generated.VOs.CustomerPage101Vo", true)!;
            var record = JsonSerializer.Deserialize("""{"billingMonth":"2024-02-01T00:00:00","periodCode":"2024-02","eventTime":"2024-02-03T04:05:06"}""",
                voType, json)!;
            using var response = JsonDocument.Parse(JsonSerializer.Serialize(record, voType, json));
            Assert.Equal("2024-02-01", response.RootElement.GetProperty("billingMonth").GetString());
            Assert.Equal("2024-02", response.RootElement.GetProperty("periodCode").GetString());
            Assert.Equal("2024-02-03T04:05:06", response.RootElement.GetProperty("eventTime").GetString());
            // 同一生成详情在平台和独立宿主仍按各自的日期文本合同输出。
            var platformResponse = JsonSerializer.SerializeToElement(record,voType,JsonDefaults.Create());
            Assert.Equal("2024-02-01",platformResponse.GetProperty("billingMonth").GetString());
            Assert.Equal("2024-02-03 04:05:06",platformResponse.GetProperty("eventTime").GetString());
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>取得真实 ORM 生成的查询，不执行 SQL。</summary>
    private static string RenderSql<TEntity>(object selection) where TEntity : class => ((ISelect<TEntity>)selection).ToSql();

    /// <summary>日期月、字符串月与普通日期时间同页，实际页开放保存、列表筛选及导入导出。</summary>
    private static MetadataCatalog CreateCatalog()
    {
        var catalog = GeneratorTests.Catalog();
        foreach (var column in catalog.ConfigColumns.Where(column => column.LogicalType == "id")) column.LogicalType = "int64";
        var page = catalog.ConfigPages.Single();
        page.IsNew = page.IsImport = page.IsExport = 1;
        page.Extra["isUpdate"] = JsonSerializer.SerializeToElement(1);
        page.Extra["exportMethod"] = JsonSerializer.SerializeToElement("list");
        Add("51", "billing_month", "账单月", "datetime", "month");
        Add("52", "period_code", "期间编码", "string", "month");
        Add("53", "event_time", "发生时刻", "datetime", "date_time");
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "51").Extra["defaultValue"] =
            JsonSerializer.SerializeToElement("2024-02");
        return RedundantMultiNameCacheTests.Published(catalog);

        void Add(string id, string name, string title, string type, string control)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = id, ConfigTableId = "1", ColumnName = name, Name = title, LogicalType = type, ControlKind = control
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "10" + id, ConfigPageId = page.Id, ConfigColumnId = id, IsShow = 1, IsEdit = 1,
                Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) }
            });
        }
    }

    /// <summary>实际加载原详情与列表 Vue，捕获交给 HTTP 的原提交与筛选对象。</summary>
    private static async Task RunProbe(string output, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "OriginalMonthProbe.mjs"));
        start.ArgumentList.Add(output);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
