using System.Text.Json;

namespace Metarill.Generation;

/// <summary>物理派生值只绑定已证明的来源外键和目标列；不能接收客户端路径或任意联接。</summary>
internal sealed record MaterializedDerivedWriteBinding(ColumnModel Storage, ColumnModel ForeignKey, TableModel Target, ColumnModel Value);

/// <summary>首批仅承接原平台三个冗余物理缓存；其余join仍不能从请求、默认值或复制中写入。</summary>
internal static class MaterializedDerivedWriteCompilation
{
    private static readonly (string Source, string Storage, string ForeignKey, string Target, string Value, string Type)[] Contracts =
    [
        ("config_page", "config_table_table_name", "config_table_id", "config_table", "table_name", "string?"),
        ("config_page_column", "config_column_column_name", "config_column_id", "config_column", "column_name", "string?"),
        ("config_page_column", "config_column_sort_no", "config_column_id", "config_column", "sort_no", "int?")
    ];

    /// <summary>使用发布元数据的完整固定关系证明，不按相似字段名推断联接或回落整目标实体。</summary>
    internal static IReadOnlyList<MaterializedDerivedWriteBinding> ForTable(GenerationModel model, TableModel table)
    {
        var result = new List<MaterializedDerivedWriteBinding>();
        if (table.Source.IsRongyu != 1 || table.IsGlobal || !table.Columns.Any(column => column.Source.ColumnName == "yz_project_id")) return result;
        foreach (var contract in Contracts.Where(contract => contract.Source == table.Source.TableName))
        {
            var storage = table.Columns.SingleOrDefault(column => column.Source.ColumnName == contract.Storage);
            var foreign = table.Columns.SingleOrDefault(column => column.Source.ColumnName == contract.ForeignKey);
            var target = model.Tables.SingleOrDefault(candidate => candidate.Source.TableName == contract.Target);
            var value = target?.Columns.SingleOrDefault(column => column.Source.ColumnName == contract.Value);
            if (storage is null || foreign is null || target is null || value is null) continue;
            if (!GenerationModel.Enabled(storage.Source.Extra, "isJoinColumn") || storage.Source.IsVirtualColumn != 0 ||
                !storage.Source.Extra.TryGetValue("columnFullName", out var fullName) || fullName.ValueKind != JsonValueKind.String || fullName.GetString() != contract.Target + "." + contract.Value ||
                foreign.Type != "long?" || foreign.IsJsonArray || GenerationModel.Enabled(foreign.Source.Extra, "isJoinColumn") ||
                target.Source.IsRongyu != 1 || target.IsGlobal || !target.Columns.Any(column => column.Source.ColumnName == "yz_project_id") ||
                value.Source.IsVirtualColumn != 0 || GenerationModel.Enabled(value.Source.Extra, "isJoinColumn") || storage.Type != contract.Type || value.Type != contract.Type || storage.IsJsonArray || value.IsJsonArray) continue;
            // 外键必须有明确的目标声明；ForeignValidation的历史命名回退不能扩大此写合同。
            if (!HasDeclaredTarget(foreign, target) || ForeignValidationRenderer.Target(model, foreign)?.Source.Id != target.Source.Id) continue;
            result.Add(new(storage, foreign, target, value));
        }
        return result;
    }

    /// <summary>只认可明确link/tableId/table/pageId，不因为外键名称刚好相同而填充数据。</summary>
    private static bool HasDeclaredTarget(ColumnModel foreign, TableModel target)
    {
        if (!string.IsNullOrWhiteSpace(foreign.Source.LinkConfigTableId)) return foreign.Source.LinkConfigTableId == target.Source.Id;
        if (foreign.Source.ControlKind is not ("List" or "SelectPage") || string.IsNullOrWhiteSpace(foreign.Source.ControlDetail)) return false;
        using var json = JsonDocument.Parse(foreign.Source.ControlDetail);
        var root = json.RootElement;
        return root.TryGetProperty("tableId", out var id) && id.ToString() == target.Source.Id ||
            root.TryGetProperty("table", out var name) && name.ToString() == target.Source.TableName ||
            root.TryGetProperty("pageId", out var page) && target.Pages.Any(candidate => candidate.Source.Id == page.ToString());
    }
}
