using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Generation;

internal sealed record ControlResult(string Markup, string? UnsupportedReason = null);
internal sealed record RelationUiContext(bool SameParent, string? TargetParentField, bool UnresolvedTarget);

/// <summary>复用原有控件；只编译已完成的类型，未接入的能力同时标明原因。</summary>
internal static class ControlRenderer
{
    /// <summary>按声明控件生成原有编辑器，字段白名单与整页只读共同限制编辑。</summary>
    public static ControlResult Render(GenerationModel model, TableModel table, PageModel page, ColumnModel column)
    {
        var label = Escape(column.Source.Name);
        var binding = $":modelValue=\"entity.{column.JsonName}\" :disabled=\"readOnlyCondition\"";
        var change = $"@update:modelValue=\"change('{column.JsonName}', $event)\"";
        var wrapper = $"      <uni-forms-item label=\"{label}\" name=\"{column.JsonName}\">";
        ControlResult Wrap(string content, string? reason = null) => new(wrapper + content + "</uni-forms-item>", reason);
        var writable = page.Writable.Contains(column);
        // 只读展示也不能把未编译关系伪装成已完成的选择控件。
        if (column.Source.ControlKind == "List" && !RelationCompilation.ForPage(model, page).Any(relation => relation.SourceColumn == column) || column.Source.ControlKind == "SelectPage" && SelectionTarget(model, column) is null)
            return Wrap($"<text>{{{{ entity.{column.JsonName} }}}}</text><text>此关系控件待移植，当前只读</text>", "目标关系尚未完成移植：" + column.Source.ControlKind);
        if (column.Source.ControlKind == "List")
        {
            var relation = RelationCompilation.ForPage(model, page).Single(candidate => candidate.SourceColumn == column);
            var context = RelationContext(page, relation, false);
            if (context.UnresolvedTarget) return Wrap($"<text>{{{{ displayValue(entity, '{column.JsonName}') }}}}</text><text>关联目标父范围尚未明确，当前只读并保留原ID</text>", "关联目标需要独立 targetParentId，当前页没有唯一可读的声明外键。");
            return Wrap(RelationMarkup(model, page, relation, context, "entity", writable ? "readOnlyCondition" : "true", writable ? change : ""));
        }
        if (column.Source.ControlKind == "FileLibrary") return Wrap(FileLibraryMarkup(table, page, column, "entity", writable));
        if (column.Source.ControlKind == "RTF" && column.Type == "string?")
            return Wrap((writable ? $"<button size=\"mini\" :disabled=\"readOnlyCondition\" @click=\"openRtf('{column.JsonName}', 'edit')\">编辑</button>" : "") + $"<button size=\"mini\" @click=\"openRtf('{column.JsonName}', 'review')\">预览</button><rich-text :nodes=\"entity.{column.JsonName} || ''\" />");
        if (!writable && column.Source.ControlKind == "IntenetImage") return Wrap($"<image v-if=\"entity.{column.JsonName}\" :src=\"entity.{column.JsonName}\" mode=\"aspectFit\" />");
        if (!writable && column.Source.ControlKind is "" or "TextArea" or "ConstList" or "ConstDownList" or "CheckBox" or "date" or "date_time" or "List" or "SelectPage" or "IntenetImage")
            return Wrap($"<text>{{{{ displayValue(entity, '{column.JsonName}') }}}}</text>");
        switch (column.Source.ControlKind)
        {
            case "": return Wrap($"<uni-easyinput {binding} {change} />");
            case "TextArea": return Wrap($"<uni-easyinput type=\"textarea\" {binding} {change} />");
            case "ConstList": case "ConstDownList":
                return Wrap($"<uni-data-select {binding} {change} :localdata=\"{Escape(ConstOptions(column))}\" />");
            case "CheckBox":
                return Wrap($"<uni-data-select {binding} {change} :localdata=\"{Escape(CheckOptions(column))}\" />");
            case "date": case "date_time":
                return Wrap($"<uni-datetime-picker type=\"{(column.Source.ControlKind == "date" ? "date" : "datetime")}\" {binding} @change=\"change('{column.JsonName}', $event)\" />");
            case "SelectPage":
                var target = SelectionTarget(model, column);
                if (target is not null) return Wrap($"<button size=\"mini\" :disabled=\"readOnlyCondition\" @click=\"selectPage('{column.JsonName}', '{target}')\">选择</button><text>{{{{ displayValue(entity, '{column.JsonName}') }}}}</text>");
                break;
            case "CustomControl":
                if (Regex.IsMatch(column.Source.ControlDetail, "^yzp-[a-z0-9]+(?:-[a-z0-9]+)*$"))
                    return Wrap($"<{column.Source.ControlDetail} :entity=\"entity\" :record-id=\"id\" :readonly=\"{(writable ? "readOnlyCondition" : "true")}\" {(writable ? "@receive=\"applyCustom\"" : "")} />");
                break;
            case "IntenetImage":
                return Wrap($"<image v-if=\"entity.{column.JsonName}\" :src=\"entity.{column.JsonName}\" mode=\"aspectFit\" /><uni-easyinput {binding} {change} />");
        }
        return Wrap($"<text>{{{{ entity.{column.JsonName} }}}}</text><text>此控件待移植，当前只读</text>", "控件或目标关系尚未完成移植：" + column.Source.ControlKind);
    }

    /// <summary>只为后端确实开放且前端已支持的筛选生成输入；复杂 JSON 控件不伪装成文本筛选。</summary>
    public static ControlResult RenderFilter(GenerationModel model, PageModel page, ColumnModel column)
    {
        var label = Escape(column.Source.Name);
        var binding = $":modelValue=\"draftFilters.{column.JsonName}\"";
        var change = $"@update:modelValue=\"setFilter('{column.JsonName}', $event)\"";
        ControlResult Wrap(string content) => new($"<uni-forms-item label=\"{label}\" name=\"{column.JsonName}\">{content}</uni-forms-item>");
        if (column.Source.ControlKind is "ConstList" or "ConstDownList") return Wrap($"<uni-data-select {binding} {change} :localdata=\"{Escape(ConstOptions(column))}\" />");
        if (column.Source.ControlKind == "CheckBox" || column.Type == "bool?" && column.Source.ControlKind == "") return Wrap($"<uni-data-select {binding} {change} :localdata=\"{Escape(CheckOptions(column))}\" />");
        if (column.Source.ControlKind == "List" && RelationCompilation.ForPage(model, page).SingleOrDefault(relation => relation.SourceColumn == column) is { } relation)
        {
            var context = RelationContext(page, relation, true);
            if (context.UnresolvedTarget) return new("", "关联筛选缺少可证明的目标父范围，当前不提供选择。");
            return Wrap(RelationMarkup(model, page, relation, context, "draftFilters", "false", change));
        }
        if (column.Type == "DateTime?" && column.Source.ControlKind is "" or "date" or "date_time")
            return Wrap($"<uni-datetime-picker type=\"{(column.Source.ControlKind == "date" ? "daterange" : "datetimerange")}\" {binding} @change=\"setFilter('{column.JsonName}', $event)\" />");
        if (column.Source.ControlKind is "" or "TextArea" or "RTF")
            return Wrap($"<uni-easyinput {binding} {change} placeholder=\"请输入{label}\" />");
        return new("", "筛选控件尚未完成移植：" + column.Source.ControlKind);
    }

    /// <summary>目标父只取同父上下文或唯一可读声明外键，不猜索引到表等跨层映射。</summary>
    internal static RelationUiContext RelationContext(PageModel page, RelationModel relation, bool filter)
    {
        var same = relation.SourceParents.Count > 0 && relation.SourceParents.Select(parent => parent.Source.Id).Order().SequenceEqual(relation.TargetParents.Select(parent => parent.Source.Id).Order());
        if (same || relation.TargetParents.Count == 0) return new(same, null, false);
        if (relation.TargetParents.Count != 1) return new(false, null, true);
        var target = relation.TargetParents[0].Source.Id;
        var fields = page.Readable.Where(column => column.Source.Id != relation.SourceColumn.Source.Id && column.Source.LinkConfigTableId == target && (!filter || page.Searchable.Contains(column))).Take(2).ToArray();
        return fields.Length == 1 ? new(false, fields[0].JsonName, false) : new(false, null, true);
    }

    /// <summary>远程外壳保持原选择器交互，只消费当前页已编译的固定端点。</summary>
    private static string RelationMarkup(GenerationModel model, PageModel page, RelationModel relation, RelationUiContext context, string row, string readOnly, string change)
    {
        var table = model.Tables.Single(table => table.Source.Id == page.Source.ConfigTableId);
        var endpoint = "/admin/auto/" + table.Source.TableName + "/" + page.Source.Id + "/" + relation.Endpoint;
        var target = context.TargetParentField is null ? "" : $" :target-parent-id=\"{row}.{context.TargetParentField}\"";
        return $"<yzp-relation-select :modelValue=\"{row}.{relation.SourceColumn.JsonName}\" endpoint=\"{Escape(endpoint)}\" :parent-id=\"parentId\"{target} :require-parent=\"{(relation.SourceParents.Count > 0).ToString().ToLowerInvariant()}\" :require-target-parent=\"{(relation.TargetParents.Count > 0).ToString().ToLowerInvariant()}\" :same-parent=\"{context.SameParent.ToString().ToLowerInvariant()}\" :readonly=\"{readOnly}\" {change} />";
    }

    /// <summary>文件库完整传递声明配置；默认保护访问与后端一致，未知或损坏值在生成阶段拒绝。</summary>
    internal static string FileLibraryMarkup(TableModel table, PageModel page, ColumnModel column, string row, bool writable)
    {
        var access = "PROTECTED"; var accept = "image,file,video"; var maximum = 9; var size = 50; var allow = true; var crop = false; var extensions = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(column.Source.ControlDetail))
        {
            using var document = JsonDocument.Parse(column.Source.ControlDetail); var detail = document.RootElement;
            if (detail.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("文件库配置必须为对象：" + column.Source.ColumnName);
            var known = new[] { "accessMode", "accept", "maxCount", "maxSizeMb", "extensions", "allowLibrary", "cropImages" };
            if (detail.EnumerateObject().Any(property => !known.Contains(property.Name, StringComparer.Ordinal))) throw new InvalidOperationException("文件库包含未知配置：" + column.Source.ColumnName);
            if (detail.TryGetProperty("accessMode", out var value) && value.ValueKind != JsonValueKind.Null) access = value.GetString()!;
            if (access is not ("PUBLIC" or "PROTECTED")) throw new InvalidOperationException("文件库访问模式无效。");
            if (detail.TryGetProperty("accept", out value) && value.ValueKind != JsonValueKind.Null)
            {
                var modes = (value.GetString() ?? "").Split(',').Select(item => item.Trim()).ToArray();
                if (modes.Length == 0 || modes.Any(mode => mode is not ("image" or "file" or "video"))) throw new InvalidOperationException("文件库上传类型无效。");
                accept = string.Join(',', modes.Distinct(StringComparer.Ordinal));
            }
            if (detail.TryGetProperty("maxCount", out value) && value.ValueKind != JsonValueKind.Null) maximum = value.GetInt32();
            if (detail.TryGetProperty("maxSizeMb", out value) && value.ValueKind != JsonValueKind.Null) size = value.GetInt32();
            if (maximum is < 1 or > 100 || size is < 1 or > 10240) throw new InvalidOperationException("文件库数量或大小上限无效。");
            if (detail.TryGetProperty("extensions", out value) && value.ValueKind != JsonValueKind.Null)
            {
                if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || !Regex.IsMatch(item.GetString() ?? "", @"^\.?[A-Za-z0-9]+$"))) throw new InvalidOperationException("文件库扩展名必须是合法数组。");
                extensions = value.EnumerateArray().Select(item => "." + item.GetString()!.TrimStart('.').ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
            }
            if (detail.TryGetProperty("allowLibrary", out value) && value.ValueKind != JsonValueKind.Null) allow = value.GetBoolean();
            if (detail.TryGetProperty("cropImages", out value) && value.ValueKind != JsonValueKind.Null) crop = value.GetBoolean();
        }
        var record = row == "entity" ? "id" : row + ".id";
        var events = writable ? $" @update:modelValue=\"change('{column.JsonName}', $event)\"" : "";
        return $"<yzp-file-library :modelValue=\"{row}.{column.JsonName}\"{events} config-column-id=\"{column.Source.Id}\" page-id=\"{page.Source.Id}\" table-id=\"{table.Source.Id}\" :record-id=\"{record}\" :parent-id=\"parentId\" access-mode=\"{access}\" accept=\"{accept}\" :max-count=\"{maximum}\" :max-size-mb=\"{Math.Min(size, 100)}\" :extensions=\"{Escape(JsonSerializer.Serialize(extensions))}\" :allow-library=\"{allow.ToString().ToLowerInvariant()}\" :crop-images=\"{crop.ToString().ToLowerInvariant()}\" :readonly=\"{(writable ? "readOnlyCondition" : "true")}\" />";
    }

    /// <summary>选择页必须存在于当前元数据生成目标，不拼接任意外部路由。</summary>
    private static string? SelectionTarget(GenerationModel model, ColumnModel column)
    {
        if (string.IsNullOrWhiteSpace(column.Source.ControlDetail)) return null;
        using var detail = JsonDocument.Parse(column.Source.ControlDetail);
        if (!detail.RootElement.TryGetProperty("pageId", out var id)) return null;
        var selected = model.Catalog.ConfigPages.SingleOrDefault(page => page.Id == id.ToString());
        var target = model.Tables.SingleOrDefault(table => table.Source.Id == selected?.ConfigTableId);
        return selected is not null && target is not null ? $"/pages/yzp/{target.Source.TableName}-list-{selected.Id}" : null;
    }

    /// <summary>转义标签和属性中的元数据文本。</summary>
    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
    /// <summary>布尔值与原整型复选框保持后端声明类型。</summary>
    private static string CheckOptions(ColumnModel column) => column.Type == "bool?" ? "[{\"text\":\"是\",\"value\":true},{\"text\":\"否\",\"value\":false}]" : "[{\"text\":\"是\",\"value\":1},{\"text\":\"否\",\"value\":0}]";
    /// <summary>编译常量选项，长整数按现有字符串 ID 协议传输，避免 JavaScript 丢精度。</summary>
    private static string ConstOptions(ColumnModel column)
    {
        var result = new List<object>();
        foreach (var token in column.Source.ControlDetail.Split(','))
        {
            var pieces = token.Split('|', 2); var text = pieces[0]; var raw = pieces.Length == 2 ? pieces[1] : pieces[0]; object? value = raw;
            if (column.Type is "long" or "long?") value = string.IsNullOrEmpty(raw) ? null : long.Parse(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            else if (column.Type == "int?") value = string.IsNullOrEmpty(raw) ? null : int.Parse(raw, CultureInfo.InvariantCulture);
            else if (column.Type == "decimal?") value = string.IsNullOrEmpty(raw) ? null : decimal.Parse(raw, CultureInfo.InvariantCulture);
            result.Add(new { text, value });
        }
        return JsonSerializer.Serialize(result);
    }
}
