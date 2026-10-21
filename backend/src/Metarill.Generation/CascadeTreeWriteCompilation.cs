namespace Metarill.Generation;

internal sealed record CascadeTreeWriteModel(CascadeTreeReadModel Read,ColumnModel Name);
internal sealed record CascadeTreeWriteResult(CascadeTreeWriteModel? Definition,string? Reason);

/// <summary>自关联级联持久化合同；辅助名称只由服务生成，不接受调用者赋值。</summary>
internal static class CascadeTreeWriteCompilation
{
    /// <summary>所有明确的原级联叶子名称从写DTO移除；原页面输入外观仍按保存的元数据生成。</summary>
    internal static GenerationModel Prepare(GenerationModel model)=>model with{Tables=model.Tables.Select(table=>
    {
        var managed=ManagedNames(table).Select(column=>column.Source.Id).ToHashSet(StringComparer.Ordinal);
        ColumnModel Fix(ColumnModel column)=>managed.Contains(column.Source.Id)?column with{IsManagedCascadeName=true}:column;
        IReadOnlyList<ColumnModel> FixAll(IEnumerable<ColumnModel> columns)=>columns.Select(Fix).ToArray();
        return table with{Columns=FixAll(table.Columns),Pages=table.Pages.Select(page=>page with{Readable=FixAll(page.Readable),Writable=FixAll(page.Writable.Where(column=>!managed.Contains(column.Source.Id))),Searchable=FixAll(page.Searchable),Sortable=FixAll(page.Sortable),ListColumns=FixAll(page.ListColumns),DetailColumns=FixAll(page.DetailColumns)}).ToArray()};
    }).ToArray()};

    /// <summary>原协议明确使用同名前缀_name，不把任意关联名称列推断为级联托管列。</summary>
    internal static IReadOnlyList<ColumnModel> ManagedNames(TableModel table)=>table.Columns.Where(source=>source.Source.ControlKind=="CascadeTree"&&source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))
        .Select(source=>table.Columns.SingleOrDefault(column=>column.Source.ColumnName==source.Source.ColumnName[..^3]+"_name"&&column.Type=="string?"&&!column.IsSystem&&!column.IsJsonArray)).OfType<ColumnModel>().DistinctBy(column=>column.Source.Id).ToArray();

    /// <summary>隐藏级联也在复制、默认值或手写钩子触发后复核，不因此扩大响应字段。</summary>
    internal static IReadOnlyList<CascadeTreeWriteModel> ForService(GenerationModel model,TableModel table,PageModel page)=>table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree")
        .Select(column=>Resolve(model,table,page,column).Definition).OfType<CascadeTreeWriteModel>().ToArray();

    /// <summary>缺少明确物理名称列、冲突关联目标及未覆盖范围继续生成拒绝规则。</summary>
    internal static CascadeTreeWriteResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        CascadeTreeWriteResult Gap(string text)=>new(null,"CascadeTree写入尚未支持："+text);
        var read=CascadeTreeReadCompilation.Resolve(model,table,page,source);
        if(read.Definition is not{} plan)return Gap(read.Reason??"未证明的读取范围。");
        if(plan.Target.Source.Id==table.Source.Id&&plan.Parent.Source.Id!=source.Source.Id)return Gap("同表非父字段的合并实体范围仍待接线，不能读取旧实体后放行新值。");
        if(source.Type is not("string?" or "long?") || !source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal) || source.IsSystem || GenerationModel.Enabled(source.Source.Extra,"isJoinColumn"))return Gap("叶子必须是字符串或int64物理_id列。");
        if(source.Source.LinkConfigTableId.Length>0&&source.Source.LinkConfigTableId!=plan.Target.Source.Id)return Gap("叶子声明关联表与级联目标不一致。");
        var name=ManagedNames(table).SingleOrDefault(column=>column.Source.ColumnName==source.Source.ColumnName[..^3]+"_name");
        if(name is null)return Gap("必须明确声明物理字符串名称列，生成器不暗中增加数据库列。");
        return new(new(plan,name),null);
    }
}
