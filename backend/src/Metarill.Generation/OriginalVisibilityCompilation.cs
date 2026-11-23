using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>原字段和按钮的条件只绑定当前页公开的实际字段。</summary>
internal static class OriginalVisibilityCompilation
{
    /// <summary>物理名和 JSON 名都必须唯一落到当前页的同一实际字段。</summary>
    internal static ColumnModel? ResolveField(PageModel page, string name) =>
        page.Readable.SingleOrDefault(column => column.JsonName == name || column.Source.ColumnName == name);

    /// <summary>只发布规则实际引用的别名、布尔控件和已声明 JSON 对象。</summary>
    internal static Dictionary<string, object> Options(PageModel page, string rule)
    {
        var options = new Dictionary<string, object>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rule)) return options;

        ButtonVisibilityRule compiled;
        try { compiled = ButtonVisibilityRule.Compile(rule); }
        catch (ArgumentException) { return options; }

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var booleanOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var jsonFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in compiled.Fields)
        {
            var names = path.Split('.', 2);
            var nested = names.Length == 2;
            var column = nested ? ReadProjectionCompilation.VisibilityJsonRoot(page, path) : ResolveField(page, names[0]);
            if (column is null) continue;
            if (nested) jsonFields.Add(column.JsonName);
            if (!nested && BackendRenderer.UsesBooleanOptionWireFormat(column))
                booleanOptions.TryAdd(names[0], column.JsonName);
            else if (names[0] != column.JsonName)
                aliases.TryAdd(names[0], column.JsonName);
        }
        if (jsonFields.Count > 0) options.Add("jsonObjectFields", jsonFields.Order(StringComparer.Ordinal).ToArray());
        if (booleanOptions.Count > 0) options.Add("booleanOptionFields", booleanOptions);
        if (aliases.Count > 0) options.Add("fieldAliases", aliases);
        return options;
    }

    /// <summary>列表与详情按钮共用原规则参数，记录授权继续由原入口执行。</summary>
    internal static Dictionary<string, object> Button(PageModel page, string id, string scheme, string rule)
    {
        var config = new Dictionary<string, object> { ["buttonId"] = id, ["visibleScheme"] = scheme, ["visibleRuleJson"] = rule };
        foreach (var (key, value) in Options(page, rule))
            config.Add(key, value);
        return config;
    }
}
