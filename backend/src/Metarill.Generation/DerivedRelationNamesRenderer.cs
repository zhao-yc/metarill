using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>派生名称的候选、导入、导出共用固定目标计划和原范围验证，不建立另一套授权。</summary>
internal static class DerivedRelationNamesRenderer
{
    /// <summary>公共候选使用原来源/目标页面授权；此处只绑定服务端身份、真实父及已声明表单值。</summary>
    internal static string Service(GenerationModel model,TableModel owner,PageModel page,RelationModel relation,bool visible)
    {
        var dependencies=DerivedRelationNamesCompilation.Require(model,owner,page,relation);var suffix=page.Source.Id+"_"+relation.SourceColumn.Source.Id;var name=relation.NameColumn.Source.ColumnName;
        var filters=string.Join("\n",relation.TargetPage.Scopes.Concat(relation.Scopes).DistinctBy(scope=>scope.Column.Source.Id).Select(scope=>"        filters.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+ScopeValue(scope.Column,scope.Source=="project"?"actor.ProjectId":"targetParentId!")+"));"));
        var same=relation.SourceParents.Count>0&&relation.SourceParents.Select(parent=>parent.Source.Id).Order().SequenceEqual(relation.TargetParents.Select(parent=>parent.Source.Id).Order());
        var variables=relation.Conditions is not{} condition?"":string.Join(",",condition.Syntax.Variables.Select(variable=>"["+Literal(variable)+"]="+(variable.StartsWith("formParameters.",StringComparison.Ordinal)?"Metarill.Core.Contracts.FixedConditionFormValue.Require(formValues,"+Literal(variable)+")":variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"actor.ProjectId":"targetParentId")));
        var bind=relation.Conditions is null?"":"        var bound=OptionsCondition"+suffix+".BindVariables(new Dictionary<string,object?>{"+variables+"});condition=condition is null?bound:condition.And(bound);";
        var result=new StringBuilder($$"""
    /// <summary>生成和静态装载时编译，普通请求不解析元数据；名称联接与物理身份一同固定。</summary>
    private static readonly NonRedundantReadPlan DerivedOptionsPlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.CompileRelation({{Literal(relation.Target.Source.Id)}},new string[]{"id",{{Literal(name)}}},new string[]{ {{string.Join(",",dependencies.Select(Literal))}} });
    /// <summary>先复用原真实父检查，再把同一页面及控件条件AND绑定到固定SQL读取。</summary>
    private async Task<(List<FixedReadFilter> Filters,FixedConditionSqlPlan? Condition)> DerivedOptionsScope{{suffix}}Async(string? parentId,string? targetParentId,RuntimeActor actor,CancellationToken cancellationToken,IReadOnlyDictionary<string,object?>? formValues=null)
    {
        _=await OptionsScope{{suffix}}Async(parentId,targetParentId,actor,cancellationToken,false,false{{(RelationConditionsCompilation.HasForms(relation)?",formValues:formValues":"")}});
{{(same?"        targetParentId??=parentId;":"")}}
        var filters=new List<FixedReadFilter>();
{{filters}}
        FixedConditionSqlPlan? condition={{(relation.TargetPage.Condition is null?"null":"OptionsPageCondition"+suffix)}};
{{bind}}
        return(filters,condition);
    }
    /// <summary>已选和导出按有限批次读取，缓存键仍由调用方的真实父与表单依赖隔离。</summary>
    private async Task<IReadOnlyList<GeneratedOption>> DerivedOptionsRows{{suffix}}Async(string? parentId,string? targetParentId,RuntimeActor actor,IReadOnlyList<long> ids,CancellationToken cancellationToken,IReadOnlyDictionary<string,object?>? formValues=null)
    {
        var scope=await DerivedOptionsScope{{suffix}}Async(parentId,targetParentId,actor,cancellationToken,formValues);var result=new List<GeneratedOption>();
        foreach(var batch in ids.Distinct().Chunk(200))
        {
            var filters=scope.Filters.Append(new FixedReadFilter("id",FixedReadComparison.In,batch.Select(id=>(object?)id).ToArray())).ToArray();
            var rows=await new NonRedundantReader(session).RowsAsync(DerivedOptionsPlan{{suffix}},filters,[new({{Literal(name)}}),new("id")],200,cancellationToken,actor.ProjectId,scope.Condition);
            result.AddRange(rows.Select(row=>new GeneratedOption(NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(name)}}),NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(name)}}))));
        }
        return result;
    }

""");
        if(!visible)return result.ToString();
        var form=RelationConditionsCompilation.HasForms(relation)?"ReadOptionsForm"+suffix+"(request.FormParameters)":"null";
        result.Append($$"""
    /// <summary>保留旧数组5000项上限，使用同一固定名称排序，不静默截断。</summary>
    public Task<IReadOnlyList<GeneratedOption>> Options{{suffix}}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken,string? targetParentId=null)=>session.InReadSnapshotAsync<IReadOnlyList<GeneratedOption>>(async()=>
    {
        ArgumentNullException.ThrowIfNull(request);var scope=await DerivedOptionsScope{{suffix}}Async(request.ParentId,targetParentId,actor,cancellationToken,{{form}});
        var rows=await new NonRedundantReader(session).RowsAsync(DerivedOptionsPlan{{suffix}},scope.Filters,[new({{Literal(name)}}),new("id")],5001,cancellationToken,actor.ProjectId,scope.Condition);
        if(rows.Count>5000)throw new InvalidOperationException("关联候选超过5000条，请使用分页查询。");
        return rows.Select(row=>new GeneratedOption(NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(name)}}),NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(name)}}))).ToArray();
    },cancellationToken);
    /// <summary>名称关键词先在SQL中筛选和排序，再按同一快照COUNT与分页。</summary>
    public Task<PageResult<GeneratedOption>> OptionsPage{{suffix}}Async(RelationOptionsPageRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>session.InReadSnapshotAsync<PageResult<GeneratedOption>>(async()=>
    {
        ArgumentNullException.ThrowIfNull(request);request.Validate();var scope=await DerivedOptionsScope{{suffix}}Async(request.ParentId,request.TargetParentId,actor,cancellationToken,{{form}});
        if(!string.IsNullOrEmpty(request.Keyword))scope.Filters.Add(new({{Literal(name)}},FixedReadComparison.Contains,request.Keyword));
        var rows=await new NonRedundantReader(session).PageAsync(DerivedOptionsPlan{{suffix}},scope.Filters,[new({{Literal(name)}}),new("id")],request.PageNo,request.PageSize,cancellationToken,projectId:actor.ProjectId,condition:scope.Condition);
        var options=rows.Records.Select(row=>new GeneratedOption(NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(name)}}),NonRedundantReads.Value<long>(row,"id"),NonRedundantReads.Value<string?>(row,{{Literal(name)}}))).ToArray();
        return new(options,rows.Total,(rows.Total+request.PageSize-1)/request.PageSize,request.PageNo,request.PageSize);
    },cancellationToken);
    /// <summary>已选回读不使用关键词，逐个核对实际候选范围，缺项整体拒绝。</summary>
    public Task<IReadOnlyList<GeneratedOption>> OptionsSelected{{suffix}}Async(RelationOptionsSelectedRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>session.InReadSnapshotAsync<IReadOnlyList<GeneratedOption>>(async()=>
    {
        ArgumentNullException.ThrowIfNull(request);var selected=request.ParseIds();var rows=await DerivedOptionsRows{{suffix}}Async(request.ParentId,request.TargetParentId,actor,selected,cancellationToken,{{form}});
        if(rows.Count!=selected.Length)throw new InvalidOperationException("关联已选项不存在、已删除或不在当前可选范围。");return rows;
    },cancellationToken);

""");return result.ToString();
    }
    /// <summary>Excel中文名称或数值ID使用同一范围的当前读，零个或重名均拒绝整批。</summary>
    internal static string Import(RelationModel relation,PageModel page,ImportDependencyPlan dependency)
    {
        var suffix=page.Source.Id+"_"+relation.SourceColumn.Source.Id;var mapped=dependency.Parents.GetValueOrDefault(relation.SourceColumn.Source.Id);
        return $$"""
    /// <summary>名称只由已发布计划解析，调用方的原导入事务负责整批回滚。</summary>
    private async Task<JsonElement> ImportRelation{{suffix}}Async(JsonElement cell,string? parentId,RuntimeActor actor{{(dependency.Contextual?",IReadOnlyDictionary<string,JsonElement> relationContext":"")}},CancellationToken cancellationToken)
    {
        if(cell.ValueKind==JsonValueKind.Null||string.IsNullOrWhiteSpace(cell.ToString()))return JsonSerializer.SerializeToElement<object?>(null);
{{(mapped is null?"":"        if(!relationContext.TryGetValue("+Literal(mapped.JsonName)+",out var targetParent)||targetParent.ValueKind==JsonValueKind.Null||string.IsNullOrWhiteSpace(targetParent.ToString()))throw PageImportException.Input(PageImportError.Relation);")}}
        var scope=await DerivedOptionsScope{{suffix}}Async(parentId,{{(mapped is null?"null":"targetParent.ToString()")}},actor,cancellationToken{{RelationConditionsCompilation.ImportArgument(relation)}});
        if(cell.ValueKind==JsonValueKind.Number)
        {
            long id;try{id=cell.GetInt64();}catch(Exception error)when(error is FormatException or OverflowException){throw PageImportException.Input(PageImportError.Number,inner:error);}
            scope.Filters.Add(new("id",FixedReadComparison.Equal,id));
        }
        else scope.Filters.Add(new({{Literal(relation.NameColumn.Source.ColumnName)}},FixedReadComparison.Equal,cell.ToString().Trim()));
        var rows=await new NonRedundantReader(session).PageAsync(DerivedOptionsPlan{{suffix}},scope.Filters,[new("id")],1,2,cancellationToken,projectId:actor.ProjectId,condition:scope.Condition,currentRead:true);
        if(rows.Records.Count!=1)throw PageImportException.Input(PageImportError.Relation);
        return JsonSerializer.SerializeToElement(NonRedundantReads.Value<long>(rows.Records[0],"id").ToString(CultureInfo.InvariantCulture));
    }

""";
    }
    /// <summary>生成变量按真实字段类型绑定，不能改变已编译的字段或操作符。</summary>
    private static string ScopeValue(ColumnModel column,string value)=>column.Type is "long?" or "long"?$"long.Parse({value},CultureInfo.InvariantCulture)":column.Type is "int?" or "int"?$"int.Parse({value},CultureInfo.InvariantCulture)":value;
    /// <summary>生成常量统一JSON转义，不拼接客户端SQL。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
