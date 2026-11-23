using System.Reflection;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
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

            var expected = Path.Combine(directory, "expected.json");
            await File.WriteAllTextAsync(expected, JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, string>
                {
                    ["名称"] = "日历样本", ["备注"] = "原备注", ["启用日期"] = "2024-02-29", ["启用时间"] = expectedDateTime
                }
            }));
            var root = NonRedundantApplicationFixture.Root();
            var start = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node", root);
            var captured = Path.Combine(directory, "original-callback.json");
            foreach (var argument in new[] { Path.Combine(root, "backend/tests/Metarill.Generation.Tests/OriginalExcelImportProbe.mjs"), root, book, expected, captured })
                start.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(start);

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
