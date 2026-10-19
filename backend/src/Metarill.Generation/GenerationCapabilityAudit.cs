using System.Text.Json.Serialization;

namespace Metarill.Generation;

internal sealed record GenerationCapabilityGap(
    [property:JsonPropertyName("pageId")]string PageId,
    [property:JsonPropertyName("column")]string Column,
    [property:JsonPropertyName("usage")]string Usage,
    [property:JsonPropertyName("reason")]string Reason);

/// <summary>原UI分支恢复后，控件存在不等于后端完成；生成提示和完整包门禁共用同一缺口事实。</summary>
internal static class GenerationCapabilityAudit
{
    /// <summary>只审计当前页实际读取/筛选的固定字段，结果不受客户端请求影响。</summary>
    internal static IReadOnlyList<GenerationCapabilityGap> Collect(GenerationModel model)
    {
        var gaps=new List<GenerationCapabilityGap>();
        foreach(var table in model.Tables)
        foreach(var page in table.Pages)
        {
            foreach(var column in page.Readable)
                if(ControlRenderer.Render(model,table,page,column).UnsupportedReason is {} reason)
                    gaps.Add(new(page.Source.Id,column.Source.ColumnName,"detail",reason));
            foreach(var column in page.Searchable)
                if(ControlRenderer.RenderFilter(model,page,column).UnsupportedReason is {} reason)
                    gaps.Add(new(page.Source.Id,column.Source.ColumnName,"filter",reason));
        }
        return gaps;
    }
}
