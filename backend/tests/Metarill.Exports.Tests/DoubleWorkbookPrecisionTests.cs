using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using Xunit;

namespace Metarill.Exports.Tests;

public sealed partial class DoubleWorkbookPrecisionTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    /// <summary>记录矩阵规模供TRX审查，不改变原始导出值。</summary>
    public DoubleWorkbookPrecisionTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;
    /// <summary>走真实票据导出后同时核对XLSX数值节点与库读取，避免只检查内存单元格。</summary>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("fr-FR")]
    public async Task Finite_double_matrix_preserves_numeric_cells_and_binary_values_in_real_xlsx(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var values = Values();
            output.WriteLine($"区域{culture}：{values.Length}个不同有限位型逐项核对XML和工作簿读取。");
            using var fixture = new ExportFixture();
            var rows = values.Select(value => new object?[] { value, BitConverter.DoubleToUInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture) }).ToArray();
            var ticket = await fixture.Service.CreateAsync(new("page1", "浮点数值往返", ["测量值", "原始位型"], rows), fixture.Actor);
            var content = await fixture.Service.OpenAsync(ticket, fixture.Actor);
            await using (content.Stream)
            {
                using var archive = new ZipArchive(content.Stream, ZipArchiveMode.Read, true);
                using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                var xml = XDocument.Load(stream);
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var cells = xml.Descendants(ns + "c").Where(cell => ((string?)cell.Attribute("r"))?.StartsWith('A') == true).Skip(1).ToArray();
                Assert.Equal(values.Length, cells.Length);
                for (var index = 0; index < values.Length; index++)
                {
                    Assert.Equal("A" + (index + 2).ToString(CultureInfo.InvariantCulture), (string?)cells[index].Attribute("r"));
                    Assert.True(cells[index].Attribute("t") is null || (string?)cells[index].Attribute("t") == "n");
                    Assert.Empty(cells[index].Elements(ns + "f"));
                    var text = Assert.Single(cells[index].Elements(ns + "v")).Value;
                    var parsed = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    Assert.True(BitConverter.DoubleToUInt64Bits(values[index]) == BitConverter.DoubleToUInt64Bits(parsed), $"XLSX第{index + 2}行数值文本失真，原位型{rows[index][1]}，文本{text}。");
                }
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("xl/externalLinks", StringComparison.Ordinal));
            }
            var reopened = await fixture.Service.OpenAsync(ticket, fixture.Actor);
            await using (reopened.Stream)
            {
                using var workbook = new XLWorkbook(reopened.Stream);
                var sheet = workbook.Worksheet(1);
                for (var index = 0; index < values.Length; index++)
                {
                    var cell = sheet.Cell(index + 2, 1);
                    Assert.Equal(XLDataType.Number, cell.DataType);
                    Assert.Equal(BitConverter.DoubleToUInt64Bits(values[index]), BitConverter.DoubleToUInt64Bits(cell.GetDouble()));
                    Assert.Equal(rows[index][1], sheet.Cell(index + 2, 2).GetString());
                    Assert.False(cell.HasFormula);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    /// <summary>覆盖已知相邻值、全指数正负幂、极值和固定随机有限位型；数值零不要求Excel保留符号。</summary>
    private static double[] Values()
    {
        var bits = new HashSet<ulong> { 0, 1, 0x0010_0000_0000_0000, 0x000f_ffff_ffff_ffff, 0x0410_0000_0000_0000, 0x040f_ffff_ffff_ffff, 0x7fef_ffff_ffff_ffff };
        foreach (var value in bits.ToArray().Where(value => value != 0)) bits.Add(value | 0x8000_0000_0000_0000);
        for (var exponent = -1074; exponent <= 1023; exponent++)
        {
            var value = Math.ScaleB(1d, exponent);
            bits.Add(BitConverter.DoubleToUInt64Bits(value));
            bits.Add(BitConverter.DoubleToUInt64Bits(-value));
        }
        var random = new Random(20261102);
        var buffer = new byte[8];
        for (var index = 0; index < 4096; index++)
        {
            random.NextBytes(buffer);
            var value = BitConverter.ToUInt64(buffer);
            if (double.IsFinite(BitConverter.UInt64BitsToDouble(value)) && (value & 0x7fff_ffff_ffff_ffff) != 0) bits.Add(value);
        }
        return bits.Order().Select(BitConverter.UInt64BitsToDouble).ToArray();
    }
}
