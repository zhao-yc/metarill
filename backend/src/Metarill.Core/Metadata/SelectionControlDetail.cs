using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>单选、多选的有效控件明细。页面非空标量覆盖同名表级设置，空值沿用原字段。</summary>
public sealed record SelectionControlDetail(string PageId, string NameField, string ButtonText, string WhereSql, string OrderBy)
{
    /// <summary>在保存和生成时使用同一套标量继承和原关联页兜底规则。</summary>
    public static SelectionControlDetail Resolve(string sourceDetail, string? pageDetail, string linkedPageId = "")
    {
        using var source = Read(sourceDetail);
        using var overlay = Read(pageDetail);
        string Value(string key, string fallback = "")
        {
            var value = Scalar(overlay.RootElement, key);
            if (string.IsNullOrWhiteSpace(value)) value = Scalar(source.RootElement, key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        return new(Value("pageId", linkedPageId), Value("nameField", "name"), Value("buttonText", "选择"),
            Value("whereSql"), Value("orderBy"));
    }

    /// <summary>只接收无重复键的对象。未知键保留给原声明，解析时不改变其中的内容。</summary>
    private static JsonDocument Read(string? text)
    {
        var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var value = document.RootElement;
        if (value.ValueKind == JsonValueKind.Object &&
            value.EnumerateObject().DistinctBy(property => property.Name, StringComparer.Ordinal).Count() == value.EnumerateObject().Count())
            return document;

        document.Dispose();
        throw new InvalidOperationException("选择页配置必须为无重复键的JSON对象。");
    }

    /// <summary>保留字符串和数字 ID 的已有输入语义，复杂对象不能转成字段或路由。</summary>
    private static string Scalar(JsonElement source, string key)
    {
        if (!source.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            throw new InvalidOperationException("选择页已知配置必须为标量：" + key);
        return value.ToString();
    }
}
