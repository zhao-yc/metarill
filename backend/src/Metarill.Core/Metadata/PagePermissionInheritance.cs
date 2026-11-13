using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>只保存已证明的页面关系，不携带字段值、SQL或连接配置。</summary>
public enum PagePermissionInheritanceKind { Child, Selection }
/// <summary>真实父子继承同类动作，选择页引用只继承来源页查询。</summary>
public sealed record PagePermissionInheritanceLink(string SourcePageId,string TargetPageId,PagePermissionInheritanceKind Kind);

/// <summary>从固定或当前元数据编译窄关系；请求不能自行提供继承来源。</summary>
public static class PagePermissionInheritance
{
    /// <summary>只接受同项目、同后台和有效真实引用；损坏或歧义记录不产生授权边。</summary>
    public static IReadOnlyList<PagePermissionInheritanceLink> Compile(MetadataCatalog catalog)
    {
        var tables=catalog.ConfigTables.Where(table=>Owned(table,catalog)).GroupBy(table=>table.Id,StringComparer.Ordinal)
            .Where(group=>group.Count()==1).Select(group=>group.Single()).ToDictionary(table=>table.Id,StringComparer.Ordinal);
        var pages=catalog.ConfigPages.Where(page=>Owned(page,catalog)&&tables.ContainsKey(page.ConfigTableId)&&!string.IsNullOrWhiteSpace(page.YzAdminTypeId))
            .GroupBy(page=>page.Id,StringComparer.Ordinal).Where(group=>group.Count()==1).Select(group=>group.Single()).ToDictionary(page=>page.Id,StringComparer.Ordinal);
        var result=new HashSet<PagePermissionInheritanceLink>();
        foreach(var row in catalog.ConfigChildren)
        {
            if(row.ValueKind!=JsonValueKind.Object||!Unique(row)||Deleted(row))continue;
            try
            {
                var owner=Scalar(row,"yzProjectId","yz_project_id");if(owner.Length>0&&owner!=catalog.YzProjectId)continue;
                Add(Scalar(row,"parentConfigPageId","parent_config_page_id"),Scalar(row,"childConfigPageId","child_config_page_id"),PagePermissionInheritanceKind.Child);
            }
            catch(ArgumentException){/* 损坏关系不影响其它页面的直接授权。 */}
        }
        var columns=catalog.ConfigColumns.Where(column=>Owned(column,catalog)&&column.ControlKind is "SelectPage" or "MultiSelectPage")
            .GroupBy(column=>column.Id,StringComparer.Ordinal).Where(group=>group.Count()==1).Select(group=>group.Single()).ToDictionary(column=>column.Id,StringComparer.Ordinal);
        foreach(var group in catalog.ConfigPageColumns.Where(field=>Owned(field,catalog)).GroupBy(field=>(field.ConfigPageId,field.ConfigColumnId)))
        {
            if(group.Count()!=1)continue;var field=group.Single();
            if(!pages.TryGetValue(field.ConfigPageId,out var source)||!columns.TryGetValue(field.ConfigColumnId,out var column)||source.ConfigTableId!=column.ConfigTableId)continue;
            try{Add(source.Id,SelectionPageId(column,field),PagePermissionInheritanceKind.Selection);}
            catch(Exception error)when(error is ArgumentException or JsonException){/* 配置无法证明时不授予目标页查询。 */}
        }
        return Array.AsReadOnly(result.OrderBy(link=>link.SourcePageId,StringComparer.Ordinal).ThenBy(link=>link.TargetPageId,StringComparer.Ordinal).ThenBy(link=>link.Kind).ToArray());
        // 页面归属相同且都在当前有效目录，才允许形成继承边；自引用不扩展权限。
        void Add(string source,string target,PagePermissionInheritanceKind kind)
        {
            if(source!=target&&pages.TryGetValue(source,out var parent)&&pages.TryGetValue(target,out var child)&&parent.YzAdminTypeId==child.YzAdminTypeId)result.Add(new(source,target,kind));
        }
    }
    /// <summary>与生成器的原控件一致：页级非空值覆盖列级值，再回退明确的页面关联。</summary>
    public static string SelectionPageId(ColumnMetadata column,PageColumnMetadata? field)
    {
        using var source=Read(column.ControlDetail);
        var overlay=field is not null&&field.Extra.TryGetValue("controlDetail",out var value)?value.ValueKind switch
            {JsonValueKind.Null=>"",JsonValueKind.String=>value.GetString()??"",JsonValueKind.Object=>value.GetRawText(),_=>throw new ArgumentException("选择页覆盖必须为对象配置。")}:"";
        using var settings=Read(overlay);
        var target=Scalar(settings.RootElement,"pageId");if(string.IsNullOrWhiteSpace(target))target=Scalar(source.RootElement,"pageId");
        if(string.IsNullOrWhiteSpace(target)&&column.Extra.TryGetValue("linkConfigPageId",out var linked))target=Value(linked);
        return target;
    }
    /// <summary>仅检查选择控件已知标量键的形状，不解释whereSql或任何未知键。</summary>
    private static JsonDocument Read(string text)
    {
        var document=JsonDocument.Parse(string.IsNullOrWhiteSpace(text)?"{}":text);
        try
        {
            var root=document.RootElement;if(root.ValueKind!=JsonValueKind.Object||!Unique(root))throw new ArgumentException("选择页配置必须是无重复键的对象。");
            foreach(var key in new[]{"pageId","nameField","buttonText","whereSql","orderBy"})_ = Scalar(root,key);
            return document;
        }
        catch{document.Dispose();throw;}
    }
    /// <summary>只接受同项目或原兼容空归属，已软删除配置不能继续授予继承。</summary>
    private static bool Owned(MetadataRecord record,MetadataCatalog catalog)=>!string.IsNullOrWhiteSpace(record.Id)&&(record.YzProjectId.Length==0||record.YzProjectId==catalog.YzProjectId)
        &&!record.Extra.Any(pair=>pair.Key is "deleteNo" or "delete_no"&&pair.Value.ValueKind!=JsonValueKind.Null);
    /// <summary>原JSON行只接受明确的空删除标志。</summary>
    private static bool Deleted(JsonElement row)=>row.EnumerateObject().Any(pair=>pair.Name is "deleteNo" or "delete_no"&&pair.Value.ValueKind!=JsonValueKind.Null);
    /// <summary>拒绝重复键，避免不同解释器为同一配置选择不同目标。</summary>
    private static bool Unique(JsonElement row)=>row.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()==row.EnumerateObject().Count();
    /// <summary>兼容物理snake键，但两种写法同时出现且冲突时拒绝。</summary>
    private static string Scalar(JsonElement row,string name,string? physical=null)
    {
        var value=row.TryGetProperty(name,out var found)?Value(found):"";
        if(physical is not null&&row.TryGetProperty(physical,out var other))
        {var alias=Value(other);if(value.Length>0&&alias!=value)throw new ArgumentException("继承配置的别名冲突。");value=alias;}
        return value;
    }
    /// <summary>标识只来自文本或原数值，复杂JSON不能转换成页面引用。</summary>
    private static string Value(JsonElement value)=>value.ValueKind switch{JsonValueKind.Null=>"",JsonValueKind.String=>value.GetString()??"",JsonValueKind.Number=>value.GetRawText(),_=>throw new ArgumentException("继承配置标识必须为标量。")};
}
