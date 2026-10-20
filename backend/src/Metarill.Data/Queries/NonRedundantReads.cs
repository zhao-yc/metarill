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
    public static NonRedundantReadPlan Compile(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string>? dependencies=null)=>metadata.DatabaseProvider switch
    {"mysql"=>new(MySqlNonRedundantReadCompiler.Compile(metadata,tableId,outputs,dependencies)),_=>throw new NotSupportedException("该数据库尚未完成固定非冗余读取适配。")};
    /// <summary>将驱动标量转换为生成属性；空值和长ID保持原类型，不经JSON或浮点中转。</summary>
    public static T? Value<T>(IReadOnlyDictionary<string,object?> row,string name)
    {
        if(!row.TryGetValue(name,out var value))throw new InvalidOperationException("已编译读取未返回声明字段。");
        if(value is null)return default;
        var target=Nullable.GetUnderlyingType(typeof(T))??typeof(T);
        if(value is T exact)return exact;
        if(target==typeof(TimeSpan))return (T)(object)(value is TimeSpan span?span:TimeSpan.Parse(Convert.ToString(value,CultureInfo.InvariantCulture)!,CultureInfo.InvariantCulture));
        return (T)Convert.ChangeType(value,target,CultureInfo.InvariantCulture);
    }
}

/// <summary>数据库中立的固定读取入口；页面授权和真实父记录检查仍由原生成服务建立。</summary>
public sealed class NonRedundantReader(DatabaseSession session)
{
    private readonly MySqlNonRedundantReader mysql=new(session);
    /// <summary>复用适配层完整范围和参数绑定，不扩展页面输出权限。</summary>
    public Task<FixedReadPage> PageAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken token=default,bool unpaged=false,string? projectId=null)=>mysql.ReadPageAsync(plan.MySql,filters,order,page,pageSize,token,unpaged,projectId);
    /// <summary>有界导出按固定顺序读取原列投影，不发出不需要的COUNT。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> RowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken token=default,string? projectId=null)=>mysql.ReadRowsAsync(plan.MySql,filters,order,limit,token,projectId);
    /// <summary>统计不会继承分页或排序。</summary>
    public Task<FixedReadStatistics> StatisticsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<string> sums,CancellationToken token=default,string? projectId=null)=>mysql.ReadStatisticsAsync(plan.MySql,filters,sums,token,projectId);
}
