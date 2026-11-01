using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;

namespace Metarill.Runtime.Exports;

/// <summary>只修正本服务生成的工作簿数值文本，不把浮点列改成文本或公式。</summary>
internal static class FloatingWorkbookWriter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    /// <summary>固定ClosedXML版本以G15输出double；仅收集会改变位型的原始浮点单元格。</summary>
    public static Dictionary<string, string> Corrections(PageExportRequest request, CancellationToken token)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var row = 0; row < request.Rows.Count; row++)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < request.Headers.Count; column++)
            {
                var value = request.Rows[row][column] switch { double number => number, float number => (double)number, _ => double.NaN };
                if (!double.IsFinite(value)) continue; // 非有限输入已由原单元格写入门槛拒绝。
                var legacy = value.ToString("G15", CultureInfo.InvariantCulture);
                if (double.TryParse(legacy, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && BitConverter.DoubleToInt64Bits(parsed) == BitConverter.DoubleToInt64Bits(value)) continue;
                result.Add(XLColumn(column + 1) + (row + 2).ToString(CultureInfo.InvariantCulture), ExactNumericValue.FormatDouble(value));
            }
        }
        return result;
    }

    /// <summary>流式复制自有ZIP，原路径不解压到磁盘；只有目标工作表的数值内容允许改变。</summary>
    public static async Task RewriteAsync(Stream source, Stream destination, Dictionary<string, string> corrections, CancellationToken token)
    {
        using var input = new ZipArchive(source, ZipArchiveMode.Read, true);
        using var output = new ZipArchive(destination, ZipArchiveMode.Create, true);
        var worksheetSeen = false;
        foreach (var entry in input.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            target.LastWriteTime = entry.LastWriteTime;
            await using var reader = entry.Open();
            await using var writer = target.Open();
            if (entry.FullName == WorksheetPath)
            {
                if (worksheetSeen) throw new InvalidOperationException("导出工作簿含重复的数值工作表。");
                worksheetSeen = true;
                RewriteWorksheet(reader, writer, corrections, token);
            }
            else await reader.CopyToAsync(writer, token);
        }
        if (!worksheetSeen || corrections.Count != 0) throw new InvalidOperationException("导出工作簿缺少待核对的浮点单元格。");
    }

    /// <summary>逐节点保留单元格类型和样式；目标必须是非公式数值且只能替换一个v节点。</summary>
    private static void RewriteWorksheet(Stream source, Stream destination, Dictionary<string, string> remaining, CancellationToken token)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        using var writer = XmlWriter.Create(destination, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        string? cell = null;
        var replaced = false;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            switch (reader.NodeType)
            {
                case XmlNodeType.XmlDeclaration: writer.WriteStartDocument(); break;
                case XmlNodeType.Element:
                    if (reader.NamespaceURI == SpreadsheetNamespace && reader.LocalName == "c")
                    {
                        var address = reader.GetAttribute("r");
                        cell = address is not null && remaining.ContainsKey(address) ? address : null;
                        replaced = false;
                        if (cell is not null && reader.GetAttribute("t") is { } type && type != "n") throw new InvalidOperationException("导出浮点单元格不是数值类型。");
                    }
                    if (cell is not null && reader.NamespaceURI == SpreadsheetNamespace && reader.LocalName == "f") throw new InvalidOperationException("导出浮点单元格不能包含公式。");
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    writer.WriteAttributes(reader, true);
                    if (cell is not null && reader.NamespaceURI == SpreadsheetNamespace && reader.LocalName == "v")
                    {
                        if (replaced || reader.IsEmptyElement) throw new InvalidOperationException("导出浮点单元格的数值节点不完整。");
                        writer.WriteString(remaining[cell]);
                        // 自有数值节点仅含文本；继续正常推进，不能跳过下一个单元格。
                        if (!reader.Read() || reader.NodeType != XmlNodeType.Text || !reader.Read() || reader.NodeType != XmlNodeType.EndElement || reader.LocalName != "v") throw new InvalidOperationException("导出浮点数值节点格式异常。");
                        writer.WriteEndElement();
                        replaced = true;
                    }
                    else if (reader.IsEmptyElement) writer.WriteEndElement();
                    break;
                case XmlNodeType.EndElement:
                    writer.WriteFullEndElement();
                    if (reader.NamespaceURI == SpreadsheetNamespace && reader.LocalName == "c" && cell is not null)
                    {
                        if (!replaced) throw new InvalidOperationException("导出浮点单元格缺少数值内容。");
                        remaining.Remove(cell); cell = null;
                    }
                    break;
                case XmlNodeType.Text: writer.WriteString(reader.Value); break;
                case XmlNodeType.CDATA: writer.WriteCData(reader.Value); break;
                case XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace: writer.WriteWhitespace(reader.Value); break;
                case XmlNodeType.Comment: writer.WriteComment(reader.Value); break;
                case XmlNodeType.ProcessingInstruction: writer.WriteProcessingInstruction(reader.Name, reader.Value); break;
            }
        }
    }

    /// <summary>只由受限列号生成Excel地址，不接收客户端单元格路径。</summary>
    private static string XLColumn(int column)
    {
        var result = "";
        while (column > 0) { column--; result = (char)('A' + column % 26) + result; column /= 26; }
        return result;
    }
}