using System.Collections.ObjectModel;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Core.Security;

namespace Metarill.Generation;

internal sealed record FixedPageCondition(string Definition,FixedConditionPlan Syntax,IReadOnlyDictionary<string,string> Types)
{
    internal IReadOnlySet<string> CrossFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    internal IReadOnlySet<string> SelfTreeFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>将原页面AND条件拆成项目／父范围与类型化条件；账号变量始终从已认证身份绑定。</summary>
internal static class FixedPageConditionCompilation
{
    /// <summary>先校验每项类型，跨表层位还须通过生成器的固定逻辑路径证明。</summary>
    internal static (IReadOnlyList<(ColumnModel Column,string Source)> Scopes,FixedPageCondition? Condition) Compile(MetadataCatalog catalog,TableMetadata table,IReadOnlyList<ColumnModel> columns,string definition)
    {
        try
        {
            var syntax=FixedConditionPlan.Compile(definition,columns.Select(column=>column.Source.ColumnName));
            var scopes=new List<(ColumnModel Column,string Source)>();var conditions=new List<FixedConditionTerm>();
            foreach(var term in syntax.Terms)
            {
                var variables=term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable).ToArray();
                if(variables.Length==0){conditions.Add(term);continue;}
                if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1)throw Rejected();
                var variable=variables[0].Text;
                if(AccountConditionVariables.IsSupported(variable))
                {
                    var accountColumn=columns.Single(column=>column.Source.ColumnName==term.Field);
                    if(!AccountConditionVariables.IsPageField(variable,term.Field)||!IsAccountColumn(accountColumn))throw Rejected();
                    conditions.Add(term);
                    continue;
                }
                var source=variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"project":variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)||variable.Equals("parentId",StringComparison.OrdinalIgnoreCase)?"parent":throw Rejected();
                var column=columns.Single(column=>column.Source.ColumnName==term.Field);
                if((column.Source.ColumnName=="yz_project_id")!=(source=="project"))throw Rejected();
                scopes.Add((column,source));
            }
            if(conditions.Count==0)return(scopes.AsReadOnly(),null);
            var physical=MetadataStoragePolicy.ForTable(catalog,table);var identity=new TableModel(table,GenerationModel.Pascal(table.TableName),columns,[]);
            var fields=conditions.Select(term=>term.Field).Distinct(StringComparer.Ordinal).ToArray();
            var crossFields=fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,field)).ToHashSet(StringComparer.Ordinal);
            var selfTreeFields=fields.Where(field=>CascadeTreeStorageColumns.IsLogicalNameField(table,columns.Select(column=>column.Source),field)).ToHashSet(StringComparer.Ordinal);
            foreach(var field in fields)
            {
                var column=columns.Single(column=>column.Source.ColumnName==field);
                if((!physical.IsPhysical(column.Source)&&!crossFields.Contains(field)&&!selfTreeFields.Contains(field))||column.IsJsonArray||column.Source.LogicalType.Equals("json",StringComparison.OrdinalIgnoreCase)||ManagerCredentialCompilation.IsCredential(identity,column))throw Rejected();
            }
            var conditionDefinition=string.Join(" AND ",conditions.Select(Term));
            var plan=FixedConditionPlan.Compile(conditionDefinition,fields);
            var types=fields.ToDictionary(field=>field,field=>columns.Single(column=>column.Source.ColumnName==field).Source.LogicalType,StringComparer.Ordinal);
            _=FixedConditionSqlPlan.Compile(plan,types);
            return(scopes.AsReadOnly(),new(conditionDefinition,plan,new ReadOnlyDictionary<string,string>(types)){CrossFields=crossFields,SelfTreeFields=selfTreeFields});
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException)
        {throw new InvalidOperationException("页面WhereSql缺少已证明的固定标量条件。",error);}
    }
    /// <summary>归属字段必须为真实业务标量；系统项目列、join缓存及凭据不能承担账号范围。</summary>
    internal static bool IsAccountColumn(ColumnModel column) =>
        column.Source.ColumnName!="yz_project_id"&&column.Type is ("string?" or "long" or "long?" or "int?")&&
        !column.IsJsonArray&&column.Source.IsVirtualColumn==0&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")&&
        !ExactDataManageValues.IsSensitiveField(column.Source.ColumnName)&&
        column.Source.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList");

    /// <summary>多选辅助名称在模型准备后才标记为数组，再次核对，不能借普通字符串初始形状接入JSON范围。</summary>
    internal static GenerationModel ValidateFinal(GenerationModel model)
    {
        foreach(var table in model.Tables)foreach(var page in table.Pages)
            if(page.Condition is{} condition)
            {
                if(condition.Syntax.Fields.Any(field=>table.Columns.Single(column=>column.Source.ColumnName==field).IsJsonArray))throw Rejected();
                if(condition.CrossFields.Count>0)
                {
                    var published=CrossTableWriteCompilation.ForService(model,table,page).SelectMany(plan=>plan.Levels)
                        .SelectMany(level=>new[]{level.Id.Source.ColumnName,level.Name.Source.ColumnName})
                        .ToHashSet(StringComparer.Ordinal);
                    if(!condition.CrossFields.IsSubsetOf(published))throw Rejected();
                }
                if(condition.SelfTreeFields.Count>0)
                {
                    var published=NonRedundantReadCompilation.SelfTreeNameQueries(model,table,page)
                        .Select(tree=>CascadeTreeViewRenderer.Name(table,tree.Source)!.Source.ColumnName);
                    if(!condition.SelfTreeFields.IsSubsetOf(published))throw Rejected();
                }
                foreach(var term in condition.Syntax.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
                {
                    var column=table.Columns.Single(item=>item.Source.ColumnName==term.Field);
                    if(!IsAccountColumn(column)||condition.CrossFields.Contains(term.Field)||condition.SelfTreeFields.Contains(term.Field)||column.IsManagedCascadeName||NonRedundantCrossParentCompilation.IsSlot(model,table,column))throw Rejected();
                }
            }
        return model;
    }
    /// <summary>按已经解析的节点规范化，字符串中的AND、引号和逗号保持原值，绝不按文本分割SQL。</summary>
    internal static string Term(FixedConditionTerm term)=>term.ToDefinition();
    /// <summary>固定条件只在类型装载时编译，普通请求绑定参数而不解释元数据。</summary>
    internal static string Declaration(PageModel page,string name)
    {
        if(page.Condition is not{} condition)return "";
        return "    /// <summary>发布时固定的页面条件，账号值在真实请求中绑定。</summary>\n"+
            $"    private static readonly Metarill.Data.Queries.FixedConditionSqlPlan {name}={ConditionExpression(condition)};\n";
    }
    /// <summary>固定目标父条件与普通页面条件共用同一生成字面量。</summary>
    internal static string ConditionExpression(FixedPageCondition? condition)=>condition is null?"null":
        $"Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile({JsonSerializer.Serialize(condition.Definition)},new string[]{{{string.Join(",",condition.Syntax.Fields.Select(field=>JsonSerializer.Serialize(field)))}}}),new Dictionary<string,string>{{{string.Join(",",condition.Types.Select(pair=>"["+JsonSerializer.Serialize(pair.Key)+"]="+JsonSerializer.Serialize(pair.Value)))}}})";
    /// <summary>实体读取和当前锁使用同一固定AND计划，逻辑字段只通过已证明的路径比较。</summary>
    internal static string Apply(PageModel page,string name,string prefix,string? logicalPlan=null,string locking="false")
    {
        if(page.Condition is null)return "";
        if(!HasLogicalFields(page))return $"        query={Bound(page,name)}.ApplyConstants(query,session,{JsonSerializer.Serialize(prefix)});\n";
        if(logicalPlan is null)throw new InvalidOperationException("该入口缺少固定页面条件的逻辑读取计划。");
        return $"        query=NonRedundantCrossTableScopeSql.ApplyCondition(query,session,{logicalPlan},{Bound(page,name)},{JsonSerializer.Serialize(prefix)},actor.ProjectId,{locking});\n";
    }
    internal static bool HasCrossFields(PageModel page)=>page.Condition?.CrossFields.Count>0;
    internal static bool HasSelfTreeFields(PageModel page)=>page.Condition?.SelfTreeFields.Count>0;
    internal static bool HasLogicalFields(PageModel page)=>HasCrossFields(page)||HasSelfTreeFields(page);
    /// <summary>原多选直接引用物理全局字典，账号条件沿实际发布页绑定。</summary>
    internal static bool HasStoredGlobalMultiScope(TableModel table,PageModel page)=>
        page.Scopes.All(scope=>scope.Source=="project")&&page.Condition is {} condition&&!HasLogicalFields(page)&&
        GlobalMultipleRelationTarget.IsStoredAccountPage(table.Source,page.Source,condition.Syntax);

    /// <summary>多值限定页包含账号、逻辑条件或由实际发布关系证明的全局物理父键。</summary>
    internal static bool HasMultiTargetScope(GenerationModel model,TableModel table,PageModel page)=>HasLogicalFields(page)||
        HasStoredGlobalMultiScope(table,page)||
        table.Source.IsRongyu==0&&(HasAccountVariables(page)||SelectionPageRenderer.Enabled(page)&&
            page.Scopes.Count(scope=>scope.Source=="parent")>1)||page.Scopes.Any(scope=>
        scope.Source=="parent"&&CrossTableStorageColumns.IsLogicalPathField(table.Source,table.Columns.Select(column=>column.Source),scope.Column.Source.ColumnName))||
        table.Source.IsRongyu==0&&page.Scopes.Any(scope=>scope.Source=="parent"&&
            model.Tables.Any(parent=>parent.IsGlobal&&parent.Source.Id==scope.Column.Source.LinkConfigTableId))&&
        NonRedundantReads.IsScopedMultiTarget(model.Catalog,table.Source,page.Source.Id);
    /// <summary>非冗余SQL按已编译表达式绑定相同条件；写后回执明确不传页面筛选。</summary>
    internal static string ReadArgument(PageModel page,bool named=false)=>page.Condition is null?"":", "+(named?"condition: ":"")+Bound(page,"PageCondition"+page.Source.Id);
    /// <summary>只有声明账号变量的页面生成请求绑定，普通静态计划维持原调用。</summary>
    internal static string Bound(PageModel page,string name)=>page.Condition is null?"null":HasAccountVariables(page)?name+".BindAccount(actor)":name;
    internal static bool HasAccountVariables(PageModel page)=>page.Condition?.Syntax.Variables.Count>0;
    /// <summary>统一拒绝未接线定义，不回退为忽略条件的宽查询。</summary>
    private static InvalidOperationException Rejected()=>new("页面条件包含未接线字段、变量或操作。");
}
