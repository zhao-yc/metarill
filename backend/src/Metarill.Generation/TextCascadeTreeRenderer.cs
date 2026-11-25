using System.Text.Json;

namespace Metarill.Generation;

/// <summary>文本键与真实主键分开投影。候选、图、Excel 和写入使用各自原有授权范围。</summary>
internal static class TextCascadeTreeRenderer
{
    private const string ReaderType="Metarill.Core.Cascades.TextSelfTreeReader";
    private const string NodeType="Metarill.Core.Cascades.TextCascadeTreeNode";

    internal static string Service(TableModel table,PageModel page,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+plan.Source.Source.Id;
        var sameRow=table.Source.Id==plan.Target.Source.Id;
        var scope=$"CascadeScope{suffix}(actor,requireSourceList,requireTargetList{(sameRow?",structureOnly":"")})";
        var order=string.Concat(plan.Order.Select(item=>$".{(item.Descending?"OrderByDescending":"OrderBy")}(row=>row.{item.Column.Property})"));
        var parent=Literal(plan.Parent.Source.ColumnName);
        var key=Literal(plan.Key.Source.ColumnName);
        var project=Projection(plan);
        var map=Map(plan);
        return $$"""
    /// <summary>按精确文本键读取，真实主键仅用于后代游标。</summary>
    private {{ReaderType}} CascadeReader{{suffix}}(RuntimeActor actor,bool locking=false,bool requireSourceList=true,bool requireTargetList=true{{(sameRow?",bool structureOnly=false":"")}}) => new(
        async(parentId,ct)=>
        {
            var scope={{scope}};
            var query=parentId is null?TextCascadeTreeSql.Root(scope,session,{{parent}}):TextCascadeTreeSql.Match(scope,session,{{parent}},{{Literal("mt_tree_parent_"+suffix)}},[parentId]);
            var rows=await query{{order}}.ToListAsync({{project}},ct);
            return {{map}};
        },
        async(ids,ct)=>
        {
            var keys=await TextCascadeTreeSql.Match({{scope}},session,{{parent}},{{Literal("mt_tree_children_"+suffix)}},ids).ToListAsync(row=>row.{{plan.Parent.Property}},ct);
            return keys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        },
        async(ids,ct)=>
        {
            var query=TextCascadeTreeSql.Match({{scope}},session,{{key}},{{Literal("mt_tree_key_"+suffix)}},ids);
            if(locking)query=query.ForUpdate();
            var rows=await query.ToListAsync({{project}},ct);
            return {{map}};
        },
        async(parents,after,take,ct)=>
        {
            var query=TextCascadeTreeSql.Match({{scope}},session,{{parent}},{{Literal("mt_tree_next_"+suffix)}},parents).Where(row=>row.Id>after).OrderBy(row=>row.Id).Limit(take);
            if(locking)query=query.ForUpdate();
            var rows=await query.ToListAsync({{project}},ct);
            return {{map}};
        },
        {{Literal(plan.Joiner)}});

    private {{ReaderType}} CascadeGraph{{suffix}}(RuntimeActor actor,bool currentRead=false)=>CascadeReader{{suffix}}(actor,currentRead,false,false);
    {{(sameRow?$"private {ReaderType} CascadeTopology{suffix}(RuntimeActor actor)=>CascadeReader{suffix}(actor,true,false,false,true);":"")}}

""";
    }

