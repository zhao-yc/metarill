using System.Reflection;
using System.Security;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>标准 uni-app 模板复用项目现有请求、登录态和 uni-ui 组件。</summary>
internal static class UniappRenderer
{
    /// <summary>渲染页面并记录尚未移植的控件。</summary>
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        var routes = new List<object>(); var gaps = new List<object>();
        foreach (var table in model.Tables)
        foreach (var page in table.Pages)
        {
            var stem = table.Source.TableName; foreach (var column in page.Readable) { var control = ControlRenderer.Render(model, table, page, column); if (control.UnsupportedReason is not null) gaps.Add(new { pageId = page.Source.Id, column = column.Source.ColumnName, reason = control.UnsupportedReason }); }
            var listPath = $"pages/yzp/{stem}-list-{page.Source.Id}";
            var detailPath = $"pages/yzp/{stem}-detail-{page.Source.Id}";
            var parameters = new Dictionary<string, string>
            {
                ["TITLE"] = Escape(page.Source.Name),
                ["API"] = "/admin/auto/" + stem + "/" + page.Source.Id,
                ["DETAIL_ROUTE"] = "/" + detailPath,
                ["CAN_CREATE"] = (page.Source.IsNew == 1).ToString().ToLowerInvariant(),
                ["CAN_SAVE"] = (page.Source.IsSave == 1).ToString().ToLowerInvariant(),
                ["CAN_DELETE"] = (page.Source.IsDelete == 1).ToString().ToLowerInvariant(),
                ["CAN_COPY"] = (page.Source.IsCopy == 1).ToString().ToLowerInvariant(),
                ["CAN_IMPORT"] = ImportRenderer.Enabled(page).ToString().ToLowerInvariant(),
                ["CAN_EXPORT"] = ExportRenderer.Enabled(page).ToString().ToLowerInvariant(),
                ["PAGE_ID"] = page.Source.Id,
                ["PAGE_SIZE"] = Math.Clamp(page.Source.PageSize, 1, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["HEADERS"] = string.Concat(page.ListColumns.Select(c => "<uni-th>" + Escape(c.Source.Name) + "</uni-th>")),
                ["CELLS"] = string.Concat(page.ListColumns.Select(c => "<uni-td>{{ row." + c.JsonName + " }}</uni-td>")),
                ["FIELDS"] = string.Join("\n", page.DetailColumns.Select(c => ControlRenderer.Render(model, table, page, c).Markup)),
                ["OPTION_REQUESTS"] = string.Join("\n", RelationCompilation.ForPage(model, page).Select(relation => $"      yzp.request({{ url: '{"/admin/auto/" + table.Source.TableName + "/" + page.Source.Id + "/" + relation.Endpoint}', method: 'POST', data: {{ parentId: this.parentId || null }}, sendSessionId: true, success: res => {{ if (res.code === 200) this.options.{relation.SourceColumn.JsonName} = res.data; }} }});")),                ["WRITABLE_FIELDS"] = JsonSerializer.Serialize(page.Writable.Select(c => c.JsonName))
            };
            yield return new("frontend/" + listPath + ".vue", RenderTemplate("list.vue.tpl", parameters));
            yield return new("frontend/" + detailPath + ".vue", RenderTemplate("detail.vue.tpl", parameters));
            routes.Add(new { path = listPath, style = new { navigationBarTitleText = page.Source.Name } });
            routes.Add(new { path = detailPath, style = new { navigationBarTitleText = page.Source.Name } });
        }
        yield return new("generation-capabilities.json", JsonSerializer.Serialize(new { schemaVersion = 1, unsupportedControls = gaps }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        yield return new("frontend/pages.generated.json", JsonSerializer.Serialize(new { pages = routes }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
    /// <summary>转义展示文本以保护模板结构。</summary>
    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
    /// <summary>嵌入模板保持离线 CLI 可搬移，配置值不能成为模板代码。</summary>
    private static string RenderTemplate(string name, Dictionary<string, string> values)
    {
        var assembly = typeof(UniappRenderer).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd().Replace("\r\n", "\n");
        foreach (var (key, value) in values) content = content.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        return content;
    }
}
