using System.Globalization;
using Metarill.Core.Contracts;

namespace Metarill.Core.Metadata;

/// <summary>原小数长度与精度字段的共同定义，保留已提交长度的原文。</summary>
public readonly record struct DecimalColumnShape(int Precision, int Scale, string ColumnLength)
{
    /// <summary>单个精度按原 MySQL DECIMAL(p) 解释为零位小数。</summary>
    public static bool TryParseLength(string? length, out DecimalColumnShape shape)
    {
        shape = default;
        if (string.IsNullOrWhiteSpace(length)) return false;
        var parts = length.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !Number(parts[0], out var precision)) return false;
        var scale = 0;
        if (parts.Length == 2 && !Number(parts[1], out scale)) return false;
        if (!IsValid(precision, scale)) return false;
        shape = new(precision, scale, length);
        return true;

        static bool Number(string value, out int number) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && value.All(char.IsAsciiDigit);
    }

    /// <summary>原表单发送完整对象，明确填写的长度优先于其上次读取的隐藏精度值。</summary>
    public static DecimalColumnShape? FromSubmission(string? columnType, string? length, int? precision, int? scale,
        bool creating, bool lengthSubmitted, bool precisionSubmitted, bool scaleSubmitted)
    {
        if (!string.Equals(columnType, "decimal", StringComparison.OrdinalIgnoreCase)) return null;
        if ((lengthSubmitted || creating) && !string.IsNullOrWhiteSpace(length))
            return TryParseLength(length, out var shape) ? shape : throw InvalidLength();

        // 缺少长度的历史反查记录可通过完整数值定义保存，并补齐原表单对应文本。
        if (lengthSubmitted && !(precisionSubmitted && scaleSubmitted))
            throw new BusinessRuleException("小数类型的字段长度必须填写。");
        if (!precisionSubmitted && !scaleSubmitted) return null;

        if (precision is not int p || scale is not int s || !IsValid(p, s))
            throw InvalidLength();
        return new(p, s, Format(p, s));
    }

    /// <summary>结构反查及显式精度写入均生成原表单能再次编辑的长度。</summary>
    public static string Format(int precision, int scale) =>
        FormattableString.Invariant($"{precision},{scale}");

    /// <summary>沿现有 MySQL 存储精度和小数位范围核对。</summary>
    public static bool IsValid(int precision, int scale) =>
        precision is >= 1 and <= 65 && scale is >= 0 and <= 30 && scale <= precision;

    private static BusinessRuleException InvalidLength() =>
        new("小数长度须为精度或精度,小数位。精度为 1 至 65，小数位为 0 至 30 且不大于精度。");
}
