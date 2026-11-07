using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Runtime;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class GeneratedDoubleValueTests
{
    /// <summary>真实票据XLSX经原SheetJS选择方法再进入实际生成导入器，核对相邻值及极值。</summary>
    private static async Task AssertOriginalWorkbookImport(GeneratedMutationGuardRun run, string directory, [CallerFilePath] string source = "")
    {
        double[] values = [BitConverter.UInt64BitsToDouble(0x0410_0000_0000_0000), BitConverter.UInt64BitsToDouble(0x040f_ffff_ffff_ffff),
            -BitConverter.UInt64BitsToDouble(0x0410_0000_0000_0000), -BitConverter.UInt64BitsToDouble(0x040f_ffff_ffff_ffff), double.Epsilon, double.MaxValue, -double.MaxValue, 0.1d];
        var exports = new ExcelPageExportService(new() { RootDirectory = Path.Combine(directory, "private-exports") });
        var ticket = await exports.CreateAsync(new("101", "原测量导入", ["测量上值"], values.Select(value => (IReadOnlyList<object?>)new object?[] { value }).ToArray()), run.Actor);
        var workbook = Path.Combine(directory, "measurements.xlsx");
        var content = await exports.OpenAsync(ticket, run.Actor);
        await using (content.Stream)
        await using (var target = File.Create(workbook)) await content.Stream.CopyToAsync(target);
        var expected = Path.Combine(directory, "expected-import.json");
        await File.WriteAllTextAsync(expected, JsonSerializer.Serialize(values.Select(value => new Dictionary<string, double> { ["测量上值"] = value }), JsonDefaults.Options));
        var actual = Path.Combine(directory, "original-import.json");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { Path.Combine(Path.GetDirectoryName(source)!, "OriginalExcelImportProbe.mjs"), NonRedundantApplicationFixture.Root(), workbook, expected, actual }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(actual));
        Assert.Equal(values.Length, document.RootElement.GetArrayLength());
        var service = run.Service([]);
        var parse = service.GetType().GetMethod("ParseImport101Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var index = 0; index < values.Length; index++)
        {
            var row = document.RootElement[index];
            Assert.Equal(BitConverter.DoubleToUInt64Bits(values[index]), BitConverter.DoubleToUInt64Bits(row.GetProperty("测量上值").GetDouble()));
            object?[] arguments = parse.GetParameters().Length == 4 ? [row, null, run.Actor, CancellationToken.None] : [row, null, run.Actor, null, false, CancellationToken.None];
            var task = (Task)parse.Invoke(service, arguments)!; await task;
            var parsed = Assert.IsAssignableFrom<IReadOnlyDictionary<string, JsonElement>>(task.GetType().GetProperty("Result")!.GetValue(task));
            Assert.Equal(BitConverter.DoubleToUInt64Bits(values[index]), BitConverter.DoubleToUInt64Bits(parsed["score"].GetDouble()));
        }
    }
}