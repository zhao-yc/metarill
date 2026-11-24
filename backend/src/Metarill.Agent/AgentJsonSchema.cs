using System.Text.Json;
using Metarill.Core.Contracts;

namespace Metarill.Runtime.Agent;

/// <summary>保留既有Agent调用边界，结构描述与MCP共用实际DTO规则。</summary>
internal static class AgentJsonSchema
{
    /// <summary>兼容既有递归深度与可选字段描述。</summary>
    public static Dictionary<string, object?> Create(Type type,int depth=0)=>RequestJsonSchema.Create(type,depth);

    /// <summary>仅在生成分页 DTO 具有筛选属性时套用发布描述。</summary>
    internal static void DescribePageFilters(Dictionary<string, object?> schema, string schemaJson)
    {
        if (schema.GetValueOrDefault("properties") is Dictionary<string, object?> properties && properties.ContainsKey("filters"))
            properties["filters"] = JsonSerializer.Deserialize<JsonElement>(schemaJson);
    }

    /// <summary>公布已编译的显式排序字段，省略排序时由原页面使用默认顺序。</summary>
    internal static void DescribePageOrder(Dictionary<string, object?> schema, IReadOnlyList<string> columns)
    {
        if (schema.GetValueOrDefault("properties") is not Dictionary<string, object?> properties) return;
        if (columns.Count == 0)
        {
            properties.Remove("orderBy");
            return;
        }

        if (properties.GetValueOrDefault("orderBy") is not Dictionary<string, object?> orderBy ||
            orderBy.GetValueOrDefault("properties") is not Dictionary<string, object?> orderFields) return;
        orderBy["description"] = "省略时使用页面默认排序。";
        AddChoices("column", columns);
        AddChoices("field", columns);
        AddChoices("order", ["ascending", "descending"]);

        void AddChoices(string name, IReadOnlyList<string> choices)
        {
            if (orderFields.GetValueOrDefault(name) is Dictionary<string, object?> property)
                property["enum"] = choices;
        }
    }
}
