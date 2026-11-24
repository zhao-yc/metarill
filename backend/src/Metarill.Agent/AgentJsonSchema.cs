using System.Text.Json;
using Metarill.Core.Contracts;

namespace Metarill.Runtime.Agent;

/// <summary>保留既有Agent调用边界，结构描述与MCP共用实际DTO规则。</summary>
internal static class AgentJsonSchema
{
    /// <summary>兼容既有递归深度与可选字段描述。</summary>
    public static Dictionary<string, object?> Create(Type type,int depth=0)=>RequestJsonSchema.Create(type,depth);

    /// <summary>分页边界沿实际页面，计数和导出仅声明其实际使用的多选上限。</summary>
    internal static void DescribePagePagination(Dictionary<string, object?> schema, GeneratedPagePaginationAttribute page, bool isList)
    {
        if (schema.GetValueOrDefault("properties") is not Dictionary<string, object?> fields) return;
        if (page.SelectionPage)
        {
            if (fields.GetValueOrDefault("selectedIds") is Dictionary<string, object?> ids)
                ids["maxItems"] = RelationOptionsSelectedRequest.MaximumItems;
            if (fields.GetValueOrDefault("pageNo") is Dictionary<string, object?> selectionPage)
                selectionPage["maximum"] = PageRequest.SelectionMaximumPageNo;
        }

        if (!isList) return;
        if (fields.GetValueOrDefault("pageNo") is Dictionary<string, object?> pageNo)
            pageNo["minimum"] = PageRequest.MinimumPageNo;
        if (fields.GetValueOrDefault("pageSize") is Dictionary<string, object?> pageSize)
        {
            pageSize["minimum"] = page.AllowsUnpaged ? 0 : 1;
            pageSize["maximum"] = PageRequest.MaximumPageSize;
        }
        if (page.AllowsUnpaged)
        {
            schema["allOf"] = new[]
            {
                new
                {
                    @if = new { required = new[] { "pageSize" }, properties = new { pageSize = new { @const = 0 } } },
                    then = new { properties = new { pageNo = new { @const = PageRequest.MinimumPageNo } } }
                }
            };
        }
    }

    /// <summary>原专用回读接受有界正 ID 文本；省略或空数组表示没有已选项。</summary>
    internal static void DescribeSelectedRows(Dictionary<string, object?> schema)
    {
        if (schema.GetValueOrDefault("properties") is Dictionary<string, object?> fields &&
            fields.GetValueOrDefault("selectedIds") is Dictionary<string, object?> ids)
        {
            ids["maxItems"] = RelationOptionsSelectedRequest.MaximumItems;
            ids["items"] = new { type = "string", pattern = "^0*[1-9][0-9]*$" };
        }
    }

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
