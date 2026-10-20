namespace Metarill.Generation;

/// <summary>只针对平台原字段配置页的旧空物理类型，不扩展其他项目、页面或字段的必填行为。</summary>
internal static class PlatformColumnTypeCompatibilityCompilation
{
    /// <summary>稳定身份和实际字段名、类型必须同时匹配；模型缺少任一证明时仍使用原必填校验。</summary>
    private static bool Enabled(GenerationModel model,TableModel table,PageModel page)
        => model.Catalog.YzProjectId=="1608097412978577409" && table.Source.Id=="6217714881724416" && table.Source.TableName=="config_column"
            && page.Source.Id=="6217715532627968" && page.Source.ConfigTableId==table.Source.Id
            && table.Columns.Any(column=>column.Source.Id=="1670726649369513985" && column.Source.ColumnName=="column_type" && column.Type=="string?")
            && table.Columns.Any(column=>column.Source.Id=="2200000000000000129" && column.Source.ColumnName=="logical_type" && column.Type=="string?");

    /// <summary>在已锁原实体上捕获类型旧值，局部赋值和手写钩子不能影响这份比较基线。</summary>
    internal static string Capture(GenerationModel model,TableModel table,PageModel page)
        => !Enabled(model,table,page) ? "" : "        // 固定平台旧空物理类型仅兼容未编辑类型的局部更新。\n        var originalColumnPhysicalType = entity.ColumnType;\n        var originalColumnLogicalType = entity.LogicalType;\n";

    /// <summary>只给Update中的精确物理类型字段生成豁免条件；创建、复制及其他必填字段仍原样执行。</summary>
    internal static string Exception(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
        => !Enabled(model,table,page) || column.Source.Id!="1670726649369513985" || column.Source.ColumnName!="column_type" ? ""
            : "Metarill.Core.Metadata.MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical(originalColumnPhysicalType, originalColumnLogicalType, submitted.Contains(\"column_type\"), submitted.Contains(\"logical_type\"), entity.ColumnType, entity.LogicalType)";
}
