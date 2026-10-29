using System.Text.Json;
namespace Metarill.Generation;

/// <summary>同表树的当前父链与完整子树共用既有图遍历；仅内部拓扑，不返回隐藏名称。</summary>
internal static class CascadeTreeTopologyRenderer
{
    /// <summary>当前读锁住父链及所有访问后代，使用200父键/1000游标页，不受候选500项截断。</summary>
    internal static string Service(TableModel table,PageModel page,CascadeTreeReadModel plan)
    {
        var suffix=page.Source.Id+"_"+plan.Source.Source.Id;var parent=plan.Parent;
        var parentValue=$"Convert.ToString(row.{parent.Property},CultureInfo.InvariantCulture)";
        var projection=$"row=>new {table.EntityType}{{Id=row.Id,{parent.Property}=row.{parent.Property}}}";
        var keys=parent.Type=="string?"?"parents.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray()":"parents.ToArray()";
        var predicate=parent.Type=="string?"?$"values.Contains(row.{parent.Property}!)":$"row.{parent.Property}!=null&&values.Contains(row.{parent.Property}.Value)";
        return $$"""
    /// <summary>只用于写事务的拓扑约束；项目/软删保持，不将页面常量过滤当成整棵物理树。</summary>
    private Metarill.Core.Cascades.CascadeTreeGraphReader CascadeTopology{{suffix}}(RuntimeActor actor)
    {
        var pathReader=new Metarill.Core.Cascades.SelfTreeReader(async(id,ct)=>
        {
            var row=await CascadeScope{{suffix}}(actor,false,false,true).Where(row=>row.Id==id).ForUpdate().FirstAsync({{projection}},ct);
            return row is null?null:new Metarill.Core.Cascades.CascadeTreeNode(row.Id,null,{{parentValue}});
        },(_,_,_)=>throw new InvalidOperationException("拓扑校验不枚举界面候选。"),(_,_)=>throw new InvalidOperationException("拓扑校验不枚举界面候选。"),"/");
        return new(pathReader,(_,_)=>throw new InvalidOperationException("拓扑校验不读取展示字段。"),async(parents,after,take,ct)=>
        {
            var values={{keys}};var rows=await CascadeScope{{suffix}}(actor,false,false,true).Where(row=>row.Id>after&&{{predicate}}).OrderBy(row=>row.Id).ForUpdate().Limit(take).ToListAsync({{projection}},ct);
            return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode(row.Id,null,{{parentValue}})).ToArray();
        },"/");
    }

""";
    }
    /// <summary>直接父控件也在通知前验证整棵分支，防止只检查父链后把已有后代推到第65层。</summary>
    internal static string After(GenerationModel model,TableModel table,PageModel page,bool creating)=>string.Concat(CascadeTreeWriteCompilation.ForService(model,table,page).Where(plan=>plan.Read.Target.Source.Id==table.Source.Id&&plan.Read.Parent.Source.Id==plan.Read.Source.Source.Id).Select(plan=>$"        if({creating.ToString().ToLowerInvariant()}||submitted.Contains({JsonSerializer.Serialize(plan.Read.Parent.Source.ColumnName)}))_ = await CascadeTopology{page.Source.Id}_{plan.Read.Source.Source.Id}(actor).FilterIdsAsync(entity.Id,false,cancellationToken);\n"));
}
