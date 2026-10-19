using System.Security;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>标准 uni-app 模板复用项目现有请求、登录态和 uni-ui 组件。</summary>
internal static class UniappRenderer
{
    /// <summary>渲染真实可用的筛选、排序和编辑，其他控件写入能力缺口清单。</summary>
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        var routes = new List<object>(); var gaps = new List<object>();
        var childDefinitions = ChildrenCompilation.Compile(model);
        foreach (var table in model.Tables)
        foreach (var page in table.Pages)
        {
            foreach (var column in page.Readable)
            {
                var control = ControlRenderer.Render(model, table, page, column);
                if (control.UnsupportedReason is not null) gaps.Add(new { pageId = page.Source.Id, column = column.Source.ColumnName, usage = "detail", reason = control.UnsupportedReason });
            }
            var filters = page.Searchable.Select(column => (Column: column, Control: ControlRenderer.RenderFilter(model, page, column))).ToArray();
            foreach (var filter in filters.Where(item => item.Control.UnsupportedReason is not null))
                gaps.Add(new { pageId = page.Source.Id, column = filter.Column.Source.ColumnName, usage = "filter", reason = filter.Control.UnsupportedReason });
            var supported = filters.Where(item => item.Control.UnsupportedReason is null).ToArray();
            var children = childDefinitions.Where(child => child.Parent == page).ToArray();
            var tabs = ChildrenCompilation.Tabs(page);
            var stem = table.Source.TableName;
            var listPath = $"pages/yzp/{stem}-list-{page.Source.Id}"; var detailPath = $"pages/yzp/{stem}-detail-{page.Source.Id}";
            var parameters = new Dictionary<string, string>
            {
                ["CHILD_IMPORTS"] = string.Join("\n", children.Select(child => $"import {child.Component} from '@/pages/yzp/{child.Table.Source.TableName}-list-{child.Page.Source.Id}.vue';")),
                ["CHILD_COMPONENTS"] = string.Join(", ", children.Select(child => child.Component)),
                ["CHILDREN_JSON"] = JsonSerializer.Serialize(children.Select(child => new { id = child.Id, pageId = child.Page.Source.Id, name = child.Page.Source.Name })),
                ["CHILDREN_MARKUP"] = ChildrenMarkup(page, children, tabs),
                ["CAN_UPDATE"] = ChildrenCompilation.CanUpdate(page).ToString().ToLowerInvariant(),
                ["REQUIRES_PARENT"] = page.Scopes.Any(scope => scope.Source == "parent").ToString().ToLowerInvariant(),
                ["READONLY_FIELD"] = JsonSerializer.Serialize(ChildrenCompilation.ReadonlyField(page)),
                ["TITLE"] = Escape(page.Source.Name), ["API"] = "/admin/auto/" + stem + "/" + page.Source.Id,
                ["DETAIL_ROUTE"] = "/" + detailPath,
                ["CAN_CREATE"] = (page.Source.IsNew == 1).ToString().ToLowerInvariant(),
                ["CAN_SAVE"] = (page.Source.IsSave == 1).ToString().ToLowerInvariant(),
                ["CAN_DELETE"] = (page.Source.IsDelete == 1).ToString().ToLowerInvariant(),
                ["CAN_COPY"] = (page.Source.IsCopy == 1).ToString().ToLowerInvariant(),
                ["CAN_IMPORT"] = ImportRenderer.Enabled(page).ToString().ToLowerInvariant(),
                ["CAN_EXPORT"] = ExportRenderer.Enabled(page).ToString().ToLowerInvariant(),
                ["PAGE_ID"] = page.Source.Id,
                ["PAGE_SIZE"] = Math.Clamp(page.Source.PageSize, 1, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["HEADERS"] = string.Concat(page.ListColumns.Select(column => "<uni-th" + (page.Sortable.Contains(column) ? $" sortable @sort-change=\"changeSort('{column.JsonName}', $event)\"" : "") + ">" + Escape(column.Source.Name) + "</uni-th>")),
                ["CELLS"] = string.Concat(page.ListColumns.Select(column => "<uni-td>" + ListCell(table, page, column) + "</uni-td>")),
                ["FILTER_FIELDS"] = string.Join("\n", supported.Select(item => item.Control.Markup)),
                ["FILTER_FIELDS_JSON"] = JsonSerializer.Serialize(supported.Select(item => new { field = item.Column.JsonName, type = item.Column.Type, name = item.Column.Source.Name })),
                ["FILTER_GAPS_JSON"] = JsonSerializer.Serialize(filters.Where(item => item.Control.UnsupportedReason is not null).Select(item => item.Column.Source.Name + "：" + item.Control.UnsupportedReason)),
                ["SORT_FIELDS_JSON"] = JsonSerializer.Serialize(page.Sortable.Select(column => new { value = column.JsonName, text = column.Source.Name.Length == 0 ? column.Source.ColumnName : column.Source.Name })),
                ["DISPLAY_FIELDS_JSON"] = JsonSerializer.Serialize(DisplayFields(model, page)),
                ["RTF_FIELDS_JSON"] = JsonSerializer.Serialize(page.Readable.Where(column => column.Source.ControlKind == "RTF" && column.Type == "string?").Select(column => column.JsonName)),
                ["FIELDS"] = string.Join("\n", page.DetailColumns.Select(column => ControlRenderer.Render(model, table, page, column).Markup)),
                ["OPTION_REQUESTS"] = "      this.refreshRelationLabels?.();",
                ["RELATION_IMPORT"] = RelationCompilation.ForPage(model, page).Count > 0 ? "import { createRelationOptionsMixin } from '@/common/yzp_runtime/relationOptionsMixin';" : "",
                ["RELATION_MIXIN"] = RelationCompilation.ForPage(model, page).Count > 0 ? "createRelationOptionsMixin(" + RelationDefinitions(model, table, page) + ")" : "",
                ["WRITABLE_FIELDS"] = JsonSerializer.Serialize(page.Writable.Select(column => column.JsonName))
            };
            yield return new("frontend/" + listPath + ".vue", RenderTemplate("list.vue.tpl", parameters));
            var detailParameters = new Dictionary<string, string>(parameters) { ["RELATION_IMPORT"] = "", ["RELATION_MIXIN"] = "" };
            yield return new("frontend/" + detailPath + ".vue", RenderTemplate("detail.vue.tpl", detailParameters));
            routes.Add(new { path = listPath, style = new { navigationBarTitleText = page.Source.Name } });
            routes.Add(new { path = detailPath, style = new { navigationBarTitleText = page.Source.Name } });
        }
        yield return new("generation-capabilities.json", JsonSerializer.Serialize(new { schemaVersion = 1, unsupportedControls = gaps }, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
        yield return new("frontend/pages.generated.json", JsonSerializer.Serialize(new { pages = routes }, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
    }

    /// <summary>子页放在父表单之外，保存成功后挂载；页签切换保持子列表的筛选和分页状态。</summary>
    private static string ChildrenMarkup(PageModel page, IReadOnlyList<ChildModel> children, bool tabs)
    {
        if (children.Count == 0) return "";
        var content = new System.Text.StringBuilder("    <view v-if=\"childrenReady\" class=\"yzp-children\">\n");
        if (tabs) content.AppendLine("      <view class=\"yzp-child-tabs\"><scroll-view class=\"yzp-child-tabs__scroll\" scroll-x><view class=\"yzp-child-tabs__inner\"><view v-for=\"(item, index) in children\" :key=\"item.id\" class=\"yzp-child-tabs__item\" :class=\"{ 'is-active': currentChild === index }\" @click=\"selectChild(index)\"><text class=\"yzp-child-tabs__text\">{{ item.name }}</text></view></view></scroll-view></view>");
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            content.AppendLine($"      <view{(tabs ? $" v-show=\"currentChild === {index}\"" : "")}><{child.Component} :key=\"id + ':{child.Id}'\" ref=\"child_{child.Id}\" :parent_id=\"id\" parent_page_sn=\"{page.Source.Id}\" :is_son_page=\"true\" :child_page_open_type=\"{child.OpenType}\" :parentReadOnlyCondition=\"readOnlyCondition\" /></view>");
        }
        return content.AppendLine("    </view>").Append("    <view v-else class=\"uni-padding-wrap\"><text>为避免数据丢失，请先保存当前页面的数据，再维护子表。</text></view>").ToString();
    }

    /// <summary>列表复杂内容复用原只读组件或预览入口，常量和关系显示统一消费实际 VO。</summary>
    private static string ListCell(TableModel table, PageModel page, ColumnModel column) => column.Source.ControlKind switch
    {
        "FileLibrary" => ControlRenderer.FileLibraryMarkup(table, page, column, "row", false),
        "RTF" when column.Type == "string?" => $"<button size=\"mini\" @click=\"previewRtf(row, '{column.JsonName}')\">预览</button>",
        _ => $"{{{{ displayValue(row, '{column.JsonName}') }}}}"
    };

    /// <summary>常量别名由后端固定生成；关系名称必须是本页真实可读列，不凭后缀发明 VO 属性。</summary>
    private static object DisplayFields(GenerationModel model, PageModel page)
    {
        var relations = RelationCompilation.ForPage(model, page);
        return page.Readable.ToDictionary(column => column.JsonName, column =>
        {
            string? alias = column.Source.ControlKind is "ConstList" or "ConstDownList" ? column.JsonName + "Show" : null;
            var relation = relations.SingleOrDefault(candidate => candidate.SourceColumn == column);
            if (relation is not null && column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal))
            {
                var physical = column.Source.ColumnName[..^3] + "_" + relation.NameColumn.Source.ColumnName;
                alias = page.Readable.SingleOrDefault(candidate => candidate.Source.ColumnName == physical && candidate.Type == "string?")?.JsonName;
            }
            return new { alias, optionField = relation?.SourceColumn.JsonName };
        }, StringComparer.Ordinal);
    }

