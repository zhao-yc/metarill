using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>为原可见规则绑定保存时仍是布尔型的选择字段。</summary>
internal static class OriginalVisibilityCompilation
{
    /// <summary>只识别本页已读且在规则中直接引用的选项字段，允许原物理名与 JSON 名。</summary>
    internal static IReadOnlyDictionary<string, string> BooleanOptions(PageModel page, string rule)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rule)) return bindings;
        var fields = page.Readable.Where(BackendRenderer.UsesBooleanOptionWireFormat).ToArray();
        if (fields.Length == 0) return bindings;

        ButtonVisibilityRule compiled;
        try { compiled = ButtonVisibilityRule.Compile(rule); }
        catch (ArgumentException) { return bindings; }

        foreach (var field in compiled.Fields)
        {
            var column = fields.SingleOrDefault(candidate =>
                candidate.JsonName == field || candidate.Source.ColumnName == field);
            if (column is not null) bindings.TryAdd(field, column.JsonName);
        }
        return bindings;
    }

    /// <summary>列表行和详情按钮发布同一规则参数。授权、来源记录和动作仍由原入口核验。</summary>
    internal static Dictionary<string, object> Button(PageModel page, string id, string scheme, string rule)
    {
        var config = new Dictionary<string, object> { ["buttonId"] = id, ["visibleScheme"] = scheme, ["visibleRuleJson"] = rule };
        var options = BooleanOptions(page, rule);
        if (options.Count > 0) config.Add("booleanOptionFields", options);
        return config;
    }
}
