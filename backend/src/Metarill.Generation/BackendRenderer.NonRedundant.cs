using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>非冗余读写分离：物理实体负责写入和当前锁，固定计划负责派生读取与回显。</summary>
internal static partial class BackendRenderer
{
    /// <summary>项目元数据仅嵌入和反序列化一次；各页只持有自己的不可变固定计划。</summary>
    private static string NonRedundantCatalog(GenerationModel model)
    {
        var metadata=JsonSerializer.Serialize(model.Catalog,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $$"""

/// <summary>生成来源的私有只读快照，不提供外部取出或在线改写入口。</summary>
internal static class GeneratedNonRedundantReadCatalog
{
    private static readonly Metarill.Core.Metadata.MetadataCatalog Snapshot=System.Text.Json.JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>({{Literal(metadata)}},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    /// <summary>只在各页静态初始化时调用，普通请求执行已固定的SQL计划。</summary>
    internal static Metarill.Data.Queries.NonRedundantReadPlan Compile(string tableId,string[] outputs,string[] dependencies)=>Metarill.Data.Queries.NonRedundantReads.Compile(Snapshot,tableId,outputs,dependencies);
}

""";
    }
    /// <summary>VO只声明原可读字段及其原自动显示别名，隐藏范围依赖不进入响应。</summary>
    private static string NonRedundantVo(GenerationModel model,TableModel table,PageModel page)
    {
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);var constants=page.Readable.Where(column=>column.Source.ControlKind is "ConstList" or "ConstDownList").ToArray();
        var result=new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Globalization;").AppendLine("using System.Text.Json.Serialization;").AppendLine("using Metarill.Data.Queries;").AppendLine($"using {model.Namespace}.Generated.Entities;").AppendLine($"namespace {model.Namespace}.Generated.VOs;").AppendLine($"public sealed class {page.Name}Vo").AppendLine("{");
        foreach(var output in plan.Outputs)
        {result.AppendLine($"    [JsonPropertyName({Literal(output.JsonName)})]");if(page.Readable.Any(column=>column.JsonName==output.JsonName&&Checkbox(column)))result.AppendLine("    [JsonConverter(typeof(Metarill.Core.Contracts.CheckboxBooleanJsonConverter))]");result.AppendLine($"    public {output.Type} {output.Property} {{get;init;}}");}
        foreach(var column in constants)
        {if(plan.Outputs.Any(output=>output.Property.Equals(column.Property+"Show",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("常量显示别名冲突。");result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName+"Show")})] public string? {column.Property}Show {{get;init;}}");}
        result.AppendLine("    /// <summary>固定SQL结果不扩充本页授权字段，驱动值按生成类型投影。</summary>").AppendLine($"    public static {page.Name}Vo FromReadRow(IReadOnlyDictionary<string,object?> row)=>new()").AppendLine("    {");
        foreach(var output in plan.Outputs)result.AppendLine($"        {output.Property}=NonRedundantReads.Value<{output.Type}>(row,{Literal(output.Storage)}),");
        foreach(var column in constants)result.AppendLine($"        {column.Property}Show={ConstantDisplayExpression(column).Replace("entity."+column.Property,$"NonRedundantReads.Value<{column.Type}>(row,{Literal(column.Source.ColumnName)})",StringComparison.Ordinal)},");
        return result.AppendLine("    };").AppendLine("}").ToString();
    }
    /// <summary>保留原物理范围给更新/删除锁定，读取使用另一个预编译计划，不对联接结果申请对象写锁。</summary>
    private static string NonRedundantQuery(GenerationModel model,TableModel table,PageModel page)
    {
        var plan=NonRedundantReadCompilation.ForPage(model,table,page);var suffix=page.Source.Id;var fixedQuery=FixedPageQueryCompilation.For(table,page);
        var outputs="new string[]{"+string.Join(",",plan.Outputs.Select(output=>Literal(output.Storage)))+"}";var dependencies="new string[]{"+string.Join(",",plan.Dependencies.Select(Literal))+"}";
        var result=new StringBuilder().AppendLine("    /// <summary>生成校验和宿主静态初始化分别编译；普通请求不解释元数据。</summary>")
            .AppendLine($"    private static readonly NonRedundantReadPlan ReadPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.Compile({Literal(table.Source.Id)},{outputs},{dependencies});")
            .AppendLine("    /// <summary>更新前仅按真实物理项目/父范围锁定主记录。</summary>").AppendLine($"    private ISelect<{table.Name}> Scope{suffix}(RuntimeActor actor,string? parentId)").AppendLine("    {").AppendLine("        ValidateActor(actor);var query=mapper.Query();");
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");result.AppendLine($"        query=query.Where(entity=>entity.{column.Property}=={ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")});");}
        result.AppendLine("        return query;").AppendLine("    }").AppendLine("    /// <summary>身份与父范围是服务附加的AND条件，请求筛选不能覆盖。</summary>").AppendLine($"    private static List<FixedReadFilter> ReadFilters{suffix}(PageRequest request,RuntimeActor actor)").AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);ValidateActor(actor);var result=new List<FixedReadFilter>();");
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(request.ParentId))throw new ArgumentException(\"当前页面要求parentId。\");");result.AppendLine($"        result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{ScopeValue(column,source=="project"?"actor.ProjectId":"request.ParentId!")}));");}
        result.AppendLine("        foreach(var filter in request.Filters??throw new ArgumentException(\"filters不能为空。\"))").AppendLine("        {").AppendLine("            switch(filter.Key)").AppendLine("            {");
        foreach(var column in page.Searchable)
        {
            result.AppendLine($"                case {Literal(column.JsonName)}:").AppendLine("                {").AppendLine("                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString())||filter.Value.ValueKind==JsonValueKind.Array&&filter.Value.GetArrayLength()==0)break;");
            if(column.Type=="DateTime?")
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
            .AppendLine("    /// <summary>固定SQL完成范围/筛选/排序/count/分页，再投影原VO。</summary>").AppendLine($"    public async Task<PageResult<{page.Name}Vo>> Page{suffix}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine("        if(request.PageNo<1||request.PageSize<0||request.PageSize>1000)throw new ArgumentException(\"分页参数无效，每页最多1000条。\");")
            .AppendLine($"        var unpaged=request.PageSize==0&&{(page.Source.PageSize==0).ToString().ToLowerInvariant()};")
            .AppendLine("        if(request.PageSize==0&&(!unpaged||request.PageNo!=1))throw new ArgumentException(\"仅已配置不分页的页面允许pageSize=0，且页码必须为1。\");")
            .AppendLine(SelectionPageRenderer.Enabled(page)?$"        await ValidateSelectionParents{suffix}Async(request.ParentId,actor,cancellationToken);":"")
            .AppendLine($"        var rows=await new NonRedundantReader(session).PageAsync(ReadPlan{suffix},ReadFilters{suffix}(request,actor),ReadOrder{suffix}(request),request.PageNo,request.PageSize,cancellationToken,unpaged,actor.ProjectId);")
            .AppendLine($"        return new(rows.Records.Select({page.Name}Vo.FromReadRow).ToArray(),rows.Total,unpaged?0:(rows.Total+request.PageSize-1)/request.PageSize,request.PageNo,request.PageSize);").AppendLine("    }")
            .AppendLine("    /// <summary>数量不读取明细，不依赖请求分页或排序。</summary>").AppendLine($"    public async Task<long> Count{suffix}Async(PageRequest request,RuntimeActor actor,CancellationToken cancellationToken) {{ {(SelectionPageRenderer.Enabled(page)?$"await ValidateSelectionParents{suffix}Async(request.ParentId,actor,cancellationToken);":"")} return (await new NonRedundantReader(session).StatisticsAsync(ReadPlan{suffix},ReadFilters{suffix}(request,actor),Array.Empty<string>(),cancellationToken,actor.ProjectId)).Total; }}")
            .AppendLine("    /// <summary>详情及写后回显使用相同真实项目/父范围，不额外授予list权限。</summary>").AppendLine($"    public async Task<{page.Name}Vo?> Detail{suffix}Async(long id,string? parentId,RuntimeActor actor,CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine(SelectionPageRenderer.Enabled(page)?$"        await ValidateSelectionParents{suffix}Async(parentId,actor,cancellationToken);":"")
            .AppendLine($"        var filters=ReadFilters{suffix}(new PageRequest{{ParentId=parentId}},actor);filters.Add(new(\"id\",FixedReadComparison.Equal,id));")
            .AppendLine($"        var rows=await new NonRedundantReader(session).PageAsync(ReadPlan{suffix},filters,Array.Empty<FixedReadOrder>(),1,1,cancellationToken,projectId:actor.ProjectId);")
            .AppendLine($"        return rows.Records.Count==0?null:{page.Name}Vo.FromReadRow(rows.Records[0]);").AppendLine("    }");
        return result.ToString();
    }
    /// <summary>原map.statistics及百分比口径保持，统计谓词使用未分页的同一读取范围。</summary>
    internal static string NonRedundantStatistics(GenerationModel model,TableModel table,PageModel page)
    {
        var definitions=OriginalListStatisticsCompilation.ForPage(model,table,page);if(definitions is null)return "";
        var suffix=page.Source.Id;var result=new StringBuilder().AppendLine("    /// <summary>派生值在SQL完整范围内聚合，当前页统计仍折叠原VO。</summary>").AppendLine($"    internal async Task<Dictionary<string,object?>> Statistics{suffix}Async(PageRequest request,RuntimeActor actor,PageResult<{page.Name}Vo> page,CancellationToken cancellationToken)").AppendLine("    {").AppendLine("        var statistics=new Dictionary<string,object?>(StringComparer.Ordinal);");
        if(definitions.Count==0)result.AppendLine("        await Task.CompletedTask;");
        foreach(var definition in definitions)
        {
            var column=definition.Column;var key=Literal(column.JsonName);var local="stat"+column.Property;
            if(definition.Kind==1)
            {var current=column.Type.EndsWith('?')?"(decimal)(row."+column.Property+"??0)":"(decimal)row."+column.Property;result.AppendLine($"        var {local}=await new NonRedundantReader(session).StatisticsAsync(ReadPlan{suffix},ReadFilters{suffix}(request,actor),new string[]{{{Literal(column.Source.ColumnName)}}},cancellationToken,actor.ProjectId);").AppendLine($"        statistics[{key}]=new{{currentPage=page.Records.Sum(row=>{current}),total={local}.Sums[{Literal(column.Source.ColumnName)}]??0m}};");}
            else
            {result.AppendLine($"        var {local}=new Dictionary<string,object?>();if(page.Records.Count>0){local}[\"currentPage\"]=LegacyStatisticsMath.Percentage(page.Records.LongCount(row=>row.{column.Property}==2),page.Records.Count);").AppendLine($"        if(page.Total>0){{var filters=ReadFilters{suffix}(request,actor);filters.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,2));var matches=await new NonRedundantReader(session).StatisticsAsync(ReadPlan{suffix},filters,Array.Empty<string>(),cancellationToken,actor.ProjectId);{local}[\"total\"]=LegacyStatisticsMath.Percentage(matches.Total,page.Total);}}").AppendLine($"        statistics[{key}]={local};");}
        }
        return result.AppendLine("        return statistics;").AppendLine("    }").ToString();
    }
}
