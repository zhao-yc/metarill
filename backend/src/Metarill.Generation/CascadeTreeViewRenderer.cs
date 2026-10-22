using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>原列表的DisplayText投影和节点对象筛选，不替换任何前端控件。</summary>
internal static class CascadeTreeViewRenderer
{
    /// <summary>只开放已编译的可读字段，并提前拒绝显示别名与真实字段重名。</summary>
    internal static IReadOnlyList<CascadeTreeReadModel> ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        var plans=CascadeTreeReadCompilation.ForPage(model,table,page).Where(plan=>page.Readable.Any(column=>column.Source.Id==plan.Source.Source.Id)).ToArray();
        foreach(var plan in plans)if(table.Columns.Any(column=>column.Property.Equals(plan.Source.Property+"DisplayText",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("级联显示别名与物理字段冲突。");
        return plans;
    }
    /// <summary>原级联ID隐式授权同前缀叶子名称；只使用已声明受管物理列，不扩大其它隐藏列。</summary>
    internal static IReadOnlyList<ColumnModel> Names(GenerationModel model,TableModel table,PageModel page)=>ForPage(model,table,page)
        .Where(plan=>plan.Source.Type is "string?" or "long?").Select(plan=>Name(table,plan.Source)).OfType<ColumnModel>().DistinctBy(column=>column.Source.Id).ToArray();
    /// <summary>名称归属复用写入规则，不能把任意名称字段或虚拟属性当叶子名称。</summary>
    internal static ColumnModel? Name(TableModel table,ColumnModel source)=>source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)
        ?CascadeTreeWriteCompilation.ManagedNames(table).SingleOrDefault(column=>column.Source.ColumnName==source.Source.ColumnName[..^3]+"_name"):null;
    /// <summary>只补原页面隐式名称，不将它加入Writable、页面布局或搜索列。</summary>
    internal static IReadOnlyList<ColumnModel> ImplicitNames(GenerationModel model,TableModel table,PageModel page)=>Names(model,table,page).Where(name=>!page.Readable.Any(column=>column.Source.Id==name.Source.Id)).ToArray();
    /// <summary>沿原JSON键输出完整路径；只能由后端装饰，不能进入写DTO。</summary>
    internal static string Vo(GenerationModel model,TableModel table,PageModel page)=>string.Concat(ForPage(model,table,page).Select(plan=>$"    [JsonPropertyName({Literal(plan.Source.JsonName+"DisplayText")})] public string? {plan.Source.Property}DisplayText {{ get; set; }}\n"))+CrossTableDisplayRenderer.Vo(model,table,page);
    /// <summary>结果只来自已授权来源页，目标继续应用项目/软删/固定条件，不增加目标列表操作门槛。</summary>
    internal static string Decorate(GenerationModel model,TableModel table,PageModel page,string expression,string entities)=>CrossTableDisplayRenderer.Decorate(model,table,page,ForPage(model,table,page).Count==0?expression:$"await DecorateCascade{page.Source.Id}Async({expression}, {entities}, actor, cancellationToken)",entities);
    /// <summary>写响应使用当前读保持已验证的新名称；普通详情沿读取快照投影。</summary>
    internal static string One(GenerationModel model,TableModel table,PageModel page,string expression,string operation)=>CrossTableDisplayRenderer.One(model,table,page,ForPage(model,table,page).Count==0?expression:$"(await DecorateCascade{page.Source.Id}Async(new[] {{ {expression} }}, new[] {{ entity }}, actor, cancellationToken, {(operation is "create" or "update" or "copy"?"true":"false")}))[0]");

    /// <summary>原节点对象转换为固定ID集合；名称仅回显，未证明定义不能降级为字符串LIKE。</summary>
    internal static string Filter(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        // 原跨表只匹配已存层级ID，不能套用目标图或后代展开。
        if(source.IsFixedCrossTableFilter&&CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is{} cross)return CrossTableFilterRenderer.Filter(cross,page);
        var resolved=CascadeTreeReadCompilation.Resolve(model,table,page,source);
        if(resolved.Definition is not{} plan||source.Type is not("string?" or "long?")||!source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))return $"                    throw new ArgumentException({Literal(resolved.Reason??"级联筛选字段类型尚未支持。")});\n";
        var suffix=page.Source.Id+"_"+source.Source.Id;var key=source.JsonName;var name=GenerationModel.Camel(source.Source.ColumnName[..^3]+"_name");
        return $"                    var selected=Metarill.Core.Cascades.CascadeTreeGraphReader.FilterId(filter.Value,{Literal(key)},{Literal(name)});if(selected is null)break;\n"+
            $"                    var ids=await CascadeGraph{suffix}(actor).FilterIdsAsync(selected.Value,{plan.Exact.ToString().ToLowerInvariant()},cancellationToken);\n"+
            (source.Type=="string?"?$"                    var keys=ids.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray();query=query.Where(entity=>keys.Contains(entity.{source.Property}!));\n":$"                    var keys=ids.ToArray();query=query.Where(entity=>entity.{source.Property}!=null&&keys.Contains(entity.{source.Property}.Value));\n");
    }
    /// <summary>批量查询固定三列，逐父批和主键游标保证宽树不受候选500项上限截断。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder(CrossTableDisplayRenderer.Service(model,table,page)+CrossTableFilterRenderer.Service(model,table,page));
        foreach(var plan in CascadeTreeReadCompilation.ForService(model,table,page))
        {
            var suffix=page.Source.Id+"_"+plan.Source.Source.Id;var target=plan.Target;var parent=plan.Parent;var name=plan.Name;
            var projection=$"row=>new {target.Name}{{Id=row.Id,{name.Property}=row.{name.Property},{parent.Property}=row.{parent.Property}}}";
            var parentValue=parent.Type=="string?"?"row."+parent.Property:"row."+parent.Property+"?.ToString(CultureInfo.InvariantCulture)";
            var map=$"rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{name.Property},{parentValue})).ToArray()";
            result.AppendLine("    /// <summary>只有来源页已授权读取或筛选可以进入此私有固定图，不暴露动态查询网关。</summary>")
                .AppendLine($"    private Metarill.Core.Cascades.CascadeTreeGraphReader CascadeGraph{suffix}(RuntimeActor actor,bool currentRead=false)=>new(CascadeReader{suffix}(actor,currentRead,false,false),")
                .AppendLine($"        async(ids,ct)=>{{var keys=ids.ToArray();var query=CascadeScope{suffix}(actor,false,false).Where(row=>keys.Contains(row.Id));if(currentRead)query=query.ForUpdate();var rows=await query.ToListAsync({projection},ct);return {map};}},")
                .AppendLine(parent.Type=="string?"?$"        async(parents,after,take,ct)=>{{var keys=parents.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray();var rows=await CascadeScope{suffix}(actor,false,false).Where(row=>row.Id>after&&keys.Contains(row.{parent.Property}!)).OrderBy(row=>row.Id).Limit(take).ToListAsync({projection},ct);return {map};}},":$"        async(parents,after,take,ct)=>{{var keys=parents.ToArray();var rows=await CascadeScope{suffix}(actor,false,false).Where(row=>row.Id>after&&row.{parent.Property}!=null&&keys.Contains(row.{parent.Property}.Value)).OrderBy(row=>row.Id).Limit(take).ToListAsync({projection},ct);return {map};}},")
                .AppendLine($"        {Literal(plan.Joiner)});");
        }
        var plans=ForPage(model,table,page);if(plans.Count==0)return result.ToString();
        result.AppendLine("    /// <summary>同页多行共用祖先缓存；坏关联只显示空文本，不丢原ID，不返回部分祖先。</summary>")
            .AppendLine($"    private async Task<IReadOnlyList<{page.Name}Vo>> DecorateCascade{page.Source.Id}Async(IReadOnlyList<{page.Name}Vo> values,IReadOnlyList<{table.Name}> entities,RuntimeActor actor,CancellationToken cancellationToken,bool currentRead=false)")
            .AppendLine("    {").AppendLine("        if(values.Count!=entities.Count)throw new InvalidOperationException(\"级联投影记录数不符。\");");
        foreach(var plan in plans)
        {
            var source=plan.Source;var suffix=page.Source.Id+"_"+source.Source.Id;
            result.AppendLine($"        var ids{source.Source.Id}=entities.Select(entity=>Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture)).Select(text=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0?id:0).Where(id=>id>0).Distinct().ToArray();")
                .AppendLine($"        var paths{source.Source.Id}=await CascadeGraph{suffix}(actor,currentRead).DisplayAsync(ids{source.Source.Id},cancellationToken);")
                .AppendLine($"        for(var index=0;index<values.Count;index++){{var text=Convert.ToString(entities[index].{source.Property},CultureInfo.InvariantCulture);values[index].{source.Property}DisplayText=long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&paths{source.Source.Id}.TryGetValue(id,out var path)?path:\"\";}}");
        }
        return result.AppendLine("        return values;").AppendLine("    }").ToString();
    }
    /// <summary>配置文本只作为转义后的C#字面量。</summary>
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
}
