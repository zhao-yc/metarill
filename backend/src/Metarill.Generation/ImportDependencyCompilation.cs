using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>固定解析顺序、目标父与全部表单依赖；覆盖匹配键保持原表字段排序。</summary>
internal sealed record ImportDependencyPlan(IReadOnlyList<ColumnModel> Order,
    IReadOnlyDictionary<string,ColumnModel> Parents,IReadOnlySet<string> MatchFields,
    IReadOnlyDictionary<string,IReadOnlyList<ColumnModel>> Dependencies)
{
    internal bool Contextual=>Dependencies.Count!=0;
    internal IEnumerable<ColumnModel> ContextValues=>Dependencies.Values.SelectMany(fields=>fields).DistinctBy(column=>column.Source.Id);
}

/// <summary>导入只在生成时编译整行依赖，运行时按固定顺序解析，不信任JSON属性排列。</summary>
internal static class ImportDependencyCompilation
{
    /// <summary>物理父和联动值先于关联名称；循环拒绝，默认及锁定原记录补充省略字段。</summary>
    internal static ImportDependencyPlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        var columns=ImportRenderer.Columns(page);var parents=new Dictionary<string,ColumnModel>(StringComparer.Ordinal);
        var dependencies=new Dictionary<string,IReadOnlyList<ColumnModel>>(StringComparer.Ordinal);
        var storage=MetadataStoragePolicy.ForTable(model.Catalog,table.Source);
        foreach(var relation in RelationCompilation.ForPage(model,page).Where(relation=>columns.Contains(relation.SourceColumn)))
        {
            var context=ControlRenderer.RelationContext(page,relation,false);
            if(context.UnresolvedTarget)
            {
                // 未证明的固定绑定继续生成原拒绝入口，不能改成宽候选。
                if(relation.ParentBinding?.Declared==true)continue;
                throw new InvalidOperationException("导入关系缺少可证明的目标父范围。");
            }
            var fields=new List<ColumnModel>();
            if(context.TargetParentField is not null)
            {
                var parent=table.Columns.Single(column=>column.JsonName==context.TargetParentField);
                if(!storage.IsPhysical(parent.Source)||parent.IsJsonArray||parent.Type is not("long?" or "long" or "int?" or "string?"))throw new InvalidOperationException("导入目标父必须来自本对象的物理ID字段。");
                parents.Add(relation.SourceColumn.Source.Id,parent);fields.Add(parent);
            }
            fields.AddRange(relation.Conditions?.Forms.Select(binding=>binding.Column)??[]);
            if(fields.Count>0)dependencies.Add(relation.SourceColumn.Source.Id,fields.DistinctBy(column=>column.Source.Id).ToArray());
        }
        if(dependencies.Count==0)return new(columns,parents,new HashSet<string>(StringComparer.Ordinal),dependencies);
        var available=columns.ToDictionary(column=>column.Source.Id,StringComparer.Ordinal);var order=new List<ColumnModel>();var state=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var column in columns)Visit(column);
        var match=new HashSet<string>(StringComparer.Ordinal);if(columns.Count>0)Closure(table.Columns.First(column=>columns.Contains(column)));
        return new(order,parents,match,dependencies);

        /// <summary>稳定拓扑仅改变解析顺序，非导入依赖仍从默认/原实体取值，不改变Excel列。</summary>
        void Visit(ColumnModel column)
        {
            if(state.GetValueOrDefault(column.Source.Id)==2)return;
            if(state.GetValueOrDefault(column.Source.Id)==1)throw new InvalidOperationException("导入关系的物理字段存在循环依赖。");
            state[column.Source.Id]=1;
            if(dependencies.TryGetValue(column.Source.Id,out var fields))foreach(var field in fields)if(available.ContainsKey(field.Source.Id))Visit(field);
            state[column.Source.Id]=2;order.Add(column);
        }
        /// <summary>覆盖先只解析首列及全部依赖；其余省略值等锁定原对象后读取。</summary>
        void Closure(ColumnModel column)
        {
            if(!match.Add(column.Source.Id))return;
            if(dependencies.TryGetValue(column.Source.Id,out var fields))foreach(var field in fields)Closure(field);
        }
    }
}
