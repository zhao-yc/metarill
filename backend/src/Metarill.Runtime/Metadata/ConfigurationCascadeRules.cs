using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Metadata;

/// <summary>校验原页面级联明细的定义形状，不重写输入或表级绑定。</summary>
internal static class ConfigurationCascadeRules
{
    /// <summary>页面可清空覆盖以继承表级定义。显式设置须满足原自树或跨表的字段约束。</summary>
    internal static void ValidatePageOverride(string detail, string columnName, string logicalType)
    {
        if (string.IsNullOrWhiteSpace(detail)) return;
        if (logicalType is not ("string" or "int64") || !columnName.EndsWith("_id", StringComparison.Ordinal))
            throw Error("叶子必须为字符串或 int64 的 _id 字段。");

        try
        {
            using var document = JsonDocument.Parse(detail);
            var definition = document.RootElement;
            RequireObject(definition);
            switch (Text(definition, "sourceMode", "crossTable"))
            {
                case "selfTree":
                    if (!definition.TryGetProperty("selfTree", out var tree))
                        throw Error("缺少 selfTree 定义。");
                    ValidateTree(tree);
                    break;
                case "crossTable":
                    if (!definition.TryGetProperty("levels", out var levels) || levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() < 2)
                        throw Error("跨表级联至少需要两级配置。");
                    ValidateLevels(levels, columnName);
                    break;
                default:
                    throw Error("sourceMode 必须为 selfTree 或 crossTable。");
            }
        }
        catch (JsonException)
        {
            throw Error("控件明细必须为有效的 JSON 对象。");
        }
    }

    /// <summary>自树的业务节点键可以使用原有非主键列，根和筛选继续使用已支持的固定模式。</summary>
    private static void ValidateTree(JsonElement tree)
    {
        RequireObject(tree);
        ValidateTargetFields(tree, parentRequired: true);
        if (Text(tree, "rootMode", "blank") != "blank")
            throw Error("selfTree.rootMode 仅支持 blank。");
        if (Text(tree, "filterMatchMode", "selfAndDescendants") is not ("selfAndDescendants" or "exact"))
            throw Error("selfTree.filterMatchMode 必须为 selfAndDescendants 或 exact。");
    }

    /// <summary>同一目标表可重复出现，层标识和来源字段前缀仍须独立，且末级匹配原叶子字段。</summary>
    private static void ValidateLevels(JsonElement levels, string columnName)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastPrefix = "";
        foreach (var level in levels.EnumerateArray())
        {
            RequireObject(level);
            var table = ValidateTargetFields(level, parentRequired: keys.Count > 0);
            lastPrefix = Identifier(level, "columnPrefix", table);
            if (!keys.Add(Text(level, "key", table)))
                throw Error("跨表级联的层标识不能重复。");
            if (!prefixes.Add(lastPrefix))
                throw Error("跨表级联的字段前缀不能重复。");
        }

        if (lastPrefix + "_id" != columnName)
            throw Error("跨表级联的末级字段前缀必须与当前叶子字段一致。");
    }

    /// <summary>先校验原字段和排序格式，具体目标列及发布范围继续由生成器根据项目目录解析。</summary>
    private static string ValidateTargetFields(JsonElement definition, bool parentRequired)
    {
        var table = Identifier(definition, "table");
        Identifier(definition, "idField", "id");
        Identifier(definition, "nameField", "name");
        if (parentRequired || Text(definition, "parentField").Length > 0)
            Identifier(definition, "parentField");

        var order = Text(definition, "orderBy");
        if (order.Length > 0 && order.Split(',').Any(segment => !Regex.IsMatch(segment.Trim(),
            @"\A(?:[A-Za-z_][A-Za-z0-9_]*|`[A-Za-z_][A-Za-z0-9_]*`)(?:\s+(?i:asc|desc))?\z")))
            throw Error("orderBy 只能填写字段名及可选的 ASC 或 DESC。");
        return table;
    }

    /// <summary>同时拒绝重复属性，避免保存和生成读取不同的定义。</summary>
    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            value.EnumerateObject().DistinctBy(property => property.Name, StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
            throw Error("控件明细必须为没有重复属性的对象。");
    }

    private static string Identifier(JsonElement value, string key, string fallback = "")
    {
        var text = Text(value, key, fallback);
        if (!MetadataValidation.IsIdentifier(text)) throw Error(key + " 必须为有效的数据库标识符。");
        return text;
    }

    private static string Text(JsonElement value, string key, string fallback = "")
    {
        if (!value.TryGetProperty(key, out var member) || member.ValueKind == JsonValueKind.Null) return fallback;
        if (member.ValueKind != JsonValueKind.String) throw Error(key + " 必须为文本。");
        var text = member.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static BusinessRuleException Error(string message) => new("CascadeTree " + message);
}
