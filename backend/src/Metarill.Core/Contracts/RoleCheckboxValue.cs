using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>原账号角色复选的有界JSON文本合同；省略状态仍由外层Optional保存。</summary>
public static class RoleCheckboxValue
{
    public const int MaximumLength = 500;
    public const int MaximumProjectionIds = 5000;
    public const int ProjectionBatchSize = 200;

    /// <summary>数组转存文本，旧数组文本保持字节，显式null清空；全部ID必须为精确正整数。</summary>
    public static string? ReadWrite(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind == JsonValueKind.Null) return null;
        var text = value.Value.ValueKind switch
        {
            JsonValueKind.Array => value.Value.GetRawText(),
            JsonValueKind.String => value.Value.GetString()!,
            _ => throw new ArgumentException("角色只接受数组、已编码JSON数组文本或null。")
        };
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("角色文本必须为JSON数组，不能用空白文本代替。");
        _ = Parse(text);
        return text;
    }

    /// <summary>历史null保持null，空数组保持空数组；输出字符串ID避免JavaScript精度丢失。</summary>
    public static JsonElement? ProjectIds(string? text) => string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.SerializeToElement(Parse(text).Select(Id));

    /// <summary>按原ID顺序回显名称，缺失或越界项保留null位置，不能令后续名称错位。</summary>
    public static JsonElement? ProjectNames(string? text, IReadOnlyDictionary<long, string?> names) => string.IsNullOrWhiteSpace(text) ? null
        : JsonSerializer.SerializeToElement(Parse(text).Select(key => names.GetValueOrDefault(key)).ToArray());

    /// <summary>整页先去重并检查总预算；超量明确拒绝，不截断角色名称或发起逐行查询。</summary>
    public static long[] ProjectionIds(IEnumerable<string?> rows)
    {
        var ids = new HashSet<long>();
        foreach (var text in rows)
        foreach (var id in Parse(text))
            if (ids.Add(id) && ids.Count > MaximumProjectionIds) throw new ArgumentException("本次角色名称读取超过5000个唯一ID，请缩小读取范围。");
        return ids.ToArray();
    }

    /// <summary>500字符上限先于解析；不接受浮点、指数、对象、布尔、非规范或超long范围标识。</summary>
    public static long[] Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (text.Length > MaximumLength) throw new ArgumentException("角色数组编码后最多500个字符。");
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("角色文本必须为JSON数组。");
            return document.RootElement.EnumerateArray().Select(value =>
            {
                var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
                if (value.ValueKind == JsonValueKind.Number && !value.TryGetInt64(out _)) throw new ArgumentException("角色数值ID必须为精确long整数。");
                if (raw is null || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || raw != Id(id))
                    throw new ArgumentException("角色ID必须为精确的正整数或正整数字符串。");
                return id;
            }).ToArray();
        }
        catch (JsonException error) { throw new ArgumentException("角色数组JSON无效。", error); }
    }

    /// <summary>原POST只接固定上下文；formParameters不参与授权、SQL或目标选择。</summary>
    public static long[]? ReadCandidateRequest(JsonElement? request)
    {
        if (request is null || request.Value.ValueKind == JsonValueKind.Null) return null;
        var body = request.Value;
        if (body.ValueKind != JsonValueKind.Object || body.GetRawText().Length > 16384) throw new ArgumentException("角色候选请求必须为有界对象。");
        var fields = new HashSet<string>(StringComparer.Ordinal); long[]? selected = null;
        foreach (var field in body.EnumerateObject())
        {
            if (!fields.Add(field.Name)) throw new ArgumentException("角色候选请求字段重复。");
            switch (field.Name)
            {
                case "formParameters":
                    if (field.Value.GetRawText().Length > 8192 || field.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.String)) throw new ArgumentException("角色表单上下文无效。");
                    break;
                case "parentId":
                    if (field.Value.ValueKind != JsonValueKind.Null && (field.Value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(field.Value.GetString()))) throw new ArgumentException("原账号候选不使用父记录。");
                    break;
                case "id": selected = Parse(ReadWrite(field.Value)); break;
                default: throw new ArgumentException("角色候选未开放请求字段：" + field.Name);
            }
        }
        return selected;
    }

    /// <summary>标识按固定整数文化输出，不经过浮点数。</summary>
    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>沿用原复选组件四字段，两个ID字段固定为字符串。</summary>
public sealed record RoleCheckboxOption(string Id, string? Name, string Value, string? Text);
