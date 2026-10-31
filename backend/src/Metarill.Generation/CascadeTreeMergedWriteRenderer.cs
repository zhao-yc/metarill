using System.Text.Json;
namespace Metarill.Generation;

/// <summary>同表非父关联按正常写入后的真实存储状态校验，避免旧行或内存比较绕过数据库范围。</summary>
internal static class CascadeTreeMergedWriteRenderer
{
    /// <summary>只有树表就是来源表且关联不是树父键时，来源变更可能同时改变被引用的路径。</summary>
    internal static bool Applies(TableModel table,CascadeTreeWriteModel plan)=>plan.Read.Target.Source.Id==table.Source.Id&&plan.Read.Parent.Source.Id!=plan.Read.Source.Source.Id;
    /// <summary>路径和范围依赖固定在生成时，不增加来自请求的字段目录或SQL。</summary>
    private static IEnumerable<string> Dependencies(TableModel table,CascadeTreeWriteModel plan)=>new[]{plan.Read.Parent,plan.Read.Name}
        .Concat(plan.Read.TargetPage.Scopes.Select(scope=>scope.Column))
        .Concat(table.Columns.Where(column=>column.Source.ColumnName is "yz_project_id" or "delete_no"))
        .Concat(plan.Read.TargetPage.Condition is{} condition?condition.Syntax.Fields.Select(field=>table.Columns.Single(column=>column.Source.ColumnName==field)):[])
        .Select(column=>column.Source.ColumnName).Distinct(StringComparer.Ordinal);
    /// <summary>来源字段省略时仅在图依赖发生变化后复核；不顺便刷新原受管名称。</summary>
    internal static string Trigger(TableModel table,CascadeTreeWriteModel plan)=>"creating||submitted.Contains("+Literal(plan.Read.Source.Source.ColumnName)+")||submitted.Contains("+Literal(plan.Name.Source.ColumnName)+")"+string.Concat(Dependencies(table,plan).Select(field=>"||submitted.Contains("+Literal(field)+")"));
    /// <summary>新增、修改和复制均在通知之前验证当前行，普通其它页面不生成额外调用。</summary>
    internal static string After(GenerationModel model,TableModel table,PageModel page,bool creating,bool importContext=false)=>string.Concat(CascadeTreeWriteCompilation.ForService(model,table,page).Where(plan=>Applies(table,plan)).Select(plan=>$"        await ValidateStoredCascade{page.Source.Id}_{plan.Read.Source.Source.Id}Async(entity,actor,submitted,{creating.ToString().ToLowerInvariant()},cancellationToken{(importContext?", authorizationOperation != RecordMutationOperation.Import":"")});\n"));
    /// <summary>写前准备名称、写后核验真实路径，均处于调用者事务；不在授权守卫后补写其它值。</summary>
    internal static string Service(TableModel table,PageModel page,CascadeTreeWriteModel plan)
    {
        var source=plan.Read.Source;var name=plan.Name;var parent=plan.Read.Parent;var label=plan.Read.Name;var suffix=page.Source.Id+"_"+source.Source.Id;
        var limit=table.Source.IsRongyu==1&&int.TryParse(name.Source.ColumnLength,out var length)&&length>0?$"if(actualName is {{ Length: > {length} }})throw new ArgumentException(\"级联名称超过物理列声明长度，未保存。\");":"";
        var assignment=source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"id";
        var emptyName=source.Type=="string?"?$"entity.{source.Property} is null?null:\"\"":"null";
        var clearIdentity=source.Type=="string?"?$"entity.{source.Property}=entity.{source.Property} is null?null:{Literal(string.Empty)};":"";
        var storedEmptyName=source.Type=="string?"?$"stored.{source.Property} is null?null:{Literal(string.Empty)}":"null";
        var identity=string.Concat(table.Columns.Where(column=>column.Source.ColumnName=="yz_project_id").Select(column=>$"||stored.{column.Property}!=entity.{column.Property}"));
        // R0自指的逻辑名称只在正常DML后按实际外键读取；不能把IsIgnore属性当作当前名称。
        var prepare=plan.Read.DerivedName?$"if(id==entity.Id){{if(creating)throw new InvalidOperationException(\"不能关联本次尚未存在的新节点。\");actualName=null;}}else{{var leaf=(await DerivedCascadeRows{suffix}Async(actor,[new(\"id\",FixedReadComparison.Equal,id)],[new(\"id\")],1,true,cancellationToken)).SingleOrDefault();if(leaf is null)throw new InvalidOperationException(\"级联记录不存在、已删除或不在当前可读范围。\");actualName=leaf.Name;}}":$"if(id==entity.Id){{if(creating)throw new InvalidOperationException(\"不能关联本次尚未存在的新节点。\");actualName=entity.{label.Property};}}\n        else{{var leaf=await CascadeScope{suffix}(actor,false,requireTargetList).Where(row=>row.Id==id).ForUpdate().FirstAsync(row=>new {table.EntityType}{{Id=row.Id,{label.Property}=row.{label.Property}}},cancellationToken);if(leaf is null)throw new InvalidOperationException(\"级联记录不存在、已删除或不在当前可读范围。\");actualName=leaf.{label.Property};}}";
        return $$"""
    /// <summary>只准备已提交关联的叶子名称；涉及来源本身时不拿旧行决定最终路径。</summary>
    private async Task ValidateCascade{{suffix}}Async({{table.EntityType}} entity,RuntimeActor actor,ISet<string> submitted,CancellationToken cancellationToken,bool requireTargetList=true,bool creating=false)
    {
        if(session.CurrentTransaction is null)throw new InvalidOperationException("级联写入必须加入当前事务。");ValidateActor(actor);
        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException("级联写入要求真实当前身份。");
        if(!creating&&!submitted.Contains({{Literal(source.Source.ColumnName)}})&&!submitted.Contains({{Literal(name.Source.ColumnName)}}))return;
        var text=Convert.ToString(entity.{{source.Property}},CultureInfo.InvariantCulture);
        if(string.IsNullOrWhiteSpace(text))
        {
            {{clearIdentity}}entity.{{name.Property}}={{emptyName}};
            submitted.Add({{Literal(source.Source.ColumnName)}});submitted.Add({{Literal(name.Source.ColumnName)}});return;
        }
        if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException("级联ID必须是不带前导零的正整数。");
        _=CascadeScope{{suffix}}(actor,false,requireTargetList);string? actualName;
        {{prepare}}
        {{limit}}
        entity.{{source.Property}}={{assignment}};entity.{{name.Property}}=actualName;submitted.Add({{Literal(source.Source.ColumnName)}});submitted.Add({{Literal(name.Source.ColumnName)}});
    }
    /// <summary>只读回本次已授权写入的行；数据库实际舍入、排序及NULL规则决定路径可见性。</summary>
    private async Task ValidateStoredCascade{{suffix}}Async({{table.EntityType}} entity,RuntimeActor actor,ISet<string> submitted,bool creating,CancellationToken cancellationToken,bool requireTargetList=true)
    {
        if(!({{Trigger(table,plan)}}))return;
        if(session.CurrentTransaction is null)throw new InvalidOperationException("级联最终校验要求来源事务。");ValidateActor(actor);
        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException("级联写入要求真实当前身份。");
        var refresh=creating||submitted.Contains({{Literal(source.Source.ColumnName)}})||submitted.Contains({{Literal(name.Source.ColumnName)}});
        var stored=await session.Select<{{table.EntityType}}>().Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(cancellationToken);
        if(stored is null||stored.{{source.Property}}!=entity.{{source.Property}}{{identity}}||(creating||submitted.Contains({{Literal(parent.Source.ColumnName)}}))&&stored.{{parent.Property}}!=entity.{{parent.Property}})throw new InvalidOperationException("级联关联ID或归属在存储后发生变化，未提交。");
        // 改父必须验证完整分支，不能把后代推过64层，也不能因书签为空漏掉约束。
        if(creating||submitted.Contains({{Literal(parent.Source.ColumnName)}}))_ = await CascadeTopology{{suffix}}(actor).FilterIdsAsync(stored.Id,false,cancellationToken);
        var text=Convert.ToString(stored.{{source.Property}},CultureInfo.InvariantCulture);string? actualName;
        if(string.IsNullOrWhiteSpace(text))actualName={{storedEmptyName}};
        else
        {
            if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException("级联ID必须是不带前导零的正整数。");
            var path=await CascadeReader{{suffix}}(actor,true,false,refresh&&requireTargetList).PathAsync(id,cancellationToken);
            if(creating&&path.Path.Any(node=>node.Id==entity.Id.ToString(CultureInfo.InvariantCulture)))throw new InvalidOperationException("级联路径不能包含本次新建节点。");
            actualName=path.Path[^1].Name;
        }
        if(!refresh)return;
        {{limit}}
        {{(table.Source.IsRongyu==1?$"if(stored.{name.Property}!=actualName)throw new InvalidOperationException(\"级联名称与实际路径不一致，未提交。\");":"")}}
        entity.{{name.Property}}=actualName;submitted.Add({{Literal(name.Source.ColumnName)}});
    }

""";
    }
    /// <summary>只生成转义字面值，绝不拼接请求中的定义。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
