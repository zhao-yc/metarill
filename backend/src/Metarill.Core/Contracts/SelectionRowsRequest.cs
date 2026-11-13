using System.Text.Json.Serialization;

namespace Metarill.Core.Contracts;

/// <summary>专用多选页只回读本页已选行，不接受客户端指定查询目标或条件。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SelectionRowsRequest
{
    public List<string> SelectedIds { get; set; } = [];
    public string? ParentId { get; set; }

    /// <summary>沿用严格长ID解析，原始数量最多200；去重保持首次选择顺序。</summary>
    public long[] ParseIds() => new RelationOptionsSelectedRequest { SelectedIds = SelectedIds }.ParseIds();
}
