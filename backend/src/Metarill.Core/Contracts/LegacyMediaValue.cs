using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>旧上传在数据库中保存 JSON 文本，原接口读取时返回文件描述数组。</summary>
public static class LegacyMediaValue
{
    /// <summary>保持原 Java 回显约定。空值或旧非数组内容返回 null，避免影响同一记录的其它字段。</summary>
    public static JsonElement? Project(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            using var document = JsonDocument.Parse(stored);
            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
