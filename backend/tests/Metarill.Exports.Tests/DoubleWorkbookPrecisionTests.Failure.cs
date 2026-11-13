using System.Collections;
using System.IO.Compression;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Exports.Tests;

public sealed partial class DoubleWorkbookPrecisionTests
{
    /// <summary>修正浮点文本不能改变大ID、精确金额、公式样式字符串或日期的原导出规则。</summary>
    [Fact]
    public async Task Corrected_workbook_keeps_other_types_and_float_values_unchanged()
    {
        using var fixture = new ExportFixture();
        object?[] values = [BitConverter.UInt64BitsToDouble(0x0410_0000_0000_0000), 0.1f, float.MaxValue, float.Epsilon, 9007199254740993L,
            123456789012345678.123456789m, "=SUM(A1:A2)", null, true, 0.1d, new DateTime(2026, 11, 2, 13, 45, 20)];
        byte[] original = [];
        var row = new ObservedRow(values, () => original = File.ReadAllBytes(Assert.Single(Directory.GetFiles(fixture.Root, "*.part"))));
        var ticket = await fixture.Service.CreateAsync(new("p", "混合精度", Enumerable.Range(0, values.Length).Select(index => "列" + index).ToArray(), [row]), fixture.Actor);
        var content = await fixture.Service.OpenAsync(ticket, fixture.Actor);
        await using (content.Stream)
        {
            using var workbook = new XLWorkbook(content.Stream);
            var sheet = workbook.Worksheet(1);
            for (var index = 0; index < 4; index++)
            {
                var expected = values[index] is double number ? number : (double)(float)values[index]!;
                Assert.Equal(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(sheet.Cell(2, index + 1).GetDouble()));
                Assert.Equal(XLDataType.Number, sheet.Cell(2, index + 1).DataType);
            }
            Assert.Equal("9007199254740993", sheet.Cell(2, 5).GetString());
            Assert.Equal("123456789012345678.123456789", sheet.Cell(2, 6).GetString());
            Assert.Equal("=SUM(A1:A2)", sheet.Cell(2, 7).GetString()); Assert.False(sheet.Cell(2, 7).HasFormula);
            Assert.True(sheet.Cell(2, 8).IsEmpty()); Assert.True(sheet.Cell(2, 9).GetBoolean());
            Assert.Equal(0.1d, sheet.Cell(2, 10).GetDouble()); Assert.Equal(values[10], sheet.Cell(2, 11).GetDateTime());
            Assert.True(sheet.Row(1).Style.Font.Bold); Assert.Equal(1, sheet.SheetView.SplitRow); Assert.Equal(22, sheet.Column(1).Width);
        }
        Assert.NotEmpty(original);
        var corrected = await fixture.Service.OpenAsync(ticket, fixture.Actor);
        await using (corrected.Stream)
        {
            using var originalStream = new MemoryStream(original);
            using var before = new ZipArchive(originalStream, ZipArchiveMode.Read);
            using var after = new ZipArchive(corrected.Stream, ZipArchiveMode.Read, true);
            Assert.Equal(before.Entries.Select(entry => entry.FullName), after.Entries.Select(entry => entry.FullName));
            foreach (var entry in before.Entries.Where(entry => entry.FullName != "xl/worksheets/sheet1.xml"))
            {
                using var left = entry.Open(); using var right = after.GetEntry(entry.FullName)!.Open();
                using var leftBytes = new MemoryStream(); using var rightBytes = new MemoryStream();
                await left.CopyToAsync(leftBytes); await right.CopyToAsync(rightBytes);
                Assert.Equal(leftBytes.ToArray(), rightBytes.ToArray());
            }
        }
    }

    /// <summary>原文件已落盘后取消或读取故障，仍清理未签票据对象并释放预留额度。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Correction_failure_cleans_unpublished_files_and_releases_ticket(bool cancel)
    {
        using var fixture = new ExportFixture(new ExportOptions { MaxActiveTickets = 1, MaxConcurrentExports = 1 });
        using var cancellation = new CancellationTokenSource();
        var entered = false;
        var row = new ObservedRow(BitConverter.UInt64BitsToDouble(0x0410_0000_0000_0000), () =>
        {
            Assert.True(new FileInfo(Assert.Single(Directory.GetFiles(fixture.Root, "*.part"))).Length > 0);
            entered = true;
            if (cancel) cancellation.Cancel(); else throw new IOException("测试末段读取失败");
        });
        var request = new PageExportRequest("p", "末段失败", ["测量值"], [row]);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateAsync(request, fixture.Actor, cancellation.Token));
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(request, fixture.Actor));
            Assert.Equal("导出文件写入失败，请检查私有存储。", error.Message);
            Assert.IsType<IOException>(error.InnerException);
        }
        Assert.True(entered); Assert.Empty(Directory.GetFiles(fixture.Root));
        Assert.NotEmpty(await fixture.Service.CreateAsync(new("p", "恢复", ["列"], [["正常"]]), fixture.Actor));
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    /// <summary>修正后压缩包也必须实时受文件限额约束，不能先签票据再检查大小。</summary>
    [Fact]
    public async Task Corrected_file_limit_is_enforced_after_original_file_succeeds()
    {
        long originalSize = 0;
        var values = Values().Where(value => Math.Abs(value) > 1e-280 && Math.Abs(value) < 1e280).TakeLast(2000).ToArray();
        PageExportRequest Request(Action observed) => new("p", "大小限制", ["测量值"], values.Select((value, index) => index == 0 ? (IReadOnlyList<object?>)new ObservedRow(value, observed) : [value]).ToArray());
        long correctedSize;
        using (var fixture = new ExportFixture())
        {
            var ticket = await fixture.Service.CreateAsync(Request(() => originalSize = new FileInfo(Assert.Single(Directory.GetFiles(fixture.Root, "*.part"))).Length), fixture.Actor);
            var content = await fixture.Service.OpenAsync(ticket, fixture.Actor); correctedSize = content.Length;
            await content.Stream.DisposeAsync();
        }
        Assert.True(correctedSize > originalSize + 1024, $"原文件{originalSize}，修正文件{correctedSize}，未形成独立末段限额样本。");
        var entered = false;
        using var limited = new ExportFixture(new ExportOptions { MaxFileBytes = (originalSize + correctedSize) / 2, MaxActiveTickets = 1 });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => limited.Service.CreateAsync(Request(() => entered = true), limited.Actor));
        Assert.Equal("导出文件超过大小上限。", error.Message); Assert.True(entered); Assert.Empty(Directory.GetFiles(limited.Root));
        Assert.NotEmpty(await limited.Service.CreateAsync(new("p", "恢复", ["列"], [["正常"]]), limited.Actor));
        Assert.Single(Directory.GetFiles(limited.Root));
    }

    /// <summary>只在第二次数值读取时观察已保存的原文件，不增加产品测试钩子。</summary>
    private sealed class ObservedRow(IReadOnlyList<object?> values, Action observed) : IReadOnlyList<object?>
    {
        private int reads;
        public ObservedRow(double value, Action observed) : this(new object?[] { value }, observed) { }
        public int Count => values.Count;
        public object? this[int index] { get { if (index == 0 && ++reads == 2) observed(); return values[index]; } }
        public IEnumerator<object?> GetEnumerator() { for (var index = 0; index < Count; index++) yield return this[index]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}