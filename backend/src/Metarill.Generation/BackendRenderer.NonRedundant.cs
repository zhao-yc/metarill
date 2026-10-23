using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>非冗余读写分离：物理实体负责写入和当前锁，固定计划负责派生读取与回显。</summary>
internal static partial class BackendRenderer
{
    /// <summary>项目元数据仅嵌入和反序列化一次；各页只持有自己的不可变固定计划。</summary>
    private static string NonRedundantCatalog(GenerationModel model)
    {
        var metadata=JsonSerializer.Serialize(GenerationMetadataPublisher.ForRelations(model.Catalog),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var cross=model.Tables.Any(table=>table.Source.IsRongyu==0&&table.Pages.Any(page=>CrossTableWriteCompilation.ForService(model,table,page).Count>0));
        var helpers=cross?"    /// <summary>目标条件与页面一起冻结，静态初始化不读取后声明字段。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileCross(string tableId,string[] outputs,string[] dependencies,Metarill.Data.Queries.NonRedundantCrossTableScope[] bindings)=>Metarill.Data.Queries.NonRedundantReads.CompileWithCrossTables(Snapshot,tableId,outputs,bindings,dependencies,deferCascadeNames:true);\n    /// <summary>与统一物理结构共用稀疏选择编码器。</summary>\n    internal static Metarill.Core.Metadata.CrossTableSparseSelection CompileSelection(string tableId,string sourceId)=>Metarill.Core.Metadata.CrossTableSparseSelection.Compile(Snapshot.ConfigTables.Single(table=>table.Id==tableId),sourceId,Snapshot.ConfigColumns.Where(column=>column.ConfigTableId==tableId));\n":"";
        var derivedHelper=model.Tables.Any(table=>table.Pages.Any(page=>RelationCompilation.ForPage(model,page).Concat(RelationCompilation.ForPage(model,page,true)).Any(relation=>DerivedRelationNamesCompilation.Declared(model,relation))||MultiSelectCompilation.ForService(model,table,page).Any(multi=>DerivedMultiNamesCompilation.Declared(model,multi.Target,multi.Name))))?"    /// <summary>派生候选不延迟级联名称，完整标量路径在发布时固定。</summary>\n    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileRelation(string tableId,string[] outputs,string[] dependencies)=>Metarill.Data.Queries.NonRedundantReads.Compile(Snapshot,tableId,outputs,dependencies);\n":"";
        return $$"""

/// <summary>生成来源的私有只读快照，不提供外部取出或在线改写入口。</summary>
internal static class GeneratedNonRedundantReadCatalog
{
    private static readonly Metarill.Core.Metadata.MetadataCatalog Snapshot=System.Text.Json.JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>({{Literal(metadata)}},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    /// <summary>只在各页静态初始化时调用，普通请求执行已固定的SQL计划。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan Compile(string tableId,string[] outputs,string[] dependencies)=>Metarill.Data.Queries.NonRedundantReads.Compile(Snapshot,tableId,outputs,dependencies,deferCascadeNames:true);
    /// <summary>匹配字段只编译一次；主记录和实际关联来源由专用写入计划锁定。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan CompileMatch(string tableId,string field)=>Metarill.Data.Queries.NonRedundantReads.CompileMatch(Snapshot,tableId,field);
{{derivedHelper}}{{helpers}}}

""";
    }
    /// <summary>VO只声明原可读字段及其原自动显示别名，隐藏范围依赖不进入响应。</summary>
    private static string NonRedundantVo(GenerationModel model,TableModel table,PageModel page)
    {
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);var constants=page.Readable.Where(column=>column.Source.ControlKind is "ConstList" or "ConstDownList").ToArray();
        var result=new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Globalization;").AppendLine("using System.Text.Json;").AppendLine("using System.Text.Json.Serialization;").AppendLine("using Metarill.Data.Queries;").AppendLine($"using {model.Namespace}.Generated.Entities;").AppendLine($"namespace {model.Namespace}.Generated.VOs;").AppendLine($"public sealed partial class {page.Name}Vo").AppendLine("{");
        foreach(var output in plan.Outputs)
        {
            var column=page.Readable.SingleOrDefault(column=>column.Source.ColumnName==output.Storage);
            result.AppendLine($"    [JsonPropertyName({Literal(output.JsonName)})]");
            if(column is not null&&Checkbox(column))result.AppendLine("    [JsonConverter(typeof(Metarill.Core.Contracts.CheckboxBooleanJsonConverter))]");
            // 物理存储仍是字符串；原接口文件值保持JSON数组或null，不能向前端返回二次编码字符串。
            result.AppendLine($"    public {(output.IsJsonArray?"JsonElement?":output.Type)} {output.Property} {{get;{(table.Columns.Any(item=>item.Source.ColumnName==output.Storage&&item.IsManagedCascadeName)?"set":"init")};}}");
        }
        foreach(var column in constants)
        {if(plan.Outputs.Any(output=>output.Property.Equals(column.Property+"Show",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("常量显示别名冲突。");result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName+"Show")})] public string? {column.Property}Show {{get;init;}}");}
        result.Append(CascadeTreeViewRenderer.Vo(model,table,page));
        result.AppendLine("    /// <summary>固定SQL结果不扩充本页授权字段，驱动值按生成类型投影。</summary>").AppendLine($"    public static {page.Name}Vo FromReadRow(IReadOnlyDictionary<string,object?> row)=>new()").AppendLine("    {");
        foreach(var output in plan.Outputs)
        {
            var column=page.Readable.SingleOrDefault(column=>column.Source.ColumnName==output.Storage);
            var value=$"NonRedundantReads.Value<{output.Type}>(row,{Literal(output.Storage)})";
            if(output.IsJsonArray)value=column is not null&&DatabaseCheckboxCompilation.Generic(table,column)?$"Metarill.Core.Contracts.DatabaseCheckboxValue.ProjectIds(NonRedundantReads.Value<string?>(row,{Literal(output.Storage)}))":$"ReadFileValue(row,{Literal(output.Storage)})";
            result.AppendLine($"        {output.Property}={value},");
        }
        result.Append(NonRedundantCrossTableCompilation.VoAssignments(model,table,page));
        foreach(var column in constants)result.AppendLine($"        {column.Property}Show={ConstantDisplayExpression(column).Replace("entity."+column.Property,$"NonRedundantReads.Value<{column.Type}>(row,{Literal(column.Source.ColumnName)})",StringComparison.Ordinal)},");
        result.AppendLine("    };");
        if(plan.Outputs.Any(output=>output.IsJsonArray))result.AppendLine("    /// <summary>沿原VO恢复数据库JSON文本，null保持空值，避免二次编码文件ID数组。</summary>")
            .AppendLine("    private static JsonElement? ReadFileValue(IReadOnlyDictionary<string,object?> row,string storage)")
            .AppendLine("    {").AppendLine("        var json=NonRedundantReads.Value<string?>(row,storage);")
            .AppendLine("        return string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<JsonElement>(json);").AppendLine("    }");
        return result.AppendLine("}").ToString();
    }
    /// <summary>普通父范围沿物理键，稀疏跨表父范围沿固定逻辑链；公开读取与私有当前锁分开。</summary>
    private static string NonRedundantQuery(GenerationModel model,TableModel table,PageModel page)
    {
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);var suffix=page.Source.Id;var fixedQuery=FixedPageQueryCompilation.For(table,page);
        var names=SelectionNameFilterCompilation.ForPage(model,table,page);var nameKeys=names.ConsumedKeys.Concat(names.Gaps.Select(gap=>gap.FilterKey)).ToHashSet(StringComparer.Ordinal);
        var outputs="new string[]{"+string.Join(",",plan.ReadOutputs.Select(Literal))+"}";var dependencies="new string[]{"+string.Join(",",plan.Dependencies.Select(Literal))+"}";
        var result=new StringBuilder().Append(FixedPageConditionCompilation.Declaration(page,"PageCondition"+suffix)).Append(NonRedundantCrossParentCompilation.Service(model,table,page));
        if(ExactDataManageCompilation.Resolve(model,table,page) is {Derived:true} exact)
            result.AppendLine("    /// <summary>匹配计划只含当前范围所需路径，避免锁住无关输出关系。</summary>")
                .AppendLine($"    private static readonly NonRedundantReadPlan ExactReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileMatch({Literal(table.Source.Id)},{Literal(exact.Column.Source.ColumnName)});");
        result.AppendLine("    /// <summary>生成校验和宿主静态初始化分别编译；普通请求不解释元数据。</summary>")
            .AppendLine($"    private static readonly NonRedundantReadPlan ReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.{(CrossTableWriteCompilation.ForService(model,table,page).Count>0?"CompileCross":"Compile")}({Literal(table.Source.Id)},{outputs},{dependencies}{NonRedundantCrossTableCompilation.BindingArgument(model,table,page)});")
            .AppendLine(NonRedundantCrossParentCompilation.Enabled(model,table,page)?"    /// <summary>更新前按真实项目及物理／逻辑父范围锁定主记录。</summary>":"    /// <summary>更新前仅按真实物理项目/父范围锁定主记录。</summary>").AppendLine($"    private ISelect<{table.Name}> Scope{suffix}(RuntimeActor actor,string? parentId{ExactDataManageCompilation.Parameter(page,"detail")}{(NonRedundantCrossParentCompilation.Enabled(model,table,page)?",bool locking=false":"")})").AppendLine("    {").AppendLine("        ValidateActor(actor);var query=mapper.Query();");
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {if(source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,table,page,column))continue;if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");result.AppendLine($"        query=query.Where(entity=>entity.{column.Property}=={ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")});");}
        if(NonRedundantCrossParentCompilation.Enabled(model,table,page))result.AppendLine($"        query=LogicalParentScope{suffix}(query,actor,parentId,locking);");
        result.Append(FixedPageConditionCompilation.Apply(page,"PageCondition"+suffix,"page_"+suffix)).Append(ExactDataManageCompilation.Query(model,table,page)).AppendLine("        return query;").AppendLine("    }").AppendLine("    /// <summary>身份与父范围是服务附加的AND条件，请求筛选不能覆盖。</summary>").AppendLine($"    private {(ExactDataManageCompilation.Declared(page)?"":"static ")}List<FixedReadFilter> ReadFilters{suffix}(PageRequest request,RuntimeActor actor{ExactDataManageCompilation.Parameter(page,"list")})").AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);ValidateActor(actor);var result=new List<FixedReadFilter>();");
        if(NonRedundantCrossParentCompilation.Enabled(model,table,page))result.AppendLine($"        _=LogicalParentId{suffix}(request.ParentId);");
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(request.ParentId))throw new ArgumentException(\"当前页面要求parentId。\");");result.AppendLine($"        result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{ScopeValue(column,source=="project"?"actor.ProjectId":"request.ParentId!")}));");}
        result.Append(ExactDataManageCompilation.ReadFilter(model,table,page));
        result.AppendLine("        foreach(var filter in request.Filters??throw new ArgumentException(\"filters不能为空。\"))").AppendLine("        {").AppendLine("            switch(filter.Key)").AppendLine("            {");
        // 名称辅助键在SQL分页/统计之前解析；不能把IsIgnore实体列当持久化列查询。
        foreach(var binding in names.Bindings)
        {
            if(binding.Kind is not(SelectionNameFilterKind.JoinedScalarLike or SelectionNameFilterKind.JoinedJsonArrayAnyLike))throw new InvalidOperationException("非冗余名称筛选缺少固定联接证明。");
            result.AppendLine($"                case {Literal(binding.FilterKey)}:")
                .AppendLine("                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString()))break;")
                .AppendLine("                    if(filter.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException(\"名称筛选只接受文本。\");")
                .AppendLine($"                    result.Add(new({Literal(binding.StorageColumn.Source.ColumnName)},FixedReadComparison.{(binding.Kind==SelectionNameFilterKind.JoinedJsonArrayAnyLike?"JsonArrayAnyContains":"Contains")},filter.Value.GetString()!));break;");
        }
        foreach(var group in names.Gaps.GroupBy(gap=>gap.FilterKey,StringComparer.Ordinal))
            result.AppendLine($"                case {Literal(group.Key)}:")
                .AppendLine("                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString()))break;")
                .AppendLine($"                    throw new ArgumentException({Literal(string.Join("；",group.Select(gap=>gap.Reason).Distinct(StringComparer.Ordinal)))});");
        foreach(var column in page.Searchable.Where(column=>!nameKeys.Contains(column.JsonName)))
        {
            result.AppendLine($"                case {Literal(column.JsonName)}:").AppendLine("                {").AppendLine("                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString())||filter.Value.ValueKind==JsonValueKind.Array&&filter.Value.GetArrayLength()==0)break;");
            if(DatabaseCheckboxCompilation.Generic(table,column))result.AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.JsonArrayHasId,Metarill.Core.Contracts.DatabaseCheckboxValue.ReadId(filter.Value)));break;");
            else if(column.IsManagedMultiNames&&DatabaseCheckboxCompilation.ForService(model,table,page).Any(def=>def.Names.Source.Id==column.Source.Id))result.AppendLine("                    if(filter.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException(\"复选名称筛选只接受文本。\");").AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.JsonArrayAnyContains,filter.Value.GetString()!));break;");
            else if(column.Source.ControlKind=="CascadeTree")result.AppendLine("                    break;");
            else if(column.Type=="DateTime?")
                result.AppendLine($"                    if(filter.Value.ValueKind==JsonValueKind.Array){{var range=PageDateRange.Parse(filter.Value);result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.GreaterOrEqual,range.Start));result.Add(new({Literal(column.Source.ColumnName)},range.EndExclusive?FixedReadComparison.Less:FixedReadComparison.LessOrEqual,range.End));break;}}")
                    .AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,DateTime.Parse(filter.Value.GetString()!,CultureInfo.InvariantCulture)));break;");
            else
            {
                var parse=Checkbox(column)?"CheckboxBooleanJsonConverter.Parse(filter.Value)":$"JsonSerializer.Deserialize<{column.Type}>(filter.Value.GetRawText(),JsonOptions)";
                var parseArray=Checkbox(column)?"filter.Value.EnumerateArray().Select(CheckboxBooleanJsonConverter.Parse).Cast<object?>().ToArray()":$"JsonSerializer.Deserialize<{column.Type}[]>(filter.Value.GetRawText(),JsonOptions)!.Cast<object?>().ToArray()";
                var field=model.Catalog.ConfigPageColumns.Single(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==column.Source.Id);var match=field.Extra.TryGetValue("searchType",out var item)?item.ToString():field.Extra.TryGetValue("matchType",out item)?item.ToString():"";
                var like=column.Type=="string?"&&(match=="like"||match.Length==0&&column.Source.ControlKind is not("List" or "ConstList" or "ConstDownList" or "SelectPage"));
                result.AppendLine($"                    if(filter.Value.ValueKind==JsonValueKind.Array){{result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.In,{parseArray}));break;}}")
                    .AppendLine($"                    result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.{(like?"Contains":"Equal")},{parse}));break;");
            }
            result.AppendLine("                }");
        }
        result.AppendLine("                default:throw new ArgumentException(\"页面未开放该筛选字段：\"+filter.Key);").AppendLine("            }").AppendLine("        }").AppendLine("        if(request.SelectedIds is null)throw new ArgumentException(\"selectedIds不能为空。\");")
            .AppendLine(SelectionPageRenderer.Enabled(page)?"        if(request.SelectedIds.Count>200||request.PageNo>1000000)throw new ArgumentException(\"多选页最多200个已选项，页码最多1000000。\");":"")
            .AppendLine("        if(request.SelectedIds.Count>0)result.Add(new(\"id\",FixedReadComparison.In,request.SelectedIds.Select(value=>(object?)long.Parse(value,CultureInfo.InvariantCulture)).ToArray()));")
            .AppendLine("        return result;").AppendLine("    }").AppendLine("    /// <summary>请求排序键仍由原页面开放列控制，默认排序保持元数据序列。</summary>").AppendLine($"    private static IReadOnlyList<FixedReadOrder> ReadOrder{suffix}(PageRequest request)").AppendLine("    {")
            .AppendLine("        if(request.OrderBy is not null&&!string.IsNullOrWhiteSpace(request.OrderBy.Order))").AppendLine("        {").AppendLine("            var descending=request.OrderBy.Order switch{\"descending\"=>true,\"ascending\"=>false,_=>throw new ArgumentException(\"排序方向无效。\")};")
            .AppendLine("            return (request.OrderBy.Column??request.OrderBy.Field) switch").AppendLine("            {");
        foreach(var column in page.Sortable)result.AppendLine($"                {Literal(column.JsonName)}=>new FixedReadOrder[]{{new({Literal(column.Source.ColumnName)},descending)}},");
        result.AppendLine("                _=>throw new ArgumentException(\"页面未开放该排序字段。\")").AppendLine("            };").AppendLine("        }")
            .AppendLine("        return new FixedReadOrder[]{"+string.Join(",",fixedQuery.DefaultOrder.Select(order=>$"new({Literal(order.Column.Source.ColumnName)},{order.Descending.ToString().ToLowerInvariant()})"))+"};").AppendLine("    }")
            .AppendLine("    /// <summary>固定SQL完成范围/筛选/排序/count/分页，再投影原VO。</summary>").AppendLine($"    public {(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"":"async ")}Task<PageResult<{page.Name}Vo>> Page{suffix}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken){(CascadeTreeQueryCompilation.Snapshot(model,table,page)?$"=>session.InReadSnapshotAsync<PageResult<{page.Name}Vo>>(async()=>":"")}").AppendLine("    {")
            .AppendLine("        if(request.PageNo<1||request.PageSize<0||request.PageSize>1000)throw new ArgumentException(\"分页参数无效，每页最多1000条。\");")
            .AppendLine($"        var unpaged=request.PageSize==0&&{(page.Source.PageSize==0).ToString().ToLowerInvariant()};")
            .AppendLine("        if(request.PageSize==0&&(!unpaged||request.PageNo!=1))throw new ArgumentException(\"仅已配置不分页的页面允许pageSize=0，且页码必须为1。\");")
            .AppendLine(SelectionPageRenderer.Enabled(page)?$"        await ValidateSelectionParents{suffix}Async(request.ParentId,actor,cancellationToken);":"")
            .AppendLine($"        var rows=await new NonRedundantReader(session).PageAsync(ReadPlan{suffix},{NonRedundantFilterCall(model,table,page)},ReadOrder{suffix}(request),request.PageNo,request.PageSize,cancellationToken,unpaged,actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page)});")
            .AppendLine($"        return new({NonRedundantProjectionCall(model,table,page,"rows.Records","false")},rows.Total,unpaged?0:(rows.Total+request.PageSize-1)/request.PageSize,request.PageNo,request.PageSize);").AppendLine(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"    },cancellationToken);":"    }")
            .AppendLine("    /// <summary>数量不读取明细，不依赖请求分页或排序。</summary>").AppendLine($"    public {(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"":"async ")}Task<long> Count{suffix}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken){(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"=>session.InReadSnapshotAsync<long>(async()=>":"")} {{ {(SelectionPageRenderer.Enabled(page)?$"await ValidateSelectionParents{suffix}Async(request.ParentId,actor,cancellationToken);":"")} return (await new NonRedundantReader(session).StatisticsAsync(ReadPlan{suffix},{NonRedundantFilterCall(model,table,page)},Array.Empty<string>(),cancellationToken,actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page)})).Total; }}{(CascadeTreeQueryCompilation.Snapshot(model,table,page)?",cancellationToken);":"")}")
            .AppendLine("    /// <summary>详情及写后回显使用相同真实项目/父范围，不额外授予list权限。</summary>").AppendLine($"    public Task<{page.Name}Vo?> Detail{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken)=>{(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"session.InReadSnapshotAsync(()=>":"")}ReadDetail{suffix}Async(id,parentId,actor,cancellationToken,false){(CascadeTreeQueryCompilation.Snapshot(model,table,page)?",cancellationToken)":"")};")
            .AppendLine("    /// <summary>私有写后当前读不暴露HTTP开关，防止旧事务快照覆盖已经验证的名称。</summary>")
            .AppendLine($"    private async Task<{page.Name}Vo?> ReadDetail{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken,bool currentRead)").AppendLine("    {")
            .AppendLine(SelectionPageRenderer.Enabled(page)?$"        await ValidateSelectionParents{suffix}Async(parentId,actor,cancellationToken);":"")
            .AppendLine($"        var filters=ReadFilters{suffix}(new PageRequest{{ParentId=parentId}},actor);filters.Add(new(\"id\",FixedReadComparison.Equal,id));")
            .AppendLine($"        var rows=await new NonRedundantReader(session).PageAsync(ReadPlan{suffix},filters,Array.Empty<FixedReadOrder>(),1,1,cancellationToken,projectId:actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page,true)},currentRead:currentRead);")
            .AppendLine($"        return rows.Records.Count==0?null:{NonRedundantProjectionCall(model,table,page,"rows.Records","currentRead",true)};").AppendLine("    }");
        if(ExactDataManageCompilation.Declared(page)||page.Condition is not null)
        {
            result.AppendLine("    /// <summary>只供已授权且已写入的对象回显；固定主键/项目/父与实际事务，不能由HTTP选择此入口。</summary>")
                .AppendLine($"    private async Task<{page.Name}Vo?> MutationResult{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken)")
                .AppendLine("    {")
                .AppendLine("        ValidateActor(actor);if(session.CurrentTransaction is null)throw new InvalidOperationException(\"写后回显必须使用实际写入事务。\");")
                .AppendLine("        var filters=new List<FixedReadFilter>{new(\"id\",FixedReadComparison.Equal,id)};");
            foreach(var (column,source) in fixedQuery.IdentityScopes)
            {
                if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");
                result.AppendLine($"        filters.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")}));");
            }
            result.AppendLine($"        var rows=await new NonRedundantReader(session).PageAsync(ReadPlan{suffix},filters,Array.Empty<FixedReadOrder>(),1,1,cancellationToken,projectId:actor.ProjectId,currentRead:true);")
                .AppendLine($"        return rows.Records.Count==0?null:{NonRedundantProjectionCall(model,table,page,"rows.Records","true",true)};").AppendLine("    }");
        }
        result.Append(NonRedundantCascadeFilters(model,table,page)).Append(NonRedundantCascadeProjection(model,table,page));
        return result.ToString();
    }
    /// <summary>原map.statistics及百分比口径保持，统计谓词使用未分页的同一读取范围。</summary>
    internal static string NonRedundantStatistics(GenerationModel model,TableModel table,PageModel page)
    {
        var definitions=OriginalListStatisticsCompilation.ForPage(model,table,page);if(definitions is null)return "";
        var suffix=page.Source.Id;var result=new StringBuilder().AppendLine("    /// <summary>派生值在SQL完整范围内聚合，当前页统计仍折叠原VO。</summary>").AppendLine($"    internal {(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"Task":"async Task")}<Dictionary<string,object?>> Statistics{suffix}Async(PageRequest request,RuntimeActor actor,PageResult<{page.Name}Vo> page,CancellationToken cancellationToken){(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"=>session.InReadSnapshotAsync<Dictionary<string,object?>>(async()=>":"")}").AppendLine("    {").AppendLine("        var statistics=new Dictionary<string,object?>(StringComparer.Ordinal);");
        if(definitions.Count==0)result.AppendLine("        await Task.CompletedTask;");
        foreach(var definition in definitions)
        {
            var column=definition.Column;var key=Literal(column.JsonName);var local="stat"+column.Property;
            if(definition.Kind==1)
            {var current=column.Type.EndsWith('?')?"(decimal)(row."+column.Property+"??0)":"(decimal)row."+column.Property;result.AppendLine($"        var {local}=await new NonRedundantReader(session).StatisticsAsync(ReadPlan{suffix},{NonRedundantFilterCall(model,table,page)},new string[]{{{Literal(column.Source.ColumnName)}}},cancellationToken,actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page)});").AppendLine($"        statistics[{key}]=new{{currentPage=page.Records.Sum(row=>{current}),total={local}.Sums[{Literal(column.Source.ColumnName)}]??0m}};");}
            else
            {result.AppendLine($"        var {local}=new Dictionary<string,object?>();if(page.Records.Count>0){local}[\"currentPage\"]=LegacyStatisticsMath.Percentage(page.Records.LongCount(row=>row.{column.Property}==2),page.Records.Count);").AppendLine($"        if(page.Total>0){{var filters={NonRedundantFilterCall(model,table,page)};filters.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,2));var matches=await new NonRedundantReader(session).StatisticsAsync(ReadPlan{suffix},filters,Array.Empty<string>(),cancellationToken,actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page)});{local}[\"total\"]=LegacyStatisticsMath.Percentage(matches.Total,page.Total);}}").AppendLine($"        statistics[{key}]={local};");}
        }
        return result.AppendLine("        return statistics;").AppendLine(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"    },cancellationToken);":"    }").ToString();
    }
}
