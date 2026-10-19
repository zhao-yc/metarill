using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>原详情页完整实体回传的固定合同；只有显式可写字段能够进入严格 DTO。</summary>
public sealed class LegacyPageWriteContract(IReadOnlyList<string> writableFields, IReadOnlyList<string> echoFields,
    IReadOnlyList<string> parentFields, string? projectField)
{
    private readonly HashSet<string> writable = new(writableFields, StringComparer.Ordinal);
    private readonly HashSet<string> echoes = new(echoFields, StringComparer.Ordinal);
    private readonly HashSet<string> parents = new(parentFields, StringComparer.Ordinal);

    /// <summary>真实身份验证之后归一化 HTTP 旧请求；Service 和 MCP 继续使用严格 DTO。</summary>
    public JsonElement Normalize(JsonElement entity, LegacyPageWriteOperation operation, string projectId, string? parentId)
    {
        if (entity.ValueKind != JsonValueKind.Object) throw new ArgumentException("写入请求必须为实体对象。");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in entity.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new ArgumentException("请求字段重复：" + field.Name);
            if (field.Name == "id")
            {
                if (operation == LegacyPageWriteOperation.Create)
                {
                    if (field.Value.ValueKind != JsonValueKind.Null && (field.Value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(field.Value.GetString())))
                        throw new ArgumentException("新增记录不能指定主键。");
                }
                else result.Add(field.Name, field.Value.Clone());
                continue;
            }
            // 父键和项目键只能回显真实上下文，不能作为覆盖输入。
            if (parents.Contains(field.Name))
            {
                if (string.IsNullOrWhiteSpace(parentId) || !SameIdentifier(field.Value, parentId)) throw new ArgumentException("实体父键与当前页面父记录不一致。");
                continue;
            }
            if (field.Name == projectField)
            {
                if (!SameIdentifier(field.Value, projectId)) throw new UnauthorizedAccessException("实体项目与真实当前项目不一致。");
                continue;
            }
            if (writable.Contains(field.Name))
            {
                if (operation != LegacyPageWriteOperation.Delete) result.Add(field.Name, field.Value.Clone());
                continue;
            }
            if (!echoes.Contains(field.Name)) throw new ArgumentException("当前页面未声明该请求字段：" + field.Name);
            // 只读列、展示别名和服务器派生名称只作回显，客户端值永远不写入实体。
        }
        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>字符串与精确整数 JSON 共用 ID 语义，不经过浮点数转换。</summary>
    private static bool SameIdentifier(JsonElement value, string expected)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return text == expected || long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var actual)
            && long.TryParse(expected, NumberStyles.None, CultureInfo.InvariantCulture, out var wanted) && actual > 0 && actual == wanted;
    }
}

public enum LegacyPageWriteOperation { Create, Update, Delete }
