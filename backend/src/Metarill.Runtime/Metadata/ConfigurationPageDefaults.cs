using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>从表新建默认页与原手动建页共用相同的表类型默认值。</summary>
internal static class ConfigurationPageDefaults
{
    internal static bool IsFunctionTable(IReadOnlyDictionary<string, object?> table) => Flag(table, "is_function_table");
    internal static bool IsView(IReadOnlyDictionary<string, object?> table) => Flag(table, "is_view");

    /// <summary>视图默认只读；项目条件只引用已登记的物理投影列。</summary>
    internal static void ApplyForTable(Dictionary<string, object?> values, IReadOnlyDictionary<string, object?> table,
        IReadOnlyList<Dictionary<string, object?>>? columns = null)
    {
        var view = IsView(table);
        var function = IsFunctionTable(table);
        var entity = !view && !function;
        values["is_new"] = entity ? 1 : 0;
        values["is_delete"] = entity ? 1 : 0;
        values["is_save"] = view ? 0 : 1;
        values["is_update"] = view ? 0 : 1;
        values["is_search"] = 1;
        values["is_edit"] = 1;
        if (view)
        {
            values["is_copy"] = 0;
            values["is_import"] = 0;
            values["import_method"] = "";
        }

        var scoped = entity || view && columns?.Any(column =>
            SqlRowStore.Text(column, "column_name") == "yz_project_id" && SqlRowStore.Id(column, "is_virtual_column") == 0) == true;
        values["where_sql"] = scoped ? "yz_project_id=@current_yz_project_id" : "";
    }

    private static bool Flag(IReadOnlyDictionary<string, object?> row, string name) =>
        row.GetValueOrDefault(name) is true || SqlRowStore.Text(row, name) == "1";
}
