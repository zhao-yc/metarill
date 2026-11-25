using System.Data.Common;

namespace Metarill.Data.Queries;

public enum FixedReadComparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Contains, IsNull, IsNotNull, In, JsonArrayAnyContains, JsonArrayHasId, LikePattern, IntegerLabelContains, TypedLabelContains, JsonArrayHasAnyId }
public sealed record FixedReadFilter(string Field,FixedReadComparison Comparison,object? Value=null)
{
    internal bool SharedExact {get;private init;}
    internal bool ExactParentText {get;private init;}
    internal bool ExactTextTree {get;private init;}

    /// <summary>已由文本自树验证的节点集合按原文本精确筛选。</summary>
    public static FixedReadFilter ForTextTree(string field,IReadOnlyList<string> keys)=>
        new(field,FixedReadComparison.In,keys){ExactTextTree=true};

    /// <summary>文本自树的根兼容原空白表示，不参与按业务键匹配。</summary>
    public static FixedReadFilter ForTextTreeRoot(string field)=>
        new(field,FixedReadComparison.IsNull){ExactTextTree=true};

    /// <summary>已发布的文本父键比较规范十进制字节，数据库排序规则不能改变父记录身份。</summary>
    public static FixedReadFilter ForTextParent(string field,string parentId)=>
        new(field,FixedReadComparison.Equal,RequireTextParent(parentId)){ExactParentText=true};

    internal static string RequireTextParent(string value)=>
        long.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var id)&&
        id>0&&value==id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?value:throw new ArgumentException("当前多选页要求规范正整数父ID。");
    /// <summary>服务端共享精确范围必须验证实际完整路径；普通请求筛选继续原逻辑。</summary>
    public static FixedReadFilter ForSharedExact(string field,IReadOnlyList<object?> values)=>
        ForCrossExact(field,values);
    /// <summary>固定精确范围从已证明完整逻辑路径匹配，禁止使用原始稀疏值。</summary>
    public static FixedReadFilter ForCrossExact(string field,IReadOnlyList<object?> values)=>
        new(field,FixedReadComparison.In,values){SharedExact=true};
}
public sealed record FixedReadOrder(string Field,bool Descending=false);
public sealed record FixedReadPage(long Total,IReadOnlyList<IReadOnlyDictionary<string,object?>> Records);
public sealed record FixedReadStatistics(long Total,IReadOnlyDictionary<string,decimal?> Sums)
{
    /// <summary>浮点统计独立保存，不把整数／decimal合计经double中转。</summary>
    public IReadOnlyDictionary<string,double?> DoubleSums {get;init;}=new System.Collections.ObjectModel.ReadOnlyDictionary<string,double?>(new Dictionary<string,double?>());
}

