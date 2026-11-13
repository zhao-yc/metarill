using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>Metarill自己的固定C#模板目录；不是原Java版本表，也不从客户端接收目录内容。</summary>
public static class TemplateVersionCatalog
{
    public const string CatalogId = "metarill-csharp-templates/v1";
    public const string CurrentId = "2300000000000000001";
    public const long CurrentValue = 2300000000000000001L;
    public const string CurrentName = "Metarill C# v1";
    public const string ProjectTableId = "1669251289347932162";
    public const string VersionColumnId = "1669606903854936065";
    private static readonly IReadOnlyList<TemplateVersionOption> Options = Array.AsReadOnly(new[] { new TemplateVersionOption(CurrentId, CurrentName, CurrentId, CurrentName) });

    /// <summary>只允许当前目录中的确切版本，不能把旧Java ID或缺失值自动转换为C#版本。</summary>
    public static void RequireSupported(long? value)
    {
        if (value != CurrentValue) throw new ArgumentException("请选择受支持的Metarill C#模板版本；原Java版本和未知版本不可使用。");
    }

    /// <summary>已有C#项目的空版本保持原读取兼容，不回写、不伪造默认；显式旧版本必须拒绝生成。</summary>
    public static void ValidateStoredProjectVersion(object? value)
    {
        if (value is null || value is string text && string.IsNullOrWhiteSpace(text)) return;
        if (!long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            throw new InvalidOperationException("项目模板版本格式无效，不能生成。");
        if (version != CurrentValue) throw new InvalidOperationException("项目选择的是原Java版本或未知模板版本，请明确选择Metarill C#模板后再生成。");
    }

    /// <summary>名称由可信目录计算，空项目仍回显空值，未知历史ID保留原值供显式修复。</summary>
    public static string? DisplayName(long? value) => value is null ? null : value == CurrentValue ? CurrentName : value.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>原候选POST只容纳旧表单上下文与只读ID，不解释formParameters，也不接受动态SQL或目录。</summary>
    public static IReadOnlyList<TemplateVersionOption> ReadOptions(JsonElement? request)
    {
        if (request is null || request.Value.ValueKind == JsonValueKind.Null) return Options;
        var body = request.Value;
        if (body.ValueKind != JsonValueKind.Object || body.GetRawText().Length > 16384) throw new ArgumentException("版本候选请求必须为有界对象。");
        var seen = new HashSet<string>(StringComparer.Ordinal); bool selected = false; long? id = null;
        foreach (var property in body.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new ArgumentException("版本候选请求字段重复。");
            var value = property.Value;
            switch (property.Name)
            {
                case "formParameters":
                    if (value.GetRawText().Length > 8192 || value.ValueKind is not (JsonValueKind.Object or JsonValueKind.String or JsonValueKind.Null)) throw new ArgumentException("版本候选表单上下文无效。");
                    break;
                case "parentId":
                    if (value.ValueKind != JsonValueKind.Null && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 32)) throw new ArgumentException("版本候选父上下文无效。");
                    break;
                case "id":
                    selected = true;
                    if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())) break;
                    var text = value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
                    if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) throw new ArgumentException("版本候选ID必须为正整数文本。");
                    RequireSupported(parsed); id = parsed; break;
                default: throw new ArgumentException("版本候选不接受该请求字段：" + property.Name);
            }
        }
        return selected && id is null ? Array.Empty<TemplateVersionOption>() : Options;
    }
}

/// <summary>保留原下拉id/name回调及value/text别名；长ID从目录源头就是字符串。</summary>
public sealed record TemplateVersionOption(string Id, string Name, string Value, string Text);
