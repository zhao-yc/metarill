using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>固定解析顺序、目标父与全部表单依赖；覆盖匹配键保持原表字段排序。</summary>
internal sealed record ImportDependencyPlan(IReadOnlyList<ColumnModel> Order,
    IReadOnlyDictionary<string,ColumnModel> Parents,IReadOnlySet<string> MatchFields,
    IReadOnlyDictionary<string,IReadOnlyList<ColumnModel>> Dependencies,bool OrderedCrossCells=false)
{
    internal bool Contextual=>Dependencies.Count!=0||OrderedCrossCells;
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
        foreach(var relation in RelationCompilation.ForPage(model,page,forExcel:true).Where(relation=>columns.Contains(relation.SourceColumn)))
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
            fields.AddRange(relation.ImportCondition?.Forms.Select(binding=>binding.Column)??[]);
            if(fields.Count>0)dependencies.Add(relation.SourceColumn.Source.Id,fields.DistinctBy(column=>column.Source.Id).ToArray());
        }
        foreach(var multi in MultiSelectCompilation.ForPage(model,table,page).Where(multi=>columns.Any(column=>column.Source.Id==multi.Source.Source.Id)))
        {
            var fields=new List<ColumnModel>();
            if(multi.TargetParentField is not null)
            {
                var parent=table.Columns.Single(column=>column.JsonName==multi.TargetParentField);
                if(!storage.IsPhysical(parent.Source)||parent.IsJsonArray)throw new InvalidOperationException("多选导入目标父必须为物理外键。");
                parents.Add(multi.Source.Source.Id,parent);
                fields.Add(parent);
            }
            fields.AddRange(multi.ImportCondition?.Forms.Select(binding=>binding.Column)??[]);
            if(fields.Count>0)dependencies.Add(multi.Source.Source.Id,fields.DistinctBy(column=>column.Source.Id).ToArray());
        }
        foreach(var checkbox in DatabaseCheckboxCompilation.ForPage(model,table,page).Where(def=>columns.Any(column=>column.Source.Id==def.Source.Source.Id)))
        {
            var fields=new List<ColumnModel>(checkbox.Conditions?.Forms.Select(binding=>binding.Column)??[]);
            if(table.Source.IsRongyu==0&&checkbox.FormParentField is {} parent)
            {
                // 非冗余名称反查从本行表单父键取值，覆盖导入可回读已锁定记录。
                parents.Add(checkbox.Source.Source.Id,parent);
                fields.Add(parent);
            }
            if(fields.Count>0)dependencies.Add(checkbox.Source.Source.Id,fields.DistinctBy(column=>column.Source.Id).ToArray());
        }
        var plans=CrossTableWriteCompilation.ForService(model,table,page);
        var auxiliaries=CrossTableExcelRenderer.Auxiliaries(table,plans,columns);
        if(dependencies.Count==0&&auxiliaries.Count==0)return new(columns,parents,new HashSet<string>(StringComparer.Ordinal),dependencies);
        var paths=plans.Where(plan=>columns.Any(column=>column.Source.Id==plan.Read.Source.Source.Id)).ToArray();
        var auxiliaryIds=auxiliaries.Select(auxiliary=>auxiliary.Column.Source.Id).ToHashSet(StringComparer.Ordinal);
        // 完整路径及显式上级先提供上下文；同名跨父节点仅按实际同行父级匹配。
        var before=auxiliaries.ToDictionary(auxiliary=>auxiliary.Column.Source.Id,auxiliary=>
            paths.Where(path=>path.Levels.Any(level=>level.Id.Source.Id==auxiliary.Column.Source.Id))
                .Select(path=>path.Read.Source)
                .Concat(auxiliary.Plan.Levels.Take(auxiliary.Index).Select(level=>level.Id)
                    .Where(column=>auxiliaryIds.Contains(column.Source.Id)))
                .DistinctBy(column=>column.Source.Id).ToArray(),StringComparer.Ordinal);
        // 关联候选依赖级联生成的祖先时，先解析实际产生该祖先的路径或辅助列。
        var producers=paths.SelectMany(plan=>plan.Levels.Select(level=>(Field:level.Id.Source.Id,Source:plan.Read.Source)))
            .Concat(auxiliaries.SelectMany(auxiliary=>auxiliary.Plan.Levels.Take(auxiliary.Index+1)
                .Select(level=>(Field:level.Id.Source.Id,Source:auxiliary.Column))))
            .GroupBy(pair=>pair.Field,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.Select(pair=>pair.Source).DistinctBy(column=>column.Source.Id).ToArray(),StringComparer.Ordinal);
        var available=columns.ToDictionary(column=>column.Source.Id,StringComparer.Ordinal);var order=new List<ColumnModel>();var state=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var column in columns)Visit(column);
        var match=new HashSet<string>(StringComparer.Ordinal);if(columns.Count>0)Closure(table.Columns.First(column=>columns.Contains(column)));
        return new(order,parents,match,dependencies,auxiliaries.Count!=0);

        /// <summary>稳定拓扑仅改变解析顺序，非导入依赖仍从默认/原实体取值，不改变Excel列。</summary>
        void Visit(ColumnModel column)
        {
            if(state.GetValueOrDefault(column.Source.Id)==2)return;
            if(state.GetValueOrDefault(column.Source.Id)==1)throw new InvalidOperationException("导入关系的物理字段存在循环依赖。");
            state[column.Source.Id]=1;
            foreach(var prior in before.GetValueOrDefault(column.Source.Id,[]))Visit(prior);
            if(dependencies.TryGetValue(column.Source.Id,out var fields))foreach(var field in fields){if(available.ContainsKey(field.Source.Id))Visit(field);foreach(var producer in producers.GetValueOrDefault(field.Source.Id,[]))if(producer.Source.Id!=column.Source.Id)Visit(producer);}
            state[column.Source.Id]=2;order.Add(column);
        }
        /// <summary>覆盖先只解析首列及全部依赖；其余省略值等锁定原对象后读取。</summary>
        void Closure(ColumnModel column)
        {
            if(!match.Add(column.Source.Id))return;
            foreach(var prior in before.GetValueOrDefault(column.Source.Id,[]))Closure(prior);
            if(dependencies.TryGetValue(column.Source.Id,out var fields))foreach(var field in fields)Closure(field);
            if(!auxiliaryIds.Contains(column.Source.Id))
                foreach(var producer in producers.GetValueOrDefault(column.Source.Id,[]))if(producer.Source.Id!=column.Source.Id)Closure(producer);
        }
    }
}
