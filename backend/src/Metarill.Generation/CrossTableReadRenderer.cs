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
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];var target=level.Target;var scope=suffix+"_"+index;
                output.Append(FixedPageConditionCompilation.Declaration(level.Page,"CrossCondition"+scope)).AppendLine("    /// <summary>固定逐级目标的数据范围，动作权限可由内部只读工厂统一要求。</summary>").AppendLine($"    private ISelect<{target.Name}> CrossScope{scope}(RuntimeActor actor,bool requireList=true)")
                    .AppendLine("    {").AppendLine("        ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"跨表级联要求当前真实身份。\");")
                    .AppendLine($"        if(requireList)relationAuthorization.RequirePagePermission({Literal(level.Page.Source.Id)},\"list\");var query=session.Select<{target.Name}>();");
                if(FixedPageQueryCompilation.SoftDeleteColumn(target) is{} deleted)output.AppendLine($"        query=query.Where(row=>row.{deleted.Property}==null);");
                var scopes=level.Page.Scopes.ToList();var project=target.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id");if(!target.IsGlobal&&project is not null&&!scopes.Any(scope=>scope.Column.Source.Id==project.Source.Id))scopes.Add((project,"project"));
                foreach(var(column,_)in scopes)output.AppendLine($"        query=query.Where(row=>row.{column.Property}=={(column.Type=="string?"?"actor.ProjectId":"long.Parse(actor.ProjectId,CultureInfo.InvariantCulture)")});");
                output.Append(FixedPageConditionCompilation.Apply(level.Page,"CrossCondition"+scope,"cross_"+scope)).AppendLine("        return query;").AppendLine("    }");
            }
            output.AppendLine("    /// <summary>来源页授权不借用客户端项目、父记录或设备上下文。</summary>").AppendLine($"    private void RequireCrossSource{suffix}(RuntimeActor actor){{ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"跨表级联要求当前真实身份。\");relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"list\");}}")
                .AppendLine("    /// <summary>固定层号只选生成分支，私有工厂不暴露动态查询入口。</summary>").AppendLine($"    private Metarill.Core.Cascades.CrossTableReader CrossReader{suffix}(RuntimeActor actor,bool currentRead=false,bool requireTargetList=true{(sameSource?", "+table.Name+"? sourceOverride=null,bool creating=false":"")})=>new(new Metarill.Core.Cascades.CrossTableLevel[]{{{string.Join(",",plan.Levels.Select(level=>$"new({Literal(level.Key)},{Literal(GenerationModel.Camel(level.Prefix+"_id"))})"))}}},index=>{{switch(index){{");
            for(var index=0;index<plan.Levels.Count;index++)output.AppendLine($"        case {index}:_=CrossScope{suffix}_{index}(actor,requireTargetList);break;");
            output.AppendLine("        default:throw new ArgumentException(\"跨表层号无效。\");}},async(index,id,ct)=>{switch(index){");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];var projection=Projection(level);
                // 写前只覆盖已授权来源自身的待存名称/父键；真实范围与路径必须在正常DML后再次由SQL核验。
                var overlay=level.Target.Source.Id==table.Source.Id?$"if(sourceOverride is not null&&id==sourceOverride.Id){{if(creating)throw new InvalidOperationException(\"不能关联本次尚未存在的新节点。\");return new Metarill.Core.Cascades.CascadeTreeNode(sourceOverride.Id,sourceOverride.{level.Name.Property},{Parent(level).Replace("row.","sourceOverride.",StringComparison.Ordinal)});}}":"";
                output.AppendLine($"        case {index}:{{{overlay}var query=CrossScope{suffix}_{index}(actor,false).Where(row=>row.Id==id);if(currentRead)query=query.ForUpdate();var row=await query.FirstAsync({projection},ct);return row is null?null:new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{level.Name.Property},{Parent(level)});}}");
            }
            output.AppendLine("        default:throw new ArgumentException(\"跨表层号无效。\");}},async(index,parent,ct)=>{switch(index){");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];var filter=level.Parent is{} parent?$"var parentKey={(parent.Type=="string?"?"parent?.ToString(CultureInfo.InvariantCulture)":"parent")};query=query.Where(row=>row.{parent.Property}==parentKey);":"";var order=string.Concat(level.Order.Select(item=>$".{(item.Descending?"OrderByDescending":"OrderBy")}(row=>row.{item.Column.Property})"));
                output.AppendLine($"        case {index}:{{var query=CrossScope{suffix}_{index}(actor,false);{filter}var rows=await query{order}.ToListAsync({Projection(level)},ct);return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{level.Name.Property},{Parent(level)})).ToArray();}}");
            }
            output.AppendLine($"        default:throw new ArgumentException(\"跨表层号无效。\");}}}},{Literal(plan.Joiner)});")
                .AppendLine("    /// <summary>逐级目标和祖先读取使用同一明确快照，弱外层事务不能混用。</summary>")
                .AppendLine($"    public Task<IReadOnlyList<Metarill.Core.Cascades.CrossTableOption>> CrossOptions{suffix}Async(CascadeTreeOptionsRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>session.InReadSnapshotAsync(async()=>{{RequireCrossSource{suffix}(actor);return await CrossReader{suffix}(actor).OptionsAsync(request.LevelKey,request.SelectedPath,cancellationToken);}},cancellationToken);")
                .AppendLine("    /// <summary>独立路径按固定末级回读，空值仍执行全部页授权。</summary>")
                .AppendLine($"    public Task<Metarill.Core.Cascades.CrossTablePath> CrossPath{suffix}Async(CascadeTreePathRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>session.InReadSnapshotAsync(async()=>{{RequireCrossSource{suffix}(actor);return await CrossReader{suffix}(actor).PathAsync({plan.Levels.Count-1},request.ParseLeaf(),cancellationToken);}},cancellationToken);");
        }
        return output.ToString();
    }
    /// <summary>只读取本级ID/名称及下一跳所需父键。</summary>
    private static string Projection(CrossTableLevelModel level)=>$"row=>new {level.Target.Name}{{Id=row.Id,{level.Name.Property}=row.{level.Name.Property}{(level.Parent is{} parent?","+parent.Property+"=row."+parent.Property:"")}}}";
    /// <summary>根层不读取父列，非根保留字符串或精确整数文本。</summary>
    private static string Parent(CrossTableLevelModel level)=>level.Parent is not{} parent?"null":parent.Type=="string?"?"row."+parent.Property:"row."+parent.Property+"?.ToString(CultureInfo.InvariantCulture)";
    /// <summary>元数据文本只能作为转义后的C#字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
