using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;

namespace Metarill.Runtime.Exports;

/// <summary>只写入显式 Excel 类型，任何字符串都不能成为公式或外部链接。</summary>
internal static class ExportCellWriter
{
    /// <summary>大整数和高精度小数以文本保存，避免 Excel 的15位数字精度损坏业务标识或金额。</summary>
    public static void Write(IXLCell cell, object? value, ExportOptions options, ref long textCharacters)
    {
        switch (value)
        {
            case null or DBNull: return;
            case string text: Text(cell, text, options, ref textCharacters); return;
            case char character: Text(cell, character.ToString(), options, ref textCharacters); return;
            case bool boolean: cell.Value = boolean; return;
            case byte or sbyte or short or ushort or int or uint: cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture); return;
            case long number:
                if (number is >= -999_999_999_999_999L and <= 999_999_999_999_999L) cell.Value = number;
                else Text(cell, number.ToString(CultureInfo.InvariantCulture), options, ref textCharacters);
                return;
            case ulong number:
                if (number <= 999_999_999_999_999UL) cell.Value = number;
                else Text(cell, number.ToString(CultureInfo.InvariantCulture), options, ref textCharacters);
                return;
            case decimal number:
                var numeric = number.ToString(CultureInfo.InvariantCulture);
                if (SignificantDigits(numeric) <= 15) cell.Value = number;
                else Text(cell, numeric, options, ref textCharacters);
                return;
            case double number when double.IsFinite(number): cell.Value = number; return;
            case float number when float.IsFinite(number): cell.Value = number; return;
            case double or float: throw new InvalidOperationException("Excel 不支持 NaN 或无穷大。");
            case DateTime date when date.Year >= 1900:
                cell.Value = date; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss.000"; return;
            case DateTime date: Text(cell, date.ToString("O", CultureInfo.InvariantCulture), options, ref textCharacters); return;
            case DateOnly date when date.Year >= 1900:
                cell.Value = date.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; return;
            case DateOnly date: Text(cell, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), options, ref textCharacters); return;
            case DateTimeOffset date: Text(cell, date.ToString("O", CultureInfo.InvariantCulture), options, ref textCharacters); return;
            case TimeOnly time:
                cell.Value = time.ToTimeSpan(); cell.Style.NumberFormat.Format = "hh:mm:ss.000"; return;
            case TimeSpan duration when duration >= TimeSpan.Zero:
                cell.Value = duration; cell.Style.NumberFormat.Format = "[h]:mm:ss.000"; return;
            case TimeSpan duration: Text(cell, duration.ToString("c", CultureInfo.InvariantCulture), options, ref textCharacters); return;
            case Guid id: Text(cell, id.ToString("D"), options, ref textCharacters); return;
            case Enum enumeration: Text(cell, enumeration.ToString(), options, ref textCharacters); return;
            case byte[] bytes:
                if ((long)bytes.Length * 4 / 3 > options.MaxCellCharacters) throw new InvalidOperationException("二进制字段超过导出单元格上限。");
                Text(cell, Convert.ToBase64String(bytes), options, ref textCharacters); return;
            case JsonElement json: WriteJson(cell, json, options, ref textCharacters); return;
            default: throw new NotSupportedException("导出值包含未经声明的复杂类型，请由页投影显式格式化。");
        }
    }

    /// <summary>文本原样存储且设置引号前缀；不向 FormulaA1、超链接或名称赋值。</summary>
    private static void Text(IXLCell cell, string text, ExportOptions options, ref long textCharacters)
    {
        if (text.Length > options.MaxCellCharacters) throw new InvalidOperationException("导出单元格文本超过长度上限。");
        textCharacters = checked(textCharacters + text.Length);
        if (textCharacters > options.MaxTotalTextCharacters) throw new InvalidOperationException("导出文本总量超过上限。");
        // ClosedXML 普通 Value 会去掉首个单引号，富文本分支保留原始内容。
        if (text.StartsWith('\'')) cell.GetRichText().AddText(text);
        else cell.Value = text;
        cell.Style.IncludeQuotePrefix = true;
    }

    /// <summary>JSON 数字仅在精度可证明安全时写为数值，其余保留原始文本。</summary>
    private static void WriteJson(IXLCell cell, JsonElement json, ExportOptions options, ref long textCharacters)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined: return;
            case JsonValueKind.True: cell.Value = true; return;
            case JsonValueKind.False: cell.Value = false; return;
            case JsonValueKind.String: Text(cell, json.GetString() ?? "", options, ref textCharacters); return;
            case JsonValueKind.Number:
                if (json.TryGetDecimal(out var number) && SignificantDigits(json.GetRawText()) <= 15) cell.Value = number;
                else Text(cell, json.GetRawText(), options, ref textCharacters);
                return;
            default: Text(cell, json.GetRawText(), options, ref textCharacters); return;
        }
    }

    /// <summary>保守计算有效位数；多余尾零保留为文本也优于静默降低精度。</summary>
    private static int SignificantDigits(string text)
    {
        var mantissa = text.Split('e', 'E')[0];
        return new string(mantissa.Where(char.IsAsciiDigit).ToArray()).TrimStart('0').Length;
    }
}
