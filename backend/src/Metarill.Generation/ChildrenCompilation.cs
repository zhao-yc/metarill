using System.Globalization;
using System.Text.Json;

namespace Metarill.Generation;

internal sealed record ChildModel(string Id, PageModel Parent, TableModel Table, PageModel Page, int SortNo, int OpenType)
{
    public string Component => "ChildList" + Id;
}

/// <summary>子页面只绑定已有父范围，不把导航中的父 ID 当作授权或外键定义。</summary>
internal static class ChildrenCompilation
{
    /// <summary>一次校验完整主子图；错误配置必须在整个生成批次落盘前暴露。</summary>
    public static IReadOnlyList<ChildModel> Compile(GenerationModel model)
    {
        var children = new List<ChildModel>();
        var known = new HashSet<string>(StringComparer.Ordinal) { "id", "name", "parentConfigPageId", "parentConfigPageName", "childConfigPageId", "childConfigPageName", "childPageOpenType", "childPageOpenTypeShow", "sortNo", "yzProjectId", "yzProjectName", "importNo", "insertTime", "updateTime", "insertManagerId", "updateManagerId", "insertManagerRealName", "updateManagerRealName", "deleteNo" };
        var pages = model.Tables.SelectMany(table => table.Pages.Select(page => (Table: table, Page: page))).ToDictionary(item => item.Page.Source.Id, StringComparer.Ordinal);
        foreach (var source in model.Catalog.ConfigChildren)
        {
            if (source.ValueKind != JsonValueKind.Object || source.EnumerateObject().Any(property => !known.Contains(property.Name)) || source.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != source.EnumerateObject().Count()) throw new InvalidOperationException("子页面包含未知配置。");
            var id = Text(source, "id");
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_]+$")) throw new InvalidOperationException("子页面定义 ID 无效。");
            if (!pages.TryGetValue(Text(source, "parentConfigPageId"), out var parent) || !pages.TryGetValue(Text(source, "childConfigPageId"), out var child)) throw new InvalidOperationException("子页面引用不存在的父页或子页：" + id);
            if (Text(source, "yzProjectId") is { Length: > 0 } project && project != model.Catalog.YzProjectId) throw new InvalidOperationException("子页面不属于当前项目：" + id);
            if (parent.Page.Source.YzAdminTypeId != child.Page.Source.YzAdminTypeId) throw new InvalidOperationException("主子页面必须属于相同后台类型：" + id);
            if (parent.Table.Source.IsFunctionTable == 1 || child.Table.Source.IsFunctionTable == 1) throw new InvalidOperationException("功能表不能作为记录主子页面：" + id);
            if (Integer(child.Page.Source.Extra, "type", 0) != 0 || Integer(child.Page.Source.Extra, "isListEdit", 0) != 0 || Integer(child.Page.Source.Extra, "isNotGenerateDetailPage", 0) != 0) throw new InvalidOperationException("子页面列表类型尚未接入主子编辑：" + id);
            var scopes = child.Page.Scopes.Where(scope => scope.Source == "parent").ToArray();
            if (scopes.Length != 1) throw new InvalidOperationException("子页面必须具有唯一的 parentId 范围：" + id);
            var column = scopes[0].Column;
            var target = model.Tables.SingleOrDefault(table => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId) ? table.Source.Id == column.Source.LinkConfigTableId : table.Source.TableName + "_id" == column.Source.ColumnName);
            // 前端父ID沿原字符串协议；新增字符串外键须证明物理归属，现有整数范围合同保持。
            var supported=column.Type is "long" or "long?"||column.Type=="string?"&&NonRedundantReadCompilation.Physical(model,child.Table,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn");
            if (target != parent.Table || !supported) throw new InvalidOperationException("子页面父范围与父表不匹配：" + id);
            var openType = Integer(source, "childPageOpenType", 1);
            if (openType is not (1 or 2)) throw new InvalidOperationException("子页面打开方式仅支持 1（新页面）和 2（PC 弹出层）：" + id);
            children.Add(new(id, parent.Page, child.Table, child.Page, Integer(source, "sortNo", 0), openType));
        }
        if (children.Select(child => child.Id).Distinct(StringComparer.Ordinal).Count() != children.Count || children.GroupBy(child => (child.Parent.Source.Id, child.Page.Source.Id)).Any(group => group.Count() > 1)) throw new InvalidOperationException("子页面定义重复。");
        var active = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string pageId)
        {
            if (!active.Add(pageId)) throw new InvalidOperationException("主子页面不能循环嵌套。");
            if (!visited.Contains(pageId)) foreach (var child in children.Where(child => child.Parent.Source.Id == pageId)) Visit(child.Page.Source.Id);
            active.Remove(pageId); visited.Add(pageId);
        }
        foreach (var parent in children.Select(child => child.Parent.Source.Id).Distinct(StringComparer.Ordinal)) Visit(parent);
        return children.OrderBy(child => child.SortNo).ThenBy(child => child.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>旧 v8 仅将 Tab 渲染为页签；其他已声明位置保持顺排，未知值不静默降级。</summary>
    public static bool Tabs(PageModel page)
    {
        var value = Text(page.Source.Extra, "childPlace");
        if (value is not ("" or "Tab" or "OneWithMany" or "NewPage")) throw new InvalidOperationException("未知子模块位置：" + value);
        return value == "Tab";
    }

    /// <summary>只读条件只接受本页真实可读属性，不能执行元数据中的任意 JavaScript。</summary>
    public static string? ReadonlyField(PageModel page)
    {
        var value = Text(page.Source.Extra, "readOnlyCondition");
        if (value.Length == 0) return null;
        return page.Readable.SingleOrDefault(column => column.JsonName == value || column.Source.ColumnName == value)?.JsonName
            ?? throw new InvalidOperationException("只读条件必须是本页真实可读字段：" + page.Source.Id);
    }

    /// <summary>更新能力沿用原 isUpdate；精简元数据未声明时使用既有保存能力。</summary>
    public static bool CanUpdate(PageModel page) => Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source);

    /// <summary>JSON 扩展只接受确切字符串，null 与未声明均使用空值。</summary>
    private static string Text(IReadOnlyDictionary<string, JsonElement> source, string name) => source.TryGetValue(name, out var value) ? Text(value) : "";
    private static string Text(JsonElement source, string name) => source.TryGetProperty(name, out var value) ? Text(value) : "";
    private static string Text(JsonElement value) => value.ValueKind switch { JsonValueKind.Null => "", JsonValueKind.String => value.GetString()!.Trim(), _ => throw new InvalidOperationException("子页面标识及配置必须为字符串。") };
    /// <summary>兼容旧排序字符串及整数元数据，拒绝浮点、布尔和非整数配置。</summary>
    private static int Integer(JsonElement source, string name, int fallback) => source.TryGetProperty(name, out var value) ? Integer(value, fallback) : fallback;
    private static int Integer(IReadOnlyDictionary<string, JsonElement> source, string name, int fallback) => source.TryGetValue(name, out var value) ? Integer(value, fallback) : fallback;
    private static int Integer(JsonElement value, int fallback)
    {
        if (value.ValueKind == JsonValueKind.Null) return fallback;
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number && int.TryParse(value.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) return number;
        throw new InvalidOperationException("子页面排序或打开方式必须为整数。");
    }
}
