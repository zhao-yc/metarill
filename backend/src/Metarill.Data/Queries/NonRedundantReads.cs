using System.Globalization;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>生成Service使用的数据库中立读计划句柄；具体SQL仍只在数据适配层构建。</summary>
public sealed class NonRedundantReadPlan
{
    internal NonRedundantReadPlan(MySqlNonRedundantReadPlan mysql){MySql=mysql;}
    internal MySqlNonRedundantReadPlan MySql{get;}
    public IReadOnlyDictionary<string,string> OutputTypes=>new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(MySql.Outputs.ToDictionary(name=>name,name=>MySql.Types[name],StringComparer.OrdinalIgnoreCase));
}

/// <summary>在生成校验或宿主启动阶段编译，业务方法不判断数据库品牌。</summary>
public static class NonRedundantReads
{
    /// <summary>输出与隐藏查询依赖分别声明；后者不会自动出现在返回记录中。</summary>
    public static NonRedundantReadPlan Compile(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)=>CompileWithCrossTables(metadata,tableId,outputs,[],dependencies,deferCascadeNames);
    /// <summary>规则记录专用：物理根实体输出与逻辑筛选/派生值分开，仍使用同一固定SQL及范围。</summary>
    public static NonRedundantReadPlan CompileRuleRecord(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false,IReadOnlyList<string>? logicalOutputs=null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata.DatabaseProvider switch{"mysql"=>new(MySqlNonRedundantReadCompiler.CompileRuleRecord(metadata,tableId,outputs,storageOutputs,crossTables,dependencies,deferCascadeNames,logicalOutputs)),_=>throw new NotSupportedException("该数据库尚未完成规则记录读取适配。")};
    }
    /// <summary>跨表逻辑读取须显式传入冻结目标页范围；保留原Compile签名及无绑定门禁。</summary>
    public static NonRedundantReadPlan CompileWithCrossTables(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)
    {
        ArgumentNullException.ThrowIfNull(metadata);ArgumentNullException.ThrowIfNull(crossTables);
        return metadata.DatabaseProvider switch{"mysql"=>new(MySqlNonRedundantReadCompiler.CompileWithCrossTables(metadata,tableId,outputs,crossTables,dependencies,deferCascadeNames)),_=>throw new NotSupportedException("该数据库尚未完成固定非冗余读取适配。")};
    }
    /// <summary>匹配计划只编译必要单值路径，并验证最终物理值，不能借别名匹配凭据或文件数组。</summary>
    public static NonRedundantReadPlan CompileMatch(MetadataCatalog metadata,string tableId,string field)=>metadata.DatabaseProvider switch
    {"mysql"=>new(MySqlNonRedundantReadCompiler.CompileMatch(metadata,tableId,field)),_=>throw new NotSupportedException("该数据库尚未完成固定匹配读取适配。")};
    /// <summary>生成期取得固定单值名称实际读取的来源物理列；同表其它角色可能形成安全超集，不返回SQL。</summary>
    public static IReadOnlyList<string> CompileSourceDependencies(MetadataCatalog metadata,string tableId,string field)
    {
        var plan=CompileMatch(metadata,tableId,field);return Array.AsReadOnly(plan.MySql.SourceDependencies.ToArray());
    }
    /// <summary>生成期证明固定名称路径是否读到指定实体，不向生成层暴露方言计划或SQL。</summary>
    public static bool MatchReadsTable(MetadataCatalog metadata,string tableId,string field,string referencedTable)
    {
        MetadataValidation.RequireIdentifier(referencedTable);return CompileMatch(metadata,tableId,field).MySql.PhysicalReferences.Any(reference=>reference.Table==referencedTable);
    }
    /// <summary>只读规则取固定逻辑别名；无独立别名的派生名称继续读取原字段，不接受客户端别名。</summary>
    public static T? RuleValue<T>(NonRedundantReadPlan plan,IReadOnlyDictionary<string,object?> row,string name)
    {
        if(!plan.MySql.Outputs.Contains(name,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("逻辑规则字段不属于固定输出。");
        return Value<T>(row,plan.MySql.RuleOutputAliases.TryGetValue(name,out var alias)?alias:name);
    }
    /// <summary>将驱动标量转换为生成属性；空值和长ID保持原类型，不经JSON或浮点中转。</summary>
    public static T? Value<T>(IReadOnlyDictionary<string,object?> row,string name)
    {
        if(!row.TryGetValue(name,out var value))throw new InvalidOperationException("已编译读取未返回声明字段。");
        if(value is null)return default;
        var target=Nullable.GetUnderlyingType(typeof(T))??typeof(T);
        if(value is T exact)return exact;
        if(target==typeof(TimeSpan))return (T)(object)(value is TimeSpan span?span:Metarill.Core.Contracts.SqlTimeValue.ParseText(Convert.ToString(value,CultureInfo.InvariantCulture))!.Value);
        return (T)Convert.ChangeType(value,target,CultureInfo.InvariantCulture);
    }
}

/// <summary>数据库中立的固定读取入口；页面授权和真实父记录检查仍由原生成服务建立。</summary>
public sealed class NonRedundantReader(DatabaseSession session)
{
    private readonly MySqlNonRedundantReader mysql=new(session);
    /// <summary>复用适配层完整范围和参数绑定，不扩展页面输出权限。</summary>
    public Task<FixedReadPage> PageAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken token=default,bool unpaged=false,string? projectId=null,FixedConditionSqlPlan? condition=null,bool currentRead=false)=>mysql.ReadPageAsync(plan.MySql,filters,order,page,pageSize,token,unpaged,projectId,condition,currentRead);
    /// <summary>有界导出按固定顺序读取原列投影，不发出不需要的COUNT。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> RowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadRowsAsync(plan.MySql,filters,order,limit,token,projectId,condition);
    /// <summary>写校验沿真实事务锁定固定投影及联接名称，不执行COUNT；旧RowsAsync合同保持。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> CurrentRowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadCurrentRowsAsync(plan.MySql,filters,order,limit,token,projectId,condition);
    /// <summary>保持原跨表候选完整数组，方言层执行无COUNT的固定排序查询。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> AllRowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadAllRowsAsync(plan.MySql,filters,order,token,projectId,condition);
    /// <summary>统计不会继承分页或排序。</summary>
    public Task<FixedReadStatistics> StatisticsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<string> sums,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadStatisticsAsync(plan.MySql,filters,sums,token,projectId,condition);
}
