using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>多选在源事务校验并读取固定名称；R1缓存的反向传播由统一关系队列管理。</summary>
internal static class MultiSelectFieldRenderer
{
    /// <summary>字段输入兼容HTTP数组与旧已编码JSON文本，省略/null仍由Optional保留。</summary>
    internal static string Assignment(PageModel page, ColumnModel column) => $"ReadMultiValue{page.Source.Id}_{column.Source.Id}(request.{column.Property}.Value)";

    /// <summary>生成本页固定字段解析和目标范围校验，任何一项失败让源事务整体回滚。</summary>
    internal static string Service(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder();
        foreach (var def in MultiSelectCompilation.ForService(model, table, page))
        {
            var suffix = page.Source.Id + "_" + def.Source.Source.Id;
            var caches=MultiNameWriteCompilation.ForSource(model,table,def);
            var scoped=(def.SharedTextParent is null||def.Target.Source.IsRongyu==0)&&
                (def.UsesGlobalExactRead||FixedPageConditionCompilation.HasMultiTargetScope(model,def.Target,def.TargetPage));
            result.Append(scoped?"":FixedPageConditionCompilation.Declaration(def.TargetPage,"MultiPageCondition"+suffix))
                .Append(SharedRedundantParentCompilation.AccountDeclaration(model,def.Target,def.TargetPage,"MultiAccountParentScope"+suffix))
                .Append(DerivedMultiNamesCompilation.Declaration(model,page,def));
            if(def.GlobalParent)result.Append(MultiGlobalParentCompilation.Declaration(model,def.TargetPage,"MultiGlobalParent"+suffix));
            // 每个逻辑名称缓存只在类型初始化时编译一次；HTTP请求只绑定选中ID和身份范围。
            foreach(var (cache,index) in caches.Select((cache,index)=>(cache,index)).Where(item=>scoped||DerivedMultiNamesCompilation.Declared(model,def.Target,item.cache.Value)))
            {
                var dependencies=def.TargetPage.Scopes.Select(s=>s.Column.Source.ColumnName)
                    .Concat(scoped?[]:def.TargetPage.Condition?.Syntax.Fields??[]).Distinct(StringComparer.OrdinalIgnoreCase);
                var call=scoped&&def.ExactTarget is not null
                    ?DerivedMultiNamesCompilation.ExactPlanCall(model,def,cache.Value,dependencies)
                    :NonRedundantReadCompilation.MultiLabelPlanCall(model,def.Target,def.TargetPage,def.TargetPage,
                        cache.Value,["id",cache.Value.Source.ColumnName],dependencies);
                result.AppendLine($"    private static readonly NonRedundantReadPlan MultiCachePlan{suffix}_{index}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{call};");
            }
            result.AppendLine("    /// <summary>保留旧JSON文本字节，数组请求持久化为JSON文本；不接受对象或其它标量。</summary>")
                .AppendLine($"    private static string? ReadMultiValue{suffix}(JsonElement? value)").AppendLine("    {")
                .AppendLine("        if (value is null || value.Value.ValueKind == JsonValueKind.Null) return null;")
                .AppendLine("        if (value.Value.ValueKind == JsonValueKind.Array) return value.Value.GetRawText();")
                .AppendLine("        if (value.Value.ValueKind != JsonValueKind.String) throw new ArgumentException(\"多选字段只接受数组、已编码JSON数组文本或null。\");")
                .AppendLine($"        var text = value.Value.GetString()!; using var parsed = ParseMultiDocument{suffix}(text);")
                .AppendLine("        if (parsed.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException(\"多选字段文本必须为JSON数组。\");")
                .AppendLine("        return text;").AppendLine("    }")
                .AppendLine("    /// <summary>非法历史JSON在写入口明确拒绝，不通过500HTML掩盖业务错误。</summary>")
                .AppendLine($"    private static JsonDocument ParseMultiDocument{suffix}(string text) {{ try {{ return JsonDocument.Parse(text); }} catch (JsonException error) {{ throw new ArgumentException(\"多选字段JSON无效。\", error); }} }}")
                .AppendLine("    /// <summary>来源字段省略不刷新名称；显式提交在同一事务内校验目标list权限及所有ID。</summary>")
                .AppendLine($"    private async Task ValidateMulti{suffix}Async({table.EntityType} entity, RuntimeActor actor, ISet<string> submitted, bool refreshNames, CancellationToken cancellationToken, RecordMutationOperation? authorizationOperation = null{(def.LogicalSourceParent?", string? logicalRelationParentId = null":"")})").AppendLine("    {")
                .AppendLine("        if (relationAuthorization is null || relationAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException(\"多选字段要求真实当前身份。\");")
                .AppendLine($"        if(authorizationOperation==RecordMutationOperation.Import)relationAuthorization.RequirePagePermission({Literal(page.Source.Id)}, \"import\");else relationAuthorization.RequirePagePermission({Literal(def.TargetPage.Source.Id)}, \"list\");")
                .AppendLine($"        if (entity.{def.Source.Property} is null)").AppendLine("        {");
            foreach(var cache in caches)result.AppendLine($"            if (refreshNames) {{ entity.{cache.Cache.Property} = null; submitted.Add({Literal(cache.Cache.Source.ColumnName)}); }}");
            result.AppendLine("            return;").AppendLine("        }")
                .AppendLine($"        using var values = ParseMultiDocument{suffix}(entity.{def.Source.Property});")
                .AppendLine("        if (values.RootElement.ValueKind != JsonValueKind.Array || values.RootElement.GetArrayLength() > 200) throw new ArgumentException(\"多选字段最多200个字符串ID。\");")
                .AppendLine("        var selected = values.RootElement.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 && value.GetString() == id.ToString(CultureInfo.InvariantCulture) ? id : throw new ArgumentException(\"多选ID必须为不带前导零的正整数字符串。\")).ToArray();")
                .AppendLine("        if (selected.Distinct().Count() != selected.Length) throw new ArgumentException(\"多选ID不能重复。\");");
            var parentExpression = def.SameParent ? table.Columns.Single(column => column.Source.Id == page.Scopes.First(scope => scope.Source == "parent").Column.Source.Id) : table.Columns.SingleOrDefault(column => column.JsonName == def.TargetParentField);
            if(def.SharedTextParent is not null&&def.LogicalSourceParent)
                result.AppendLine(NonRedundantCrossParentCompilation.ValidateTextParent(model,table,page,"logicalRelationParentId"));
            else if(def.SharedTextParent is not null)
                result.AppendLine(SharedRedundantParentCompilation.ValidateTextParent(model,table,page,$"entity.{parentExpression!.Property}"));
            else if (def.TargetParents.Count > 0)
            {
                result.AppendLine(def.LogicalSourceParent?"        var targetParentId = logicalRelationParentId;":$"        var targetParentId = Convert.ToString(entity.{parentExpression!.Property}, CultureInfo.InvariantCulture);")
                    .AppendLine("        if (!long.TryParse(targetParentId, NumberStyles.None, CultureInfo.InvariantCulture, out var targetParentKey) || targetParentKey <= 0) throw new ArgumentException(\"多选目标要求有效父记录。\");");
                if(def.PublishedPhysicalSource)
                {
                    if(def.CompoundSourceDependencies.Count==0&&parentExpression!.Type=="string?")
                        result.AppendLine("        if (targetParentId != targetParentKey.ToString(CultureInfo.InvariantCulture)) throw new InvalidOperationException(\"多选来源父键必须使用规范正整数。\");");
                    foreach(var peer in def.CompoundSourceDependencies)
                        result.AppendLine($"        if (Convert.ToString(entity.{peer.Property}, CultureInfo.InvariantCulture) != targetParentKey.ToString(CultureInfo.InvariantCulture)) throw new InvalidOperationException(\"多选来源父字段必须指向同一条记录。\");");
                }
                if(def.GlobalParent)result.AppendLine(MultiGlobalParentCompilation.Check("MultiGlobalParent"+suffix,"targetParentKey","true"));
                foreach (var parent in def.TargetParents.Where(_=>!def.GlobalParent))
                {
                    var project = parent.Columns.Single(column => column.Source.ColumnName == "yz_project_id");
                    var key=def.LogicalParentBusinessKey?.Property??"Id";
                    result.AppendLine($"        var parentQuery = session.Select<{parent.EntityType}>().Where(row => row.{key} == targetParentKey && row.{project.Property} == {Scope(project, "actor.ProjectId")});");
                    if (parent.Columns.Any(column => column.Source.ColumnName == "delete_no")) result.AppendLine("        parentQuery = parentQuery.Where(row => row.DeleteNo == null);");
                    result.AppendLine("        if (!await parentQuery.AnyAsync(cancellationToken)) throw new InvalidOperationException(\"多选目标父记录不存在、已删除或不属于当前项目。\");");
                }
            }
            var filters=new List<string>{"new(\"id\",FixedReadComparison.In,selected.Select(id=>(object?)id).ToArray())"};
            foreach(var scope in def.TargetPage.Scopes)
                filters.Add("new("+Literal(scope.Column.Source.ColumnName)+",FixedReadComparison.Equal,"+
                    Scope(scope.Column,scope.Source=="project"?"actor.ProjectId":"targetParentId!")+")");
            var filterArgument="new FixedReadFilter[]{"+string.Join(",",filters)+"}";
            if(scoped)
            {
                result.AppendLine("        if(selected.Length==0)").AppendLine("        {");
                foreach(var cache in caches)
                    result.AppendLine($"            if(refreshNames){{entity.{cache.Cache.Property}=\"[]\";submitted.Add({Literal(cache.Cache.Source.ColumnName)});}}");
                result.AppendLine("            return;").AppendLine("        }");
                if(def.ExactTarget is {} exact)
                {
                    filterArgument="selectedFilters";
                    result.AppendLine($"        var selectedFilters=new List<FixedReadFilter>{{{string.Join(",",filters)}}};")
                        .Append(ExactDataManageCompilation.ReadFilter(def.TargetPage,exact,Literal("list"),"multiExact","selectedFilters"));
                }
                if(caches.Count==0)
                    result.AppendLine($"        var rows=await new NonRedundantReader(session).CurrentRowsAsync(MultiDerivedPlan{suffix}.BindAccount(actor),{filterArgument},new FixedReadOrder[]{{new(\"id\")}},200,cancellationToken,actor.ProjectId);")
                        .AppendLine("        if(rows.Count!=selected.Length)throw new InvalidOperationException(\"多选项不存在、已删除或不在目标页范围，源记录未保存。\");");
            }
            else
            {
                result.AppendLine($"        var query = session.Select<{def.Target.EntityType}>().Where(row => selected.Contains(row.Id));");
                if (def.Target.Columns.Any(column => column.Source.ColumnName == "delete_no")) result.AppendLine("        query = query.Where(row => row.DeleteNo == null);");
                var targetProject = def.Target.Columns.SingleOrDefault(column => column.Source.ColumnName == "yz_project_id");
                if (targetProject is not null && (!def.Target.IsGlobal || def.TargetPage.Scopes.Any(scope => scope.Source == "project"))) result.AppendLine($"        query = query.Where(row => row.{targetProject.Property} == {Scope(targetProject, "actor.ProjectId")});");
                foreach (var scope in def.TargetPage.Scopes.Where(scope => scope.Source == "parent"))
                    result.AppendLine(ParentFilter(def,scope.Column,"        "));
                result.Append(FixedPageConditionCompilation.Apply(def.TargetPage,"MultiPageCondition"+suffix,"multi_page_"+suffix))
                    .Append(SharedRedundantParentCompilation.ApplyAccountScope(model,def.Target,def.TargetPage,"MultiAccountParentScope"+suffix,"r1_multi","true"));
                if(def.ExactTarget is not null)
                    result.AppendLine("        if(selected.Length>0){").Append(ExactQuery(model,def,"multiExact")).AppendLine("        }");
                // 所有缓存都来自本次已授权候选的同一当前锁读；ID展示页也不能退化为旧RR快照。
                result.AppendLine("        query=query.OrderBy(row=>row.Id);").AppendLine("        query=query.ForUpdate();");
                // ORM只读取真实物理值；R0逻辑名称必须走与自树相同的固定当前读，不能投影IsIgnore属性。
                var projection=string.Concat(caches.Select((cache,index)=>(cache,index)).Where(item=>!DerivedMultiNamesCompilation.Declared(model,def.Target,item.cache.Value)).Select(item=>", "+(item.index==0?"Name":"CacheName"+item.index)+" = row."+item.cache.Value.Property));
                result.AppendLine($"        var rows = await query.Limit(200).ToListAsync(row => new {{ row.Id{projection} }}, cancellationToken);")
                    .AppendLine("        if (rows.Count != selected.Length) throw new InvalidOperationException(\"多选项不存在、已删除或不在目标页范围，源记录未保存。\");");
            }
            foreach(var (cache,index) in caches.Select((cache,index)=>(cache,index)))
            {
                var variable=index==0?"names":"names"+index;var value=index==0?"Name":"CacheName"+index;
                if(scoped||DerivedMultiNamesCompilation.Declared(model,def.Target,cache.Value))
                {
                    var condition=scoped?"null":FixedPageConditionCompilation.Bound(def.TargetPage,"MultiPageCondition"+suffix);
                    var nameExpression=$"NonRedundantReads.Value<{cache.Value.Type}>(row,{Literal(cache.Value.Source.ColumnName)})";
                    nameExpression=MultiSelectExcelRenderer.LabelText(cache.Value,nameExpression);
                    result.AppendLine($"        var cacheRows{index}=await new NonRedundantReader(session).CurrentRowsAsync(MultiCachePlan{suffix}_{index}.BindAccount(actor),{filterArgument},new FixedReadOrder[]{{new(\"id\")}},200,cancellationToken,actor.ProjectId,{condition});")
                        .AppendLine($"        if(cacheRows{index}.Count!=selected.Length)throw new InvalidOperationException(\"派生多选目标当前范围已改变，源记录未保存。\");")
                        .AppendLine($"        var {variable}=cacheRows{index}.ToDictionary(row=>NonRedundantReads.Value<long>(row,\"id\"),row=>{nameExpression});");
                }
                else
                {
                    // ID 顺序、空值位置和字符串数组类型在保存、传播及回填之间一致。
                    var label=MultiSelectExcelRenderer.LabelText(cache.Value,"row."+value);
                    result.AppendLine($"        var {variable} = rows.ToDictionary(row => row.Id, row => {label});");
                }
                result.AppendLine($"        if (refreshNames) {{ entity.{cache.Cache.Property} = JsonSerializer.Serialize(selected.Select(id => {variable}[id]).ToArray()); submitted.Add({Literal(cache.Cache.Source.ColumnName)}); }}");
            }
            result.AppendLine("    }");
        }
        return result.ToString();
    }

