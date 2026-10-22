namespace Metarill.Generation;

/// <summary>已验完整包的非冗余自树子集；同表、父参数或其它关系混用继续独立验收。</summary>
internal static class NonRedundantSelfTreeSubset
{
    /// <summary>复用正式读写计划，不把驱动可用或原控件存在当成完整包支持。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(table.Source.IsRongyu!=0||table.IsGlobal||page.Scopes.Any(scope=>scope.Source=="parent")||ExactDataManageCompilation.Declared(page))return false;
        if(table.Columns.Count(column=>column.Source.ControlKind=="CascadeTree")!=1||table.Columns.Any(column=>column.Source.Id!=source.Source.Id&&Metarill.Core.Metadata.MetadataStoragePolicy.IsRelationColumn(column.Source)))return false;
        var plan=CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition;
        return plan is not null&&plan.Read.Target.Source.IsRongyu==1&&!plan.Read.Target.IsGlobal&&plan.Read.Target.Source.Id!=table.Source.Id;
    }
}
