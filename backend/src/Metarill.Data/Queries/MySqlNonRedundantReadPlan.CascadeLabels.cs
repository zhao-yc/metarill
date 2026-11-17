using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed record CascadeCacheOwner(TableMetadata Table,string Alias);

    /// <summary>只保存多值名称实际依赖的级联候选页面，运行时可重新证明字段和条件。</summary>
    public static IReadOnlySet<string> PublishedCascadeLabelPages(MetadataCatalog catalog)
        =>PublishedCascadeLabelPages(catalog,out _);

    /// <summary>账号缓存额外保留来源页的归属条件，运行时用来重新证明物理行的归属列。</summary>
    public static IReadOnlySet<string> PublishedCascadeLabelPages(MetadataCatalog catalog,out IReadOnlySet<string> ownershipPages)
    {
        var pages=new HashSet<string>(StringComparer.Ordinal);
        var owners=new HashSet<string>(StringComparer.Ordinal);
        foreach(var (source,target,field) in DeclaredMultiNameTargets(catalog).Distinct())
            try
            {
                var storage=MetadataStoragePolicy.ForTable(catalog,target);
                var column=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(item=>item.ConfigTableId==target.Id))
                    .SingleOrDefault(item=>item.ColumnName==field);
                if(column is not {LogicalType:"string"}||storage.IsPhysical(column))continue;
                var plan=CompileMultiLabel(catalog,target.Id,field);
                pages.UnionWith(plan.CascadeLabelPages);
                if(plan.RequiresAccount&&source?.IsRongyu==1)
                    owners.UnionWith(catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id).Select(page=>page.Id));
            }
            catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
            {
                // 无效配置由原控件的发布门禁报告，不冻结猜测到的范围。
            }
        ownershipPages=owners;
        return pages;
    }

    /// <summary>发现实际多值控件绑定的目标名称，普通非关联输出仍使用原读取计划。</summary>
    private static IEnumerable<(TableMetadata? Source,TableMetadata Table,string Field)> DeclaredMultiNameTargets(MetadataCatalog catalog)
    {
        foreach(var relation in catalog.ConfigColumns.Where(column=>column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"))
        {
            (TableMetadata? Source,TableMetadata Table,string Field)? result=null;
            try
            {
                var definition=Definition(relation);
                var field=Value(definition,"nameField");
                if(field.Length==0)field="name";
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
                var source=catalog.ConfigTables.SingleOrDefault(table=>table.Id==relation.ConfigTableId);
                result=(source,target,field);
            }
            catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
            {
                // 无效控件不改变无关页面的读取权限。
            }
            if(result is {} candidate)yield return candidate;
        }
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

    /// <summary>读页选择与原级联相同。只接受原账号等值条件与物理常量。</summary>
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
        if(terms.Length==0)return(chosen,null);
        var physical=columns.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,target,column))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
        foreach(var term in terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||term.Field=="yz_project_id"||
                !AccountConditionVariables.IsPageField(term.Values[0].Text,term.Field)||
                !physical.TryGetValue(term.Field,out var type)||type is not("int32" or "int64" or "string"))
                throw new NotSupportedException("级联多值名称只允许原账号字段的物理等值范围。");
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
        private readonly HashSet<string> cascadeLabelPages=new(StringComparer.Ordinal);
        private HashSet<string>? declaredMultiLabelFields;
        private readonly CascadeCacheOwner? cascadeCacheOwner;
        private readonly HashSet<string> cascadeCacheOwnerFields=new(StringComparer.Ordinal);

        /// <summary>读候选绑定请求账号。重算缓存绑定来源记录的物理归属，两条路径复用同一候选条件。</summary>
        private string CascadeLabelCondition(TableMetadata target,PageMetadata page,FixedConditionSqlPlan condition,string alias,string prefix)
        {
            if(cascadeCacheOwner is {} owner&&condition.Variables.Count>0)
            {
                var proof=AccountScopedCrossCache.Compile(catalog,owner.Table,target,page);
                cascadeCacheOwnerFields.UnionWith(proof.Ownership.Select(binding=>binding.Source.ColumnName));
                return AccountCacheScope(proof,target,alias,owner.Alias,render:true,owner.Table,prefix);
            }
            var clause=condition.RenderAccountProjection(new MySqlDatabaseProvider(),prefix,field=>
            {
                Use(target,Column(target,field));
                Touch(alias,field);
                return alias+"."+Q(field);
            });
            foreach(var pair in clause.Parameters)crossConstants.Add(pair.Key,pair.Value);
            return clause.Sql;
        }

        /// <summary>普通选择页只在已绑定的派生名称下读取嵌套级联。精确数据管理保持原范围。</summary>
        private bool AllowsCascadeLabel(TableMetadata table,string alias)
        {
            if(multiLabel&&output==cascadeLabelField)return true;
            if(matching||refreshFields is not null||table.Id==root.Id&&alias==rootAlias)return false;
            declaredMultiLabelFields??=DeclaredMultiNameTargets(catalog).Where(item=>item.Table.Id==root.Id)
                .Select(item=>item.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return declaredMultiLabelFields.Contains(output);
        }

        /// <summary>多值名称接入完整稀疏路径。坏存储或共享分支冲突不能进入名称数组、筛选或导入。</summary>
        private (string Sql,string Type)? TryCascadeLabel(TableMetadata table,string owner,ColumnMetadata column)
        {
            if(table.IsRongyu!=0||column.LogicalType!="string"||
                column.IsVirtualColumn!=0||storage[table.Id].IsPhysical(column)||
                !column.ColumnName.EndsWith("_name",StringComparison.Ordinal)||ExactDataManageValues.IsSensitiveField(column.ColumnName))
                return null;
            var field=column.ColumnName;
            var control=columns[table.Id].Values.FirstOrDefault(item=>item.ControlKind=="CascadeTree"&&
                (CrossTableStorageColumns.ContainsPathField(item,field)||IsSelfTreeLabel(item,field)));
            if(control is null||!AllowsCascadeLabel(table,owner))return null;
            if(IsSelfTreeLabel(control,field))
            {
                var leaf=SelfTreeLabelExpression(table,owner,control,column);
                cascadeLabelOutputs.Add(output);
                return(leaf,"string");
            }
            var group=SharedCrossGroup(table,control.Id);
            (string Sql,string Type) expression;
            string[] controls;
            if(group is null)
            {
                var binding=CascadeLabelScope(catalog,control);
                cascadeLabelPages.UnionWith(binding.Levels.Select(level=>level.PageId));
                var selection=CrossTableSparseSelection.Compile(table,control.Id,columns[table.Id].Values);
                expression=CrossValue(table,owner,control,selection,binding,field,labelPath:true);
                controls=[control.Id];
            }
            else
            {
                controls=group.Branches.Select(branch=>branch.SourceColumnId).ToArray();
                foreach(var controlId in controls)
                {
                    var member=columns[table.Id].Values.Single(item=>item.Id==controlId);
                    var binding=CascadeLabelScope(catalog,member);
                    cascadeLabelPages.UnionWith(binding.Levels.Select(level=>level.PageId));
                    crossTables[member.Id]=binding;
                }
                expression=SharedCrossValue(table,owner,group,field,labelPath:true);
            }
            var validity=string.Join(" AND ",controls.Select(controlId=>"("+crossProjections[owner+":"+controlId].Validity+")"));
            cascadeLabelOutputs.Add(output);
            return("CASE WHEN "+validity+" THEN "+expression.Sql+" END",expression.Type);
        }
    }
}
