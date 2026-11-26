using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>规则记录的根实体读取物理存储，派生值与筛选继续复用同一已冻结逻辑计划。</summary>
    public static MySqlNonRedundantReadPlan CompileRuleRecord(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false,IReadOnlyList<string>? logicalOutputs=null) =>
        CompileRuleRecord(catalog,tableId,outputs,storageOutputs,crossTables,dependencies,deferCascadeNames,logicalOutputs,null);

    /// <summary>规则逻辑读复用本页单值及多值目标范围，实体仍读取真实根存储。</summary>
    internal static MySqlNonRedundantReadPlan CompileRuleRecord(MetadataCatalog catalog,string tableId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,
        IReadOnlyList<string>? dependencies,bool deferCascadeNames,IReadOnlyList<string>? logicalOutputs,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets,
        IReadOnlyList<NonRedundantMultiTargetScope>? multiTargets=null)
    {
        ArgumentNullException.ThrowIfNull(storageOutputs);
        var plan=CompileRelationScope(catalog,tableId,outputs,crossTables,dependencies,deferCascadeNames,
            singleTargets:singleTargets,multiTargets:multiTargets);
        // 与普通编译使用同一根别名和字段来源；适配器内生成标识符，不向Service泄漏方言。
        return new Builder(catalog,tableId,deferCascadeNames:deferCascadeNames,crossTables:crossTables).RecordStorage(plan,storageOutputs,logicalOutputs??[]);
    }
    /// <summary>先证明精确联接为非凭据标量，再冻结同页目标范围和附件所需的真实存储。</summary>
    internal static MySqlNonRedundantReadPlan CompilePublishedJoinRecord(MetadataCatalog catalog,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,
        IReadOnlyList<string> dependencies)
    {
        MetadataValidation.Validate(catalog);
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        var page=catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        var field=Metarill.Core.Security.ExactDataManageValues.Field(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString());
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var column=catalog.ConfigColumns.Single(item=>item.ConfigTableId==tableId&&item.ColumnName==field);
        if(table.IsRongyu!=0||GlobalCrossTableTarget.Declared(table)||storage.Kind(column)!=MetadataFieldStorage.Join||
            !dependencies.Contains(field,StringComparer.Ordinal))
            throw new NotSupportedException("附件精确联接必须使用已发布的项目内单值字段。");

        // 精确终点按匹配模式验证。根记录另用普通模式读取共享槽，其逻辑值依旧由已发布路径解析。
        var match=new Builder(catalog,tableId,matching:true){MultiNamePathPage=page,PublishedExactJoinField=field}.Compile([field],[]);
        if(match.Types[field]!=column.LogicalType)throw new NotSupportedException("附件精确联接的实际类型不一致。");
        var builder=new Builder(catalog,tableId,crossTables:crossTables)
        {
            MultiNamePathPage=page,PublishedExactJoinField=field,LockPublishedJoinScopes=true
        };
        var plan=builder.Compile(outputs,dependencies);
        return storageOutputs.Count==0?plan:builder.RecordStorage(plan,storageOutputs,[]);
    }

    private sealed partial class Builder
    {
        /// <summary>必须是本计划输出中的真实根物理列；虚拟、派生、重名和未知字段均拒绝。</summary>
        internal MySqlNonRedundantReadPlan RecordStorage(MySqlNonRedundantReadPlan plan,IReadOnlyList<string> fields,IReadOnlyList<string> logicalOutputs)
        {
            if(fields.Count==0||fields.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fields.Count)throw new InvalidOperationException("规则物理输出不能为空或重复。");
            var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var name in fields)
            {
                if(!plan.Outputs.Contains(name,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("规则物理字段必须属于已发布输出。");
                var column=Column(root,name);if(!storage[root.Id].IsPhysical(column))throw new InvalidOperationException("规则物理输出不是根实体存储字段。");
                values.Add(name,rootAlias+"."+Q(column.ColumnName));
            }
            if(logicalOutputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=logicalOutputs.Count||logicalOutputs.Any(name=>!plan.Outputs.Contains(name,StringComparer.OrdinalIgnoreCase)))throw new InvalidOperationException("逻辑规则只能引用唯一已发布输出。");
            var aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var used=plan.Outputs.ToHashSet(StringComparer.OrdinalIgnoreCase);var ordinal=0;
            foreach(var name in logicalOutputs.Where(values.ContainsKey))
            {string alias;do{alias="metarill_rule_value_"+ordinal++;}while(!used.Add(alias));aliases.Add(name,alias);}
            return plan.WithStorageOutputs(values,aliases);
        }
    }
}
