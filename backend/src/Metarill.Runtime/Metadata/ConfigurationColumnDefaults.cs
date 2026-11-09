using Metarill.Core.Metadata;

namespace Metarill.Runtime.Metadata;

/// <summary>为字段配置补充原表单要求的默认值。</summary>
internal static class ConfigurationColumnDefaults
{
    /// <summary>统一补齐缺省布局，保留种子和自动生成流程显式给出的值。</summary>
    public static void FillLayout(Dictionary<string, object?> values)
    {
        values.TryAdd("col_span", 1);
        values.TryAdd("mobile_list_page_col_span", 1);
    }

    /// <summary>只补服务端尚未赋值的键，明确的字段类型、归属和业务默认值保持原值。</summary>
    public static void Fill(Dictionary<string, object?> values, MetadataCatalog platform)
    {
        FillLayout(values);
        values.TryAdd("default_value", "");
        var definition = platform.ConfigTables.Single(table => table.TableName == "config_column");
        foreach (var column in platform.ConfigColumns.Where(column => column.ConfigTableId == definition.Id && column.IsDbNotNull == 1 && !values.ContainsKey(column.ColumnName)))
            values[column.ColumnName] = column.LogicalType switch { "int32" or "int64" or "decimal" => 0, "bool" => false, _ => "" };
    }
}
