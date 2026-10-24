using System.Globalization;
using System.Text.Json.Serialization;

namespace Metarill.Core.Contracts;

/// <summary>保留旧数组入口的已知请求及 formParameters，不允许动态查询目标或 SQL 键。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RelationOptionsLegacyRequest
{
    public int PageNo { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Dictionary<string, System.Text.Json.JsonElement> Filters { get; set; } = [];
    public string? ParentId { get; set; }
    /// <summary>目标父仅是固定关联范围的值，来源父仍单独由 ParentId 核验。</summary>
    public string? TargetParentId { get; set; }
    public PageOrderBy? OrderBy { get; set; }
    public List<string> SelectedIds { get; set; } = [];
    public System.Text.Json.JsonElement? FormParameters { get; set; }
    public Optional<System.Text.Json.JsonElement?> Id { get; set; }

    /// <summary>原只读字段只回读指定 ID；显式空值保持空候选，不得退化成全部数据。</summary>
    public RelationOptionsSelectedRequest? ToSelectedRequest()
    {
        Validate();
        if (!Id.IsSpecified) return null;
        var value = Id.Value;
        if (value is null || value.Value.ValueKind == System.Text.Json.JsonValueKind.Null
            || value.Value.ValueKind == System.Text.Json.JsonValueKind.String && string.IsNullOrWhiteSpace(value.Value.GetString()))
            return new() { ParentId = ParentId, TargetParentId = TargetParentId, FormParameters = FormParameters?.Clone() };
        var text = value.Value.ValueKind is System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Number ? value.Value.ToString() : null;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0) throw new ArgumentException("只读关联 ID 必须为正整数文本。");
        return new() { SelectedIds = [number.ToString(CultureInfo.InvariantCulture)], ParentId = ParentId, TargetParentId = TargetParentId, FormParameters = FormParameters?.Clone() };
    }

    /// <summary>旧入口不解释筛选、排序或已选状态；这些请求必须使用明确的新合同。</summary>
    public void Validate()
    {
        if (Filters is null || Filters.Count != 0 || OrderBy is not null || SelectedIds is null || SelectedIds.Count != 0)
            throw new ArgumentException("旧关联数组入口不接受筛选、排序或已选条件，请使用分页或已选回读入口。");
    }
    /// <summary>转为既有 Service 合同，不传递未知配置或扩大查询范围。</summary>
    public PageRequest ToPageRequest()
    { Validate(); return new PageRequest { ParentId = ParentId, PageNo = PageNo, PageSize = PageSize, FormParameters = FormParameters?.Clone() }; }
}

/// <summary>固定关系候选分页，不接受表名、列名、SQL、排序或客户端项目。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RelationOptionsPageRequest
{
    public int PageNo { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }
    public string? ParentId { get; set; }
    public string? TargetParentId { get; set; }
    /// <summary>沿用原表单槽；只有生成计划声明的叶值进入固定参数。</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? FormParameters { get; set; }

    /// <summary>限制单次查询和偏移，关键词始终作为数据进入强类型表达式。</summary>
    public void Validate()
    {
        if (PageNo is < 1 or > 1000000 || PageSize is < 1 or > 100) throw new ArgumentException("关联分页参数无效，每页最多 100 条。");
        if (Keyword?.Length > 100) throw new ArgumentException("关联关键词最多 100 个字符。");
    }
}

/// <summary>已选项回读只接受有限个明确字符串 ID；任何非法或越界项均整体拒绝。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RelationOptionsSelectedRequest
{
    public List<string> SelectedIds { get; set; } = [];
    public string? ParentId { get; set; }
    public string? TargetParentId { get; set; }
    /// <summary>已选回读与候选使用同一表单上下文，不扩大行范围。</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? FormParameters { get; set; }

    /// <summary>先限制原始数量再去重，不允许表达式、负数或超出整型范围的 ID。</summary>
    public long[] ParseIds()
    {
        if (SelectedIds is null || SelectedIds.Count > 200) throw new ArgumentException("关联已选项最多 200 个，不能为 null。");
        return SelectedIds.Select(id => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new ArgumentException("关联已选项 ID 必须为正整数文本。")).Distinct().ToArray();
    }
}
