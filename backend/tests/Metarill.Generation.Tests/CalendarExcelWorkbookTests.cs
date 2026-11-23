using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Data;
using Metarill.Core.Persistence;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class CalendarExcelWorkbookTests
{
    /// <summary>真实生成列表或详情导出经生产 XLSX 和原 SheetJS 回调，进入标准追加或首列覆盖。</summary>
    [Theory]
    [InlineData("list", "OnlyNew", 1_234_560, "2026-11-24T12:34:00.123456")]
    [InlineData("edit", "OverWriteByFirstColumn", 1_234_570, "2026-11-24T12:34:00.123457")]
    [InlineData("edit", "OverWriteByFirstColumn", 0, "2026-11-24T12:34")]
    public async Task Calendar_workbook_roundtrips_the_original_export_and_import(string exportMode, string importMode, long fraction, string expectedDateTime)
    {
        var catalog = GeneratorTests.Catalog();
        var page = catalog.ConfigPages.Single();
        page.IsExport = page.IsImport = 1;
        page.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        page.Extra["importMethod"] = JsonSerializer.SerializeToElement(importMode);
        catalog.ConfigColumns.AddRange(
        [
            new() { Id = "90", ConfigTableId = "1", ColumnName = "starts_on", Name = "启用日期", LogicalType = "date", ControlKind = "date" },
            new() { Id = "91", ConfigTableId = "1", ColumnName = "starts_at", Name = "启用时间", LogicalType = "datetime", ColumnType = "datetime(6)", ControlKind = "date_time", SortNo = -10 }
        ]);
        catalog.ConfigPageColumns.AddRange(
        [
            new() { Id = "190", ConfigPageId = "101", ConfigColumnId = "90", IsShow = 1, IsEdit = 1 },
            new() { Id = "191", ConfigPageId = "101", ConfigColumnId = "91", IsShow = 1, IsEdit = 1, SortNo = -10 }
        ]);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-calendar-excel");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.GuardContract");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run = new GeneratedMutationGuardRun(assembly);
            var date = new DateTime(2024, 2, 29);
            var timestamp = new DateTime(2026, 11, 24, 12, 34, 0).AddTicks(fraction);
            run.SeedRow = entity =>
            {
                var type = entity.GetType();
                type.GetProperty("Id")!.SetValue(entity, 1L);
                type.GetProperty("Name")!.SetValue(entity, "日历样本");
                type.GetProperty("Remark")!.SetValue(entity, "原备注");
                type.GetProperty("StartsOn")!.SetValue(entity, date);
                type.GetProperty("StartsAt")!.SetValue(entity, timestamp);
            };
            var observer = new ImportedCalendarRow();
            var service = run.Service([], [observer]);
            var export = await (Task<PageExportRequest>)service.GetType().GetMethod("Export101Async")!
                .Invoke(service, [new PageRequest(), run.Actor, 10, CancellationToken.None])!;
            var exports = new ExcelPageExportService(new() { RootDirectory = Path.Combine(directory, "storage") });
            var ticket = await exports.CreateAsync(export, run.Actor);
            var content = await exports.OpenAsync(ticket, run.Actor);
            var book = Path.Combine(directory, "calendar.xlsx");
            await using (content.Stream)
            await using (var file = File.Create(book)) await content.Stream.CopyToAsync(file);
            var headerColumns = export.Headers.Select((title, index) => (title, index))
                .ToDictionary(pair => pair.title, pair => pair.index + 1);
            using (var workbook = new XLWorkbook(book))
            {
                var sheet = workbook.Worksheet(1);
                var dateCell = sheet.Cell(2, headerColumns["启用日期"]);
                var timeCell = sheet.Cell(2, headerColumns["启用时间"]);
                Assert.Equal(XLDataType.Text, dateCell.DataType);
                Assert.Equal("2024-02-29", dateCell.GetString());
                Assert.Equal(XLDataType.Text, timeCell.DataType);
                Assert.Equal(expectedDateTime, timeCell.GetString());
            }

            var captured = Path.Combine(directory, "original-callback.json");
            await ReadOriginalWorkbookAsync(book, new[]
            {
                new Dictionary<string, string>
                {
                    ["名称"] = "日历样本", ["备注"] = "原备注", ["启用日期"] = "2024-02-29", ["启用时间"] = expectedDateTime
                }
            }, captured);

            var requestType = assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101ImportRequest", true)!;
            var request = Activator.CreateInstance(requestType)!;
            requestType.GetProperty("RequestJson")!.SetValue(request, JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(captured)));
            await run.Invoke(service, RecordMutationOperation.Import, request);
            var written = Assert.Single(observer.Written);
            Assert.Equal(date, Assert.IsType<DateTime>(written.SubmittedFields["starts_on"]));
            Assert.Equal(timestamp, Assert.IsType<DateTime>(written.SubmittedFields["starts_at"]));
            Assert.Equal(importMode == "OnlyNew", written.IsCreate);
            Assert.Contains(importMode == "OnlyNew" ? "write:Insert" : "write:Update", run.Trace);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>覆盖工作簿日期的跨日偏移、夏令时缺口及普通单元格。</summary>
    [Theory]
    [InlineData("Asia/Shanghai")]
    [InlineData("America/Los_Angeles")]
    public async Task Native_calendar_cells_preserve_wall_clock(string timeZone)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-calendar-excel");
        var directory = Directory.CreateDirectory(Path.Combine(boundary, Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var book = Path.Combine(directory, "native-calendar.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("日期样本");
                var headers = new[] { "日期", "日期时间", "数量", "启用", "原文本" };
                for (var column = 0; column < headers.Length; column++)
                    sheet.Cell(1, column + 1).Value = headers[column];

                sheet.Cell(2, 1).Value = new DateTime(2026, 11, 24);
                sheet.Cell(2, 1).Style.DateFormat.Format = "yyyy-mm-dd";
                sheet.Cell(2, 2).Value = new DateTime(2026, 3, 8, 2, 30, 0, 125);
                sheet.Cell(2, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss.000";
                sheet.Cell(2, 3).Value = 42;
                sheet.Cell(2, 4).Value = true;
                sheet.Cell(2, 5).Value = "2026-11-24T01:00:00Z";
                workbook.SaveAs(book);
            }

            await ReadOriginalWorkbookAsync(book, new[]
            {
                new Dictionary<string, object>
                {
                    ["日期"] = "2026-11-24T00:00:00.000", ["日期时间"] = "2026-03-08T02:30:00.125",
                    ["数量"] = 42, ["启用"] = true, ["原文本"] = "2026-11-24T01:00:00Z"
                }
            }, timeZone: timeZone);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>从真实 XLS 和 XLSX 取回日期序列的毫秒，覆盖两种日期系统、进位和显式日期。</summary>
    [Theory]
    [InlineData("xls", false)]
    [InlineData("xls", true)]
    [InlineData("xlsx", true)]
    public async Task Native_excel_serials_round_to_the_nearest_millisecond(string extension, bool date1904)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-calendar-excel");
        var directory = Directory.CreateDirectory(Path.Combine(boundary, Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var book = Path.Combine(directory, $"native-calendar.{extension}");
            var root = NonRedundantApplicationFixture.Root();
            var start = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node", root);
            foreach (var argument in new[] { Path.Combine(root, "backend/tests/Metarill.Generation.Tests/NativeCalendarWorkbookFixture.mjs"), root, book, date1904 ? "1904" : "1900" })
                start.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(start);

            var row = new Dictionary<string, object>
            {
                ["Stamp"] = "2026-03-08T02:30:00.125", ["Rollover"] = "2026-11-25T00:00:00.000", ["Number"] = 42
            };
            if (extension == "xlsx")
                row["Explicit"] = "2026-11-24T12:34:00.125";

            await ReadOriginalWorkbookAsync(book, new[] { row }, timeZone: "America/Los_Angeles");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>CSV 保留输入的日期与微秒文本，同时维持数字和布尔值的原解析类型。</summary>
    [Fact]
    public async Task Csv_calendar_text_preserves_digits_without_coercing_other_values()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-calendar-excel");
        var directory = Directory.CreateDirectory(Path.Combine(boundary, Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var book = Path.Combine(directory, "native-calendar.CSV");
            await File.WriteAllTextAsync(book, "Date,Stamp,Number,Flag\n2026-11-24,2026-03-08T02:30:00.123456,42,TRUE\n");
            await ReadOriginalWorkbookAsync(book, new[]
            {
                new Dictionary<string, object>
                {
                    ["Date"] = "2026-11-24", ["Stamp"] = "2026-03-08T02:30:00.123456", ["Number"] = 42, ["Flag"] = true
                }
            }, timeZone: "America/Los_Angeles");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>生成页面标注 TIME 标题，真实工作簿经原上传回调进入追加或首列覆盖事务。</summary>
    [Theory]
    [InlineData("OnlyNew")]
    [InlineData("OverWriteByFirstColumn")]
    public async Task Native_elapsed_time_cells_execute_generated_imports(string importMode)
    {
        var catalog = ClockTimeGenerationTests.Catalog();
        catalog.ConfigPages.Single().Extra["importMethod"] = JsonSerializer.SerializeToElement(importMode);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-calendar-excel");
        var directory = Directory.CreateDirectory(Path.Combine(boundary, Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, "Example.GuardContract");
            var list = Assert.Single(Directory.EnumerateFiles(output, "*-list-101.vue", SearchOption.AllDirectories));
            var button = Assert.Single(await File.ReadAllLinesAsync(list), line => line.Contains("chooseImportFile(", StringComparison.Ordinal));
            var hint = Regex.Match(button, @"\}\);\},(\[[^\]]+\])\);");
            Assert.True(hint.Success, "必须由原导入按钮传递 TIME 字段标题。");
            var titles = JsonSerializer.Deserialize<string[]>(hint.Groups[1].Value)!;
            Assert.Equal(new[] { "时刻", "时长", "默认时刻" }.Order(StringComparer.Ordinal), titles.Order(StringComparer.Ordinal));

            var valid = Path.Combine(directory, "native-time.xlsx");
            var negativeDuration = importMode == "OverWriteByFirstColumn";
            WriteNativeTimeWorkbook(valid, crossDay: false, negativeDuration: negativeDuration);
            var duration = negativeDuration ? "-30:45:10.250001" : "30:45:10.250001";
            var payload = Path.Combine(directory, "original-callback.json");
            await ReadOriginalWorkbookAsync(valid, new[]
            {
                new Dictionary<string, string> { ["名称"] = "本轮", ["时刻"] = "09:15:03.125456", ["时长"] = duration, ["默认时刻"] = "2026-03-08T02:30:00.125", ["备注"] = "1899-12-31T01:00:00.000" }
            }, payload, "America/Los_Angeles", titles);


            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            object Request()
            {
                var type = assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101ImportRequest", true)!;
                var request = Activator.CreateInstance(type)!;
                type.GetProperty("RequestJson")!.SetValue(request, JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(payload)));
                return request;
            }

            using var run = new GeneratedMutationGuardRun(assembly, new MySqlDatabaseProvider().ValidateTimeValue);
            run.SeedRow = entity =>
            {
                entity.GetType().GetProperty("Id")!.SetValue(entity, 1L);
                entity.GetType().GetProperty("Name")!.SetValue(entity, "本轮");
            };
            var observer = new ImportedCalendarRow();
            var service = run.Service([], [observer]);
            await run.Invoke(service, RecordMutationOperation.Import, Request());
            var written = Assert.Single(observer.Written);
            Assert.Equal(ClockTimeValue.ParseText("09:15:03.125456"), written.SubmittedFields["clock"]);
            Assert.Equal(SqlTimeValue.ParseText(duration), written.SubmittedFields["duration"]);
            Assert.Equal(ClockTimeValue.ParseText("02:30:00.125"), written.SubmittedFields["clock_default"]);
            Assert.Equal(importMode == "OnlyNew", written.IsCreate);

            var negative = Path.Combine(directory, "invalid-clock.xlsx");
            WriteNativeTimeWorkbook(negative, crossDay: true, negativeDuration: true);
            await ReadOriginalWorkbookAsync(negative, new[]
            {
                new Dictionary<string, string> { ["名称"] = "本轮", ["时刻"] = "25:15:00", ["时长"] = "-30:45:10.250001", ["默认时刻"] = "2026-03-08T02:30:00.125", ["备注"] = "1899-12-31T01:00:00.000" }
            }, payload, "Asia/Shanghai", titles);
            var error = await Assert.ThrowsAsync<PageImportException>(() => run.Invoke(service, RecordMutationOperation.Import, Request()));
            Assert.Equal("时刻", error.ColumnName);
            Assert.Single(observer.Written);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>使用有列格式的数值创建真实日期与原生时长，普通字段也含相同时间格式。</summary>
    private static void WriteNativeTimeWorkbook(string path, bool crossDay, bool negativeDuration)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("时长样本");
        var headers = new[] { "名称", "时刻", "时长", "默认时刻", "备注" };
        for (var index = 0; index < headers.Length; index++)
            sheet.Cell(1, index + 1).Value = headers[index];

        sheet.Cell(2, 1).Value = "本轮";
        sheet.Cell(2, 2).Value = (crossDay ? 25 * 3600 + 15 * 60 : 9 * 3600 + 15 * 60 + 3.125456) / 86_400;
        sheet.Cell(2, 2).Style.DateFormat.Format = "hh:mm:ss.000000";
        sheet.Cell(2, 3).Value = (negativeDuration ? -1 : 1) * (30 * 3600 + 45 * 60 + 10.250001) / 86_400;
        sheet.Cell(2, 3).Style.DateFormat.Format = "[h]:mm:ss.000000;[Red]-[h]:mm:ss.000000";
        sheet.Cell(2, 4).Value = new DateTime(2026, 3, 8, 2, 30, 0, 125);
        sheet.Cell(2, 4).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss.000";
        sheet.Cell(2, 5).Value = 1.0 / 24;
        sheet.Cell(2, 5).Style.DateFormat.Format = "hh:mm:ss";
        workbook.SaveAs(path);
    }

    /// <summary>从真实工作簿执行原文件选择回调，指定时区只作用于本轮 Node 进程。</summary>
    private static async Task ReadOriginalWorkbookAsync(string workbook, object rows, string? outputPath = null, string? timeZone = null, string[]? timeHeaders = null)
    {
        var expected = Path.Combine(Path.GetDirectoryName(workbook)!, "expected.json");
        await File.WriteAllTextAsync(expected, JsonSerializer.Serialize(rows));
        var root = NonRedundantApplicationFixture.Root();
        var start = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node", root);
        if (timeZone is not null)
            start.Environment["TZ"] = timeZone;
        if (timeHeaders is not null)
            start.Environment["METARILL_EXCEL_TIME_HEADERS"] = JsonSerializer.Serialize(timeHeaders);

        foreach (var argument in new[] { Path.Combine(root, "backend/tests/Metarill.Generation.Tests/OriginalExcelImportProbe.mjs"), root, workbook, expected })
            start.ArgumentList.Add(argument);
        if (outputPath is not null)
            start.ArgumentList.Add(outputPath);

        await NonRedundantApplicationFixture.RunAsync(start);
    }

    private sealed class ImportedCalendarRow : IRecordMutationObserver
    {
        internal List<RecordMutationContext> Written { get; } = [];

        /// <summary>只记录标准 Service 送入写事务的最终类型化值。</summary>
        public Task BeforeCommitAsync(RecordMutationContext context, CancellationToken cancellationToken = default)
        {
            Written.Add(context);
            return Task.CompletedTask;
        }
    }
}
