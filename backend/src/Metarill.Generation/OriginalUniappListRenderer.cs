using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>沿用原v8列表模板、组件、分支与事件；只把元数据展开改为C#。</summary>
internal static partial class OriginalUniappListRenderer
{
    /// <summary>普通页、选择页和子页共享原PC/移动布局，固定模板不能由客户端选择。</summary>
    internal static string Render(GenerationModel model, TableModel table, PageModel page)
    {
        var kind = Integer(page.Source.Extra, "type", 0);
        var child = page.Scopes.Any(scope => scope.Source == "parent");
        var selection = kind == 2;
        var detail = table.Source.TableName + "_detail_" + page.Source.Id;
        var canPopup = kind == 0 && !Flag(page.Source.Extra, "isNotGenerateDetailPage");
        var listEdit = Flag(page.Source.Extra, "isListEdit");
        var layout = Integer(page.Source.Extra, "mobileListPageStyleType", 1);
        if (layout is < 1 or > 3) throw new InvalidOperationException("原移动列表布局只支持1、2、3，不得静默换成新布局。");
        var fields = page.ListColumns;
        var filter = Filters(model, table, page);
        var buttons = Buttons(model, table, page, kind);
        var action = buttons.HasGrid || buttons.Batch || kind == 1 ? "true" : "page_is_edit";
        var queryVisible = child ? "page_is_edit" : "permissionVersion >= 0 && page_is_edit && $yzp.hasPagePermission(page_sn, 'is_query')";
        var newVisible = child ? "!readOnlyCondition && page_is_new" : "permissionVersion >= 0 && !readOnlyCondition && page_is_new && $yzp.hasPagePermission(page_sn, 'is_new')";
        var (desktop, mobile) = Rows(model, table, page, fields, layout, kind, action, queryVisible, buttons, detail, listEdit);
        var nav = child || kind != 0 ? "" : "<uni-nav-bar left-icon=\"left\" @clickLeft=\"$yzp.back\" fixed statusBar :border=\"false\" title=\"" + Html(page.Source.Name) + "\"></uni-nav-bar>";
        var mobileNav = child || kind != 0 ? "" : nav.Replace("></uni-nav-bar>", (Flag(page.Source.Extra, "isSearch", filter.Count > 0) ? " rightText=\"筛选\" @clickRight=\"openFilterPopup('bottom')\"" : "") + "></uni-nav-bar>");
        var section = child || kind != 0;
        var data = new StringBuilder().Append("page_sn:").Append(J(page.Source.Id)).Append(',')
            .Append("page_is_new:").Append(B(page.Source.IsNew == 1)).Append(',')
            .Append("page_is_update:").Append(B(ChildrenCompilation.CanUpdate(page))).Append(',')
            .Append("page_is_edit:").Append(B(Flag(page.Source.Extra, "isEdit", true))).Append(',')
            .Append("page_is_filter:").Append(B(Flag(page.Source.Extra, "isSearch", filter.Count > 0) && filter.Count > 0)).Append(",permissionVersion:0,");
        if (canPopup) data.Append("childDetailPopupVisible:false,childDetailPopupId:'',childDetailPopupKey:0,");
        if (buttons.Batch && !selection) data.Append("selectedList:[],isSelectAll:false,showSelectedOnly:false,");
        data.Append(filter.Data);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auto_hide"] = child && Flag(page.Source.Extra, "isAutoHide") ? "v-if=\"list.length>0 || list_error || load_more_status === 'loading'\"" : "",
            ["pc_top_uni_section"] = section ? "<uni-section title='" + Html(page.Source.Name) + "' class='" + (child ? "son-page-uni-section" : "select-page-uni-section") + "' type='line'><view class='section-view'>" : "",
            ["pc_end_uni_section"] = section ? "</view></uni-section>" : "",
            ["pc_nav_bar"] = nav, ["mobile_nav_bar"] = mobileNav,
            ["filter_columns"] = filter.Markup,
            ["listNewButtonVisibleCondition"] = newVisible,
            ["list_buttons"] = buttons.Markup,
            ["list_main"] = desktop,
            ["mobile_list"] = Template("original-list-mobile-" + layout + ".vue.tpl").Replace("##list_columns##", mobile, StringComparison.Ordinal),
            ["multi_select_buttons"] = selection ? "<view class=\"multi-select-page-buttons\"><button size=\"mini\" @click=\"toggleSelectedOnly\">{{ showSelectedOnly ? '查看全部' : '仅看已选' }}</button><button type=\"primary\" size=\"mini\" @click=\"sureSelect\">选好了（{{ selectedList.length }}）</button><button size=\"mini\" @click=\"sureClose\">关闭</button></view>" : "",
            ["child_detail_popup"] = canPopup ? Popup(page, detail) : "",
            ["script_top"] = ScriptTop(table, page, detail, canPopup || listEdit, selection, child),
            ["//##other_return_data##"] = data.ToString(),
            ["//##lists_list_and_index##"] = "",
            ["//##const_list_column_ranges##"] = "",
            ["//##mounted_scripts##"] = filter.Defaults,
            ["//##popup_detail_methods##"] = canPopup ? PopupMethods : "",
            ["//##customer_button_handlers##"] = buttons.Methods + SortMethods(page),
            ["//##list_buttons_handler##"] = "",
            ["//##lists_select_change##"] = filter.Methods,
            ["//##init_lists##"] = filter.Requests,
            ["//##reset_columns##"] = filter.Reset,
            ["table_name"] = table.Source.TableName, ["page_sn"] = page.Source.Id, ["page_id"] = page.Source.Id,
            ["toDetailNewGuard"] = child ? "if (id === '' && (this.readOnlyCondition || !this.page_is_new)) {" : "if (id === '' && (this.readOnlyCondition || !this.page_is_new || !yzp.hasPagePermission(this.page_sn, 'is_new'))) {",
            ["toDetailQueryGuard"] = child ? "if (id !== '' && !this.page_is_edit) {" : "if (id !== '' && (!this.page_is_edit || !yzp.hasPagePermission(this.page_sn, 'is_query'))) {"
        };
        var result = Template("original-list-ordinary.vue.tpl");
        // 列表继承父/全局只读；新增和更新按各自能力判断，避免旧模板使“仅新增”页面丢失新增入口。
        result = result.Replace("!this.page_is_update || this.parentReadOnlyCondition", "this.parentReadOnlyCondition || getApp().globalData.readOnly", StringComparison.Ordinal);
        result = result.Replace("this.clicked_id = id;", "this.clicked_id = id;const detailIdentity=this.originalListIdentity();const detailCurrent=()=>this._originalListAlive && detailIdentity===this.originalListIdentity();", StringComparison.Ordinal)
            .Replace("that.handleDetailChange(change);", "if(detailCurrent())that.handleDetailChange(change);", StringComparison.Ordinal)
            .Replace("+ id + '&parent_id=' + this.parent_id + '&parent_page_sn=' + this.parent_page_sn", "+ encodeURIComponent(id) + '&parent_id=' + encodeURIComponent(this.parent_id || '') + '&parent_page_sn=' + encodeURIComponent(this.parent_page_sn || '')", StringComparison.Ordinal);
        foreach (var (key, value) in values)
            result = result.Replace(key.StartsWith("//##", StringComparison.Ordinal) ? key : "##" + key + "##", value, StringComparison.Ordinal);
        if (System.Text.RegularExpressions.Regex.IsMatch(result, @"##[a-zA-Z_]+##")) throw new InvalidOperationException("原列表模板有未解析占位符。");
        return result;
    }

    /// <summary>原PC操作列和三种移动单元格使用同一已发布字段集合，字段跨度和重点样式保持原配置。</summary>
    private static (string Desktop, string Mobile) Rows(GenerationModel model, TableModel table, PageModel page, IReadOnlyList<ColumnModel> fields, int layout, int kind, string action, string queryVisible, ListButtons buttons, string detail, bool listEdit)
    {
        if (listEdit) return ($"<template v-for=\"item in list\" :key=\"item.id\"><{detail} :is_props='true' :parentReadOnlyCondition='readOnlyCondition' :props_id='item.id' :props_parent_id='parent_id' :props_parent_page_sn='parent_page_sn' ref='{detail}'></{detail}></template>", MobileCells(model, table, page, fields, layout));
        var result = new StringBuilder("<uni-table class=\"list-page-pc-body-table\" border  stripe emptyText=\"暂无更多数据\"><uni-tr>\n<!-- 操作列可见性与默认查看权限分离，行内按钮按自身规则显示。 -->");
        result.Append("<uni-th align=\"center\" v-if=\"").Append(action).Append("\" width=\"").Append(buttons.HasGrid ? "" : "90").Append("\">");
        if (kind != 2) result.Append("操作");
        if (buttons.Batch) result.Append("<checkbox " + (kind != 2 ? "style='margin-left:10px;' " : "") + ":checked=\"isSelectAll\" @click=\"selectAll\" />");
        result.AppendLine("</uni-th>");
        foreach (var column in fields) result.Append("<uni-th ").Append(page.Sortable.Contains(column) ? "sortable @sort-change=\"" + column.JsonName + "SortChange\"" : "").Append('>').Append(Html(column.Source.Name)).AppendLine("</uni-th>");
        result.Append("</uni-tr>\n<uni-tr :class=\"clicked_id == item.id ? 'uni-table-tr-clicked' : ''\" v-for='(item, index) in list' :key='item.id'><uni-td v-if=\"").Append(action).Append("\" class=\"buttons\"><view>");
        if (kind == 1) result.Append(Link("选择", "", "selectThis(item)", "default"));
        else
        {
            if (kind != 2) result.Append(Link("查看", queryVisible, "toDetail(item.id)", "default"));
            if (buttons.Batch) result.Append("<checkbox :checked=\"item.isSelect\" @click=\"chkChange(index)\" />");
        }
        result.Append(buttons.Grid).Append("</view></uni-td>");
        foreach (var column in fields) result.Append("<uni-td>").Append(Cell(model, table, page, column)).AppendLine("</uni-td>");
        result.Append("</uni-tr>");
        if (Flag(page.Source.Extra, "isSum"))
        {
            result.Append("<uni-tr><uni-td></uni-td>");
            foreach (var column in fields)
            {
                var field = Field(model, page, column); var sum = Integer(field.Extra, "sumKind", 0);
                result.Append("<uni-td>");
                if (sum is >= 1 and <= 3) result.Append("<view>").Append(sum == 1 ? "合计:" : sum == 2 ? "全部:" : "平均:").Append("{{statistics.").Append(column.JsonName).Append(".total}}").Append(sum == 1 ? "" : "%").Append("</view>");
                result.Append("</uni-td>");
            }
            result.Append("</uni-tr>");
        }
        return (result.Append("</uni-table>").ToString(), MobileCells(model, table, page, fields, layout));
    }

    /// <summary>按原移动端字段顺序展开，不把移动卡片替换成PC表格。</summary>
    private static string MobileCells(GenerationModel model, TableModel table, PageModel page, IReadOnlyList<ColumnModel> fields, int layout)
    {
        var result = new StringBuilder();
        for (var index = 0; index < fields.Count; index++)
        {
            var column = fields[index];
            if (layout == 3) { if (index == 0) result.Append("<view class='yz-row3-first-row'>"); else if (index == 1) result.Append("<view>"); else if (index == 3) result.Append("</view></view>"); }
            result.Append(Integer(column.Source.Extra, "mobileListPageColSpan", 1) == 2 ? "<view class='row-item span2'>" : "<view class='row-item'>");
            var cell = Cell(model, table, page, column);
            if (column.Source.ControlKind is not ("Image" or "MultiImage" or "IntenetImage" or "InternetImage" or "Upload")) result.Append("<text>").Append(Html(column.Source.Name)).Append("</text>");
            result.Append(cell).Append("</view>");
        }
        // 原三式卡片前三列构成头部；少于三列时也必须闭合相同的容器。
        if (layout == 3 && fields.Count > 0 && fields.Count <= 3) { if (fields.Count > 1) result.Append("</view>"); result.Append("</view>"); }
        return result.ToString();
    }

    /// <summary>原列表展示组件和命名保留；关系名称只使用真实声明辅助列，不能凭空添加数据库字段。</summary>
    private static string Cell(GenerationModel model, TableModel table, PageModel page, ColumnModel column)
    {
        var kind = Effective(column.Source, Field(model, page, column), "controlKind", column.Source.ControlKind);
        var field = Field(model, page, column);
        if (kind == "FileLibrary") return ControlRenderer.FileLibraryMarkup(table, page, column, "item", false).Replace(":parent-id=\"parentId\"", ":parent-id=\"parent_id\"", StringComparison.Ordinal);
        if (kind == "CascadeTree") return "<text>{{item." + column.JsonName + "DisplayText}}</text>";
        if (column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal) && kind.Length > 0 || column.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal)) return "<text>{{$yzp.relationDisplayText(item." + RelationName(table, column) + ")}}</text>";
        if (kind is "ConstList" or "ConstDownList") return "{{item." + column.JsonName + "Show}}";
        if (kind == "CheckBox") return "{{item." + column.JsonName + " === true || item." + column.JsonName + " === 1 || item." + column.JsonName + " === '1' ? '是' : '否'}}";
        if (kind is "Month" or "month") return "{{item." + column.JsonName + " ? String(item." + column.JsonName + ").slice(0, 7) : ''}}";
        if (kind is "IntenetImage" or "InternetImage") return "<image class='list-img' mode='widthFix' :src='item." + column.JsonName + "'></image>";
        if (kind is "Image" or "MultiImage") return "<uni-file-picker :imageStyles='$imageStyles' disabled readonly limit='" + Html(Effective(column.Source, field, "controlDetail", column.Source.ControlDetail)) + "' v-model='item." + column.JsonName + "'></uni-file-picker>";
        if (kind == "Upload") return "";
        return "<text " + (Flag(field.Extra, "isImportant") ? "class='list-column-important'" : "") + ">{{item." + column.JsonName + "}}</text>";
    }

    /// <summary>名称辅助列必须来自现有声明；未声明时保留原ID，不发明列。</summary>
    private static string RelationName(TableModel table,ColumnModel column)=>SelectionDisplayFieldCompilation.Resolve(table,column).JsonName;
    /// <summary>页级控件设置按原规则覆盖字段，空值继承基础配置。</summary>
    private static string Effective(ColumnMetadata column, PageColumnMetadata field, string key, string fallback) => field.Extra.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null && value.ToString().Length > 0 ? value.ToString() : fallback;
    private static PageColumnMetadata Field(GenerationModel model, PageModel page, ColumnModel column) => model.Catalog.ConfigPageColumns.Single(field => field.ConfigPageId == page.Source.Id && field.ConfigColumnId == column.Source.Id);
    /// <summary>元数据数字或布尔只在固定键上解释。</summary>
    private static bool Flag(Dictionary<string, JsonElement> extra, string key, bool fallback = false) => !extra.TryGetValue(key, out var value) ? fallback : value.ValueKind == JsonValueKind.True || value.ToString() == "1";
    private static int Integer(Dictionary<string, JsonElement> extra, string key, int fallback) => extra.TryGetValue(key, out var value) && int.TryParse(value.ToString(), out var result) ? result : fallback;
    private static string Text(Dictionary<string, JsonElement> extra, string key, string fallback = "") => extra.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback;
    private static string J(object? value) => JsonSerializer.Serialize(value);
    private static string B(bool value) => value ? "true" : "false";
    private static string Html(string value) => SecurityElement.Escape(value) ?? "";
    private static string Template(string name)
    {
        var assembly = typeof(OriginalUniappListRenderer).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(value => value.EndsWith("." + name, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!); return reader.ReadToEnd();
    }
}