    /// <summary>同一目标父谓词用于多选保存和 Excel；普通物理父依旧按声明字段比较。</summary>
    internal static string ParentFilter(MultiSelectModel def,ColumnModel column,string indent)=>def.SharedTextParent==column
        ?$"{indent}query=global::Metarill.Data.Queries.NonRedundantReads.ApplyTextParent(query,session,{Literal(column.Source.ColumnName)},targetParentId!,businessKey:true);"
        :$"{indent}query = query.Where(row => row.{column.Property} == {Scope(column,"targetParentId!")});";
    /// <summary>已选保存和导入复用目标列表授权，派生匹配沿该目标页的同事务当前读。</summary>
    internal static string ExactQuery(GenerationModel model,MultiSelectModel multi,string prefix)
    {
        if(multi.ExactTarget is null)return "";
        var plan=$"global::{model.Namespace}.Generated.Services.{multi.Target.Name}Service.ExactReadPlan{multi.TargetPage.Source.Id}";
        return ExactDataManageCompilation.Query(model,multi.Target,multi.TargetPage,operation:Literal("list"),
            prefix:prefix,locking:"true",planReference:plan);
    }

    /// <summary>固定逻辑类型进入强类型参数表达式。</summary>
    private static string Scope(ColumnModel column, string value) => column.Type switch { "string?" => value, "int?" => $"int.Parse({value}, CultureInfo.InvariantCulture)", _ => $"long.Parse({value}, CultureInfo.InvariantCulture)" };
    /// <summary>配置字符串只能成为生成字面量。</summary>
    private static string Literal(string text) => JsonSerializer.Serialize(text);
}
