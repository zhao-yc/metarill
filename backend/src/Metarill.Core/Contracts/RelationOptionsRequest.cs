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
    public PageOrderBy? OrderBy { get; set; }
    public List<string> SelectedIds { get; set; } = [];
    public System.Text.Json.JsonElement? FormParameters { get; set; }

    /// <summary>旧入口不解释筛选、排序或已选状态；这些请求必须使用明确的新合同。</summary>
    public void Validate()
    {
        if (Filters is null || Filters.Count != 0 || OrderBy is not null || SelectedIds is null || SelectedIds.Count != 0)
            throw new ArgumentException("旧关联数组入口不接受筛选、排序或已选条件，请使用分页或已选回读入口。");
    }
    /// <summary>转为既有 Service 合同，不传递未知配置或扩大查询范围。</summary>
    public PageRequest ToPageRequest()
    { Validate(); return new PageRequest { ParentId = ParentId, PageNo = PageNo, PageSize = PageSize }; }
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

    /// <summary>先限制原始数量再去重，不允许表达式、负数或超出整型范围的 ID。</summary>
    public long[] ParseIds()
    {
        if (SelectedIds is null || SelectedIds.Count > 200) throw new ArgumentException("关联已选项最多 200 个，不能为 null。");
        return SelectedIds.Select(id => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new ArgumentException("关联已选项 ID 必须为正整数文本。")).Distinct().ToArray();
    }
}
