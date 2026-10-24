using System.Text.Json;
namespace Metarill.Generation;

/// <summary>派生自树所有名称用途共用固定投影及原范围，根空值沿既有IN含null合同。</summary>
internal static class DerivedCascadeNamesRenderer
{
    /// <summary>编译一次目标名称／父结构，普通及当前读不执行COUNT，不接受外部字段或SQL。</summary>
    internal static string Reader(GenerationModel model,TableModel table,PageModel page,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+plan.Source.Source.Id;var dependencies=DerivedCascadeNamesCompilation.Require(model,table,plan);var scopes=plan.TargetPage.Scopes.Select(scope=>"        bound.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+(scope.Column.Type=="string?"?"actor.ProjectId":"long.Parse(actor.ProjectId,CultureInfo.InvariantCulture)")+"));");
        var parent=plan.Parent;var nodeParent=parent.Type=="string?"?"NonRedundantReads.Value<string?>(row,"+Literal(parent.Source.ColumnName)+")":"NonRedundantReads.Value<long?>(row,"+Literal(parent.Source.ColumnName)+")?.ToString(CultureInfo.InvariantCulture)";
        var childParent=parent.Type=="string?"?"parentId.Value.ToString(CultureInfo.InvariantCulture)":"(object)parentId.Value";var root=Root(plan);var condition=plan.TargetPage.Condition is null?"null":"CascadeCondition"+suffix;
        var parents=parent.Type=="string?"?"var keys=ids.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray();var rows=await CascadeScope"+suffix+"(actor).Where(row=>keys.Contains(row."+parent.Property+"!)).Distinct().ToListAsync(row=>row."+parent.Property+",ct);return rows.Select(text=>long.Parse(text!,CultureInfo.InvariantCulture)).ToHashSet();":"var keys=ids.ToArray();var rows=await CascadeScope"+suffix+"(actor).Where(row=>row."+parent.Property+"!=null&&keys.Contains(row."+parent.Property+".Value)).Distinct().ToListAsync(row=>row."+parent.Property+",ct);return rows.Select(value=>value!.Value).ToHashSet();";
        return $$"""
    /// <summary>目标三列与依赖只从发布定义编译，不读取忽略持久化的实体名称。</summary>
    private static readonly NonRedundantReadPlan DerivedCascadePlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.CompileRelation({{Literal(plan.Target.Source.Id)}},new string[]{"id",{{Literal(plan.Name.Source.ColumnName)}},{{Literal(parent.Source.ColumnName)}}},new string[]{ {{string.Join(",",dependencies.Select(Literal))}} });
    private static readonly FixedReadOrder[] DerivedCascadeOrder{{suffix}}=[{{Orders(plan)}}];
    /// <summary>授权与固定条件沿原树范围；当前读要求真实事务并锁住根及名称联接。</summary>
    private async Task<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeNode>> DerivedCascadeRows{{suffix}}Async(RuntimeActor actor,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,bool locking,CancellationToken ct,bool requireSourceList=false,bool requireTargetList=false)
    {
        _=CascadeScope{{suffix}}(actor,requireSourceList,requireTargetList);var bound=new List<FixedReadFilter>(filters);
{{string.Join("\n",scopes)}}
        var reader=new NonRedundantReader(session);var rows=locking?await reader.CurrentRowsAsync(DerivedCascadePlan{{suffix}},bound,order,limit,ct,actor.ProjectId,{{condition}}):await reader.RowsAsync(DerivedCascadePlan{{suffix}},bound,order,limit,ct,actor.ProjectId,{{condition}});
        return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(plan.Name.Source.ColumnName)}}),{{nodeParent}})).ToArray();
    }
    /// <summary>候选按派生名称在SQL排序，根同时匹配null与空字符串；父链逐跳仍走原算法。</summary>
    private Metarill.Core.Cascades.SelfTreeReader CascadeReader{{suffix}}(RuntimeActor actor,bool locking=false,bool requireSourceList=true,bool requireTargetList=true)=>new(
        async(id,ct)=>(await DerivedCascadeRows{{suffix}}Async(actor,[new("id",FixedReadComparison.Equal,id)],[new("id")],1,locking,ct,requireSourceList,requireTargetList)).SingleOrDefault(),
        async(parentId,limit,ct)=>await DerivedCascadeRows{{suffix}}Async(actor,[parentId is null?{{root}}:new({{Literal(parent.Source.ColumnName)}},FixedReadComparison.Equal,{{childParent}})],DerivedCascadeOrder{{suffix}},limit,locking,ct,requireSourceList,requireTargetList),
        async(ids,ct)=>{ {{parents}} },{{Literal(plan.Joiner)}});

""";
    }
    /// <summary>图批量路径和后代筛选共享同一读计划，分页前按原图算法恢复可见祖先。</summary>
    internal static string Graph(PageModel page,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+plan.Source.Source.Id;var parents=plan.Parent.Type=="string?"?"parents.Select(id=>(object?)id.ToString(CultureInfo.InvariantCulture)).ToArray()":"parents.Select(id=>(object?)id).ToArray()";
        return $$"""
    /// <summary>已授权来源内部图不额外授予候选读取，按固定批次读取和稳定ID游标遍历。</summary>
    private Metarill.Core.Cascades.CascadeTreeGraphReader CascadeGraph{{suffix}}(RuntimeActor actor,bool currentRead=false)=>new(CascadeReader{{suffix}}(actor,currentRead,false,false),
        async(ids,ct)=>await DerivedCascadeRows{{suffix}}Async(actor,[new("id",FixedReadComparison.In,ids.Select(id=>(object?)id).ToArray())],[new("id")],ids.Count,currentRead,ct),
        async(parents,after,take,ct)=>await DerivedCascadeRows{{suffix}}Async(actor,[new({{Literal(plan.Parent.Source.ColumnName)}},FixedReadComparison.In,{{parents}}),new("id",FixedReadComparison.Greater,after)],[new("id")],take,currentRead,ct),{{Literal(plan.Joiner)}});

""";
    }
    /// <summary>完整路径逐层当前读精确反查，任何歧义或断链交由原导入事务整批回滚。</summary>
    internal static string Import(PageModel page,ColumnModel source,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+source.Source.Id;var parent=plan.Parent.Type=="string?"?"parentId.Value.ToString(CultureInfo.InvariantCulture)":"(object)parentId.Value";
        return $$"""
    /// <summary>名称与原父范围同一固定SQL内匹配，不退回IsIgnore属性或宽表查询。</summary>
    private async Task<JsonElement> ImportCascade{{suffix}}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)
    {
        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);
        if(cell.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))throw PageImportException.Input(PageImportError.Relation);
        if(session.CurrentTransaction is null)throw new InvalidOperationException("级联导入要求来源事务。");
        var id=await Metarill.Core.Cascades.CascadeTreeExcelPath.ResolveAsync(cell.ToString(),{{Literal(plan.Joiner)}},async(text,parentId,ct)=>await DerivedCascadeRows{{suffix}}Async(actor,[new({{Literal(plan.Name.Source.ColumnName)}},FixedReadComparison.Equal,text),parentId is null?{{Root(plan)}}:new({{Literal(plan.Parent.Source.ColumnName)}},FixedReadComparison.Equal,{{parent}})],[new("id")],2,true,ct),cancellationToken);
        return JsonSerializer.SerializeToElement({{(source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"(long?)id")}},JsonOptions);
    }

""";
    }
    /// <summary>复用已支持的IN空成员语义，保持字符串空根与整数null根，不新增操作符。</summary>
    private static string Root(CascadeTreeReadModel plan)=>"new("+Literal(plan.Parent.Source.ColumnName)+","+(plan.Parent.Type=="string?"?"FixedReadComparison.In,new object?[]{null,"+Literal("")+"}":"FixedReadComparison.IsNull")+")";
    /// <summary>排序字段及方向来自生成期证明，不能由请求扩展。</summary>
    private static string Orders(CascadeTreeReadModel plan)=>string.Join(",",plan.Order.Select(item=>"new("+Literal(item.Column.Source.ColumnName)+","+(item.Descending?"true":"false")+")"));
    /// <summary>配置文本先序列化成C#字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
