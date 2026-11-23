using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>原嵌入式复选的候选与最终写入共用固定范围，不借用弹层目标页或客户端SQL。</summary>
internal static class DatabaseCheckboxRenderer
{
    /// <summary>原候选入口按来源页查询／新增／修改任一开放动作授权，保留四字段数组。</summary>
    internal static string Controller(GenerationModel model,TableModel table,PageModel page)=>string.Concat(DatabaseCheckboxCompilation.ForPage(model,table,page).Select(def=>$$"""
    /// <summary>完整有界候选用于原全选，指定空ID只回读空集合。</summary>
    [HttpPost("{{def.Endpoint}}")] [RequestSizeLimit(262144)]
    public async Task<R<IReadOnlyList<DatabaseCheckboxOption>>> {{def.Endpoint}}([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] System.Text.Json.JsonElement? body, CancellationToken cancellationToken)
    {
        var actor=RelationOptionAuthorization.Require(authorization,"{{page.Source.Id}}",{{Bool(table.Source.IsFunctionTable==0&&page.Source.IsNew==1)}},{{Bool(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source))}});
        if(Request.Query.Count!=0)throw new ArgumentException("数据库复选候选不接受query参数。");
        var request=DatabaseCheckboxOptionsRequest.Read(body);
        return R<IReadOnlyList<DatabaseCheckboxOption>>.Success(await service.CheckboxOptions{{Suffix(page,def)}}Async(request,actor,cancellationToken));
    }

"""));

