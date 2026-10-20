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
            // 虚拟列在模型物理列过滤前存在；固定目录声明不能因该过滤而从Full门禁静默消失。
            foreach(var field in model.Catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==page.Source.Id))
            {
                // GenerationModel已验证同页字段的同表归属；这里只审实际声明，不能从其它页借覆盖。
                var source=model.Catalog.ConfigColumns.Single(column=>column.Id==field.ConfigColumnId && column.ConfigTableId==table.Source.Id);
                if(source.IsVirtualColumn!=0 && FixedOptionCatalogCompilation.HasDeclaration(source,field))
                    gaps.Add(new(page.Source.Id,source.ColumnName,"detail","固定版本目录必须使用已声明的非联接物理版本字段。"));
            }
            foreach(var column in page.Readable)
                if(ControlRenderer.Render(model,table,page,column).UnsupportedReason is {} reason)
                    gaps.Add(new(page.Source.Id,column.Source.ColumnName,"detail",reason));
            // 原选择页按辅助名称键搜索；只有同一固定编译计划确认可执行时才解除筛选缺口。
            var names=SelectionNameFilterCompilation.ForPage(model,table,page);
            foreach(var gap in names.Gaps)gaps.Add(new(page.Source.Id,gap.Source.Source.ColumnName,"filter",gap.Reason));
            foreach(var column in page.Searchable.Where(column=>!SelectionNameFilterCompilation.UsesNameInput(model,page,column)&&!SelectionNameFilterCompilation.UsesSourceTextOverride(model,page,column)))
                if(ControlRenderer.RenderFilter(model,page,column).UnsupportedReason is {} reason)
                    gaps.Add(new(page.Source.Id,column.Source.ColumnName,"filter",reason));
        }
        return gaps;
    }
}
