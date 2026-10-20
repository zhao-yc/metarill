using System.Text.Json;

namespace Metarill.Generation;

/// <summary>单选写入复用候选固定范围，真实实体父键由标准Service归属绑定后提供。</summary>
internal static class NonRedundantSelectionRenderer
{
    /// <summary>新增/实际提交FK或目标父变化时，以当前读核对候选；省略其它字段不刷新历史关系。</summary>
    internal static string Validate(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        var relation=RelationCompilation.ForPage(model,page,true).Single(relation=>relation.SourceColumn.Source.Id==source.Source.Id);
        var context=ControlRenderer.RelationContext(page,relation,false);
        if(context.UnresolvedTarget)throw new InvalidOperationException("非冗余单选写入缺少可证明的目标父上下文。");
        var parent=page.Scopes.FirstOrDefault(scope=>scope.Source=="parent").Column;
        var target=context.TargetParentField is null?null:table.Columns.Single(column=>column.JsonName==context.TargetParentField);
        if(target is not null&&!NonRedundantReadCompilation.Physical(model,table,target))throw new InvalidOperationException("单选目标父必须绑定物理外键。");
        if(ImportRenderer.Enabled(page)&&target is not null)throw new InvalidOperationException("非冗余单选跨父导入需按整行依赖解析，当前不能借来源parentId替代。");
        var trigger="creating || submitted.Contains("+JsonSerializer.Serialize(source.Source.ColumnName)+")"+(target is null?"":" || submitted.Contains("+JsonSerializer.Serialize(target.Source.ColumnName)+")");
        var suffix=page.Source.Id+"_"+source.Source.Id;
        return $$"""
        if (({{trigger}}) && entity.{{source.Property}} is not null)
        {
            // 当前对象操作已经授权；普通单选仍按原import/create/update动作，不额外加目标列表权限。
            var selected{{source.Source.Id}} = await OptionsScope{{suffix}}Async({{Value(parent)}}, {{Value(target)}}, actor, cancellationToken, true, false);
            var valid{{source.Source.Id}} = await selected{{source.Source.Id}}.Where(row => row.Id == entity.{{source.Property}}.Value).Limit(1).ForUpdate().ToListAsync(row => new { row.Id }, cancellationToken);
            if (valid{{source.Source.Id}}.Count != 1) throw new InvalidOperationException("单选记录不在当前真实候选范围。");
        }

""";
    }
    /// <summary>父上下文只从已绑定实体的物理字段读取，格式化保持字符串ID。</summary>
    private static string Value(ColumnModel? column)=>column is null?"null":column.Type=="string?"?"entity."+column.Property:"entity."+column.Property+(column.Type=="long"?".ToString(CultureInfo.InvariantCulture)":"?.ToString(CultureInfo.InvariantCulture)");
}
