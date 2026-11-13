namespace Metarill.Generation;

internal sealed record FixedCustomControlReadContract(string Tag, IReadOnlyList<ColumnModel> Columns);
internal sealed record FixedCustomControlReadResult(FixedCustomControlReadContract? Contract, string? Reason);

/// <summary>只证明原平台三个固定组件的实体读取；不声明写入、HTTP授权或完整包组件已接线。</summary>
internal static class FixedCustomControlReadCompilation
{
    /// <summary>精确标签、固定宿主表列和当前页读取上限必须同时成立，不能把clone整实体当成读取全列。</summary>
    internal static FixedCustomControlReadResult Resolve(GenerationModel model, TableModel table, PageModel page, ColumnModel source)
    {
        FixedCustomControlReadResult Gap(string reason) => new(null, "固定CustomControl读取尚未证明：" + reason);
        if (!model.Tables.Any(item => ReferenceEquals(item, table)) || !table.Pages.Any(item => ReferenceEquals(item, page)) ||
            !table.Columns.Any(item => item.Source.Id == source.Source.Id) || source.Source.ControlKind != "CustomControl")
            return Gap("控件不属于当前生成模型的原详情分支。");
        var field = model.Catalog.ConfigPageColumns.SingleOrDefault(item => item.ConfigPageId == page.Source.Id && item.ConfigColumnId == source.Source.Id);
        if (field is null) return Gap("来源页未声明控件。");
        // 与原详情Field一致：只有长度为零才继承；空白标签或夹带属性绝不解释成已知组件。
        var tag = OriginalDetailContext.Text(field.Extra, "controlDetail");
        if (tag.Length == 0) tag = source.Source.ControlDetail;
        string[] required;
        string[] absent = [];
        switch ((tag, table.Source.TableName, source.Source.ColumnName))
        {
            case ("yzp-button-visible-rule-editor", "config_button", "visible_rule_json"):
                required = ["visible_rule_json"];
                break;
            case ("yzp-table-storage-mode", "config_table", "is_rongyu"):
                required = ["id", "is_rongyu", "is_view", "is_function_table"];
                break;
            case ("yzp-control-detail-editor", "config_column", "control_detail"):
                required = ["control_detail", "control_kind", "name"];
                absent = ["config_column_id"];
                break;
            case ("yzp-control-detail-editor", "config_page_column", "control_detail"):
                required = ["control_detail", "config_column_id", "name"];
                absent = ["control_kind"];
                break;
            default:
                return Gap("标签、属性或宿主表列不在固定合同内：" + tag);
        }
        // 两种control_detail宿主使用原组件不同分支；新属性不能未经证明改变继承读取入口。
        if (absent.Any(name => table.Columns.Any(column => column.Source.ColumnName == name)))
            return Gap("宿主出现了固定分支之外的实体属性：" + string.Join("、", absent));
        var columns = new List<ColumnModel>();
        foreach (var name in required)
        {
            var column = table.Columns.SingleOrDefault(item => item.Source.ColumnName == name);
            if (column is null || !page.Readable.Any(item => item.Source.Id == column.Source.Id))
                return Gap("本页缺少已声明可读依赖：" + name);
            if (column.Source.IsVirtualColumn != 0 || GenerationModel.Enabled(column.Source.Extra, "isJoinColumn") || column.IsJsonArray)
                return Gap("依赖不是固定物理标量：" + name);
            var supported = name switch
            {
                "id" => column.IsId,
                "is_rongyu" => column.Type == "int?",
                "is_view" or "is_function_table" => column.Type is "int?" or "bool?",
                "config_column_id" => column.Type is "long?" or "string?",
                _ => column.Type == "string?"
            };
            if (!supported) return Gap("固定依赖类型未证明：" + name);
            columns.Add(column);
        }
        return new(new(tag, columns.DistinctBy(column => column.Source.Id).OrderBy(column => column.Source.Id, StringComparer.Ordinal).ToArray()), null);
    }
}
