using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>原跨表两个POST的强类型读实现；每级Scope由固定模型确定，不接受请求查询定义。</summary>
internal static class CrossTableReadRenderer
{
    /// <summary>响应分别保留四字段候选和含levelKey的路径，不改原自关联协议。</summary>
    internal static string Controller(GenerationModel model,TableModel table,PageModel page)=>string.Concat(CrossTableReadCompilation.ForPage(model,table,page).Select(plan=>$$"""
    /// <summary>原跨表逐级候选，来源与当前及实际祖先页分别授权。</summary>
    [HttpPost("cascadeTreeOptions{{plan.Source.Source.Id}}")]
    public async Task<R<IReadOnlyList<Metarill.Core.Cascades.CrossTableOption>>> CrossOptions{{plan.Source.Source.Id}}([FromBody] CascadeTreeOptionsRequest request,CancellationToken cancellationToken)=>R<IReadOnlyList<Metarill.Core.Cascades.CrossTableOption>>.Success(await service.CrossOptions{{page.Source.Id}}_{{plan.Source.Source.Id}}Async(request,Actor("list"),cancellationToken));
    /// <summary>空路径也检查全部目标页授权，不返回部分祖先。</summary>
    [HttpPost("cascadeTreePath{{plan.Source.Source.Id}}")]
    public async Task<R<Metarill.Core.Cascades.CrossTablePath>> CrossPath{{plan.Source.Source.Id}}([FromBody] CascadeTreePathRequest request,CancellationToken cancellationToken)=>R<Metarill.Core.Cascades.CrossTablePath>.Success(await service.CrossPath{{page.Source.Id}}_{{plan.Source.Source.Id}}Async(request,Actor("list"),cancellationToken));

"""));
    /// <summary>固定投影及父谓词均由C#表达式生成，跨级读取共享显式RR快照。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var output=new StringBuilder();
        foreach(var plan in CrossTableReadCompilation.ForService(model,table,page))
        {
            var suffix=page.Source.Id+"_"+plan.Source.Source.Id;var sameSource=plan.Levels.Any(level=>level.Target.Source.Id==table.Source.Id);
            var factory=plan.HasTextKeys?"Metarill.Core.Cascades.CrossTableReader.WithTextKeys":"new";
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];var target=level.Target;var scope=suffix+"_"+index;
                output.Append(FixedPageConditionCompilation.Declaration(level.Page,"CrossCondition"+scope)).AppendLine("    /// <summary>固定逐级目标的数据范围，动作权限可由内部只读工厂统一要求。</summary>").AppendLine($"    private ISelect<{target.EntityType}> CrossScope{scope}(RuntimeActor actor,bool requireList=true)")
                    .AppendLine("    {").AppendLine("        ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"跨表级联要求当前真实身份。\");")
                    .AppendLine($"        if(requireList)relationAuthorization.RequirePagePermission({Literal(level.Page.Source.Id)},\"list\");var query=session.Select<{target.EntityType}>();");
                if(FixedPageQueryCompilation.SoftDeleteColumn(target) is{} deleted)output.AppendLine($"        query=query.Where(row=>row.{deleted.Property}==null);");
                var scopes=level.Page.Scopes.ToList();var project=target.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id");if(!target.IsGlobal&&project is not null&&!scopes.Any(scope=>scope.Column.Source.Id==project.Source.Id))scopes.Add((project,"project"));
                foreach(var(column,_)in scopes)output.AppendLine($"        query=query.Where(row=>row.{column.Property}=={(column.Type=="string?"?"actor.ProjectId":"long.Parse(actor.ProjectId,CultureInfo.InvariantCulture)")});");
                var logicalPlan=FixedPageConditionCompilation.HasLogicalFields(level.Page)?"CrossTargetPlan"+scope+".BindAccount(actor)":null;
                output.Append(FixedPageConditionCompilation.Apply(level.Page,"CrossCondition"+scope,logicalPlan is null?"cross_"+scope:"nested_scope",logicalPlan))
                    .AppendLine("        return query;").AppendLine("    }");
                if(target.Source.IsRongyu==0)output.Append(NonRedundantCrossTargetsRenderer.Service(model,table,page,plan,index));
            }
            output.AppendLine("    /// <summary>来源页授权不借用客户端项目、父记录或设备上下文。</summary>").AppendLine($"    private void RequireCrossSource{suffix}(RuntimeActor actor){{ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"跨表级联要求当前真实身份。\");relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"list\");}}")
                .AppendLine("    /// <summary>固定层号只选生成分支，私有工厂不暴露动态查询入口。</summary>").AppendLine($"    private Metarill.Core.Cascades.CrossTableReader CrossReader{suffix}(RuntimeActor actor,bool currentRead=false,bool requireTargetList=true{(sameSource?", "+table.EntityType+"? sourceOverride=null,bool creating=false":"")})=>{factory}(new Metarill.Core.Cascades.CrossTableLevel[]{{{string.Join(",",plan.Levels.Select(level=>$"new({Literal(level.Key)},{Literal(GenerationModel.Camel(level.Prefix+"_id"))}{(level.TextKey?",true":"")})"))}}},index=>{{switch(index){{");
            for(var index=0;index<plan.Levels.Count;index++)output.AppendLine($"        case {index}:_=CrossScope{suffix}_{index}(actor,requireTargetList);break;");
            output.AppendLine("        default:throw new ArgumentException(\"跨表层号无效。\");}},async(index,id,ct)=>{switch(index){");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];var projection=Projection(level);
                // 写前只覆盖已授权来源自身的待存名称/父键；真实范围与路径必须在正常DML后再次由SQL核验。
                var overlay=level.Target.Source.Id==table.Source.Id?$"if(sourceOverride is not null&&id==sourceOverride.{level.KeyProperty}){{if(creating)throw new InvalidOperationException(\"不能关联本次尚未存在的新节点。\");return new Metarill.Core.Cascades.CascadeTreeNode({level.KeyValue("sourceOverride")},{(level.DerivedName?"null":MultiSelectExcelRenderer.LabelText(level.Name,"sourceOverride."+level.Name.Property))},{Parent(level).Replace("row.","sourceOverride.",StringComparison.Ordinal)});}}":"";
                // 派生名称写前不读IsIgnore属性；R0不缓存名称，正常DML后必须用真实FK重新锁读完整路径。
                if(level.Target.Source.IsRongyu==0){output.AppendLine(NonRedundantCrossTargetsRenderer.Find(page,plan,index,overlay));continue;}
                var scopeQuery=$"CrossScope{suffix}_{index}(actor,false)";
                var lookup=level.TextKey?TextMatch(scopeQuery,level.KeyColumn,"mt_cross_key_"+suffix+"_"+index,"[id]"):
                    scopeQuery+$".Where(row=>row.{level.KeyProperty}=={(plan.HasTextKeys?"long.Parse(id,CultureInfo.InvariantCulture)":"id")})";
                output.AppendLine($"        case {index}:{{{overlay}var query={lookup};if(currentRead)query=query.ForUpdate();var row=await query.FirstAsync({projection},ct);return row is null?null:{Node(plan,level,"row")};}}");
            }
            output.AppendLine("        default:throw new ArgumentException(\"跨表层号无效。\");}},async(index,parent,ct)=>{switch(index){");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                if(plan.Levels[index].Target.Source.IsRongyu==0){output.AppendLine(NonRedundantCrossTargetsRenderer.Children(page,plan,index));continue;}
                var level=plan.Levels[index];var filter=ParentFilter(plan,index,"mt_cross_parent_"+suffix+"_"+index);var order=string.Concat(level.Order.Select(item=>$".{(item.Descending?"OrderByDescending":"OrderBy")}(row=>row.{item.Column.Property})"));
                output.AppendLine($"        case {index}:{{var query=CrossScope{suffix}_{index}(actor,false);{filter}var rows=await query{order}.ToListAsync({Projection(level)},ct);return rows.Select(row=>{Node(plan,level,"row")}).ToArray();}}");
            }
            output.AppendLine($"        default:throw new ArgumentException(\"跨表层号无效。\");}}}},{Literal(plan.Joiner)},{(sameSource?"sourceOverride is not null?null:":"")}async(index,ids,ct)=>{{switch(index){{");
            // 待写来源覆盖仅走原单路径委托，批读不会丢失尚未提交的名称或父键。
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];
                if(level.Target.Source.IsRongyu==0)
                {
                    output.AppendLine($"        case {index}:return await CrossTargetRows{suffix}_{index}Async(actor,[new({Literal(level.KeyColumn)},FixedReadComparison.In,ids.Select(id=>(object?)id).ToArray())],ids.Count,currentRead,ct);");continue;
                }
                var batchScope=$"CrossScope{suffix}_{index}(actor,false)";
                var keys=plan.HasTextKeys?"ids.Select(id=>"+(level.AlternateKey?.Type=="long?"?"(long?)":"")+"long.Parse(id,CultureInfo.InvariantCulture)).ToArray()":
                    level.AlternateKey?.Type=="long?"?"ids.Select(id=>(long?)id).ToArray()":"ids.ToArray()";
                var query=level.TextKey?"var query="+TextMatch(batchScope,level.KeyColumn,"mt_cross_batch_"+suffix+"_"+index,"ids")+";":
                    $"var keys={keys};var query={batchScope}.Where(row=>keys.Contains(row.{level.KeyProperty}));";
                output.AppendLine($"        case {index}:{{{query}if(currentRead)query=query.ForUpdate();var rows=await query.ToListAsync({Projection(level)},ct);return rows.Select(row=>{Node(plan,level,"row")}).ToArray();}}");
            }
            output.AppendLine("        default:throw new ArgumentException(\"跨表层号无效。\");}});")
                .AppendLine("    /// <summary>逐级目标和祖先读取使用同一明确快照，弱外层事务不能混用。</summary>")
                .AppendLine($"    public Task<IReadOnlyList<Metarill.Core.Cascades.CrossTableOption>> CrossOptions{suffix}Async(CascadeTreeOptionsRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>{GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync(async()=>{{RequireCrossSource{suffix}(actor);return await CrossReader{suffix}(actor).OptionsAsync(request.LevelKey,request.SelectedPath,cancellationToken);}},cancellationToken);")
                .AppendLine("    /// <summary>独立路径按固定末级回读，空值仍执行全部页授权。</summary>")
                .AppendLine($"    public Task<Metarill.Core.Cascades.CrossTablePath> CrossPath{suffix}Async(CascadeTreePathRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>{GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync(async()=>{{RequireCrossSource{suffix}(actor);return await CrossReader{suffix}(actor).Path{(plan.HasTextKeys?"ByKey":"")}Async({plan.Levels.Count-1},{(plan.Levels[^1].TextKey?"request.ParseTextLeaf()":plan.HasTextKeys?"request.ParseLeaf()?.ToString(CultureInfo.InvariantCulture)":"request.ParseLeaf()")},cancellationToken);}},cancellationToken);");
        }
        return output.ToString();
    }
    /// <summary>候选、路径与 Excel 共用同一节点投影及字符串 ID 协议。</summary>
    internal static string Node(CrossTableReadModel plan,CrossTableLevelModel level,string row)
    {
        var label=MultiSelectExcelRenderer.LabelText(level.Name,row+"."+level.Name.Property);
        var key=level.KeyValue(row);
        var parent=Parent(level).Replace("row.",row+".",StringComparison.Ordinal);
        if(!plan.HasTextKeys)return $"new Metarill.Core.Cascades.CascadeTreeNode({key},{label},{parent})";
        var value=level.TextKey?key+"!":key+".ToString(CultureInfo.InvariantCulture)";
        return $"new Metarill.Core.Cascades.TextCascadeTreeNode({value},{row}.Id,{label},{parent})";
    }

    /// <summary>文本父键严格按原字节比较，其它层继续绑定精确整数。</summary>
    internal static string ParentFilter(CrossTableReadModel plan,int index,string prefix)
    {
        if(plan.Levels[index].Parent is not {} parent)return "";
        if(plan.Levels[index-1].TextKey)
            return "query="+TextMatch("query",parent.Source.ColumnName,prefix,"[parent!]")+";";
        var key=parent.Type=="string?"?(plan.HasTextKeys?"parent":"parent?.ToString(CultureInfo.InvariantCulture)"):
            plan.HasTextKeys?"parent is null?(long?)null:long.Parse(parent,CultureInfo.InvariantCulture)":"parent";
        return $"var parentKey={key};query=query.Where(row=>row.{parent.Property}==parentKey);";
    }

    /// <summary>复用文本自树的字节匹配，标识符冻结，键值通过数据库参数绑定。</summary>
    internal static string TextMatch(string query,string column,string prefix,string values)=>
        $"Metarill.Data.Queries.TextCascadeTreeSql.Match({query},session,{Literal(column)},{Literal(prefix)},{values})";

    /// <summary>只读取本级ID/名称及下一跳所需父键。</summary>
    internal static string Projection(CrossTableLevelModel level)
    {
        var properties = new[] {"Id",level.Name.Property,level.Parent?.Property,level.AlternateKey?.Property}
            .OfType<string>().Distinct(StringComparer.Ordinal);
        return $"row=>new {level.Target.EntityType}{{{string.Join(",",properties.Select(property=>property+"=row."+property))}}}";
    }
    /// <summary>根层不读取父列，非根保留字符串或精确整数文本。</summary>
    private static string Parent(CrossTableLevelModel level)=>level.Parent is not{} parent?"null":parent.Type=="string?"?"row."+parent.Property:"row."+parent.Property+"?.ToString(CultureInfo.InvariantCulture)";
    /// <summary>元数据文本只能作为转义后的C#字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
