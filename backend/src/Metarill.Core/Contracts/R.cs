namespace Metarill.Core.Contracts;

/// <summary>沿用现有前端的统一响应，不把实体直接作为页面输出。</summary>
public sealed record R<T>(int Code, string? Msg, T? Data, Dictionary<string, object?> Map)
{
    /// <summary>创建业务成功响应。</summary>
    public static R<T> Success(T? data = default) => new(200, null, data, []);
    /// <summary>创建业务失败响应。</summary>
    public static R<T> Error(string message, int code = 500) => new(code, message, default, []);
}

public sealed class PageRequest
{
    public int PageNo { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Dictionary<string, System.Text.Json.JsonElement> Filters { get; set; } = [];
    public string? ParentId { get; set; }
    public PageOrderBy? OrderBy { get; set; }
    public List<string> SelectedIds { get; set; } = [];
}
public sealed class PageOrderBy
{
    public string? Column { get; set; }
    public string? Field { get; set; }
    public string? Order { get; set; }
}
public sealed record PageResult<T>(IReadOnlyList<T> Records, long Total, long Pages, int Current, int Size);
