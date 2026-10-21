using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

public sealed record PublishedGenerationMetadata(MetadataCatalog Metadata, IReadOnlyList<string> RedactedPaths);

/// <summary>独立包和离线请求共享同一发布脱敏规则；不读取环境文件，也不按普通说明文本猜测凭据。</summary>
public static class GenerationMetadataPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };

    /// <summary>复制完整元数据后清除敏感配置与密码默认值，字段定义和原始输入保持不变。</summary>
    public static PublishedGenerationMetadata Publish(MetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var json = JsonNode.Parse(JsonSerializer.Serialize(catalog, Json))!;
        var redactions = new List<string>();
        Scrub(json, "$", redactions);
        var sensitiveColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in json["configColumns"]?.AsArray() ?? [])
        {
            if (column is not JsonObject item || !IsSensitiveKey(item["columnName"]?.ToString() ?? "")) continue;
            sensitiveColumns.Add(item["id"]?.ToString() ?? "");
            foreach (var key in new[] { "defaultValue", "initValue" }) if (item[key] is not null) { item[key] = ""; redactions.Add("$.configColumns[" + item["id"] + "]." + key); }
        }
        foreach (var field in json["configPageColumns"]?.AsArray() ?? [])
            if (field is JsonObject item && sensitiveColumns.Contains(item["configColumnId"]?.ToString() ?? ""))
                foreach (var key in new[] { "defaultValue", "initValue" }) if (item[key] is not null) { item[key] = ""; redactions.Add("$.configPageColumns[" + item["id"] + "]." + key); }
        return new(json.Deserialize<MetadataCatalog>(Json)!, redactions.Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>递归清理敏感键及嵌套JSON字符串；配置中的普通SQL定义只作为数据保留，不执行。</summary>
    internal static void Scrub(JsonNode node, string path, List<string> redactions)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                var childPath = path + "." + property.Key;
                if (IsSensitiveKey(property.Key)) { obj.Remove(property.Key); redactions.Add(childPath); }
                else if (property.Value is JsonObject or JsonArray) Scrub(property.Value, childPath, redactions);
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var content) && content.TrimStart() is [ '{' or '[', .. ])
                {
                    try { var nested = JsonNode.Parse(content); if (nested is JsonObject or JsonArray) { Scrub(nested, childPath, redactions); obj[property.Key] = nested.ToJsonString(Json); } }
                    catch (JsonException) { /* 非JSON说明保持原文，不执行或猜测其内容。 */ }
                }
            }
        else if (node is JsonArray array) for (var i = 0; i < array.Count; i++) if (array[i] is { } item) Scrub(item, path + "[" + i + "]", redactions);
    }
    /// <summary>生成发布和窄范围守卫共用敏感字段名判定，不以可写或查询声明豁免凭据。</summary>
    internal static bool IsSensitiveKey(string value) => Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(value);

}
