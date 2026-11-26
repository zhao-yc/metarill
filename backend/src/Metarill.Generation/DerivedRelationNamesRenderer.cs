using System.Text;
using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>派生名称的候选、导入、导出复用已发布目标及原范围验证。</summary>
internal static class DerivedRelationNamesRenderer
{
    /// <summary>公共候选使用原来源/目标页面授权；此处只绑定服务端身份、真实父及已声明表单值。</summary>
    internal static string Service(GenerationModel model,TableModel owner,PageModel page,RelationModel relation,bool visible)
    {
        var dependencies=DerivedRelationNamesCompilation.Require(model,owner,page,relation);var suffix=page.Source.Id+"_"+relation.SourceColumn.Source.Id;var name=relation.NameColumn.Source.ColumnName;
        var exact = DerivedRelationNamesCompilation.CandidateExact(model, owner, relation);
        var exactFilters = exact is null ? "" : ExactDataManageCompilation.ReadFilter(relation.TargetPage, exact, Literal("list"), "targetExact", "filters");
        var accountParent = SharedRedundantParentCompilation.HasAccountPath(model,relation.Target,relation.TargetPage);
        var logical=DerivedRelationNamesCompilation.UsesLogicalPage(relation);
        var globalAccount=DerivedRelationNamesCompilation.HasGlobalAccountScope(relation.Target,relation.TargetPage);
        var validateCurrent=exact is not null||accountParent||logical||relation.CompoundProjectSource||relation.CompoundProjectTarget||
            relation.CanonicalProjectParent||relation.FixedGlobalSelection||FixedPageConditionCompilation.HasAccountVariables(relation.TargetPage);
        var projectTargets=DerivedRelationNamesCompilation.ProjectCandidateTargets(model,relation,exact);
        var bindParent=validateCurrent||projectTargets.Count>0?".BindAccount(actor)":"";
        var globalExact=DerivedRelationNamesCompilation.IsGlobalExactSingle(relation);
        var embeddedParent=DerivedRelationNamesCompilation.EmbedsParentScope(relation.Target,relation.FixedParent);
        var embedsPageScope=globalExact||embeddedParent||DerivedRelationNamesCompilation.UsesGlobalPrimaryKeyDisplay(relation)||
            relation.Target.IsGlobal&&(logical||globalAccount||relation.FunctionForm||
                NonRedundantReadCompilation.GlobalSingleParentPage(relation.Target,relation.TargetPage)||
                FixedPageConditionCompilation.HasGlobalProjectSingleScope(relation.Target,relation.TargetPage)||
                FixedPageConditionCompilation.HasGlobalListParentScope(relation.Target,relation.TargetPage));
        var parentPlan = globalExact?"":exact is not null && embeddedParent
            ? NonRedundantCrossTableCompilation.BindingArgument(exact.CrossPlans ?? [])
            : accountParent ? ",OptionsAccountParentScope"+suffix : "";
        var projection=DerivedRelationNamesCompilation.ProjectionCondition(relation);
        var conditionCaches=DerivedRelationNamesCompilation.StoredConditionCacheFields(relation);
        var planStart = globalExact
            ? $"CompileGlobalExactSingleCandidate({Literal(relation.Target.Source.Id)},{Literal(relation.TargetPage.Source.Id)}"
            : logical
            ? DerivedRelationNamesCompilation.LogicalPagePlanStart(model,relation.Target,relation.TargetPage,projection,exact)
            : DerivedRelationNamesCompilation.UsesGlobalPlan(model, relation)
            ? $"CompileGlobalCandidate({Literal(relation.Target.Source.Id)},{Literal(relation.TargetPage.Source.Id)}"
            : DerivedRelationNamesCompilation.UsesPhysicalPlan(model, relation)
                ? conditionCaches.Length>0
                    ? $"CompileStoredSingleRelation({Literal(relation.Target.Source.Id)},{Literal(relation.TargetPage.Source.Id)}"
                    : $"CompilePhysicalRelation({Literal(relation.Target.Source.Id)}"
                : projectTargets.Count>0
                ? $"CompileProjectRelation({Literal(relation.Target.Source.Id)}"
                : NonRedundantReadCompilation.RelationPlanStart(relation.Target, relation.TargetPage);
        var projectTargetArgument=NonRedundantReadCompilation.SingleTargetArgument(projectTargets,logical||globalExact?"singleTargets":null);
        var outputs = relation.NameColumn.IsId?"new string[]{\"id\"}":"new string[]{\"id\"," + Literal(name) + "}";
        var fields = "new string[]{ " + string.Join(",", dependencies.Select(Literal)) + " }";
        // 精确过滤只引入目标页匹配字段所在路径，共享分支仍由同一个整组计划验证。
        var additionalFields=exact is not null&&(globalExact||embeddedParent)&&projection is {HasLogicalFields:true}
            ?",listFields:new string[]{"+string.Join(",",projection.Syntax.Fields.Select(Literal))+"}":"";
        var cacheArgument=conditionCaches.Length==0?"":",new string[]{"+string.Join(",",conditionCaches.Select(Literal))+"}";
        var plan = $"{planStart},{outputs},{fields}{cacheArgument}{parentPlan}{projectTargetArgument}{additionalFields})";
        if (!globalExact && !logical && exact is not null && (exact.CrossPlans is not null || exact.SingleTargets.Count > 0))
        {
            var crossArgument = exact.CrossPlans is { } crossPlans
                ? NonRedundantCrossTableCompilation.BindingArgument(crossPlans)
                : ",Array.Empty<Metarill.Data.Queries.NonRedundantCrossTableScope>()";
            var compiler = projectTargets.Count == 0 ? "CompileCross" : "CompileProjectPage";
            plan = $"{compiler}({Literal(relation.Target.Source.Id)},{outputs},{fields}{crossArgument}{projectTargetArgument})";
        }
        var label = relation.NameColumn.IsId?"id.ToString(CultureInfo.InvariantCulture)":
            MultiSelectExcelRenderer.LabelText(relation.NameColumn, $"NonRedundantReads.Value<{relation.NameColumn.Type}>(row,{Literal(name)})");
        var search = relation.NameColumn.Source.LogicalType switch
        {
            "int32" or "int64" => "IntegerLabelContains",
            "string" => "Contains",
            _ => "TypedLabelContains"
        };
        var order = relation.OptionsOrder is { } ordering
            ? string.Join(", ", ordering.Select(item => $"new({Literal(item.Column.Source.ColumnName)}, {(item.Descending ? "true" : "false")})"))
            : relation.NameColumn.IsId?"new(\"id\")":$"new({Literal(name)}), new(\"id\")";
        var hasIndependentOrder = relation.IndependentOrder.Count > 0;
        var independentOrder = visible && hasIndependentOrder;
        var originalOrder = hasIndependentOrder
            ? string.Join(", ", relation.OptionsOrder!.Where(item => !relation.IndependentOrder.Contains(item.Column))
                .Select(item => $"new({Literal(item.Column.Source.ColumnName)}, {(item.Descending ? "true" : "false")})"))
            : order;
        var sortArgument = independentOrder ? $"OptionsSortPlan{suffix}.BindAccount(actor)," : "";
        var sortDeclaration = independentOrder ? CandidateOrderDeclaration(model, relation, suffix) : "";
        var rowRead = independentOrder ? $$"""
            var reader=new NonRedundantReader(session);
            var rows=orderCandidates
                ?await reader.OrderedRowsAsync(DerivedOptionsPlan{{suffix}}{{bindParent}},{{sortArgument}}filters,[{{order}}],200,cancellationToken,actor.ProjectId,scope.Condition)
                :await reader.RowsAsync(DerivedOptionsPlan{{suffix}}{{bindParent}},filters,[{{originalOrder}}],200,cancellationToken,actor.ProjectId,scope.Condition);
""" : $"            var rows=await new NonRedundantReader(session).RowsAsync(DerivedOptionsPlan{suffix}{bindParent},filters,[{originalOrder}],200,cancellationToken,actor.ProjectId,scope.Condition);";
        // 固定名称查询单独执行，冗余字典的文本业务键与普通候选使用同一字节比较规则。
        var storedBusinessParent=relation.Target.Source.IsRongyu==1?relation.SharedTextParent:null;
        var filters=string.Join("\n",relation.TargetPage.Scopes.Concat(relation.Scopes).DistinctBy(scope=>scope.Column.Source.Id)
            .Select(scope=>scope.Source=="parent"&&scope.Column==storedBusinessParent
                ?$"        filters.Add(FixedReadFilter.ForTextParent({Literal(scope.Column.Source.ColumnName)},targetParentId!,businessKey:true));"
                :(relation.CompoundProjectSource||relation.CompoundProjectTarget||relation.CanonicalProjectParent||
                relation.FixedGlobalSelection)&&
                scope.Source=="parent"&&scope.Column.Type=="string?"&&
                relation.FixedParent?.HasTarget(scope.Column.Source.Id,logicalOnly:true)!=true
                ?$"        filters.Add(FixedReadFilter.ForTextParent({Literal(scope.Column.Source.ColumnName)},targetParentId!));"
                :"        filters.Add(new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+ScopeValue(scope.Column,scope.Source=="project"?"actor.ProjectId":"targetParentId!")+"));"));
        var same=ControlRenderer.RelationContext(page,relation,false).SameParent;
        var variables=relation.Conditions is not{} condition?"":RelationConditionsCompilation.Variables(condition,"targetParentId");
        var bind=relation.Conditions is null?"":"        var bound=OptionsCondition"+suffix+".BindVariables(new Dictionary<string,object?>{"+variables+"});condition=condition is null?bound:condition.And(bound);";
        var result=new StringBuilder($$"""
    /// <summary>生成和静态装载时编译，普通请求不解析元数据；名称联接与物理身份一同固定。</summary>
    private static readonly NonRedundantReadPlan DerivedOptionsPlan{{suffix}}=global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.{{plan}};{{(independentOrder?"\n"+sortDeclaration.TrimEnd():"")}}
    /// <summary>先复用原真实父检查，再把同一页面及控件条件AND绑定到固定SQL读取。</summary>
    private async Task<(List<FixedReadFilter> Filters,FixedConditionSqlPlan? Condition)> DerivedOptionsScope{{suffix}}Async(string? parentId,string? targetParentId,RuntimeActor actor,CancellationToken cancellationToken,IReadOnlyDictionary<string,object?>? formValues=null{{(validateCurrent?",bool locking=false":"")}})
    {
        _=await OptionsScope{{suffix}}Async(parentId,targetParentId,actor,cancellationToken,{{(validateCurrent?"locking":"false")}},false{{(RelationConditionsCompilation.HasForms(relation)?",formValues:formValues":"")}});
{{(same?"        targetParentId??=parentId;":"")}}
        var filters=new List<FixedReadFilter>();
{{filters}}
{{exactFilters}}        FixedConditionSqlPlan? condition={{(embedsPageScope?"null":FixedPageConditionCompilation.Bound(relation.TargetPage,"OptionsPageCondition"+suffix))}};
{{bind}}
        return(filters,condition);
    }
    /// <summary>原下拉及 Excel 导出使用同一可还原名称文本。</summary>
    private static GeneratedOption ReadDerivedOption{{suffix}}(IReadOnlyDictionary<string,object?> row)
    {
        var id = NonRedundantReads.Value<long>(row, "id");
        var label = {{label}};
        return new(id, label, id, label);
    }
    /// <summary>已选和导出按有限批次读取，缓存键仍由调用方的真实父与表单依赖隔离。</summary>
    private async Task<IReadOnlyList<GeneratedOption>> DerivedOptionsRows{{suffix}}Async(string? parentId,string? targetParentId,RuntimeActor actor,IReadOnlyList<long> ids,CancellationToken cancellationToken,IReadOnlyDictionary<string,object?>? formValues=null{{(independentOrder?",bool orderCandidates=false":"")}})
    {
        var scope=await DerivedOptionsScope{{suffix}}Async(parentId,targetParentId,actor,cancellationToken,formValues);var result=new List<GeneratedOption>();
        foreach(var batch in ids.Distinct().Chunk(200))
        {
            var filters=scope.Filters.Append(new FixedReadFilter("id",FixedReadComparison.In,batch.Select(id=>(object?)id).ToArray())).ToArray();
{{rowRead}}
            result.AddRange(rows.Select(ReadDerivedOption{{suffix}}));
        }
        return result;
    }

""");
        if(!visible)return result.ToString();
        var form=RelationConditionsCompilation.HasForms(relation)?"ReadOptionsForm"+suffix+"(request.FormParameters)":"null";
        result.Append($$"""
    /// <summary>保留旧数组5000项上限，使用同一固定名称排序，不静默截断。</summary>
    public Task<IReadOnlyList<GeneratedOption>> Options{{suffix}}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken,string? targetParentId=null)=>{{GlobalCachedSourceCompilation.ReadSession(model)}}.InReadSnapshotAsync<IReadOnlyList<GeneratedOption>>(async()=>
    {
        ArgumentNullException.ThrowIfNull(request);var scope=await DerivedOptionsScope{{suffix}}Async(request.ParentId,targetParentId,actor,cancellationToken,{{form}});
        var rows=await new NonRedundantReader(session).{{(independentOrder?"OrderedRowsAsync":"RowsAsync")}}(DerivedOptionsPlan{{suffix}}{{bindParent}},{{sortArgument}}scope.Filters,[{{order}}],5001,cancellationToken,actor.ProjectId,scope.Condition);
        if(rows.Count>5000)throw new InvalidOperationException("关联候选超过5000条，请使用分页查询。");
        return rows.Select(ReadDerivedOption{{suffix}}).ToArray();
    },cancellationToken);
    /// <summary>名称关键词先在SQL中筛选和排序，再按同一快照COUNT与分页。</summary>
    public Task<PageResult<GeneratedOption>> OptionsPage{{suffix}}Async(RelationOptionsPageRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>{{GlobalCachedSourceCompilation.ReadSession(model)}}.InReadSnapshotAsync<PageResult<GeneratedOption>>(async()=>
    {
        ArgumentNullException.ThrowIfNull(request);request.Validate();var scope=await DerivedOptionsScope{{suffix}}Async(request.ParentId,request.TargetParentId,actor,cancellationToken,{{form}});
        if(!string.IsNullOrEmpty(request.Keyword))scope.Filters.Add(new({{Literal(name)}},FixedReadComparison.{{search}},request.Keyword));
        var rows=await new NonRedundantReader(session).{{(independentOrder?"OrderedPageAsync":"PageAsync")}}(DerivedOptionsPlan{{suffix}}{{bindParent}},{{sortArgument}}scope.Filters,[{{order}}],request.PageNo,request.PageSize,cancellationToken,projectId:actor.ProjectId,condition:scope.Condition);
        var options=rows.Records.Select(ReadDerivedOption{{suffix}}).ToArray();
        return new(options,rows.Total,(rows.Total+request.PageSize-1)/request.PageSize,request.PageNo,request.PageSize);
    },cancellationToken);
    /// <summary>已选回读不使用关键词，逐个核对实际候选范围，缺项整体拒绝。</summary>
    public Task<IReadOnlyList<GeneratedOption>> OptionsSelected{{suffix}}Async(RelationOptionsSelectedRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>{{GlobalCachedSourceCompilation.ReadSession(model)}}.InReadSnapshotAsync<IReadOnlyList<GeneratedOption>>(async()=>
    {
        ArgumentNullException.ThrowIfNull(request);var selected=request.ParseIds();var rows=await DerivedOptionsRows{{suffix}}Async(request.ParentId,request.TargetParentId,actor,selected,cancellationToken,{{form}}{{(independentOrder?",orderCandidates:true":"")}});
        if(rows.Count!=selected.Length)throw new InvalidOperationException("关联已选项不存在、已删除或不在当前可选范围。");return rows;
    },cancellationToken);

""");return result.ToString();
    }
    /// <summary>独立联接只返回主键和排序值，原候选、导出及写前查询仍使用自己的名称。</summary>
    private static string CandidateOrderDeclaration(GenerationModel model, RelationModel relation, string suffix)
    {
        var columns = string.Join(",", new[] { "id" }.Concat(relation.IndependentOrder
            .Select(column => column.Source.ColumnName)).Select(Literal));
        var targets = NonRedundantReadCompilation.SingleTargetArgument(
            DerivedRelationNamesCompilation.CandidateOrderTargets(model, relation), "singleTargets");
        return $$"""
    /// <summary>独立排序按本级发布的字典范围编译，对外继续返回原名称。</summary>
    private static readonly NonRedundantReadPlan OptionsSortPlan{{suffix}}=
        global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.CompileRelation(
            {{Literal(relation.Target.Source.Id)}},[{{columns}}],[]{{targets}});
""";
    }

