using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Runtime;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class DoublePersistenceNativeMySqlTests
{
    /// <summary>实际票据XLSX下载、重新读取、原SheetJS回调与原可编辑数值列导入，不改页面解析参数。</summary>
    private static async Task VerifyExport(HttpClient client, string route, Metarill.Core.Contracts.PageRequest request, string upperHeader, string lowerHeader, double upper, double lower, string evidence, [CallerFilePath] string sourceFile = "")
    {
        var isSource = route == SourceRoute;
        // 下载前先核对原页面当前值，区分夹具状态错误、数据库读取错误和真正的工作簿失真。
        var page = await Send(client, HttpMethod.Post, route + "page", request);
        var beforeExport = Assert.Single(page.GetProperty("data").GetProperty("records").EnumerateArray());
        Bits(upper, beforeExport.GetProperty(isSource ? "measurement" : "amount").GetDouble());
        Bits(lower, beforeExport.GetProperty(isSource ? "measurementPrevious" : "previousAmount").GetDouble());
        if (isSource) Bits(lower, beforeExport.GetProperty("customerAmount").GetDouble());
        var grant = (await Post(client, route + "initExport", request)).GetString()!;
        using var response = await client.GetAsync(ExportDownloadAccess.DownloadPath + "?exportId=" + Uri.EscapeDataString(grant)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(); var tag = isSource ? "nonred" : "ordinary";
        var path = Path.Combine(evidence, "double-" + tag + ".xlsx"); await File.WriteAllBytesAsync(path, bytes);
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);
        using (var stream = new MemoryStream(bytes))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(1); Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
            var headers = sheet.Row(1).CellsUsed().ToDictionary(cell => cell.GetString(), cell => cell.Address.ColumnNumber, StringComparer.Ordinal);
            foreach (var (header, value) in new[] { (upperHeader, upper), (lowerHeader, lower) })
            {
                var cell = sheet.Cell(2, headers[header]); Assert.Equal(XLDataType.Number, cell.DataType); Bits(value, cell.GetDouble()); Assert.False(cell.HasFormula);
            }
            if (isSource) Bits(lower, sheet.Cell(2, headers["customer_amount"]).GetDouble());
            foreach (var pair in headers)
            {
                var cell = sheet.Cell(2, pair.Value); if (cell.IsEmpty()) continue;
                expected[pair.Key] = cell.DataType switch { XLDataType.Number => cell.GetDouble(), XLDataType.Boolean => cell.GetBoolean(), XLDataType.Text => cell.GetString(), _ => throw new InvalidOperationException("数值工作簿出现未声明类型。") };
            }
        }
        var expectedPath = Path.Combine(evidence, tag + "-xlsx-expected.json"); var actualPath = Path.Combine(evidence, tag + "-xlsx-original.json"); await File.WriteAllTextAsync(expectedPath, JsonSerializer.Serialize(new[] { expected }, JsonDefaults.Options));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { Path.Combine(Path.GetDirectoryName(sourceFile)!, "OriginalExcelImportProbe.mjs"), NonRedundantApplicationFixture.Root(), path, expectedPath, actualPath }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(actualPath)); var row = Assert.Single(document.RootElement.EnumerateArray());
        Bits(upper, row.GetProperty(upperHeader).GetDouble()); Bits(lower, row.GetProperty(lowerHeader).GetDouble());
        // 派生列维持原只读门禁；这里只将原回调中的已开放数值列送入真正的生成导入器。
        var editable = Row(isSource ? "记录名" : "name", isSource ? "xlsx-nonred-roundtrip" : "xlsx-ordinary-roundtrip", upperHeader, row.GetProperty(upperHeader).Clone(), lowerHeader, row.GetProperty(lowerHeader).Clone());
        await Send(client, HttpMethod.Post, route + "importDatas" + (isSource ? "101?parentId=71" : "201"), new { parentId = isSource ? "71" : null, requestJson = new[] { editable } });
        var result = await Send(client, HttpMethod.Post, route + "page", Request(isSource ? "xlsx-nonred-roundtrip" : "xlsx-ordinary-roundtrip", isSource ? "71" : null));
        var written = Assert.Single(result.GetProperty("data").GetProperty("records").EnumerateArray()); Bits(upper, written.GetProperty(isSource ? "measurement" : "amount").GetDouble()); Bits(lower, written.GetProperty(isSource ? "measurementPrevious" : "previousAmount").GetDouble());
    }
}