    /// <summary>列表标签只按当前页ID调用固定 selected；目标父映射只能来自可读声明字段。</summary>
    private static string RelationDefinitions(GenerationModel model, TableModel table, PageModel page) => JsonSerializer.Serialize(RelationCompilation.ForPage(model, page).Select(relation =>
    {
        var context = ControlRenderer.RelationContext(page, relation, false);
        return new { field = relation.SourceColumn.JsonName, endpoint = "/admin/auto/" + table.Source.TableName + "/" + page.Source.Id + "/" + relation.Endpoint,
            requireParent = relation.SourceParents.Count > 0, requireTargetParent = relation.TargetParents.Count > 0,
            sameParent = context.SameParent, targetParentField = context.TargetParentField, unresolvedTarget = context.UnresolvedTarget };
    }));

    /// <summary>转义展示文本以保护模板结构。</summary>
    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
    /// <summary>嵌入模板保持离线 CLI 可搬移，配置值不能成为模板代码。</summary>
    private static string RenderTemplate(string name, Dictionary<string, string> values)
    {
        var assembly = typeof(UniappRenderer).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(nameValue => nameValue.EndsWith("." + name, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var content = reader.ReadToEnd().Replace("\r\n", "\n");
        foreach (var (key, value) in values) content = content.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        return content;
    }
}
