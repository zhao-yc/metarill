using System.Reflection;
using System.Security;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>标准 uni-app 模板复用项目现有请求、登录态和 uni-ui 组件。</summary>
internal static class UniappRenderer
{
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        var routes = new List<object>();
        foreach (var table in model.Tables)
        foreach (var page in table.Pages)
        {
            var stem = table.Source.TableName;
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
                ["PAGE_SIZE"] = Math.Clamp(page.Source.PageSize, 1, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["HEADERS"] = string.Concat(page.Readable.Select(c => "<uni-th>" + Escape(c.Source.Name) + "</uni-th>")),
                ["CELLS"] = string.Concat(page.Readable.Select(c => "<uni-td>{{ row." + c.JsonName + " }}</uni-td>")),
                ["FIELDS"] = string.Join("\n", page.Readable.Select(c => page.Writable.Contains(c)
                    ? $"      <uni-forms-item label=\"{Escape(c.Source.Name)}\" name=\"{c.JsonName}\"><uni-easyinput :modelValue=\"entity.{c.JsonName}\" @update:modelValue=\"change('{c.JsonName}', $event)\" /></uni-forms-item>"
                    : $"      <uni-forms-item label=\"{Escape(c.Source.Name)}\"><text>{{{{ entity.{c.JsonName} }}}}</text></uni-forms-item>")),
                ["WRITABLE_FIELDS"] = JsonSerializer.Serialize(page.Writable.Select(c => c.JsonName))
            };
            yield return new("frontend/" + listPath + ".vue", RenderTemplate("list.vue.tpl", parameters));
            yield return new("frontend/" + detailPath + ".vue", RenderTemplate("detail.vue.tpl", parameters));
            routes.Add(new { path = listPath, style = new { navigationBarTitleText = page.Source.Name } });
            routes.Add(new { path = detailPath, style = new { navigationBarTitleText = page.Source.Name } });
        }
        yield return new("frontend/pages.generated.json", JsonSerializer.Serialize(new { pages = routes }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
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
