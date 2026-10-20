using System.Data.Common;

namespace Metarill.Data.Queries;

public enum FixedReadComparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Contains, IsNull, IsNotNull, In }
public sealed record FixedReadFilter(string Field,FixedReadComparison Comparison,object? Value=null);
public sealed record FixedReadOrder(string Field,bool Descending=false);
public sealed record FixedReadPage(long Total,IReadOnlyList<IReadOnlyDictionary<string,object?>> Records);
public sealed record FixedReadStatistics(long Total,IReadOnlyDictionary<string,decimal?> Sums);

/// <summary>执行固定单值读计划；调用方仍须先建立原页面授权和父对象读取范围。</summary>
public sealed class MySqlNonRedundantReader(DatabaseSession session)
{
    /// <summary>相同SQL范围用于总数与分页，派生值在数据库内先筛选、排序再分页。</summary>
    public async Task<FixedReadPage> ReadPageAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken cancellationToken=default,bool unpaged=false,string? projectId=null)
    {
        if(session.Provider.Name!="mysql")throw new NotSupportedException("固定读取计划与数据库方言不一致。");
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(filters);ArgumentNullException.ThrowIfNull(order);
        if(page<1||pageSize<0||pageSize==0&&!unpaged||unpaged&&(page!=1||pageSize!=0))throw new ArgumentOutOfRangeException(nameof(page));
        projectId=ValidateProject(projectId??plan.ProjectId);
        // 所有字段/操作/值先验证，不能在发出COUNT后才发现排序非法。
        filters=filters.Select(filter=>Prepare(plan,filter)).ToArray();order=order.ToArray();
        var clauses=filters.Select((filter,index)=>Filter(plan,filter,index)).ToArray();
        var orders=order.Select(item=>plan.Expression(item.Field)+(item.Descending?" DESC":" ASC")).ToList();
        if(!order.Any(item=>item.Field.Equals("id",StringComparison.OrdinalIgnoreCase)))orders.Add(plan.Expression("id")+" DESC");
        var where=string.Join(" AND ",new[]{plan.Scope}.Concat(clauses).Where(part=>part.Length>0));
        var from=plan.From+(where.Length==0?"":" WHERE "+where);
        var offset=checked(((long)page-1)*pageSize);
        return await OnConnection(async connection=>
        {
            await using var count=Command(connection);Bind(count,projectId,filters);count.CommandText="SELECT COUNT(*)"+from;
            var total=Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken),System.Globalization.CultureInfo.InvariantCulture);
            await using var read=Command(connection);Bind(read,projectId,filters);if(!unpaged){Add(read,"page_limit",pageSize);Add(read,"page_offset",offset);}
            read.CommandText="SELECT "+string.Join(",",plan.Outputs.Select(name=>plan.Expression(name)+" AS "+Q(name)))+from+" ORDER BY "+string.Join(",",orders)+(unpaged?"":" LIMIT @page_limit OFFSET @page_offset");
            var records=new List<IReadOnlyDictionary<string,object?>>();await using var reader=await read.ExecuteReaderAsync(cancellationToken);
            while(await reader.ReadAsync(cancellationToken))
            {var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<reader.FieldCount;i++)row.Add(reader.GetName(i),await reader.IsDBNullAsync(i,cancellationToken)?null:reader.GetValue(i));records.Add(new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(row));}
            return new FixedReadPage(total,records.AsReadOnly());
        },cancellationToken);
    }
    /// <summary>数值派生表达式在同一完整过滤范围内汇总，不先分页或对当前页记录求和。</summary>
    public async Task<FixedReadStatistics> ReadStatisticsAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<string> sumFields,CancellationToken cancellationToken=default,string? projectId=null)
    {
        if(session.Provider.Name!="mysql")throw new NotSupportedException("固定读取计划与数据库方言不一致。");
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(filters);ArgumentNullException.ThrowIfNull(sumFields);
        projectId=ValidateProject(projectId??plan.ProjectId);var fields=sumFields.ToArray();
        if(fields.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fields.Length)throw new InvalidOperationException("统计字段重复。");
        foreach(var field in fields){plan.Expression(field);if(plan.Types[field] is not("int32" or "int64" or "decimal"))throw new InvalidOperationException("求和字段必须是已编译数值。");}
        filters=filters.Select(filter=>Prepare(plan,filter)).ToArray();
        var where=string.Join(" AND ",new[]{plan.Scope}.Concat(filters.Select((filter,index)=>Filter(plan,filter,index))).Where(part=>part.Length>0));
        var sql="SELECT COUNT(*) AS total"+string.Concat(fields.Select((field,index)=>",SUM("+plan.Expression(field)+") AS sum_"+index))+plan.From+(where.Length==0?"":" WHERE "+where);
        return await OnConnection(async connection=>
        {
            await using var command=Command(connection);Bind(command,projectId,filters);command.CommandText=sql;
            await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("固定统计未返回结果。");
            var total=reader.GetInt64(0);var sums=new Dictionary<string,decimal?>(StringComparer.OrdinalIgnoreCase);
            for(var index=0;index<fields.Length;index++)sums.Add(fields[index],await reader.IsDBNullAsync(index+1,cancellationToken)?null:Convert.ToDecimal(reader.GetValue(index+1),System.Globalization.CultureInfo.InvariantCulture));
            return new FixedReadStatistics(total,new System.Collections.ObjectModel.ReadOnlyDictionary<string,decimal?>(sums));
        },cancellationToken);
    }
    /// <summary>复制请求并按发布类型绑定，长ID字符串不经double，防止高位项目/记录误匹配。</summary>
    private static FixedReadFilter Prepare(MySqlNonRedundantReadPlan plan,FixedReadFilter filter)
    {
        plan.Expression(filter.Field);var value=filter.Value;
        if(filter.Comparison==FixedReadComparison.In)
        {if(value is not IReadOnlyList<object?> items)throw new InvalidOperationException("集合筛选必须使用明确标量列表。");return filter with{Value=Array.AsReadOnly(items.Select(item=>Prepare(plan,new(filter.Field,FixedReadComparison.Equal,item)).Value).ToArray())};}
        if(value is null||filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)return filter;
        var type=plan.Types[filter.Field];var culture=System.Globalization.CultureInfo.InvariantCulture;
        object converted=type switch
        {
            "int64" when value is long or int=>Convert.ToInt64(value,culture),
            "int64" when value is string text&&long.TryParse(text,System.Globalization.NumberStyles.Integer,culture,out var number)=>number,
            "int32" when value is int=>value,
            "int32" when value is string text&&int.TryParse(text,System.Globalization.NumberStyles.Integer,culture,out var number)=>number,
            "decimal" when value is decimal or int or long=>Convert.ToDecimal(value,culture),
            "decimal" when value is string text&&decimal.TryParse(text,System.Globalization.NumberStyles.AllowLeadingSign|System.Globalization.NumberStyles.AllowDecimalPoint,culture,out var number)=>number,
            "string" when value is string=>value,
            "bool" when value is bool=>value,
            "datetime" or "date" when value is DateTime=>value,
            "datetime" or "date" when value is string text&&DateTime.TryParse(text,culture,System.Globalization.DateTimeStyles.RoundtripKind,out var date)=>date,
            "time" when value is TimeSpan=>value,
            "time" when value is string text&&TimeSpan.TryParse(text,culture,out var time)=>time,
            _=>throw new InvalidOperationException("筛选值与发布字段类型不匹配。")
        };
        return filter with{Value=converted};
    }
    /// <summary>校验完整操作与标量类型；NULL使用明确SQL语义，LIKE使用字面转义。</summary>
    private static string Filter(MySqlNonRedundantReadPlan plan,FixedReadFilter filter,int index)
    {
        var expression=plan.Expression(filter.Field);var value=filter.Value;var parameter="@filter_"+index;
        if(filter.Comparison==FixedReadComparison.In)
        {var items=(IReadOnlyList<object?>)value!;var indexes=Enumerable.Range(0,items.Count).Where(item=>items[item] is not null).ToArray();var member=indexes.Length==0?"":expression+" IN ("+string.Join(",",indexes.Select(item=>parameter+"_"+item))+")";var hasNull=items.Any(item=>item is null);return member.Length==0?hasNull?expression+" IS NULL":"1=0":hasNull?"("+member+" OR "+expression+" IS NULL)":member;}
        if(value is not(null or string or long or int or decimal or double or float or bool or DateTime or DateTimeOffset or TimeSpan))throw new InvalidOperationException("固定筛选值必须是受支持的标量。");
        if(filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)
        {if(value is not null)throw new InvalidOperationException("空值筛选不能同时带值。");return expression+(filter.Comparison==FixedReadComparison.IsNull?" IS NULL":" IS NOT NULL");}
        if(filter.Comparison==FixedReadComparison.Contains)
        {if(value is not string||plan.Types[filter.Field]!="string")throw new InvalidOperationException("字面包含仅适用于文本字段。");return expression+" LIKE "+parameter+" ESCAPE '!'";}
        if(value is null)return filter.Comparison switch{FixedReadComparison.Equal=>expression+" IS NULL",FixedReadComparison.NotEqual=>expression+" IS NOT NULL",_=>throw new InvalidOperationException("范围比较不能使用null。")};
        return expression+(filter.Comparison switch{FixedReadComparison.Equal=>"=",FixedReadComparison.NotEqual=>"<>",FixedReadComparison.Greater=>">",FixedReadComparison.GreaterOrEqual=>">=",FixedReadComparison.Less=>"<",FixedReadComparison.LessOrEqual=>"<=",_=>throw new InvalidOperationException("未知固定筛选操作。")})+parameter;
    }
    /// <summary>结构与业务值分离，根及目标共用同一发布项目绑定。</summary>
    private static void Bind(DbCommand command,string projectId,IReadOnlyList<FixedReadFilter> filters)
    {
        Add(command,"project_scope_text",projectId);Add(command,"project_scope_number",long.Parse(projectId,System.Globalization.CultureInfo.InvariantCulture));
        for(var i=0;i<filters.Count;i++)
        {var filter=filters[i];if(filter.Comparison==FixedReadComparison.In){var items=(IReadOnlyList<object?>)filter.Value!;for(var item=0;item<items.Count;item++)if(items[item] is {} memberValue)Add(command,"filter_"+i+"_"+item,memberValue);continue;}if(filter.Value is null||filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)continue;var value=filter.Comparison==FixedReadComparison.Contains?"%"+((string)filter.Value).Replace("!","!!",StringComparison.Ordinal).Replace("%","!%",StringComparison.Ordinal).Replace("_","!_",StringComparison.Ordinal)+"%":filter.Value;Add(command,"filter_"+i,value);}
    }
    /// <summary>执行项目必须来自宿主已认证Actor；这里仅做类型校验，不授予页面权限。</summary>
    private static string ValidateProject(string projectId)
    {if(!long.TryParse(projectId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var id)||id<=0)throw new InvalidOperationException("执行项目标识无效。");return projectId;}
    /// <summary>当前事务使用同一个真实连接；普通读取创建并释放自己的连接。</summary>
    private async Task<T> OnConnection<T>(Func<DbConnection,Task<T>> action,CancellationToken token)
    {
        if(session.CurrentTransaction is {} transaction)return await action(transaction.Connection??throw new InvalidOperationException("事务连接已关闭。"));
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync(token);return await action(connection);
    }
    /// <summary>每个命令加入现有业务事务，不另开隐式事务或修改结构。</summary>
    private DbCommand Command(DbConnection connection){var command=connection.CreateCommand();command.Transaction=session.CurrentTransaction;return command;}
    /// <summary>驱动参数承载值，不插入SQL文本。</summary>
    private static void Add(DbCommand command,string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);}
    /// <summary>输出别名也只能是已校验单段标识符。</summary>
    private static string Q(string name){Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(name);return "`"+name+"`";}
}
