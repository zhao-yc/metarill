using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>只保存多值名称实际依赖的级联候选页面，运行时可重新证明字段和条件。</summary>
    public static IReadOnlySet<string> PublishedCascadeLabelPages(MetadataCatalog catalog)
    {
        var pages=new HashSet<string>(StringComparer.Ordinal);
        foreach(var relation in catalog.ConfigColumns.Where(column=>column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"))
        {
            try
            {
                var definition=Definition(relation);
                var field=Value(definition,"nameField");
                if(!field.EndsWith("_name",StringComparison.Ordinal))continue;
                var pageId=Value(definition,"pageId");
                if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
                var tableId=Value(definition,"tableId");
                if(tableId.Length==0)tableId=relation.LinkConfigTableId;
                if(tableId.Length==0&&pageId.Length>0)tableId=catalog.ConfigPages.SingleOrDefault(page=>page.Id==pageId)?.ConfigTableId??"";
                var tableName=Value(definition,"table");
                if(tableName.Length==0)tableName=Text(relation,"joinTable");
                if(tableName.Length==0&&relation.ColumnName.EndsWith("_ids",StringComparison.Ordinal))tableName=relation.ColumnName[..^4];
                var target=catalog.ConfigTables.SingleOrDefault(table=>tableId.Length>0?table.Id==tableId:table.TableName==tableName);
                if(target is null||target.IsRongyu!=0||GlobalCrossTableTarget.Declared(target))continue;
                var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
                var groups=SharedCrossTableSparseSelection.CompileAll(target,columns);
                foreach(var control in columns.Where(column=>column.ControlKind=="CascadeTree"&&CrossTableStorageColumns.ContainsPathField(column,field)))
                {
                    var group=groups.SingleOrDefault(item=>item.Branches.Any(branch=>branch.SourceColumnId==control.Id));
                    var controls=group is null?[control]:group.Branches.Select(branch=>columns.Single(column=>column.Id==branch.SourceColumnId)).ToArray();
                    foreach(var member in controls)
                        foreach(var level in CascadeLabelScope(catalog,member).Levels)pages.Add(level.PageId);
                }
            }
            catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
            {
                // 无法编译的公开控件由生成门禁报告。这里不发布猜测到的候选范围。
            }
        }
        return pages;
    }

    /// <summary>复用固定跨表读取资格，保留每级的唯一可读页、物理条件及原项目范围。</summary>
    private static NonRedundantCrossTableScope CascadeLabelScope(MetadataCatalog catalog,ColumnMetadata source)
    {
        var levels=Definition(source).GetProperty("levels").EnumerateArray().ToArray();
        var scopes=new List<NonRedundantCrossTableTargetScope>();
        for(var index=0;index<levels.Length;index++)
        {
            var level=levels[index];
            var target=catalog.ConfigTables.Single(table=>table.TableName==Value(level,"table"));
            var name=Value(level,"nameField");
            if(name.Length==0)name="name";
            var parent=index==0?"":Value(level,"parentField");
            var (page,condition)=CascadeLabelPage(catalog,target,name,parent);
            scopes.Add(new(page.Id,condition));
        }
        return new(source.Id,scopes.ToArray());
    }

    /// <summary>读页选择与原级联相同。请求相关条件不能进入跨账号共用的名称缓存。</summary>
    private static (PageMetadata Page,FixedConditionSqlPlan? Condition) CascadeLabelPage(
        MetadataCatalog catalog, TableMetadata target, string name, string parent)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
        var nameColumn=columns.Single(column=>column.ColumnName==name);
        var parentColumn=parent.Length==0?null:columns.Single(column=>column.ColumnName==parent);
        var required=parentColumn is null?new[]{nameColumn.Id}:new[]{nameColumn.Id,parentColumn.Id};
        var allFields=columns.Select(column=>column.ColumnName).ToArray();
        var candidates=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length==0&&
            required.All(id=>catalog.ConfigPageColumns.Any(column=>column.ConfigPageId==page.Id&&column.ConfigColumnId==id&&
                ((column.Extra.ContainsKey("isInGrid")?CrossFlag(column,"isInGrid"):column.IsShow==1) ||
                (column.Extra.ContainsKey("isInEdit")?CrossFlag(column,"isInEdit"):column.IsEdit==1)))))
            .Select(page=>(Page:page,Syntax:FixedConditionPlan.Compile(page.WhereSql,allFields)))
            .Where(item=>!item.Syntax.Variables.Any(variable=>variable.Equals("parentId",StringComparison.OrdinalIgnoreCase)||variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if(candidates.Length!=1)throw new NotSupportedException("级联多值名称要求每级唯一且无父参数的可读页面。");
        var (chosen,syntax)=candidates[0];
        var terms=syntax.Terms.Where(term=>!IsProjectScope(term)).ToArray();
        if(terms.SelectMany(term=>term.Values).Any(value=>value.Kind==FixedConditionValueKind.Variable))
            throw new NotSupportedException("级联多值名称的候选节点不能依赖请求账号范围。");
        if(terms.Length==0)return(chosen,null);
        var physical=columns.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,target,column))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
        var condition=FixedConditionPlan.Compile(string.Join(" AND ",terms.Select(term=>term.ToDefinition())),physical.Keys);
        return(chosen,FixedConditionSqlPlan.Compile(condition,physical));
    }

    /// <summary>项目等值由同一物理范围生成，不在内部候选再绑定请求字段。</summary>
    private static bool IsProjectScope(FixedConditionTerm term)=>term.Field=="yz_project_id"&&
        term.Comparison==FixedConditionComparison.Equal&&term.Values.Count==1&&
        term.Values[0].Kind==FixedConditionValueKind.Variable&&
        term.Values[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);

    private static bool CrossFlag(MetadataRecord column,string key)=>column.Extra.TryGetValue(key,out var flag)&&
        (flag.ValueKind==System.Text.Json.JsonValueKind.True||flag.ToString()=="1");

    private sealed partial class Builder
    {
        private readonly string? cascadeLabelField;
        private readonly HashSet<string> cascadeLabelOutputs=new(StringComparer.OrdinalIgnoreCase);

        /// <summary>多值名称接入完整稀疏路径。坏存储或共享分支冲突不能进入名称数组、筛选或导入。</summary>
        private (string Sql,string Type)? TryCascadeLabel(TableMetadata table,string owner,ColumnMetadata column)
        {
            if(table.Id!=root.Id||owner!=rootAlias||column.LogicalType!="string"||
                column.IsVirtualColumn!=0||storage[table.Id].IsPhysical(column)||
                !column.ColumnName.EndsWith("_name",StringComparison.Ordinal)||ExactDataManageValues.IsSensitiveField(column.ColumnName))
                return null;
            var field=column.ColumnName;
            var control=columns[table.Id].Values.FirstOrDefault(item=>item.ControlKind=="CascadeTree"&&CrossTableStorageColumns.ContainsPathField(item,field));
            if(control is null)return null;
            var group=SharedCrossGroup(table,control.Id);
            (string Sql,string Type) expression;
            if(group is null)
            {
                var binding=CascadeLabelScope(catalog,control);
                var selection=CrossTableSparseSelection.Compile(table,control.Id,columns[table.Id].Values);
                expression=CrossValue(table,owner,control,selection,binding,field);
            }
            else
            {
                foreach(var branch in group.Branches)
                {
                    var member=columns[table.Id].Values.Single(item=>item.Id==branch.SourceColumnId);
                    crossTables[member.Id]=CascadeLabelScope(catalog,member);
                }
                expression=SharedCrossValue(table,owner,group,field);
            }
            var validity=string.Join(" AND ",crossProjections.Values.Select(path=>"("+path.Validity+")"));
            cascadeLabelOutputs.Add(output);
            return("CASE WHEN "+validity+" THEN "+expression.Sql+" END",expression.Type);
        }
    }
}
