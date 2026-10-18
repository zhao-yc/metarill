using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Generation;

internal sealed record ControlResult(string Markup, string? UnsupportedReason = null);

/// <summary>优先复用现有控件；未接入的能力明确显示为只读，避免错误保存。</summary>
internal static class ControlRenderer
{
    /// <summary>按声明控件生成复用组件或只读保护。</summary>
    public static ControlResult Render(GenerationModel model, TableModel table, PageModel page, ColumnModel column)
    {
        var label = Escape(column.Source.Name);
        var binding = $":modelValue=\"entity.{column.JsonName}\"";
        var change = $"@update:modelValue=\"change('{column.JsonName}', $event)\"";
        var wrapper = $"      <uni-forms-item label=\"{label}\" name=\"{column.JsonName}\">";
        ControlResult Wrap(string content, string? reason = null) => new(wrapper + content + "</uni-forms-item>", reason);
        if (!page.Writable.Contains(column)) return Wrap($"<text>{{{{ entity.{column.JsonName} }}}}</text>");
        switch (column.Source.ControlKind)
        {
            case "": return Wrap($"<uni-easyinput {binding} {change} />");
            case "TextArea": return Wrap($"<uni-easyinput type=\"textarea\" {binding} {change} />");
            case "ConstList": case "ConstDownList":
                return Wrap($"<uni-data-select {binding} {change} :localdata=\"{Escape(ConstOptions(column))}\" />");
            case "CheckBox":
                var choices = column.Type == "bool?" ? "[{\"text\":\"是\",\"value\":true},{\"text\":\"否\",\"value\":false}]" : "[{\"text\":\"是\",\"value\":1},{\"text\":\"否\",\"value\":0}]";
                return Wrap($"<uni-data-select {binding} {change} :localdata=\"{Escape(choices)}\" />");
            case "date": case "date_time":
                return Wrap($"<uni-datetime-picker type=\"{(column.Source.ControlKind == "date" ? "date" : "datetime")}\" {binding} @change=\"change('{column.JsonName}', $event)\" />");
            case "List":
                var relation = RelationCompilation.ForPage(model, page).SingleOrDefault(r => r.SourceColumn == column);
                if (relation is not null) return Wrap($"<uni-data-select {binding} {change} :localdata=\"options.{column.JsonName} || []\" />");
                break;
            case "FileLibrary":
                return Wrap($"<yzp-file-library {binding} {change} config-column-id=\"{column.Source.Id}\" page-id=\"{page.Source.Id}\" table-id=\"{table.Source.Id}\" :record-id=\"id\" :parent-id=\"parentId\" access-mode=\"PROTECTED\" accept=\"image,file\" />");
            case "SelectPage":
                if (!string.IsNullOrWhiteSpace(column.Source.ControlDetail))
                {
                    using var detail = JsonDocument.Parse(column.Source.ControlDetail);
                    if (detail.RootElement.TryGetProperty("pageId", out var id))
                    {
                        var selected = model.Catalog.ConfigPages.SingleOrDefault(p => p.Id == id.ToString());
                        var target = model.Tables.SingleOrDefault(t => t.Source.Id == selected?.ConfigTableId);
                        if (selected is not null && target is not null) return Wrap($"<button size=\"mini\" @click=\"selectPage('{column.JsonName}', '/pages/yzp/{target.Source.TableName}-list-{selected.Id}')\">选择</button><text>{{{{ entity.{column.JsonName} }}}}</text>");
                    }
                }
                break;
            case "CustomControl":
                if (Regex.IsMatch(column.Source.ControlDetail, "^yzp-[a-z0-9]+(?:-[a-z0-9]+)*$"))
                    return Wrap($"<{column.Source.ControlDetail} :entity=\"entity\" :record-id=\"id\" :readonly=\"false\" @receive=\"applyCustom\" />");
                break;
            case "IntenetImage":
                return Wrap($"<image v-if=\"entity.{column.JsonName}\" :src=\"entity.{column.JsonName}\" mode=\"aspectFit\" /><uni-easyinput {binding} {change} />");
        }
        return Wrap($"<text>{{{{ entity.{column.JsonName} }}}}</text><text>此控件待移植，当前只读</text>", "控件或目标关系尚未完成移植：" + column.Source.ControlKind);
    }
    /// <summary>转义标签和属性中的元数据文本。</summary>
    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
    /// <summary>编译常量选项并保留字段值类型。</summary>
    private static string ConstOptions(ColumnModel column)
    {
        var result = new List<object>();
        foreach (var token in column.Source.ControlDetail.Split(','))
        {
            var pieces = token.Split('|', 2);
            var text = pieces[0]; var raw = pieces.Length == 2 ? pieces[1] : pieces[0];
            object? value = raw;
            if (column.Type is "long?" or "int?") value = string.IsNullOrEmpty(raw) ? null : long.Parse(raw, CultureInfo.InvariantCulture);
            else if (column.Type == "decimal?") value = string.IsNullOrEmpty(raw) ? null : decimal.Parse(raw, CultureInfo.InvariantCulture);
            result.Add(new { text, value });
        }
        return JsonSerializer.Serialize(result);
    }
}
