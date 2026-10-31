using Metarill.Core.Metadata;

namespace Metarill.Runtime.Metadata;

/// <summary>自动系统字段与批量建字段共用必填布局默认值，避免完整详情回写被自身空值拒绝。</summary>
internal static class ConfigurationColumnDefaults
{
    /// <summary>只补服务端尚未赋值的键，明确的字段类型、归属和业务默认值保持原值。</summary>
    public static void Fill(Dictionary<string, object?> values, MetadataCatalog platform)
    {
        values.TryAdd("col_span", 1);
        values.TryAdd("mobile_list_page_col_span", 1);
        values.TryAdd("default_value", "");
        var definition = platform.ConfigTables.Single(table => table.TableName == "config_column");
        foreach (var column in platform.ConfigColumns.Where(column => column.ConfigTableId == definition.Id && column.IsDbNotNull == 1 && !values.ContainsKey(column.ColumnName)))
            values[column.ColumnName] = column.LogicalType switch { "int32" or "int64" or "decimal" => 0, "bool" => false, _ => "" };
    }
}