    internal static string Import(PageModel page,ColumnModel source,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+source.Source.Id;
        var name=plan.Name;
        var lookup=name.Type=="string?"?"text":"key";
        var parse=name.Type=="string?"?"":MultiSelectExcelRenderer.ParseLabel(name,"text","key");
        var parent=Literal(plan.Parent.Source.ColumnName);
        return $$"""
    /// <summary>以原 Excel 路径逐层查找，父上下文按完整文本键锁定。</summary>
    private async Task<JsonElement> ImportCascade{{suffix}}Async(JsonElement cell,RuntimeActor actor,CancellationToken cancellationToken)
    {
        if(cell.ValueKind==JsonValueKind.Null||cell.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.GetString()))return JsonSerializer.SerializeToElement<object?>(null);
        if({{MultiSelectExcelRenderer.InvalidLabelKind(name,allowNumber:true,input:"cell")}})throw PageImportException.Input(PageImportError.Relation);
        if(session.CurrentTransaction is null)throw new InvalidOperationException("级联导入要求来源事务。");
        var id=await Metarill.Core.Cascades.CascadeTreeExcelPath.ResolveTextAsync(cell.ToString(),{{Literal(plan.Joiner)}},async(text,parentId,ct)=>
        {
            {{parse}}
            var scope=CascadeScope{{suffix}}(actor,false,false).Where(row=>row.{{name.Property}}=={{lookup}});
            var query=parentId is null?TextCascadeTreeSql.Root(scope,session,{{parent}}):TextCascadeTreeSql.Match(scope,session,{{parent}},{{Literal("mt_tree_excel_"+suffix)}},[parentId]);
            var rows=await query.OrderBy(row=>row.Id).ForUpdate().Limit(2).ToListAsync({{Projection(plan)}},ct);
            return {{Map(plan)}};
        },cancellationToken);
        return JsonSerializer.SerializeToElement(id,JsonOptions);
    }

""";
    }

    /// <summary>同表写后验证只读实际拓扑。隐藏的名称和候选条件不会截断后代链。</summary>
    internal static string Topology(PageModel page,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+plan.Source.Source.Id;
        var key=plan.Key.Property;
        var parent=plan.Parent.Property;
        var target=plan.Target.EntityType;
        var project=$"row=>new {target}{{Id=row.Id,{key}=row.{key},{parent}=row.{parent}}}";
        var map=$"rows.Select(row=>new {NodeType}(row.{key}!,row.Id,null,row.{parent})).ToArray()";
        return $$"""
    private {{ReaderType}} CascadeTopology{{suffix}}(RuntimeActor actor)=>new(
        (_,_)=>throw new InvalidOperationException("拓扑校验不枚举界面候选。"),
        (_,_)=>throw new InvalidOperationException("拓扑校验不枚举界面候选。"),
        async(ids,ct)=>
        {
            var query=TextCascadeTreeSql.Match(CascadeScope{{suffix}}(actor,false,false,true),session,
                {{Literal(plan.Key.Source.ColumnName)}},{{Literal("mt_tree_topology_key_"+suffix)}},ids);
            var rows=await query.ForUpdate().ToListAsync({{project}},ct);
            return {{map}};
        },
        async(parents,after,take,ct)=>
        {
            var query=TextCascadeTreeSql.Match(CascadeScope{{suffix}}(actor,false,false,true),session,
                {{Literal(plan.Parent.Source.ColumnName)}},{{Literal("mt_tree_topology_parent_"+suffix)}},parents);
            var rows=await query.Where(row=>row.Id>after).OrderBy(row=>row.Id).ForUpdate().Limit(take).ToListAsync({{project}},ct);
            return {{map}};
        },"/");

""";
    }

    private static string Projection(CascadeTreeReadModel plan)
    {
        var fields=new[]{"Id",plan.Key.Property,plan.Name.Property,plan.Parent.Property}.Distinct(StringComparer.Ordinal)
            .Select(property=>$"{property}=row.{property}");
        return $"row=>new {plan.Target.EntityType}{{{string.Join(",",fields)}}}";
    }

    private static string Map(CascadeTreeReadModel plan)
    {
        var label=MultiSelectExcelRenderer.LabelText(plan.Name,"row."+plan.Name.Property);
        return $"rows.Select(row=>new {NodeType}(row.{plan.Key.Property}!,row.Id,{label},row.{plan.Parent.Property})).ToArray()";
    }

    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
