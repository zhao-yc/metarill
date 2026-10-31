using System.Data.Common;

namespace Metarill.Data.Queries;

public enum FixedReadComparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Contains, IsNull, IsNotNull, In, JsonArrayAnyContains, JsonArrayHasId }
public sealed record FixedReadFilter(string Field,FixedReadComparison Comparison,object? Value=null);
public sealed record FixedReadOrder(string Field,bool Descending=false);
public sealed record FixedReadPage(long Total,IReadOnlyList<IReadOnlyDictionary<string,object?>> Records);
public sealed record FixedReadStatistics(long Total,IReadOnlyDictionary<string,decimal?> Sums);

/// <summary>执行固定单值及多选读计划；调用方仍须先建立原页面授权和父对象读取范围。</summary>
public sealed class MySqlNonRedundantReader(DatabaseSession session)
{
    // 名称数组也是原Excel的纯文本单元格；保持中文与标点可读，对外HTTP仍由宿主JSON编码。
    private static readonly System.Text.Json.JsonSerializerOptions MultiNamesJson=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    /// <summary>相同SQL范围用于总数与分页，派生值在数据库内先筛选、排序再分页。</summary>
    public async Task<FixedReadPage> ReadPageAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken cancellationToken=default,bool unpaged=false,string? projectId=null,FixedConditionSqlPlan? condition=null,bool currentRead=false)
        => await ReadAsync(plan,filters,order,page,pageSize,cancellationToken,unpaged,projectId,true,condition,currentRead);
    /// <summary>有界导出只读明细，不额外执行分页总数；限制与查询验证仍共用固定读取入口。</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> ReadRowsAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken cancellationToken=default,string? projectId=null,FixedConditionSqlPlan? condition=null)
        => (await ReadAsync(plan,filters,order,1,limit,cancellationToken,false,projectId,false,condition)).Records;
    /// <summary>分页和有界明细共用结构生成；是否求总数由服务固定用途决定。</summary>
    /// <summary>固定有界当前读与原分页共用参数及类型验证，写事务内锁定根和名称联接，不额外COUNT。</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> ReadCurrentRowsAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken cancellationToken=default,string? projectId=null,FixedConditionSqlPlan? condition=null)
        =>(await ReadAsync(plan,filters,order,1,limit,cancellationToken,false,projectId,false,condition,true)).Records;
    /// <summary>原跨表候选无分页合同，单条SQL返回完整固定范围，不执行COUNT或静默截断。</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> ReadAllRowsAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,CancellationToken cancellationToken=default,string? projectId=null,FixedConditionSqlPlan? condition=null)
        =>(await ReadAsync(plan,filters,order,1,0,cancellationToken,true,projectId,false,condition)).Records;
    private async Task<FixedReadPage> ReadAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken cancellationToken,bool unpaged,string? projectId,bool includeTotal,FixedConditionSqlPlan? condition,bool currentRead=false)
    {
        if(currentRead&&session.CurrentTransaction is null)throw new InvalidOperationException("当前读必须加入真实业务事务。");
        if(session.Provider.Name!="mysql")throw new NotSupportedException("固定读取计划与数据库方言不一致。");
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(filters);ArgumentNullException.ThrowIfNull(order);
        if(page<1||pageSize<0||pageSize==0&&!unpaged||unpaged&&(page!=1||pageSize!=0))throw new ArgumentOutOfRangeException(nameof(page));
        projectId=RequirePlanProject(plan,projectId);
        // 所有字段/操作/值先验证，不能在发出COUNT后才发现排序非法。
        filters=filters.Select(filter=>Prepare(plan,filter)).ToArray();order=order.ToArray();
        var clauses=filters.Select((filter,index)=>Filter(plan,filter,index)).ToArray();
        var orders=order.Select(item=>plan.Expression(item.Field)+(item.Descending?" DESC":" ASC")).ToList();
        if(!order.Any(item=>item.Field.Equals("id",StringComparison.OrdinalIgnoreCase)))orders.Add(plan.Expression("id")+" DESC");
        // 联接依赖同时包含响应、筛选、排序和固定条件；条件不能因未显示而丢失目标范围。
        var required=plan.HasMaintenanceJoinProjection?plan.Outputs.Concat(filters.Select(filter=>filter.Field)).Concat(order.Select(item=>item.Field)).Append("id").ToHashSet(StringComparer.OrdinalIgnoreCase):null;
        var conditionClause=condition?.RenderConstants(session.Provider,"page_condition",field=>{required?.Add(field);return plan.Expression(field);});
        var where=string.Join(" AND ",new[]{plan.Scope,conditionClause?.Sql??""}.Concat(clauses).Where(part=>part.Length>0));
        var from=(required is null?plan.From:plan.FromForFields(required))+(where.Length==0?"":" WHERE "+where);
        var offset=checked(((long)page-1)*pageSize);
        return await OnConnection(async connection=>
        {
            var total=0L;
            if(includeTotal){await using var count=Command(connection);Bind(count,projectId,filters);BindCross(count,plan);BindCondition(count,conditionClause);count.CommandText="SELECT COUNT(*)"+(plan.Crosses.Count==0?"":","+CrossValidity(plan))+from+(currentRead?" FOR SHARE":"");if(plan.Crosses.Count==0)total=Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken),System.Globalization.CultureInfo.InvariantCulture);else{await using var counted=await count.ExecuteReaderAsync(cancellationToken);if(!await counted.ReadAsync(cancellationToken))throw new InvalidOperationException("跨表计数未返回结果。");RequireValidCross(counted,1);total=counted.GetInt64(0);}}
            await using var read=Command(connection);Bind(read,projectId,filters);BindCross(read,plan);BindCondition(read,conditionClause);if(!unpaged){Add(read,"page_limit",pageSize);Add(read,"page_offset",offset);}
            // 普通投影分页后批量补名；只有筛选/排序名称数组时使用预编译表达式，不按每行展开根查询。
            var multis=plan.Outputs.Where(name=>!plan.StorageOutputs.ContainsKey(name)&&plan.MultiProjections.ContainsKey(name)).ToArray();var hidden=new Dictionary<string,string>(StringComparer.Ordinal);var projection=plan.Outputs.Select(name=>(!plan.StorageOutputs.ContainsKey(name)&&plan.MultiProjections.ContainsKey(name)?"NULL":plan.OutputExpression(name))+" AS "+Q(name)).ToList();
            var ordinal=0;foreach(var name in multis){string alias;do{alias="metarill_multi_input_"+ordinal++;}while(plan.Outputs.Contains(alias,StringComparer.OrdinalIgnoreCase));hidden.Add(name,alias);projection.Add(plan.MultiProjections[name].SourceExpression+" AS "+Q(alias));}
            var crossInputs=new List<(MySqlCrossTableProjection Plan,Dictionary<string,string> Fields)>();var sharedValidity=new List<string>();var used=plan.Outputs.Concat(hidden.Values).Concat(plan.RuleOutputAliases.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var rule in plan.RuleOutputAliases)projection.Add(plan.Expression(rule.Key)+" AS "+Q(rule.Value));
            foreach(var cross in plan.Crosses)
            {
                if(cross.RequireRowValidity){string validAlias;do{validAlias="metarill_cross_valid_"+ordinal++;}while(!used.Add(validAlias));sharedValidity.Add(validAlias);projection.Add("CASE WHEN ("+cross.Validity+") THEN 1 ELSE 0 END AS "+Q(validAlias));}
                var fields=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var pair in cross.RawSlots){string alias;do{alias="metarill_cross_input_"+ordinal++;}while(!used.Add(alias));fields.Add(pair.Key,alias);projection.Add(pair.Value+" AS "+Q(alias));}crossInputs.Add((cross,fields));
            }
            read.CommandText="SELECT "+string.Join(",",projection)+from+" ORDER BY "+string.Join(",",orders)+(unpaged?"":" LIMIT @page_limit OFFSET @page_offset")+(currentRead?" FOR SHARE":"");
            var records=new List<Dictionary<string,object?>>();
            await using(var reader=await read.ExecuteReaderAsync(cancellationToken))
                while(await reader.ReadAsync(cancellationToken))
                {var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<reader.FieldCount;i++)row.Add(reader.GetName(i),await reader.IsDBNullAsync(i,cancellationToken)?null:reader.GetValue(i));records.Add(row);}
            foreach(var row in records)
            {
                // 无COUNT用途也必须拒绝冲突的共享祖先，不能只在分页总数阶段验证。
                foreach(var alias in sharedValidity){if(Convert.ToInt64(row[alias],System.Globalization.CultureInfo.InvariantCulture)!=1)throw new InvalidOperationException("共享跨表存储或实际祖先路径冲突，未返回数据。");row.Remove(alias);}foreach(var input in crossInputs){_=input.Plan.Selection.ReadStored(input.Fields.ToDictionary(pair=>pair.Key,pair=>row[pair.Value],StringComparer.Ordinal));foreach(var alias in input.Fields.Values)row.Remove(alias);}
            }
            foreach(var name in multis)await PopulateMultiAsync(connection,plan.MultiProjections[name],name,hidden[name],records,projectId,currentRead,cancellationToken);
            return new FixedReadPage(total,records.Select(row=>(IReadOnlyDictionary<string,object?>)new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(row)).ToArray());
        },cancellationToken);
    }
    /// <summary>每个关系按页合并ID、分批主键读取名称；写回执使用目标当前锁读，不能被旧RR快照覆盖。</summary>
    private async Task PopulateMultiAsync(DbConnection connection,MySqlMultiProjection projection,string output,string hidden,IReadOnlyList<Dictionary<string,object?>> rows,string projectId,bool currentRead,CancellationToken token)
    {
        var members=rows.Select(row=>MultiMembers(row[hidden])).ToArray();var ids=members.SelectMany(items=>items).Where(id=>id is not null).Select(id=>long.TryParse(id,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var key)&&key>0&&key.ToString(System.Globalization.CultureInfo.InvariantCulture)==id?(long?)key:null).OfType<long>().Distinct().Order().ToArray();
        var names=new Dictionary<string,string?>(StringComparer.Ordinal);
        foreach(var batch in ids.Chunk(200))
        {
            await using var command=Command(connection);Bind(command,projectId,[]);for(var index=0;index<batch.Length;index++)Add(command,"multi_id_"+index,batch[index]);
            command.CommandText="SELECT multi_value.`id`,"+(projection.NameExpression??"multi_value."+projection.TargetName)+" FROM "+projection.TargetTable+" multi_value"+projection.Joins+" WHERE "+(projection.TargetScope.Length==0?"":projection.TargetScope+" AND ")+"multi_value.`id` IN ("+string.Join(",",Enumerable.Range(0,batch.Length).Select(index=>"@multi_id_"+index))+") ORDER BY multi_value.`id`"+(currentRead?" FOR SHARE":"");
            await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))names.Add(reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),await reader.IsDBNullAsync(1,token)?null:reader.GetString(1));
        }
        for(var index=0;index<rows.Count;index++){rows[index][output]=System.Text.Json.JsonSerializer.Serialize(members[index].Select(id=>id is null?null:names.GetValueOrDefault(id)).ToArray(),MultiNamesJson);rows[index].Remove(hidden);}
    }
    /// <summary>只读取历史数组的顺序和值，损坏或非数组保持原空集合显示，不修补源存储。</summary>
    private static string?[] MultiMembers(object? value)
    {
        if(value is null)return [];try{using var doc=System.Text.Json.JsonDocument.Parse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!);return doc.RootElement.ValueKind==System.Text.Json.JsonValueKind.Array?doc.RootElement.EnumerateArray().Select(item=>item.ValueKind is System.Text.Json.JsonValueKind.String?item.GetString():item.ValueKind==System.Text.Json.JsonValueKind.Number?item.GetRawText():null).ToArray():[];}catch(System.Text.Json.JsonException){return [];}
    }
    /// <summary>数值派生表达式在同一完整过滤范围内汇总，不先分页或对当前页记录求和。</summary>
    public async Task<FixedReadStatistics> ReadStatisticsAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<string> sumFields,CancellationToken cancellationToken=default,string? projectId=null,FixedConditionSqlPlan? condition=null)
    {
        if(session.Provider.Name!="mysql")throw new NotSupportedException("固定读取计划与数据库方言不一致。");
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(filters);ArgumentNullException.ThrowIfNull(sumFields);
        projectId=RequirePlanProject(plan,projectId);var fields=sumFields.ToArray();
        if(fields.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fields.Length)throw new InvalidOperationException("统计字段重复。");
        foreach(var field in fields){plan.Expression(field);if(plan.Types[field] is not("int32" or "int64" or "decimal"))throw new InvalidOperationException("求和字段必须是已编译数值。");}
        filters=filters.Select(filter=>Prepare(plan,filter)).ToArray();
        var conditionClause=condition?.RenderConstants(session.Provider,"page_condition",plan.Expression);
        var where=string.Join(" AND ",new[]{plan.Scope,conditionClause?.Sql??""}.Concat(filters.Select((filter,index)=>Filter(plan,filter,index))).Where(part=>part.Length>0));
        var sql="SELECT COUNT(*) AS total"+string.Concat(fields.Select((field,index)=>",SUM("+plan.Expression(field)+") AS sum_"+index))+(plan.Crosses.Count==0?"":","+CrossValidity(plan))+plan.From+(where.Length==0?"":" WHERE "+where);
        return await OnConnection(async connection=>
        {
            await using var command=Command(connection);Bind(command,projectId,filters);BindCross(command,plan);BindCondition(command,conditionClause);command.CommandText=sql;
            await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("固定统计未返回结果。");
            if(plan.Crosses.Count>0)RequireValidCross(reader,fields.Length+1);
            var total=reader.GetInt64(0);var sums=new Dictionary<string,decimal?>(StringComparer.OrdinalIgnoreCase);
            for(var index=0;index<fields.Length;index++)sums.Add(fields[index],await reader.IsDBNullAsync(index+1,cancellationToken)?null:Convert.ToDecimal(reader.GetValue(index+1),System.Globalization.CultureInfo.InvariantCulture));
            return new FixedReadStatistics(total,new System.Collections.ObjectModel.ReadOnlyDictionary<string,decimal?>(sums));
        },cancellationToken);
    }
    /// <summary>复制请求并按发布类型绑定，长ID字符串不经double，防止高位项目/记录误匹配。</summary>
    private static FixedReadFilter Prepare(MySqlNonRedundantReadPlan plan,FixedReadFilter filter)
    {
        plan.Expression(filter.Field);var value=filter.Value;
        if(filter.Comparison==FixedReadComparison.JsonArrayHasId)
        {
            var text=value is string or long or int?Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture):null;
            if(!plan.ArrayIdPredicates.ContainsKey(filter.Field)||text is null||!long.TryParse(text,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(System.Globalization.CultureInfo.InvariantCulture))throw new InvalidOperationException("数组成员筛选只接受已编译的复选字段和规范正整数ID。");
            return filter with{Value=text};
        }
        if(filter.Comparison==FixedReadComparison.In)
        {if(value is not IReadOnlyList<object?> items)throw new InvalidOperationException("集合筛选必须使用明确标量列表。");return filter with{Value=Array.AsReadOnly(items.Select(item=>Prepare(plan,new(filter.Field,FixedReadComparison.Equal,item)).Value).ToArray())};}
        if(value is null||filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)return filter;
        var type=plan.Types[filter.Field];var culture=System.Globalization.CultureInfo.InvariantCulture;
        object converted=type switch
        {
            "int64" when value is long or int=>Convert.ToInt64(value,culture),
            // byte[]可变；冻结一次，避免COUNT与明细绑定之间被调用方改写。
            "binary" when value is byte[] bytes=>bytes.ToArray(),
            "int64" when value is string text&&long.TryParse(text,System.Globalization.NumberStyles.Integer,culture,out var number)=>number,
            "int32" when value is int=>value,
            "int32" when value is string text&&int.TryParse(text,System.Globalization.NumberStyles.Integer,culture,out var number)=>number,
            "decimal" when value is decimal or int or long=>Convert.ToDecimal(value,culture),
            "decimal" when value is string text&&decimal.TryParse(text,System.Globalization.NumberStyles.AllowLeadingSign|System.Globalization.NumberStyles.AllowDecimalPoint,culture,out var number)=>number,
            "double" when value is double or float or decimal or int or long && double.IsFinite(Convert.ToDouble(value,culture))=>Convert.ToDouble(value,culture),
            "double" when value is string text&&double.TryParse(text,System.Globalization.NumberStyles.Float,culture,out var real)&&double.IsFinite(real)=>real,
            "string" when value is string=>value,
            "bool" when value is bool=>value,
            "datetime" or "date" when value is DateTime=>value,
            "datetime" or "date" when value is string text&&DateTime.TryParse(text,culture,System.Globalization.DateTimeStyles.RoundtripKind,out var date)=>date,
            "time" when value is TimeSpan=>value,
            "time" when value is string text=>Metarill.Core.Contracts.SqlTimeValue.ParseText(text)??throw new InvalidOperationException("非空TIME筛选不能为空。"),
            _=>throw new InvalidOperationException("筛选值与发布字段类型不匹配。")
        };
        if(converted is TimeSpan time)new MySqlDatabaseProvider().ValidateTimeValue(time);
        return filter with{Value=converted};
    }
    /// <summary>校验完整操作与标量类型；NULL使用明确SQL语义，LIKE使用字面转义。</summary>
    private static string Filter(MySqlNonRedundantReadPlan plan,FixedReadFilter filter,int index)
    {
        var expression=plan.Expression(filter.Field);var value=filter.Value;var parameter="@filter_"+index;
        if(filter.Comparison==FixedReadComparison.In)
        {var items=(IReadOnlyList<object?>)value!;var indexes=Enumerable.Range(0,items.Count).Where(item=>items[item] is not null).ToArray();var member=indexes.Length==0?"":expression+" IN ("+string.Join(",",indexes.Select(item=>parameter+"_"+item))+")";var hasNull=items.Any(item=>item is null);return member.Length==0?hasNull?expression+" IS NULL":"1=0":hasNull?"("+member+" OR "+expression+" IS NULL)":member;}
        if(value is not(null or string or byte[] or long or int or decimal or double or float or bool or DateTime or DateTimeOffset or TimeSpan))throw new InvalidOperationException("固定筛选值必须是受支持的标量。");
        if(filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)
        {if(value is not null)throw new InvalidOperationException("空值筛选不能同时带值。");return expression+(filter.Comparison==FixedReadComparison.IsNull?" IS NULL":" IS NOT NULL");}
        if(filter.Comparison==FixedReadComparison.JsonArrayHasId)
        {
            if(value is not string||!plan.ArrayIdPredicates.TryGetValue(filter.Field,out var idPredicate))throw new InvalidOperationException("数组成员筛选缺少固定字段与ID。");return idPredicate.Replace("{parameter}",parameter,StringComparison.Ordinal);
        }
        if(filter.Comparison==FixedReadComparison.JsonArrayAnyContains)
        {
            if(value is not string||!plan.ArrayNamePredicates.TryGetValue(filter.Field,out var arrayPredicate))throw new InvalidOperationException("数组名称筛选只接受已编译多值关系与文本。");
            // 元素匹配不能退化为序列化JSON文本LIKE；根COUNT、分页及统计仍使用同一EXISTS。
            return arrayPredicate.Replace("{parameter}",parameter,StringComparison.Ordinal);
        }
        if(filter.Comparison==FixedReadComparison.Contains)
        {if(value is not string||plan.Types[filter.Field]!="string")throw new InvalidOperationException("字面包含仅适用于文本字段。");return expression+" LIKE "+parameter+" ESCAPE '!'";}
        if(value is null)return filter.Comparison switch{FixedReadComparison.Equal=>expression+" IS NULL",FixedReadComparison.NotEqual=>expression+" IS NOT NULL",_=>throw new InvalidOperationException("范围比较不能使用null。")};
        return expression+(filter.Comparison switch{FixedReadComparison.Equal=>"=",FixedReadComparison.NotEqual=>"<>",FixedReadComparison.Greater=>">",FixedReadComparison.GreaterOrEqual=>">=",FixedReadComparison.Less=>"<",FixedReadComparison.LessOrEqual=>"<=",_=>throw new InvalidOperationException("未知固定筛选操作。")})+parameter;
    }
    /// <summary>COUNT与统计在同一原范围内检测坏稀疏存储，不隐藏错误行或增加逐行查询。</summary>
    private static string CrossValidity(MySqlNonRedundantReadPlan plan)=>"COALESCE(MAX(CASE WHEN "+string.Join(" AND ",plan.Crosses.Select(cross=>"("+cross.Validity+")"))+" THEN 0 ELSE 1 END),0)";
    /// <summary>多位、非规范或越界ID明确拒绝，不在读取时修补历史值。</summary>
    private static void RequireValidCross(DbDataReader reader,int ordinal){if(Convert.ToInt32(reader.GetValue(ordinal),System.Globalization.CultureInfo.InvariantCulture)!=0)throw new InvalidOperationException("跨表稀疏存储含歧义或无效ID，未返回不完整逻辑路径。");}
    /// <summary>固定目标条件与请求筛选分开绑定，分页、数量及统计使用同一份冻结常量。</summary>
    private static void BindCross(DbCommand command,MySqlNonRedundantReadPlan plan){foreach(var pair in plan.Constants)Add(command,pair.Key,pair.Value??DBNull.Value);}
    /// <summary>固定页面条件的常量参数与用户筛选分别命名，所有COUNT/明细/统计共用同一绑定。</summary>
    private static void BindCondition(DbCommand command,FixedConditionSqlPlan.Clause? condition)
    {if(condition is not null)foreach(var pair in condition.Parameters)Add(command,pair.Key,pair.Value??DBNull.Value);}
    /// <summary>结构与业务值分离，根及目标共用同一发布项目绑定。</summary>
    private static void Bind(DbCommand command,string projectId,IReadOnlyList<FixedReadFilter> filters)
    {
        Add(command,"project_scope_text",projectId);Add(command,"project_scope_number",long.Parse(projectId,System.Globalization.CultureInfo.InvariantCulture));
        for(var i=0;i<filters.Count;i++)
        {var filter=filters[i];if(filter.Comparison==FixedReadComparison.In){var items=(IReadOnlyList<object?>)filter.Value!;for(var item=0;item<items.Count;item++)if(items[item] is {} memberValue)Add(command,"filter_"+i+"_"+item,memberValue);continue;}if(filter.Value is null||filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)continue;var value=filter.Comparison is FixedReadComparison.Contains or FixedReadComparison.JsonArrayAnyContains?"%"+((string)filter.Value).Replace("!","!!",StringComparison.Ordinal).Replace("%","!%",StringComparison.Ordinal).Replace("_","!_",StringComparison.Ordinal)+"%":filter.Value;Add(command,"filter_"+i,value);}
    }
    /// <summary>无项目列来源必须绑定发布目录身份，不能靠请求参数取得另一项目的全局数据。</summary>
    private static string RequirePlanProject(MySqlNonRedundantReadPlan plan,string? project)
    {var value=ValidateProject(project??plan.ProjectId);if(plan.RequiresPublishedProjectIdentity&&value!=plan.ProjectId)throw new UnauthorizedAccessException("当前身份不属于已发布全局来源目录。");return value;}
    /// <summary>执行项目来自已认证Actor；原项目来源保留既有类型校验，不授予页面权限。</summary>
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