/// <summary>执行固定单值及多选读计划；调用方仍须先建立原页面授权和父对象读取范围。</summary>
public sealed class MySqlNonRedundantReader(DatabaseSession session)
{
    private const string InvalidCrossStorage = "跨表稀疏存储含歧义或无效ID，未返回不完整逻辑路径。";
    // 名称数组也是原Excel的纯文本单元格；保持中文与标点可读，对外HTTP仍由宿主JSON编码。
    private static readonly System.Text.Json.JsonSerializerOptions MultiNamesJson=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    /// <summary>相同SQL范围用于总数与分页，派生值在数据库内先筛选、排序再分页。</summary>
    public async Task<FixedReadPage> ReadPageAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken cancellationToken=default,bool unpaged=false,string? projectId=null,FixedConditionSqlPlan? condition=null,bool currentRead=false)
        => await ReadAsync(plan,filters,order,page,pageSize,cancellationToken,unpaged,projectId,true,condition,currentRead);
    /// <summary>详情及已选回读由主键集合限定，逐行保留原分页的全部跨表存储校验。</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> ReadKeyRowsAsync(
        MySqlNonRedundantReadPlan plan, IReadOnlyList<FixedReadFilter> filters, IReadOnlyList<long> keys,
        CancellationToken cancellationToken=default, string? projectId=null, FixedConditionSqlPlan? condition=null, bool currentRead=false)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(keys));
        var keyFilter = keys.Count == 1
            ? new FixedReadFilter("id", FixedReadComparison.Equal, keys[0])
            : new FixedReadFilter("id", FixedReadComparison.In, keys.Cast<object?>().ToArray());
        var rows = await ReadAsync(plan, filters.Append(keyFilter).ToArray(), [], 1, keys.Count, cancellationToken,
            false, projectId, false, condition, currentRead, validateAllCrosses: true);
        return rows.Records;
    }

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
    /// <summary>覆盖导入最多锁读两条。根行从首次匹配就持有排他锁，避免随后升级共享锁。</summary>
    internal async Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> ReadImportMatchesAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        CancellationToken cancellationToken=default,string? projectId=null,FixedConditionSqlPlan? condition=null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if(plan.HasMaintenanceJoinProjection||plan.ImportMatchLock is null)throw new InvalidOperationException("覆盖匹配要求完整联接计划。");
        return (await ReadAsync(plan,filters,[],1,2,cancellationToken,false,projectId,false,condition,true,plan.ImportMatchLock)).Records;
    }
    /// <summary>独立排序只用于快照候选，事务当前读沿用原查询及锁范围。</summary>
    internal Task<FixedReadPage> ReadOrderedAsync(MySqlNonRedundantReadPlan plan, MySqlNonRedundantReadPlan sortPlan,
        IReadOnlyList<FixedReadFilter> filters, IReadOnlyList<FixedReadOrder> order, int page, int pageSize,
        CancellationToken token, string? projectId, bool includeTotal, FixedConditionSqlPlan? condition) =>
        ReadAsync(plan,filters,order,page,pageSize,token,false,projectId,includeTotal,condition,sortPlan:sortPlan);

    private async Task<FixedReadPage> ReadAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken cancellationToken,bool unpaged,string? projectId,bool includeTotal,FixedConditionSqlPlan? condition,bool currentRead=false,string? rowLock=null,bool validateAllCrosses=false,MySqlNonRedundantReadPlan? sortPlan=null)
    {
        if(currentRead&&session.CurrentTransaction is null)throw new InvalidOperationException("当前读必须加入真实业务事务。");
        if(session.Provider.Name!="mysql")throw new NotSupportedException("固定读取计划与数据库方言不一致。");
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(filters);ArgumentNullException.ThrowIfNull(order);
        if(page<1||pageSize<0||pageSize==0&&!unpaged||unpaged&&(page!=1||pageSize!=0))throw new ArgumentOutOfRangeException(nameof(page));
        projectId=RequirePlanProject(plan,projectId);
        // 所有字段/操作/值先验证，不能在发出COUNT后才发现排序非法。
        filters=filters.Select(filter=>Prepare(plan,filter)).ToArray();order=order.ToArray();
        RequireMultiParentFilter(plan,filters);
        var clauses=filters.Select((filter,index)=>Filter(plan,filter,index)).ToArray();
        var parentClause = CandidateParentClause(plan,projectId,currentRead);
        const string sortAlias = "metarill_candidate_order";
        var sortFields = sortPlan?.Outputs.Where(field => !field.Equals("id",StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortJoin = sortPlan is null ? null : CandidateSortJoin(plan,sortPlan,sortAlias,projectId);
        var orders=order.Select(item=>(sortFields?.Contains(item.Field)==true
            ? Q(sortAlias)+"."+Q(item.Field)
            : plan.Expression(item.Field))+(item.Descending?" DESC":" ASC")).ToList();
        if(!order.Any(item=>item.Field.Equals("id",StringComparison.OrdinalIgnoreCase)))orders.Add(plan.Expression("id")+" DESC");
        // 联接依赖同时包含响应、筛选、排序和固定条件；条件不能因未显示而丢失目标范围。
        // 普通快照读沿用聚合的单行联接证明。当前读仍按原计划取得锁。
        var readJoins = currentRead ? null : plan.AggregateFrom;
        var required = plan.HasMaintenanceJoinProjection || readJoins is not null
            ? plan.Outputs.Concat(filters.Select(filter => filter.Field)).Concat(order.Where(item => sortFields?.Contains(item.Field)!=true).Select(item => item.Field))
                .Append("id").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var aggregateFields = includeTotal && readJoins is not null
            ? filters.Select(filter => filter.Field).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var conditionClause = condition?.RenderConstants(session.Provider, "page_condition", field =>
        {
            required?.Add(field);
            aggregateFields?.Add(field);
            return plan.Expression(field);
        });
        var where=string.Join(" AND ",new[]{plan.Scope,conditionClause?.Sql??"",parentClause?.Sql??""}.Concat(clauses).Where(part=>part.Length>0));
        var whereSql = where.Length == 0 ? "" : " WHERE " + where;
        var baseFrom = required is null ? plan.From : readJoins?.Render(required) ?? plan.FromForFields(required);
        var from = baseFrom + sortJoin?.Sql + whereSql;
        // 排序单值通过主键左联接，只影响明细的顺序，原计数沿既有条件和投影。
        var countFrom = (aggregateFields is null ? baseFrom : readJoins!.Render(aggregateFields)) + whereSql;
        var offset=checked(((long)page-1)*pageSize);
        return await OnConnection(async connection=>
        {
            var total=0L;
            if(includeTotal){await using var count=Command(connection);Bind(count,projectId,filters);BindCross(count,plan);BindCondition(count,conditionClause);BindCondition(count,parentClause);count.CommandText=LockNestedReads("SELECT COUNT(*)"+(plan.Crosses.Count==0?"":","+CrossValidity(plan))+countFrom+(currentRead?" FOR SHARE":""),currentRead);if(plan.Crosses.Count==0)total=Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken),System.Globalization.CultureInfo.InvariantCulture);else{await using var counted=await count.ExecuteReaderAsync(cancellationToken);if(!await counted.ReadAsync(cancellationToken))throw new InvalidOperationException("跨表计数未返回结果。");RequireValidCross(counted,1);total=counted.GetInt64(0);}}
            await using var read=Command(connection);Bind(read,projectId,filters);BindCross(read,plan);BindCondition(read,conditionClause);BindCondition(read,parentClause);BindCondition(read,sortJoin);if(!unpaged){Add(read,"page_limit",pageSize);Add(read,"page_offset",offset);}
            // 普通投影分页后批量补名；只有筛选/排序名称数组时使用预编译表达式，不按每行展开根查询。
            var multis=plan.Outputs.Where(name=>!plan.StorageOutputs.ContainsKey(name)&&plan.MultiProjections.ContainsKey(name)).ToArray();
            var hidden=new Dictionary<string,(string Source,string[] Owners)>(StringComparer.Ordinal);
            var projection=plan.Outputs.Select(name=>(!plan.StorageOutputs.ContainsKey(name)&&plan.MultiProjections.ContainsKey(name)?"NULL":plan.OutputExpression(name))+" AS "+Q(name)).ToList();
            var ordinal=0;
            var used=plan.Outputs.Concat(plan.RuleOutputAliases.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var name in multis)
            {
                var multi=plan.MultiProjections[name];
                var source=Input(multi.SourceExpression,"metarill_multi_input_");
                var owners=multi.Owner is { } owner?owner.Fields.Select(field=>Input(owner.SourceExpressions?.GetValueOrDefault(field)??Q(owner.Alias)+"."+Q(field),"metarill_multi_owner_")).ToArray():[];
                hidden.Add(name,(source,owners));
            }

            string Input(string expression,string prefix)
            {
                string alias;
                do{alias=prefix+ordinal++;}while(!used.Add(alias));
                projection.Add(expression+" AS "+Q(alias));
                return alias;
            }

            var crossInputs=new List<(MySqlCrossTableProjection Plan,Dictionary<string,string> Fields)>();var crossValidityColumns=new List<string>();
            foreach(var rule in plan.RuleOutputAliases)projection.Add(plan.Expression(rule.Key)+" AS "+Q(rule.Value));
            foreach(var cross in plan.Crosses)
            {
                if (cross.RequireRowValidity || validateAllCrosses)
                {
                    string alias;
                    do { alias = "metarill_cross_valid_" + ordinal++; } while (!used.Add(alias));
                    crossValidityColumns.Add(alias);
                    projection.Add("CASE WHEN (" + cross.Validity + ") THEN 1 ELSE 0 END AS " + Q(alias));
                }
                var fields=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var pair in cross.RawSlots){string alias;do{alias="metarill_cross_input_"+ordinal++;}while(!used.Add(alias));fields.Add(pair.Key,alias);projection.Add(pair.Value+" AS "+Q(alias));}crossInputs.Add((cross,fields));
            }
            read.CommandText=LockNestedReads("SELECT "+string.Join(",",projection)+from+" ORDER BY "+string.Join(",",orders)+(unpaged?"":" LIMIT @page_limit OFFSET @page_offset")+(rowLock??(currentRead?" FOR SHARE":"")),currentRead);
            var records=new List<Dictionary<string,object?>>();
            await using(var reader=await read.ExecuteReaderAsync(cancellationToken))
                while(await reader.ReadAsync(cancellationToken))
                {var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<reader.FieldCount;i++)row.Add(reader.GetName(i),await reader.IsDBNullAsync(i,cancellationToken)?null:reader.GetValue(i));records.Add(row);}
            foreach(var row in records)
            {
                // 主键读取覆盖匹配的全部行，可以在同一条SELECT校验原COUNT中的跨表条件。
                foreach (var alias in crossValidityColumns)
                {
                    if (Convert.ToInt64(row[alias], System.Globalization.CultureInfo.InvariantCulture) != 1)
                        throw new InvalidOperationException(validateAllCrosses ? InvalidCrossStorage : "共享跨表存储或实际祖先路径冲突，未返回数据。");
                    row.Remove(alias);
                }
                foreach(var input in crossInputs){_=input.Plan.Selection.ReadStored(input.Fields.ToDictionary(pair=>pair.Key,pair=>row[pair.Value],StringComparer.Ordinal));foreach(var alias in input.Fields.Values)row.Remove(alias);}
            }
            foreach(var name in multis)await PopulateMultiAsync(connection,plan,plan.MultiProjections[name],name,hidden[name].Source,hidden[name].Owners,records,projectId,currentRead,cancellationToken);
            return new FixedReadPage(total,records.Select(row=>(IReadOnlyDictionary<string,object?>)new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(row)).ToArray());
        },cancellationToken);
    }
    /// <summary>独立字典按同一候选主键左联接，参数另用命名空间，排序为空时原候选仍保留。</summary>
    private static FixedConditionSqlPlan.Clause CandidateSortJoin(MySqlNonRedundantReadPlan plan,
        MySqlNonRedundantReadPlan sortPlan,string alias,string projectId)
    {
        _=RequirePlanProject(sortPlan,projectId);
        if (!sortPlan.Outputs.Contains("id",StringComparer.OrdinalIgnoreCase) ||
            !plan.Origins.TryGetValue("id",out var origin) || sortPlan.Origins.GetValueOrDefault("id") != origin ||
            sortPlan.RequiredMultiParent is not null || sortPlan.CandidateParents.Count > 0)
            throw new InvalidOperationException("候选排序必须使用同一物理主键和固定单值联接。");

        var columns = string.Join(",",sortPlan.Outputs.Select(field => sortPlan.Expression(field)+" AS "+Q(field)));
        var where = sortPlan.Scope.Length == 0 ? "" : " WHERE "+sortPlan.Scope;
        var sql = " LEFT JOIN (SELECT "+columns+sortPlan.From+where+") AS "+Q(alias)+
            " ON "+Q(alias)+".id="+plan.Expression("id");
        const string prefix = "candidate_order_";
        var constants = sortPlan.Constants;
        // 编译器生成的常量全部参数化。一次替换完整参数名，项目参数沿原读命令绑定。
        sql = System.Text.RegularExpressions.Regex.Replace(sql,@"@([A-Za-z_][A-Za-z0-9_]*)",match =>
            constants.ContainsKey(match.Groups[1].Value) ? "@"+prefix+match.Groups[1].Value : match.Value,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return new(sql,constants.ToDictionary(pair => prefix+pair.Key,pair => pair.Value,StringComparer.Ordinal));
    }

    /// <summary>每个关系按页合并ID、分批主键读取名称；写回执使用目标当前锁读，不能被旧RR快照覆盖。</summary>
    private async Task PopulateMultiAsync(DbConnection connection,MySqlNonRedundantReadPlan plan,MySqlMultiProjection projection,
        string output,string hidden,string[] ownerColumns,IReadOnlyList<Dictionary<string,object?>> rows,
        string projectId,bool currentRead,CancellationToken token)
    {
        if(projection.Owner is null)
            await PopulateMultiGroupAsync(connection,plan,projection,output,hidden,rows,[],projectId,currentRead,token);
        else
        {
            // 同一个被选 ID 在不同来源归属下可能有不同名称。按完整归属分别当前读。
            // 表单条件的比较使用持久字段类型，不能把 bigint 等值退化成 MySQL 的浮点隐式比较。
            object?[] OwnerValues(Dictionary<string,object?> row)=>ownerColumns.Select(name=>
                row[name] is {} value && !projection.Owner.PreserveScalarTypes
                    ?Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture):row[name]).ToArray();
            var groups=rows.GroupBy(row=>System.Text.Json.JsonSerializer.Serialize(OwnerValues(row),MultiNamesJson),StringComparer.Ordinal);
            foreach(var group in groups)
                await PopulateMultiGroupAsync(connection,plan,projection,output,hidden,group.ToArray(),
                    OwnerValues(group.First()),projectId,currentRead,token);
        }
        foreach(var row in rows)
            foreach(var owner in ownerColumns)row.Remove(owner);
    }

    /// <summary>本批归属作为已锁来源值绑定，目标继续使用原批量主键和共享锁。</summary>
    private async Task PopulateMultiGroupAsync(DbConnection connection,MySqlNonRedundantReadPlan plan,MySqlMultiProjection projection,
        string output,string hidden,IReadOnlyList<Dictionary<string,object?>> rows,object?[] owners,
        string projectId,bool currentRead,CancellationToken token)
    {
        var ownerSql=projection.Owner is {} scope
            ?" CROSS JOIN (SELECT "+string.Join(",",scope.Fields.Select((field,index)=>"@multi_owner_"+index+" AS "+Q(field)))+") "+Q(scope.Alias)
            :"";
        var members=rows.Select(row=>MultiMembers(row[hidden])).ToArray();var ids=members.SelectMany(items=>items).Where(id=>id is not null).Select(id=>long.TryParse(id,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var key)&&key>0&&key.ToString(System.Globalization.CultureInfo.InvariantCulture)==id?(long?)key:null).OfType<long>().Distinct().Order().ToArray();
        var names=new Dictionary<string,string?>(StringComparer.Ordinal);
        foreach(var batch in ids.Chunk(200))
        {
            await using var command=Command(connection);Bind(command,projectId,[]);BindCross(command,plan);for(var index=0;index<batch.Length;index++)Add(command,"multi_id_"+index,batch[index]);
            for(var index=0;index<owners.Length;index++)Add(command,"multi_owner_"+index,owners[index] is {} value?value:DBNull.Value);
            command.CommandText=LockNestedReads("SELECT multi_value.`id`,"+(projection.NameExpression??"multi_value."+projection.TargetName)+" FROM "+projection.TargetTable+" multi_value"+ownerSql+projection.Joins+" WHERE "+(projection.TargetScope.Length==0?"":projection.TargetScope+" AND ")+"multi_value.`id` IN ("+string.Join(",",Enumerable.Range(0,batch.Length).Select(index=>"@multi_id_"+index))+") ORDER BY multi_value.`id`"+(currentRead?" FOR SHARE":""),currentRead);
            await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))names.Add(reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),await reader.IsDBNullAsync(1,token)?null:reader.GetString(1));
        }
        for(var index=0;index<rows.Count;index++){rows[index][output]=System.Text.Json.JsonSerializer.Serialize(members[index].Select(id=>id is null?null:names.GetValueOrDefault(id)).ToArray(),MultiNamesJson);rows[index].Remove(hidden);}
    }
    /// <summary>只读取历史数组的顺序和值，损坏或非数组保持原空集合显示，不修补源存储。</summary>
    private static string?[] MultiMembers(object? value)
    {
        if(value is null)return [];try{using var doc=System.Text.Json.JsonDocument.Parse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!);return doc.RootElement.ValueKind==System.Text.Json.JsonValueKind.Array?doc.RootElement.EnumerateArray().Select(item=>item.ValueKind is System.Text.Json.JsonValueKind.String?item.GetString():item.ValueKind==System.Text.Json.JsonValueKind.Number?item.GetRawText():null).ToArray():[];}catch(System.Text.Json.JsonException){return [];}
    }
    /// <summary>列表与数量共享实际父参数约束，禁止无父范围的读取。</summary>
    private static void RequireMultiParentFilter(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters)
    {
        if(plan.RequiredMultiParent is {} parent&&!filters.Any(filter=>filter.Field==parent&&
            filter.Comparison==FixedReadComparison.Equal&&filter.Value is not null))
            throw new InvalidOperationException("多值目标页读取必须绑定实际父ID。");
    }

    /// <summary>数值派生表达式在同一完整过滤范围内汇总，不先分页或对当前页记录求和。</summary>
    public async Task<FixedReadStatistics> ReadStatisticsAsync(MySqlNonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,
        IReadOnlyList<string> sumFields,CancellationToken cancellationToken=default,string? projectId=null,FixedConditionSqlPlan? condition=null)
    {
        if(session.Provider.Name!="mysql")throw new NotSupportedException("固定读取计划与数据库方言不一致。");
        ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(filters);ArgumentNullException.ThrowIfNull(sumFields);
        if(plan.CandidateParents.Count>0)throw new InvalidOperationException("候选父范围计划不能用于业务统计。");
        projectId=RequirePlanProject(plan,projectId);var fields=sumFields.ToArray();
        if(fields.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fields.Length)throw new InvalidOperationException("统计字段重复。");
        foreach(var field in fields){plan.Expression(field);if(plan.Types[field] is not("int32" or "int64" or "decimal" or "double"))throw new InvalidOperationException("求和字段必须是已编译数值。");}
        filters=filters.Select(filter=>Prepare(plan,filter)).ToArray();
        RequireMultiParentFilter(plan,filters);
        var aggregateFields = plan.AggregateFrom is null
            ? null
            : fields.Concat(filters.Select(filter => filter.Field)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conditionClause = condition?.RenderConstants(session.Provider, "page_condition", field =>
        {
            aggregateFields?.Add(field);
            return plan.Expression(field);
        });
        var where=string.Join(" AND ",new[]{plan.Scope,conditionClause?.Sql??""}.Concat(filters.Select((filter,index)=>Filter(plan,filter,index))).Where(part=>part.Length>0));
        var scope = (aggregateFields is null ? plan.From : plan.AggregateFrom!.Render(aggregateFields))
            + (where.Length == 0 ? "" : " WHERE " + where);
        if(fields.Any(field=>plan.Types[field]=="double"))
        {
            // 每个浮点字段最多2047桶；混合统计用一条UNION语句共享同一读视图，精确类型继续原SUM。
            var floatingFields=fields.Where(field=>plan.Types[field]=="double").ToArray();var exactFields=fields.Where(field=>plan.Types[field]!="double").ToArray();var needsBase=fields.Length!=1;
            string Projection(int index,string? field)
            {
                var prefix=field is null?"SELECT -1 AS field_no,NULL AS `Exponent`,NULL AS `Coefficient`,COUNT(*) AS `Rows`,0 AS `Values`":"SELECT "+index+" AS field_no,"+MySqlFloatingSum.Fields(plan.Expression(field));
                prefix+=string.Concat(exactFields.Select((name,ordinal)=>","+(field is null?"SUM("+plan.Expression(name)+")":"NULL")+" AS exact_"+ordinal));
                prefix+=","+(plan.Crosses.Count==0?"0":CrossValidity(plan))+" AS cross_invalid"+scope;
                return field is null?prefix:prefix+" GROUP BY "+MySqlFloatingSum.Exponent(plan.Expression(field))+" LIMIT "+(MySqlFloatingSum.MaximumBuckets+1);
            }
            var branches=floatingFields.Select((field,index)=>Projection(index,field)).ToArray();
            var bucketSql=needsBase?"SELECT * FROM ("+string.Join(" UNION ALL ",new[]{Projection(-1,null)}.Concat(branches).Select(branch=>"("+branch+")"))+") metarill_float_rows":branches[0];
            return await OnConnection(async connection=>
            {
                await using var command=Command(connection);Bind(command,projectId,filters);BindCross(command,plan);BindCondition(command,conditionClause);command.CommandText=bucketSql;
                await using var reader=await command.ExecuteReaderAsync(cancellationToken);var buckets=floatingFields.Select(_=>new List<FloatingSumRow>()).ToArray();var exact=new Dictionary<string,decimal?>(StringComparer.OrdinalIgnoreCase);long? total=null;var baseSeen=false;
                while(await reader.ReadAsync(cancellationToken))
                {
                    if(plan.Crosses.Count>0)RequireValidCross(reader,5+exactFields.Length);
                    var index=Convert.ToInt32(reader.GetValue(0),System.Globalization.CultureInfo.InvariantCulture);
                    if(index==-1)
                    {
                        if(!needsBase||baseSeen)throw new InvalidOperationException("浮点混合统计基础行无效。");baseSeen=true;total=reader.GetInt64(3);if(total<0)throw new InvalidOperationException("浮点统计总行数无效。");
                        for(var ordinal=0;ordinal<exactFields.Length;ordinal++)exact.Add(exactFields[ordinal],await reader.IsDBNullAsync(5+ordinal,cancellationToken)?null:Convert.ToDecimal(reader.GetValue(5+ordinal),System.Globalization.CultureInfo.InvariantCulture));
                        continue;
                    }
                    if(index<0||index>=floatingFields.Length||buckets[index].Count>=MySqlFloatingSum.MaximumBuckets)throw new InvalidOperationException("浮点统计字段或桶数量无效。");
                    buckets[index].Add(new(){Exponent=reader.IsDBNull(1)?null:Convert.ToInt32(reader.GetValue(1),System.Globalization.CultureInfo.InvariantCulture),Coefficient=reader.IsDBNull(2)?null:reader.GetString(2),Rows=reader.GetInt64(3),Values=reader.GetInt64(4)});
                }
                if(needsBase&&!baseSeen)throw new InvalidOperationException("浮点混合统计缺少基础行。");
                var doubles=new Dictionary<string,double?>(StringComparer.OrdinalIgnoreCase);
                for(var index=0;index<floatingFields.Length;index++)
                {
                    var value=MySqlFloatingSum.Reduce(buckets[index]);if(total is {} count&&count!=value.Rows)throw new InvalidOperationException("浮点统计字段未读取同一完整范围。");total??=value.Rows;doubles.Add(floatingFields[index],value.Value);
                }
                return new FixedReadStatistics(total??0,new System.Collections.ObjectModel.ReadOnlyDictionary<string,decimal?>(exact)){DoubleSums=new System.Collections.ObjectModel.ReadOnlyDictionary<string,double?>(doubles)};
            },cancellationToken);
        }
        var sql="SELECT COUNT(*) AS total"+string.Concat(fields.Select((field,index)=>",SUM("+plan.Expression(field)+") AS sum_"+index))+(plan.Crosses.Count==0?"":","+CrossValidity(plan))+scope;
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
            if(!plan.ArrayIdSources.ContainsKey(filter.Field)||text is null||!long.TryParse(text,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(System.Globalization.CultureInfo.InvariantCulture))throw new InvalidOperationException("数组成员筛选只接受已编译的复选字段和规范正整数ID。");
            return filter with{Value=text};
        }
        if (filter.Comparison == FixedReadComparison.JsonArrayHasAnyId)
        {
            if (!plan.ArrayIdSources.ContainsKey(filter.Field) || value is not long[] ids ||
                ids.Length is 0 or > Metarill.Core.Contracts.DatabaseCheckboxValue.MaximumItems || ids.Any(id => id <= 0))
                throw new InvalidOperationException("多值筛选必须使用已发布的数组列及有界正整数 ID 集合。");
            var selected = System.Text.Json.JsonSerializer.Serialize(ids.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return filter with { Value = selected };
        }
        var type = plan.Types[filter.Field];
        if(filter.ExactParentText&&(type!="string"||filter.Comparison!=FixedReadComparison.Equal||value is not string))
            throw new InvalidOperationException("文本父键筛选缺少实际的文本字段或值。");
        if(filter.ExactTextTree&&(type!="string"||filter.Comparison switch
        {
            FixedReadComparison.IsNull=>value is not null,
            FixedReadComparison.In=>value is not IReadOnlyList<object?> keys||keys.Any(key=>key is not string text||string.IsNullOrWhiteSpace(text)),
            _=>true
        }))
            throw new InvalidOperationException("文本自树筛选缺少有效的文本节点集合。");
        // 普通 JSON 文本与关系数组分别处理。精确比较沿用 MySQL 的 JSON 值比较。
        if (type == "json")
        {
            if (filter.Comparison is not (FixedReadComparison.LikePattern or FixedReadComparison.Equal or
                FixedReadComparison.NotEqual or FixedReadComparison.IsNull or FixedReadComparison.IsNotNull) ||
                value is not (null or string))
                throw new InvalidOperationException("JSON 筛选只支持文本模式、精确比较和空值判断。");
            return filter;
        }
        if(filter.Comparison==FixedReadComparison.In)
        {if(value is not IReadOnlyList<object?> items||filter.SharedExact&&items.Any(item=>item is null))throw new InvalidOperationException("集合筛选必须使用明确标量列表。");return filter with{Value=Array.AsReadOnly(items.Select(item=>Prepare(plan,new(filter.Field,FixedReadComparison.Equal,item)).Value).ToArray())};}
        if (filter.Comparison == FixedReadComparison.IntegerLabelContains)
        {
            if (type is not ("int32" or "int64") || value is not string)
                throw new InvalidOperationException("整数名称搜索需要固定整数列及文本关键词。");
            return filter;
        }
        if(filter.Comparison==FixedReadComparison.TypedLabelContains)
        {
            if(!Metarill.Core.Contracts.MultiValueLabel.IsTyped(type)||value is not string)
                throw new InvalidOperationException("标量名称搜索需要固定类型及文本关键词。");
            return filter;
        }
        if(value is null||filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)return filter;
        var culture=System.Globalization.CultureInfo.InvariantCulture;
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
            // 与普通字段、固定条件共用有限double规则，非零指数下溢不能变成匹配零值。
            "double" when value is string text&&Metarill.Core.Contracts.ExactNumericValue.TryDouble(text,out var real)=>real,
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
    /// <summary>校验完整操作与标量类型；Contains按字面转义，LikePattern保留原LIKE模式。</summary>
    private static string Filter(MySqlNonRedundantReadPlan plan,FixedReadFilter filter,int index)
    {
        var expression=filter.SharedExact?ExactDataManageSql.CrossExpression(plan,filter.Field):plan.Expression(filter.Field);var value=filter.Value;var parameter="@filter_"+index;
        if(filter.ExactTextTree)
        {
            if(filter.Comparison==FixedReadComparison.IsNull)return "("+expression+" IS NULL OR "+expression+" REGEXP '^[[:space:]]*$')";
            var keys=(IReadOnlyList<object?>)value!;
            return keys.Count==0?"1=0":"BINARY ("+expression+") IN ("+
                string.Join(",",Enumerable.Range(0,keys.Count).Select(item=>"BINARY "+parameter+"_"+item))+")";
        }
        if(filter.Comparison==FixedReadComparison.In)
        {var items=(IReadOnlyList<object?>)value!;var indexes=Enumerable.Range(0,items.Count).Where(item=>items[item] is not null).ToArray();var member=indexes.Length==0?"":expression+" IN ("+string.Join(",",indexes.Select(item=>parameter+"_"+item))+")";var hasNull=items.Any(item=>item is null);return member.Length==0?hasNull?expression+" IS NULL":"1=0":hasNull?"("+member+" OR "+expression+" IS NULL)":member;}
        if(value is not(null or string or byte[] or long or int or decimal or double or float or bool or DateTime or DateTimeOffset or TimeSpan))throw new InvalidOperationException("固定筛选值必须是受支持的标量。");
        if(filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)
        {if(value is not null)throw new InvalidOperationException("空值筛选不能同时带值。");return expression+(filter.Comparison==FixedReadComparison.IsNull?" IS NULL":" IS NOT NULL");}
        if (filter.Comparison is FixedReadComparison.JsonArrayHasId or FixedReadComparison.JsonArrayHasAnyId)
        {
            if (value is not string || !plan.ArrayIdSources.TryGetValue(filter.Field, out var array))
                throw new InvalidOperationException("数组成员筛选缺少固定字段与 ID。");
            return JsonArrayNameQuery.ArrayMemberSql(array, parameter, filter.Comparison == FixedReadComparison.JsonArrayHasAnyId);
        }
        if(filter.Comparison==FixedReadComparison.JsonArrayAnyContains)
        {
            if(value is not string||!plan.ArrayNamePredicates.TryGetValue(filter.Field,out var arrayPredicate))throw new InvalidOperationException("数组名称筛选只接受已编译多值关系与文本。");
            // 元素匹配不能退化为序列化JSON文本LIKE；根COUNT、分页及统计仍使用同一EXISTS。
            return arrayPredicate.Replace("{parameter}",parameter,StringComparison.Ordinal);
        }
        // 普通文本保留旧LIKE模式；Contains继续用于已确认的字面名称搜索，不能混用。
        if(filter.Comparison==FixedReadComparison.LikePattern)
        {if(value is not string||plan.Types[filter.Field] is not ("string" or "json"))throw new InvalidOperationException("原LIKE模式仅适用于文本或普通JSON字段。");return expression+" LIKE "+parameter;}
        if(filter.Comparison==FixedReadComparison.Contains)
        {if(value is not string||plan.Types[filter.Field]!="string")throw new InvalidOperationException("字面包含仅适用于文本字段。");return expression+" LIKE "+parameter+" ESCAPE '!'";}
        // 仅搜索时按十进制文本比较；排序、投影及精确匹配仍使用原整数。
        if (filter.Comparison == FixedReadComparison.IntegerLabelContains)
            return "CAST(" + expression + " AS CHAR) LIKE " + parameter + " ESCAPE '!'";
        if(filter.Comparison==FixedReadComparison.TypedLabelContains)
            return MySqlMultiValueLabel.Text(expression,plan.Types[filter.Field])+" LIKE "+parameter+" ESCAPE '!'";
        if(value is null)return filter.Comparison switch{FixedReadComparison.Equal=>expression+" IS NULL",FixedReadComparison.NotEqual=>expression+" IS NOT NULL",_=>throw new InvalidOperationException("范围比较不能使用null。")};
        if(filter.ExactParentText||filter.Field==plan.RequiredMultiParent&&filter.Comparison==FixedReadComparison.Equal&&value is string)
            return "BINARY ("+expression+")=BINARY "+parameter;
        return expression+(filter.Comparison switch{FixedReadComparison.Equal=>"=",FixedReadComparison.NotEqual=>"<>",FixedReadComparison.Greater=>">",FixedReadComparison.GreaterOrEqual=>">=",FixedReadComparison.Less=>"<",FixedReadComparison.LessOrEqual=>"<=",_=>throw new InvalidOperationException("未知固定筛选操作。")})+parameter;
    }
    /// <summary>同一已绑定父范围参与候选计数与明细；缺少真实账号时停止于创建命令之前。</summary>
    /// <summary>根记录锁不覆盖子查询，仅在事务当前读展开已声明的子查询锁。</summary>
    private static string LockNestedReads(string sql,bool currentRead)=>
        currentRead?MySqlNonRedundantReadCompiler.LockNestedReads(sql):sql;

    private FixedConditionSqlPlan.Clause? CandidateParentClause(MySqlNonRedundantReadPlan plan,string projectId,bool currentRead)
    {
        if(plan.CandidateParents.Count==0)return null;
        var actor=plan.CandidateActor??throw new UnauthorizedAccessException("固定候选父范围缺少认证账号。");
        if(actor.ProjectId!=projectId)throw new UnauthorizedAccessException("候选父范围与当前项目身份不一致。");
        return RedundantSharedParentScopeSql.Render(session.Provider,plan.CandidateParents,actor,
            "r1_filter",plan.Expression,currentRead);
    }

    /// <summary>COUNT与统计在同一原范围内检测坏稀疏存储，不隐藏错误行或增加逐行查询。</summary>
    private static string CrossValidity(MySqlNonRedundantReadPlan plan)=>"COALESCE(MAX(CASE WHEN "+string.Join(" AND ",plan.Crosses.Select(cross=>"("+cross.Validity+")"))+" THEN 0 ELSE 1 END),0)";
    /// <summary>多位、非规范或越界ID明确拒绝，不在读取时修补历史值。</summary>
    private static void RequireValidCross(DbDataReader reader,int ordinal){if(Convert.ToInt32(reader.GetValue(ordinal),System.Globalization.CultureInfo.InvariantCulture)!=0)throw new InvalidOperationException(InvalidCrossStorage);}
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
        {var filter=filters[i];if(filter.Comparison==FixedReadComparison.In){var items=(IReadOnlyList<object?>)filter.Value!;for(var item=0;item<items.Count;item++)if(items[item] is {} memberValue)Add(command,"filter_"+i+"_"+item,memberValue);continue;}if(filter.Value is null||filter.Comparison is FixedReadComparison.IsNull or FixedReadComparison.IsNotNull)continue;var value=filter.Comparison==FixedReadComparison.LikePattern?"%"+(string)filter.Value+"%":filter.Comparison is FixedReadComparison.Contains or FixedReadComparison.JsonArrayAnyContains or FixedReadComparison.IntegerLabelContains or FixedReadComparison.TypedLabelContains?"%"+((string)filter.Value).Replace("!","!!",StringComparison.Ordinal).Replace("%","!%",StringComparison.Ordinal).Replace("_","!_",StringComparison.Ordinal)+"%":filter.Value;Add(command,"filter_"+i,value);}
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
    private DbCommand Command(DbConnection connection){var command=MySqlFloatingCommand.Wrap(connection.CreateCommand());command.Transaction=session.CurrentTransaction;return command;}
    /// <summary>驱动参数承载值，不插入SQL文本。</summary>
    private static void Add(DbCommand command,string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);}
    /// <summary>输出别名也只能是已校验单段标识符。</summary>
    private static string Q(string name){Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(name);return "`"+name+"`";}
}
