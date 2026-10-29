using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>规则记录的根实体读取物理存储，派生值与筛选继续复用同一已冻结逻辑计划。</summary>
    public static MySqlNonRedundantReadPlan CompileRuleRecord(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false,IReadOnlyList<string>? logicalOutputs=null)
    {
        ArgumentNullException.ThrowIfNull(storageOutputs);
        var plan=CompileWithCrossTables(catalog,tableId,outputs,crossTables,dependencies,deferCascadeNames);
        // 与普通编译使用同一根别名和字段来源；适配器内生成标识符，不向Service泄漏方言。
        return new Builder(catalog,tableId,deferCascadeNames:deferCascadeNames,crossTables:crossTables).RecordStorage(plan,storageOutputs,logicalOutputs??[]);
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