    /// <summary>全部关系字段含隐藏字段都保留内部校验，公开候选只为本页已开放字段生成。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();var exposed=DatabaseCheckboxCompilation.ForPage(model,table,page).Select(def=>def.Source.Source.Id).ToHashSet(StringComparer.Ordinal);
        var tableCaches=DatabaseCheckboxCompilation.Caches(model,table).ToLookup(binding=>binding.Source.Source.Id);
        foreach(var def in DatabaseCheckboxCompilation.ForService(model,table,page))
        {
            var suffix=Suffix(page,def);var project=def.Target.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id");
            var caches=tableCaches[def.Source.Source.Id].ToArray();
            var sharedCaches=caches.Length>1;
            result.Append(Declaration(def,suffix)).Append(Reader(def,suffix));
            if((def.PhysicalTargetPage??DerivedCheckboxNamesCompilation.ExactMembershipPage(model,def)) is {} physicalPage)
                result.Append(FixedPageConditionCompilation.Declaration(physicalPage,"CheckboxTargetCondition"+suffix));
            if(def.GlobalPage is not null&&!def.DerivedName)
                result.Append(FixedPageConditionCompilation.Declaration(def.GlobalPage,"CheckboxGlobalCondition"+suffix));
            if(def.DerivedName)result.Append(DerivedCheckboxNamesRenderer.Service(model,table,page,def,
                sharedCaches?caches.Select(cache=>cache.Value).DistinctBy(label=>label.Source.Id).ToArray():[]));
            if(def.GlobalParentPage is {} parentPage)
                result.Append(MultiGlobalParentCompilation.Declaration(model,parentPage,"CheckboxGlobalParent"+suffix));
            result.AppendLine("    /// <summary>来源身份、真实父和发布条件始终进入数据库；写校验使用当前事务锁读。</summary>")
                .AppendLine($"    private async Task<ISelect<{def.Target.EntityType}>> CheckboxScope{suffix}Async(string? parentId,RuntimeActor actor,IReadOnlyDictionary<string,object?> formValues,bool locking,CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine("        await Task.CompletedTask;")
                .AppendLine("        ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"数据库复选要求真实当前身份。\");")
                .AppendLine("        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException(\"复选最终校验必须加入实际写事务。\");");
            if(def.SourceParent is null)result.AppendLine("        if(!string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前复选来源未声明父范围。\");");
            else if(def.LogicalSourceParent)
            {
                result.AppendLine($"        var parentKey=LogicalParentId{page.Source.Id}(parentId);");
                // 同一个实际父记录也要通过每条发布路径的范围核验；写入口复用原事务。
                foreach(var path in def.LogicalSourcePaths)
                    result.AppendLine($"        await CrossReader{page.Source.Id}_{path.Read.Source.Source.Id}(actor,locking,false).PathAsync({path.Index},parentKey,cancellationToken);");
                if(def.GlobalParent)result.AppendLine(MultiGlobalParentCompilation.Check("CheckboxGlobalParent"+suffix,"parentKey","locking"));
            }
            else
            {
                result.AppendLine("        if(!long.TryParse(parentId,NumberStyles.None,CultureInfo.InvariantCulture,out var parentKey)||parentKey<=0||parentId!=parentKey.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"来源父ID必须为规范正整数字符串。\");");
                if(def.GlobalParent)result.AppendLine(MultiGlobalParentCompilation.Check("CheckboxGlobalParent"+suffix,"parentKey","locking"));
                foreach(var parent in def.SourceParents.Where(_=>!def.GlobalParent))
                {
                    var parentProject=parent.Columns.Single(column=>column.Source.ColumnName=="yz_project_id");
                    result.AppendLine($"        var parentQuery=session.Select<{parent.EntityType}>().Where(row=>row.Id==parentKey&&row.{parentProject.Property}=={Scope(parentProject,"actor.ProjectId")});");
                    if(parent.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("        parentQuery=parentQuery.Where(row=>row.DeleteNo==null);");
                    result.AppendLine("        if(locking)parentQuery=parentQuery.ForUpdate();")
                        .AppendLine("        if(!await parentQuery.AnyAsync(cancellationToken))throw new InvalidOperationException(\"复选来源父不存在、已删除或不属于当前项目。\");");
                }
            }
            result.AppendLine(def.Target.IsGlobal?$"        var query=session.Select<{def.Target.EntityType}>();":$"        var query=session.Select<{def.Target.EntityType}>().Where(row=>row.{project!.Property}=={Scope(project,"actor.ProjectId")});");
            if(def.Target.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("        query=query.Where(row=>row.DeleteNo==null);");
            foreach(var (column,source) in def.Scopes.Where(scope=>def.LogicalTargetPage is null||!NonRedundantCrossParentCompilation.Managed(model,def.Target,def.LogicalTargetPage,scope.Column)))
            {
                // 物理字典的字符串父键与回填共用规范十进制比较，避免数据库文本排序规则扩大范围。
                if(source=="parent"&&column.Type=="string?")
                    result.AppendLine($"        query=global::Metarill.Data.Queries.NonRedundantReads.ApplyTextParent(query,session,{Literal(column.Source.ColumnName)},parentId!);");
                else
                    result.AppendLine($"        query=query.Where(row=>row.{column.Property}=={Scope(column,source=="project"?"actor.ProjectId":"parentId!")});");
            }
            if(def.GlobalPage is not null&&!def.DerivedName)
                result.Append(FixedPageConditionCompilation.Apply(def.GlobalPage,"CheckboxGlobalCondition"+suffix,"checkbox_global_"+suffix));
            if(def.Conditions is {} condition)
            {
                var variables=RelationConditionsCompilation.Variables(condition,"parentId");
                result.AppendLine($"        query=CheckboxCondition{suffix}.Apply(query,session,new Dictionary<string,object?> {{ {variables} }},\"checkbox_{suffix}\");");
            }
            if(def.PhysicalTargetPage is {} targetPage)
                result.Append(FixedPageConditionCompilation.Apply(targetPage,"CheckboxTargetCondition"+suffix,"checkbox_target_"+suffix));
            if(def.ExactTarget is not null&&!def.DerivedName)
            {
                var exactPage=def.NameTargetPage!;
                var plan=$"global::{model.Namespace}.Generated.Services.{def.Target.Name}Service.ExactReadPlan{exactPage.Source.Id}";
                result.Append(ExactDataManageCompilation.Query(model,def.Target,exactPage,operation:Literal("list"),
                    prefix:"checkboxExact",locking:"locking",planReference:plan));
            }
            result.AppendLine("        return locking?query.ForUpdate():query;").AppendLine("    }");
            if(exposed.Contains(def.Source.Source.Id))
            {
                if (def.CandidateOrderTargets is not null)
                    result.Append(CandidateSortPlan(model, def, suffix));
                var order = def.CandidateOrderTargets is null
                    ? string.Concat(def.Order.Select(item=>$".{(item.Descending?"OrderByDescending":"OrderBy")}(row=>row.{item.Column.Property})"))
                    : ".OrderBy(row=>row.Id)";
                result.AppendLine("    /// <summary>全选不得截断；父验证和候选在同一读快照，权限在服务入口再次核对。</summary>")
                    .AppendLine($"    public Task<IReadOnlyList<DatabaseCheckboxOption>> CheckboxOptions{suffix}Async(DatabaseCheckboxOptionsRequest request,RuntimeActor actor,CancellationToken cancellationToken)")
                    .AppendLine("    {")
                    .AppendLine($"        if(relationAuthorization is null||RelationOptionAuthorization.Require(relationAuthorization,{Literal(page.Source.Id)},{Bool(table.Source.IsFunctionTable==0&&page.Source.IsNew==1)},{Bool(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source))})!=actor)throw new UnauthorizedAccessException(\"复选读取身份已变化。\");")
                    .Append(def.ExactTarget is null?"":$"        relationAuthorization.RequirePagePermission({Literal(def.NameTargetPage!.Source.Id)},\"list\");\n")
                    .AppendLine("        if(request.Selected is {} ids&&(ids.Length>DatabaseCheckboxValue.MaximumItems||ids.Any(id=>id<=0)||ids.Distinct().Count()!=ids.Length))throw new ArgumentException(\"复选指定ID无效。\");")
                    .AppendLine($"        var form=ReadCheckboxForm{suffix}(request.FormParameters);")
                    .AppendLine($"        return {GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync<IReadOnlyList<DatabaseCheckboxOption>>(async()=>")
                    .AppendLine("        {")
                    .Append(def.DerivedName?"":$"            var query=await CheckboxScope{suffix}Async(request.ParentId,actor,form,false,cancellationToken);\n")
                    .Append(def.DerivedName?"":"            if(request.Selected is {} selected){if(selected.Length==0)return [];query=query.Where(row=>selected.Contains(row.Id));}\n")
                    .AppendLine(def.DerivedName?$"            var rows=await DerivedCheckboxRows{suffix}Async(request.ParentId,actor,form,request.Selected,null,false,5001,false,cancellationToken);":$"            var rows=await query{order}.Limit(5001).ToListAsync(row=>new {{row.Id,Name=row.{def.Name.Property}}},cancellationToken);")
                    .AppendLine("            if(rows.Count>DatabaseCheckboxValue.MaximumItems)throw new InvalidOperationException(\"复选候选超过5000项，全选不能使用截断结果。\");")
                    .AppendLine(def.CandidateOrderTargets is null
                        ? $"            return rows.Select(row=>{{var id=row.Id.ToString(CultureInfo.InvariantCulture);var label={LabelText(def,"row.Name")};return new DatabaseCheckboxOption(id,label,id,label);}}).ToArray();"
                        : RankedOptions(def, suffix))
                    .AppendLine("        },cancellationToken);").AppendLine("    }");
            }
            if(table.Source.IsFunctionTable==1)continue;
            var writeParent=def.LogicalSourceParent?"logicalRelationParentId":ParentValue(def,"entity");
            var projection=sharedCaches?"new {row.Id,"+string.Join(",",caches.Select(cache=>"row."+cache.Value.Property).Distinct(StringComparer.Ordinal))+"}":
                $"new {{row.Id,Name=row.{def.Name.Property}}}";
            result.AppendLine("    /// <summary>来源依赖变化重新校验完整所选集合，冗余名称仅在本事务派生；未提交无关字段不刷新。</summary>")
                .AppendLine($"    private async Task ValidateCheckbox{suffix}Async({table.EntityType} entity,RuntimeActor actor,ISet<string> submitted,CancellationToken cancellationToken,RecordMutationOperation? operation=null{(def.LogicalSourceParent?",string? logicalRelationParentId=null":"")})")
                .AppendLine("    {")
                .AppendLine("        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"复选保存要求真实当前身份。\");")
                .AppendLine($"        if(operation==RecordMutationOperation.Import)relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"import\");")
                .AppendLine($"        var selected=DatabaseCheckboxValue.Parse(entity.{def.Source.Property});");
            result.Append(RequireMatchingParents(def,"entity","        "));
            if(int.TryParse(def.Source.Source.ColumnLength,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var sourceLimit)&&sourceLimit>0)result.AppendLine($"        if(entity.{def.Source.Property} is {{Length:>{sourceLimit}}})throw new ArgumentException(\"复选ID数组超过已声明物理列长度。\");");
            var scope=$"CheckboxScope{suffix}Async({writeParent},actor,{EntityForm(def,"entity")},true,cancellationToken)";
            var physicalRead=$"Where(row=>selected.Contains(row.Id)).Limit(DatabaseCheckboxValue.MaximumItems).ToListAsync(row=>{projection},cancellationToken)";
            var readRows=def.DerivedName
                ?$"DerivedCheckboxRows{suffix}Async({writeParent},actor,{EntityForm(def,"entity")},selected,null,false,DatabaseCheckboxValue.MaximumItems,true,cancellationToken)"
                :$"query.{physicalRead}";
            if(def.FormParentField is not null)
            {
                // 空表单沿用原保存规则，其他情况用实际表单外键验证固定物理或逻辑路径。
                var read=def.DerivedName?readRows:$"(await {scope}).{physicalRead}";
                result.AppendLine("        ValidateActor(actor);")
                    .AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"复选最终校验必须加入实际写事务。\");")
                    .AppendLine($"        var rows=selected.Length==0&&string.IsNullOrWhiteSpace({writeParent})?[]:await {read};");
            }
            else result
                .Append(def.DerivedName?"":$"        var query=await {scope};\n")
                .AppendLine($"        var rows=await {readRows};");
            result.AppendLine("        if(rows.Count!=selected.Length)throw new InvalidOperationException(\"复选项不存在、已删除或已离开当前固定候选范围，整笔未保存。\");");
            if(table.Source.IsRongyu==1)
            {
                // 同一有界当前锁读返回所有名称列，每份缓存沿原所选 ID 次序写入。
                result.AppendLine(sharedCaches?"        var names=rows.ToDictionary(row=>row.Id);":
                    $"        var names=rows.ToDictionary(row=>row.Id,row=>{LabelText(def,"row.Name")});");
                foreach(var cache in caches)
                {
                    var label=sharedCaches?$"names[id].{(def.DerivedName?"Cache":"")}{cache.Value.Property}":"names[id]";
                    result.AppendLine($"        entity.{cache.Cache.Property}=entity.{def.Source.Property} is null?null:JsonSerializer.Serialize(selected.Select(id=>{label}).ToArray());submitted.Add({Literal(cache.Cache.Source.ColumnName)});");
                    if(int.TryParse(cache.Cache.Source.ColumnLength,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var nameLimit)&&nameLimit>0)
                        result.AppendLine($"        if(entity.{cache.Cache.Property} is {{Length:>{nameLimit}}})throw new ArgumentException(\"复选名称数组超过已声明物理列长度。\");");
                }
            }
            result.AppendLine("    }");
        }
        return result.ToString();
    }
    /// <summary>候选与名称数组都保留空值及十进制文本，避免 bigint 在 JavaScript 中丢失精度。</summary>
    internal static string LabelText(DatabaseCheckboxModel def,string expression)=>
        MultiSelectExcelRenderer.LabelText(def.Name,expression);

    /// <summary>已编译条件在类型加载时创建一次，候选请求只提供参数。</summary>
    private static string Declaration(DatabaseCheckboxModel def,string suffix)
    {
        if(def.Conditions is not {} condition)return "";
        var fields=string.Join(",",condition.Syntax.Fields.Select(Literal));var types=string.Join(",",condition.Types.Select(pair=>"["+Literal(pair.Key)+"]="+Literal(pair.Value)));
        return $"    private static readonly Metarill.Data.Queries.FixedConditionSqlPlan CheckboxCondition{suffix}=Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile({Literal(condition.Definition)},new string[]{{{fields}}}),new Dictionary<string,string>{{{types}}});\n";
    }
    /// <summary>只提取原表单槽中发布时声明的物理叶字段。</summary>
    private static string Reader(DatabaseCheckboxModel def,string suffix)=>$"    private static IReadOnlyDictionary<string,object?> ReadCheckboxForm{suffix}(JsonElement? formParameters)=>new Dictionary<string,object?>{{"+string.Join(",",(def.Conditions?.Forms??[]).Select(binding=>"["+Literal(binding.Variable)+"]=Metarill.Data.Queries.FixedConditionSqlPlan.BindScalar(Metarill.Core.Contracts.FixedConditionFormValue.Read(formParameters,new string[]{"+string.Join(",",binding.Path.Select(Literal))+"},new string[]{"+string.Join(",",binding.FallbackPath.Select(Literal))+"}),"+Literal(binding.Column.Source.LogicalType)+")"))+"};\n";
    internal static string EntityForm(DatabaseCheckboxModel def,string entity)=>"new Dictionary<string,object?>{"+string.Join(",",(def.Conditions?.Forms??[]).Select(binding=>"["+Literal(binding.Variable)+"]="+entity+"."+binding.Column.Property))+"}";
    internal static string ParentValue(DatabaseCheckboxModel def,string entity)=>ParentValue(def.SourceParent,entity);

    private static string ParentValue(ColumnModel? field,string entity)=>field is null?"null":field.Type=="string?"
        ?entity+"."+field.Property:entity+"."+field.Property+"?.ToString(CultureInfo.InvariantCulture)";

    /// <summary>普通账号字典的同父来源需逐一核对物理字段，保存及名称导出共用精确十进制字符串。</summary>
    internal static string RequireMatchingParents(DatabaseCheckboxModel def,string entity,string indent)
    {
        if(!def.HasProjectStoredAccountScope||def.SourceScopeColumns.Count<2)return "";
        var parent=ParentValue(def,entity);
        return string.Concat(def.SourceScopeColumns.Where(column=>column.Source.Id!=def.SourceParent?.Source.Id)
            .Select(column=>$"{indent}if(!string.Equals({parent},{ParentValue(column,entity)},StringComparison.Ordinal))throw new InvalidOperationException(\"复选的来源父字段必须属于同一父记录。\");\n"));
    }
    /// <summary>生成为固定标识与方向，请求不能扩展排序字段。</summary>
    internal static string FixedOrder(DatabaseCheckboxModel definition) =>
        "[" + string.Join(",", definition.Order.Select(item =>
            "new(" + Literal(item.Column.Source.ColumnName) + "," + Bool(item.Descending) + ")")) + "]";

    /// <summary>固定排序只投影 ID，范围来自原候选查询的授权结果。</summary>
    private static string CandidateSortPlan(GenerationModel model, DatabaseCheckboxModel definition, string suffix) => $$"""
    private static readonly NonRedundantReadPlan CheckboxSortPlan{{suffix}}=
        global::{{model.Namespace}}.Generated.GeneratedNonRedundantReadCatalog.CompileRelation(
            {{Literal(definition.Target.Source.Id)}},["id"],[{{string.Join(",", definition.Order.Select(item => Literal(item.Column.Source.ColumnName)))}}]
            {{NonRedundantReadCompilation.SingleTargetArgument(definition.CandidateOrderTargets!, "singleTargets")}});
    private static readonly FixedReadOrder[] CheckboxSortOrder{{suffix}}={{FixedOrder(definition)}};

""";

    /// <summary>使用同一快照中的原候选 ID 限定排序，逐项匹配后才返回原名称。</summary>
    private static string RankedOptions(DatabaseCheckboxModel definition, string suffix) => $$"""
            DatabaseCheckboxOption Option(long key, {{definition.Name.Type}} name)
            {
                var id = key.ToString(CultureInfo.InvariantCulture);
                var label = {{LabelText(definition, "name")}};
                return new(id,label,id,label);
            }

            if (rows.Count < 2) return rows.Select(row=>Option(row.Id,row.Name)).ToArray();
            var ids = rows.Select(row=> (object?)row.Id).ToArray();
            var ranked = await new NonRedundantReader(session).RowsAsync(CheckboxSortPlan{{suffix}}.BindAccount(actor),
                [new("id",FixedReadComparison.In,ids)],CheckboxSortOrder{{suffix}},rows.Count,cancellationToken,actor.ProjectId);
            if (ranked.Count != rows.Count) throw new InvalidOperationException("复选排序与原候选范围不一致。");
            var remaining = rows.ToDictionary(row=>row.Id);
            return ranked.Select(item=>
            {
                var id = NonRedundantReads.Value<long>(item,"id");
                if (!remaining.Remove(id,out var row)) throw new InvalidOperationException("复选排序出现未知或重复候选。");
                return Option(id,row.Name);
            }).ToArray();
""";

    internal static string Suffix(PageModel page,DatabaseCheckboxModel def)=>page.Source.Id+"_"+def.Source.Source.Id;
    private static string Scope(ColumnModel column,string value)=>column.Type switch{"string?"=>value,"int?"=>$"int.Parse({value},CultureInfo.InvariantCulture)",_=>$"long.Parse({value},CultureInfo.InvariantCulture)"};
    private static string Bool(bool value)=>value?"true":"false";
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
}
