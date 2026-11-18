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
        foreach(var (source,target,field,relation) in DeclaredMultiNameTargets(catalog).Distinct())
            try
            {
                var storage=MetadataStoragePolicy.ForTable(catalog,target);
                var column=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(item=>item.ConfigTableId==target.Id))
                    .SingleOrDefault(item=>item.ColumnName==field);
                if(column is null)continue;
                var selected=MultiTargetLogicalPage(catalog,target,relation);
                if(selected is null&&(column.LogicalType!="string"||storage.IsPhysical(column)))continue;
                var plan=selected is null?CompileMultiLabel(catalog,target.Id,field):
                    CompileScopedMultiLabel(catalog,target.Id,selected.Id,field,[field],[]);
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

    /// <summary>固定记录范围只编译条件实际依赖的跨表组，保留整组祖先证明和真实目标页条件。</summary>
    internal static MySqlNonRedundantReadPlan CompileCrossPageMembership(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        IReadOnlyList<string> conditionFields,IReadOnlySet<string> crossFields)
    {
        if(!catalog.ConfigPages.Contains(page)||page.ConfigTableId!=table.Id||
            table.IsRongyu!=0||crossFields.Count==0||
            !crossFields.IsSubsetOf(conditionFields))throw new NotSupportedException("固定跨表页面范围必须属于普通非冗余来源。");
        return CompileWithCrossTables(catalog,table.Id,["id"],CrossMembershipBindings(catalog,table,page,crossFields),conditionFields);
    }

    /// <summary>向显式绑定的多值目标页取得逻辑条件。原物理候选和未指定页面的复选维持原范围。</summary>
    internal static PageMetadata? MultiTargetLogicalPage(MetadataCatalog catalog,TableMetadata table,ColumnMetadata relation)
    {
        if(table.IsRongyu!=0||GlobalCrossTableTarget.Declared(table))return null;
        var members=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        if(!members.Any(column=>column.ControlKind=="CascadeTree"))return null;
        var pageId=MultiPageId(Definition(relation));
        if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
        var page=catalog.ConfigPages.SingleOrDefault(item=>item.ConfigTableId==table.Id&&item.Id==pageId);
        if(page is null||page.WhereSql.Length==0)return null;
        var syntax=FixedConditionPlan.Compile(page.WhereSql,RelationStorageColumns.Expand(table,members).Select(column=>column.ColumnName));
        return syntax.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,members,field))?page:null;
    }

    /// <summary>原发布元数据的页面 ID 可为精确整数或字符串。</summary>
    private static string MultiPageId(System.Text.Json.JsonElement definition)=>
        definition.TryGetProperty("pageId",out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.Number&&value.TryGetInt64(out var id)
            ?id.ToString(System.Globalization.CultureInfo.InvariantCulture):Value(definition,"pageId");

    /// <summary>发布已绑定目标页及逻辑名称，原候选、导入、回读共用固定条件。</summary>
    internal static MySqlNonRedundantReadPlan CompileScopedMultiLabel(MetadataCatalog catalog,string tableId,string pageId,
        string field,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("多值目标页逻辑条件要求已发布的MySQL读取计划。");
        var page=catalog.ConfigPages.Single(item=>item.ConfigTableId==tableId&&item.Id==pageId);
        if(page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
            throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
        return new Builder(catalog,tableId,matching:true,multiLabel:true,cascadeLabelField:field)
        {
            SelectedMultiPage=page
        }.Compile(outputs,dependencies);
    }

    /// <summary>范围只冻结实际依赖的控件和共享同伴，页面覆盖必须保留相同路径定义。</summary>
    private static IReadOnlyList<NonRedundantCrossTableScope> CrossMembershipBindings(MetadataCatalog catalog,TableMetadata table,
        PageMetadata page,IReadOnlySet<string> crossFields)
    {
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var selected=columns.Where(column=>column.ControlKind=="CascadeTree"&&
            crossFields.Any(field=>CrossTableStorageColumns.ContainsPathField(column,field))).Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var group in SharedCrossTableSparseSelection.CompileAll(table,columns))
            if(group.Branches.Any(branch=>selected.Contains(branch.SourceColumnId)))
                selected.UnionWith(group.Branches.Select(branch=>branch.SourceColumnId));
        if(selected.Count==0||crossFields.Any(field=>!CrossTableStorageColumns.IsLogicalPathField(table,columns,field)))
            throw new NotSupportedException("固定页面范围找不到已声明的跨表层位。");
        return columns.Where(column=>selected.Contains(column.Id)).Select(column=>
        {
            var overlay=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==column.Id)
                ?.Extra.GetValueOrDefault("controlDetail").ToString();
            if(!CrossTableStorageColumns.KeepsDefinition(column.ControlDetail,overlay))
                throw new NotSupportedException("固定页面范围不能忽略级联页覆盖引起的路径变化。");
            return CascadeLabelScope(catalog,column);
        }).ToArray();
    }

    /// <summary>发现实际多值控件绑定的目标名称，普通非关联输出仍使用原读取计划。</summary>
    private static IEnumerable<(TableMetadata? Source,TableMetadata Table,string Field,ColumnMetadata Relation)> DeclaredMultiNameTargets(MetadataCatalog catalog)
    {
        foreach(var relation in catalog.ConfigColumns.Where(column=>column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"))
        {
            (TableMetadata? Source,TableMetadata Table,string Field,ColumnMetadata Relation)? result=null;
            try
            {
                var definition=Definition(relation);
                var field=Value(definition,"nameField");
                if(field.Length==0)field="name";
                var pageId=MultiPageId(definition);
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
                result=(source,target,field,relation);
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
    internal static (PageMetadata Page,FixedConditionSqlPlan? Condition) CascadeLabelPage(
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
        return(chosen,CascadeScopeCondition(target,storage,columns,syntax));
    }

    /// <summary>固定页条件保留实际类型与原账号等值，只有已声明逻辑路径可以引用非物理层位。</summary>
    private static FixedConditionSqlPlan? CascadeScopeCondition(TableMetadata target,MetadataStoragePlan storage,
        IReadOnlyList<ColumnMetadata> columns,FixedConditionPlan syntax)
    {
        var terms=syntax.Terms.Where(term=>!IsProjectScope(term)).ToArray();
        if(terms.Length==0)return null;
        var physical=columns.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,target,column))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
        foreach(var term in terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||term.Field=="yz_project_id"||
                CrossTableStorageColumns.IsLogicalPathField(target,columns,term.Field)||!AccountConditionVariables.IsPageField(term.Values[0].Text,term.Field)||
                !physical.TryGetValue(term.Field,out var type)||type is not("int32" or "int64" or "string"))
                throw new NotSupportedException("级联多值名称只允许原账号字段的物理等值范围。");
        var types=new Dictionary<string,string>(physical,StringComparer.OrdinalIgnoreCase);
        foreach(var column in columns.Where(column=>CrossTableStorageColumns.IsLogicalPathField(target,columns,column.ColumnName)))
            types.TryAdd(column.ColumnName,column.LogicalType);
        var condition=FixedConditionPlan.Compile(string.Join(" AND ",terms.Select(term=>term.ToDefinition())),types.Keys);
        return FixedConditionSqlPlan.Compile(condition,types);
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
        private string CascadeLabelCondition(TableMetadata target,PageMetadata page,FixedConditionSqlPlan condition,string alias,string prefix,CascadeCacheOwner? cacheOwner=null)
        {
            if((cacheOwner??cascadeCacheOwner) is {} owner&&condition.Variables.Count>0)
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
