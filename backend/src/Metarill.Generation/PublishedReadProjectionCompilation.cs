namespace Metarill.Generation;

/// <summary>只缩小原单VO的物理读取；不接用途IR、不修改响应或完整写锁实体。</summary>
internal static class PublishedReadProjectionCompilation
{
    /// <summary>覆盖原FromEntity及名称装饰依赖，稳定去重；隐藏条件列仍由数据库筛选。</summary>
    internal static IReadOnlyList<ColumnModel> Columns(GenerationModel model, TableModel table, PageModel page)
    {
        if (NonRedundantReadCompilation.Requires(model, table))
            throw new InvalidOperationException("非冗余表必须沿用固定读取计划。");
        return page.Readable.Concat(table.Columns.Where(column => column.IsId))
            .Concat(MultiNameStorageCompilation.ImplicitNames(model, table, page))
            .Concat(CascadeTreeViewRenderer.ImplicitNames(model, table, page))
            .Concat(CrossTableDisplayRenderer.ForPage(model, table, page).SelectMany(plan => plan.Levels.Select(level => level.Name)))
            .Where(column => !ManagerCredentialCompilation.IsCredential(table, column))
            .DistinctBy(column => column.Source.Id).OrderByDescending(column => column.IsId)
            .ThenBy(column => column.Source.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>账号表保留已有专用脱敏投影；其他普通读取生成固定强类型列集合。</summary>
    internal static string Expression(GenerationModel model, TableModel table, PageModel page) => ManagerCredentialCompilation.IsTable(table)
        ? ManagerCredentialCompilation.ReadProjection(table)
        : "entity => new " + table.Name + " { " + string.Join(", ", Columns(model, table, page)
            .Select(column => column.Property + " = entity." + column.Property)) + " }";
}
