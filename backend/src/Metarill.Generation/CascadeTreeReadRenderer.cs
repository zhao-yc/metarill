using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>保持原级联两个POST地址，只生成固定目标、参数化查询及最小三列投影。</summary>
internal static class CascadeTreeReadRenderer
{
    /// <summary>配置文本只能作为转义后的C#字面量进入生成物。</summary>
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
    /// <summary>按来源页和字段稳定ID生成原接口地址。</summary>
    internal static string Controller(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var plan in CascadeTreeReadCompilation.ForPage(model,table,page))
        {
            var suffix=page.Source.Id+"_"+plan.Source.Source.Id;
            result.AppendLine("    /// <summary>原自关联树候选协议，来源与固定目标页均需list权限。</summary>")
                .AppendLine($"    [HttpPost(\"cascadeTreeOptions{plan.Source.Source.Id}\")]")
                .AppendLine($"    public async Task<R<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeOption>>> CascadeOptions{plan.Source.Source.Id}([FromBody] CascadeTreeOptionsRequest request,CancellationToken cancellationToken) => R<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeOption>>.Success(await service.CascadeOptions{suffix}Async(request,Actor(\"list\"),cancellationToken));")
                .AppendLine("    /// <summary>原路径回读协议，断链、越权和循环整体拒绝。</summary>")
                .AppendLine($"    [HttpPost(\"cascadeTreePath{plan.Source.Source.Id}\")]")
                .AppendLine($"    public async Task<R<Metarill.Core.Cascades.CascadeTreePath>> CascadePath{plan.Source.Source.Id}([FromBody] CascadeTreePathRequest request,CancellationToken cancellationToken) => R<Metarill.Core.Cascades.CascadeTreePath>.Success(await service.CascadePath{suffix}Async(request,Actor(\"list\"),cancellationToken));");
        }
        return result.ToString();
    }
    /// <summary>编译固定范围、最小投影和按层读取，拒绝动态目标。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var plan in CascadeTreeReadCompilation.ForService(model,table,page))
        {
            var suffix=page.Source.Id+"_"+plan.Source.Source.Id;var target=plan.Target;var parent=plan.Parent;var name=plan.Name;
            string ParentValue(string expression)=>parent.Type=="string?"?expression+"?.ToString(CultureInfo.InvariantCulture)":expression;
            string NodeParent(string expression)=>parent.Type=="string?"?expression:expression+"?.ToString(CultureInfo.InvariantCulture)";
            var projection=$"row => new {target.Name} {{ Id=row.Id,{name.Property}=row.{name.Property},{parent.Property}=row.{parent.Property} }}";
            var ordering=string.Join("",plan.Order.Select(item=>$".{(item.Descending?"OrderByDescending":"OrderBy")}(row=>row.{item.Column.Property})"));
            result.Append(FixedPageConditionCompilation.Declaration(plan.TargetPage,"CascadeCondition"+suffix));
            result.AppendLine("    /// <summary>固定候选范围使用真实身份，普通调用与路径每一跳共用权限和SQL条件。</summary>")
                .AppendLine($"    private ISelect<{target.Name}> CascadeScope{suffix}(RuntimeActor actor,bool requireSourceList=true,bool requireTargetList=true)")
                .AppendLine("    {").AppendLine("        ValidateActor(actor);")
                .AppendLine($"        if(relationAuthorization is null || relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"级联读取要求当前真实授权身份。\"); if(requireSourceList)relationAuthorization.RequirePagePermission({Literal(page.Source.Id)},\"list\");if(requireTargetList)relationAuthorization.RequirePagePermission({Literal(plan.TargetPage.Source.Id)},\"list\");")
                .AppendLine($"        var query=session.Select<{target.Name}>();");
            if(FixedPageQueryCompilation.SoftDeleteColumn(target) is{} deleted)result.AppendLine($"        query=query.Where(row=>row.{deleted.Property}==null);");
            var scopes=plan.TargetPage.Scopes.ToList();var project=target.Columns.SingleOrDefault(column=>column.Source.ColumnName=="yz_project_id");
            if(!target.IsGlobal&&project is not null&&!scopes.Any(scope=>scope.Column.Source.Id==project.Source.Id))scopes.Add((project,"project"));
            foreach(var (column,_) in scopes)result.AppendLine($"        query=query.Where(row=>row.{column.Property}=={(column.Type=="string?"?"actor.ProjectId":"long.Parse(actor.ProjectId,CultureInfo.InvariantCulture)")});");
            result.Append(FixedPageConditionCompilation.Apply(plan.TargetPage,"CascadeCondition"+suffix,"cascade_"+suffix)).AppendLine("        return query;").AppendLine("    }");
            result.AppendLine("    /// <summary>只投影树结构必需三列，候选有界且不加载整表。</summary>")
                .AppendLine($"    private Metarill.Core.Cascades.SelfTreeReader CascadeReader{suffix}(RuntimeActor actor,bool locking=false,bool requireSourceList=true,bool requireTargetList=true) => new(")
                .AppendLine($"        async (id,ct)=>{{var query=CascadeScope{suffix}(actor,requireSourceList,requireTargetList).Where(row=>row.Id==id);if(locking)query=query.ForUpdate();var row=await query.FirstAsync({projection},ct);return row is null?null:new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{name.Property},{NodeParent("row."+parent.Property)});}},")
                .AppendLine($"        async (parentId,limit,ct)=>{{var query=CascadeScope{suffix}(actor);var parentKey={ParentValue("parentId")};query=parentId is null?query.Where(row=>row.{parent.Property}==null{(parent.Type=="string?"?$"||row.{parent.Property}==\"\"":"")}):query.Where(row=>row.{parent.Property}==parentKey);var rows=await query{ordering}.Limit(limit).ToListAsync({projection},ct);return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(row.Id,row.{name.Property},{NodeParent("row."+parent.Property)})).ToArray();}},")
                .AppendLine(parent.Type=="string?"
                    ? $"        async (ids,ct)=>{{var keys=ids.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray();var rows=await CascadeScope{suffix}(actor).Where(row=>keys.Contains(row.{parent.Property}!)).Distinct().ToListAsync(row=>row.{parent.Property},ct);return rows.Select(text=>long.Parse(text!,CultureInfo.InvariantCulture)).ToHashSet();}},"
                    : $"        async (ids,ct)=>{{var keys=ids.ToArray();var rows=await CascadeScope{suffix}(actor).Where(row=>row.{parent.Property}!=null&&keys.Contains(row.{parent.Property}.Value)).Distinct().ToListAsync(row=>row.{parent.Property},ct);return rows.Select(value=>value!.Value).ToHashSet();}},")
                .AppendLine($"        {Literal(plan.Joiner)});")
                .AppendLine("    /// <summary>同一读取事务内回读根链及候选，保留原组件请求合同。</summary>")
                .AppendLine($"    public Task<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeOption>> CascadeOptions{suffix}Async(CascadeTreeOptionsRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>session.InTransactionAsync(async()=>{{var parent=request.ParseParent();_=CascadeScope{suffix}(actor);return await CascadeReader{suffix}(actor).OptionsAsync(parent,cancellationToken);}},cancellationToken);")
                .AppendLine("    /// <summary>空路径也执行来源和目标权限检查。</summary>")
                .AppendLine($"    public Task<Metarill.Core.Cascades.CascadeTreePath> CascadePath{suffix}Async(CascadeTreePathRequest request,RuntimeActor actor,CancellationToken cancellationToken)=>session.InTransactionAsync(async()=>{{var leaf=request.ParseLeaf();_=CascadeScope{suffix}(actor);return await CascadeReader{suffix}(actor).PathAsync(leaf,cancellationToken);}},cancellationToken);");
        }
        return result.ToString();
    }
}
