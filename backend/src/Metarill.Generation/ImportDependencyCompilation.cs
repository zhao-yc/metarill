using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>固定解析顺序、每个关系的目标父和覆盖键依赖，不向客户端暴露元数据解释入口。</summary>
internal sealed record ImportDependencyPlan(
    IReadOnlyList<ColumnModel> Order,
    IReadOnlyDictionary<string, ColumnModel> Parents,
    IReadOnlySet<string> MatchFields)
{
    internal bool Contextual => Parents.Count != 0;
}

/// <summary>导入只在生成时编译整行依赖，运行时按固定顺序解析，不信任JSON属性排列。</summary>
internal static class ImportDependencyCompilation
{
    /// <summary>物理目标父先于关联名称；循环拒绝，覆盖匹配键继续按原表字段排序。</summary>
    internal static ImportDependencyPlan ForPage(GenerationModel model, TableModel table, PageModel page)
    {
        var columns = ImportRenderer.Columns(page);
        var parents = new Dictionary<string, ColumnModel>(StringComparer.Ordinal);
        var storage = MetadataStoragePolicy.ForTable(model.Catalog, table.Source);
        foreach (var relation in RelationCompilation.ForPage(model, page).Where(relation => columns.Contains(relation.SourceColumn)))
        {
            var context = ControlRenderer.RelationContext(page, relation, false);
            if (context.UnresolvedTarget) throw new InvalidOperationException("导入关系缺少可证明的目标父范围。");
            if (context.TargetParentField is null) continue;
            var parent = table.Columns.Single(column => column.JsonName == context.TargetParentField);
            if (!storage.IsPhysical(parent.Source) || parent.IsJsonArray || parent.Type is not ("long?" or "long" or "int?" or "string?"))
                throw new InvalidOperationException("导入目标父必须来自本对象的物理ID字段。");
            parents.Add(relation.SourceColumn.Source.Id, parent);
        }
        if (parents.Count == 0) return new(columns, parents, new HashSet<string>(StringComparer.Ordinal));
        var available = columns.ToDictionary(column => column.Source.Id, StringComparer.Ordinal);
        var order = new List<ColumnModel>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var column in columns) Visit(column);
        var match = new HashSet<string>(StringComparer.Ordinal);
        if (columns.Count > 0) Closure(table.Columns.First(column => columns.Contains(column)));
        return new(order, parents, match);

        /// <summary>稳定拓扑序只改变解析先后，不改变Excel列和原覆盖首列；只读父由可信上下文提供。</summary>
        void Visit(ColumnModel column)
        {
            if (state.GetValueOrDefault(column.Source.Id) == 2) return;
            if (state.GetValueOrDefault(column.Source.Id) == 1) throw new InvalidOperationException("导入关系的目标父字段存在循环依赖。");
            state[column.Source.Id] = 1;
            if (parents.TryGetValue(column.Source.Id, out var parent) && available.ContainsKey(parent.Source.Id)) Visit(parent);
            state[column.Source.Id] = 2;
            order.Add(column);
        }
        /// <summary>前置匹配只解析匹配键及其依赖，其他省略值留待锁定原对象后读取。</summary>
        void Closure(ColumnModel column)
        {
            if (!match.Add(column.Source.Id)) return;
            if (parents.TryGetValue(column.Source.Id, out var parent)) Closure(parent);
        }
    }
}