    /// <summary>把 Excel 独有的逻辑字段编入单独计划，避免它们的账号依赖影响普通候选。</summary>
    private static string ExactImportDeclaration(GenerationModel model, RelationModel relation,
        ExactDataManageModel exact, RelationConditionModel condition, string planName)
    {
        var targetId = relation.Target.Source.Id;
        var pageId = relation.TargetPage.Source.Id;
        var outputs = relation.NameColumn.IsId ? new[] { "id" } : ["id", relation.NameColumn.Source.ColumnName];
        var dependencies = relation.TargetPage.Scopes.Concat(relation.Scopes).Select(scope => scope.Column.Source.ColumnName)
            .Concat(relation.Conditions?.Syntax.Fields ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var fields = condition.Syntax.Fields.Concat(relation.Conditions?.Syntax.Fields ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var targets = DerivedRelationNamesCompilation.ProjectCandidateTargets(model, relation, exact, forImport: true);
        var globalExact = DerivedRelationNamesCompilation.IsGlobalExactSingle(relation);
        var targetArgument = NonRedundantReadCompilation.SingleTargetArgument(targets, "singleTargets");
        static string Columns(IReadOnlyList<string> columns) => "new string[]{" + string.Join(",", columns.Select(Literal)) + "}";
        string planStart;
        var additional = "";
        if (globalExact)
        {
            _ = NonRedundantReads.CompileGlobalExactSingleCandidate(model.Catalog, targetId, pageId,
                outputs, dependencies, targets, null, fields);
            planStart = $"CompileGlobalExactSingleCandidate({Literal(targetId)},{Literal(pageId)}";
            additional = ",listFields:" + Columns(fields);
        }
        else
        {
            _ = DerivedRelationNamesCompilation.CompileLogicalPage(model, relation.Target, relation.TargetPage,
                outputs, dependencies, condition, targets, exact);
            planStart = DerivedRelationNamesCompilation.LogicalPagePlanStart(model, relation.Target, relation.TargetPage, condition, exact);
            if (DerivedRelationNamesCompilation.EmbedsParentScope(relation.Target, relation.FixedParent))
                additional = NonRedundantCrossTableCompilation.BindingArgument(exact.CrossPlans ?? [], "bindings") +
                    ",listFields:" + Columns(fields);
        }
        return $$"""
    /// <summary>Excel 当前读使用实际发布页的精确范围及导入专用投影。</summary>
    private static readonly NonRedundantReadPlan {{planName}}=
        global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.{{planStart}},
            {{Columns(outputs)}},{{Columns(dependencies)}}{{targetArgument}}{{additional}});

""";
    }

    /// <summary>Excel按名称类型绑定同一范围的当前读，零个或重名均拒绝整批。</summary>
    internal static string Import(GenerationModel model,RelationModel relation,PageModel page,ImportDependencyPlan dependency)
    {
        var owner = model.Tables.Single(table => table.Source.Id == page.Source.ConfigTableId);
        var exact = DerivedRelationNamesCompilation.CandidateExact(model, owner, relation);
        var accountParent=SharedRedundantParentCompilation.HasAccountPath(model,relation.Target,relation.TargetPage);
        var validateCurrent=exact is not null||accountParent||DerivedRelationNamesCompilation.UsesLogicalPage(relation)||
            relation.FixedGlobalSelection||FixedPageConditionCompilation.HasAccountVariables(relation.TargetPage);
        var lockParents=validateCurrent?",locking:true":"";
        var bindParent=validateCurrent||DerivedRelationNamesCompilation.ProjectCandidateTargets(model,relation,exact).Count>0?".BindAccount(actor)":"";
        var suffix=page.Source.Id+"_"+relation.SourceColumn.Source.Id;var mapped=dependency.Parents.GetValueOrDefault(relation.SourceColumn.Source.Id);
        var name = relation.NameColumn;
        var importCondition=relation.ImportCondition;
        var conditionName="SingleImportCondition"+suffix;
        var importDeclaration=importCondition is null?"":RelationConditionsCompilation.Declaration(importCondition,conditionName);
        var readPlan="DerivedOptionsPlan"+suffix;
        if(DerivedRelationNamesCompilation.UsesSeparateExactImportPlan(relation))
        {
            readPlan="DerivedImportPlan"+suffix;
            importDeclaration+=ExactImportDeclaration(model,relation,exact!,importCondition!,readPlan);
        }
        var sameParent=ControlRenderer.RelationContext(page,relation,false).SameParent;
        var targetParent=mapped is not null?"targetParent.ToString()":sameParent?"parentId":"null";
        var importBinding=importCondition is null?"":RelationConditionsCompilation.BindImport(importCondition,conditionName,targetParent);
        var scopedCondition=importCondition is null?"scope.Condition":"scope.Condition is null?importCondition:scope.Condition.And(importCondition)";
        var addMatchFilter = name.Type == "string?" ? $$"""
        if(cell.ValueKind==JsonValueKind.Number)
        {
            long id;try{id=cell.GetInt64();}catch(Exception error)when(error is FormatException or OverflowException){throw PageImportException.Input(PageImportError.Number,inner:error);}
            scope.Filters.Add(new("id",FixedReadComparison.Equal,id));
        }
        else scope.Filters.Add(new({{Literal(name.Source.ColumnName)}},FixedReadComparison.Equal,cell.ToString().Trim()));
""" : $$"""
        // 按目标列的实际类型绑定，不把业务名称当作记录 ID。
        if ({{MultiSelectExcelRenderer.InvalidLabelKind(name, true, "cell")}})
            throw PageImportException.Input(PageImportError.Relation);
        {{MultiSelectExcelRenderer.ParseLabel(name, "cell.ToString().Trim()", "key")}}
        scope.Filters.Add(new({{Literal(name.Source.ColumnName)}}, FixedReadComparison.Equal, key));
""";
        var importAuthorization = exact is null ? "" : $$"""
        if(relationAuthorization is null || relationAuthorization.RequireActor() != actor || session.CurrentTransaction is null)
            throw new UnauthorizedAccessException("精确范围单选导入要求来源真实身份和实际事务。");
        relationAuthorization.RequirePagePermission({{Literal(page.Source.Id)}}, "import");

""";
        return importDeclaration+$$"""
    /// <summary>名称只由已发布计划解析，调用方的原导入事务负责整批回滚。</summary>
    private async Task<JsonElement> ImportRelation{{suffix}}Async(JsonElement cell,string? parentId,RuntimeActor actor{{(dependency.Contextual?",IReadOnlyDictionary<string,JsonElement> relationContext":"")}},CancellationToken cancellationToken)
    {
{{importAuthorization}}        if(cell.ValueKind==JsonValueKind.Null||string.IsNullOrWhiteSpace(cell.ToString()))return JsonSerializer.SerializeToElement<object?>(null);
{{(mapped is null?"":"        if(!relationContext.TryGetValue("+Literal(mapped.JsonName)+",out var targetParent)||targetParent.ValueKind==JsonValueKind.Null||string.IsNullOrWhiteSpace(targetParent.ToString()))throw PageImportException.Input(PageImportError.Relation);")}}
        var scope=await DerivedOptionsScope{{suffix}}Async(parentId,{{targetParent}},actor,cancellationToken{{RelationConditionsCompilation.ImportArgument(relation)}}{{lockParents}});
{{importBinding}}{{addMatchFilter}}
        var rows=await new NonRedundantReader(session).PageAsync({{readPlan}}{{bindParent}},scope.Filters,[new("id")],1,2,cancellationToken,projectId:actor.ProjectId,condition:{{scopedCondition}},currentRead:true);
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
