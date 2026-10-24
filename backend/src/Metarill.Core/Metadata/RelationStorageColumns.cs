namespace Metarill.Core.Metadata;

/// <summary>结构、存储与生成共用辅助列扩展入口，避免同一关系在不同层产生不同物理定义。</summary>
public static class RelationStorageColumns
{
    /// <summary>级联包含跨表派生，再补普通冗余多值缓存；所有扩展只返回新集合。</summary>
    public static IReadOnlyList<ColumnMetadata> Expand(TableMetadata table,IEnumerable<ColumnMetadata> declared)=>MultiNameStorageColumns.Expand(table,CascadeTreeStorageColumns.Expand(table,declared));
}
