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
        var nodeKey=plan.Key.Property;
        var rowKey=plan.Key.IsId?"row.Id":$"row.{nodeKey}!.Value";
        var projection=$"row=>new {table.EntityType}{{{nodeKey}=row.{nodeKey},{parent.Property}=row.{parent.Property}}}";
        var keys=parent.Type=="string?"?"parents.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray()":"parents.ToArray()";
        var predicate=parent.Type=="string?"?$"values.Contains(row.{parent.Property}!)":$"row.{parent.Property}!=null&&values.Contains(row.{parent.Property}.Value)";
        return $$"""
    /// <summary>只用于写事务的拓扑约束；项目/软删保持，不将页面常量过滤当成整棵物理树。</summary>
    private Metarill.Core.Cascades.CascadeTreeGraphReader CascadeTopology{{suffix}}(RuntimeActor actor)
    {
        var pathReader=new Metarill.Core.Cascades.SelfTreeReader(async(id,ct)=>
        {
            var row=await CascadeScope{{suffix}}(actor,false,false,true).Where(row=>row.{{nodeKey}}==id).ForUpdate().FirstAsync({{projection}},ct);
            return row is null?null:new Metarill.Core.Cascades.CascadeTreeNode({{rowKey}},null,{{parentValue}});
        },(_,_,_)=>throw new InvalidOperationException("拓扑校验不枚举界面候选。"),(_,_)=>throw new InvalidOperationException("拓扑校验不枚举界面候选。"),"/");
        return new(pathReader,(_,_)=>throw new InvalidOperationException("拓扑校验不读取展示字段。"),async(parents,after,take,ct)=>
        {
            var values={{keys}};var rows=await CascadeScope{{suffix}}(actor,false,false,true).Where(row=>row.{{nodeKey}}>after&&{{predicate}}).OrderBy(row=>row.{{nodeKey}}).ForUpdate().Limit(take).ToListAsync({{projection}},ct);
            return rows.Select(row=>new Metarill.Core.Cascades.CascadeTreeNode({{rowKey}},null,{{parentValue}})).ToArray();
        },"/");
    }

""";
    }
    /// <summary>直接父控件也在通知前验证整棵分支，防止只检查父链后把已有后代推到第65层。</summary>
    internal static string After(GenerationModel model,TableModel table,PageModel page,bool creating)=>string.Concat(
        CascadeTreeWriteCompilation.ForService(model,table,page)
            .Where(plan=>plan.Read.Target.Source.Id==table.Source.Id&&plan.Read.Parent.Source.Id==plan.Read.Source.Source.Id)
            .Select(plan=>
            {
                var read=plan.Read;
                var changed=ParentOrKeyChanged(read);
                var suffix=page.Source.Id+"_"+read.Source.Source.Id;
                var trigger=creating.ToString().ToLowerInvariant()+"||"+changed;
                var parent=read.Parent.Property;
                var key=read.Key.Property;
                var storedName=table.Source.IsRongyu==1?plan.Name.Property:null;
                var projection=string.Join(",",new[]{parent,key,storedName}.OfType<string>()
                    .Distinct(StringComparer.Ordinal).Select(property=>property+"=row."+property));
                var verifyParent=creating?"true":"submitted.Contains("+JsonSerializer.Serialize(read.Parent.Source.ColumnName)+")";
                var verifyName=storedName is null?"":
                    $"            if(({verifyParent})&&directTree.{storedName}!=entity.{storedName})throw new InvalidOperationException(\"自树父节点名称在存储后发生变化，未提交。\");\n";
                var verifyKey=read.Key.IsId?"":
                    $"            var storedKey=directTree.{key};\n            if(storedKey is null||storedKey!=entity.{key})throw new InvalidOperationException(\"自树业务键在存储后发生变化，未提交。\");\n";
                var topologyKey=read.Key.IsId?"entity.Id":read.TextKey?"storedKey":"storedKey.Value";

                // 先比较本次提交与真实存储，再验证完整分支。控制名只在冗余来源落库。
                return $$"""
        if({{trigger}})
        {
            var directTree=await CascadeScope{{suffix}}(actor,false,false,true).Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {{table.EntityType}}{{{projection}}},cancellationToken);
            if(directTree is null)throw new InvalidOperationException("自树节点在存储后不存在或归属已变化，未提交。");
            if(({{verifyParent}})&&directTree.{{parent}}!=entity.{{parent}})throw new InvalidOperationException("自树父节点在存储后发生变化，未提交。");
{{verifyName}}{{verifyKey}}            _ = await CascadeTopology{{suffix}}(actor).FilterIdsAsync({{topologyKey}},false,cancellationToken);
        }

""";
            }));

    /// <summary>业务键也是拓扑依赖，改键需要重新验证即使父键未提交。</summary>
    internal static string ParentOrKeyChanged(CascadeTreeReadModel plan)=>
        $"submitted.Contains({JsonSerializer.Serialize(plan.Parent.Source.ColumnName)})"+
        (plan.Key.IsId?"":$"||submitted.Contains({JsonSerializer.Serialize(plan.Key.Source.ColumnName)})");

    /// <summary>现有图按已配置节点键遍历。缺少或无效的键复用原路径验证拒绝。</summary>
    internal static string NodeKey(CascadeTreeReadModel plan,string variable)=>
        plan.Key.IsId?$"{variable}.Id":plan.TextKey?$"{variable}.{plan.Key.Property}!":$"{variable}.{plan.Key.Property}.GetValueOrDefault()";

    /// <summary>直接与路径的原协议文本比较，业务文本键省略数字格式化。</summary>
    internal static string NodeKeyText(CascadeTreeReadModel plan,string variable)=>
        NodeKey(plan,variable)+(plan.TextKey?"":".ToString(CultureInfo.InvariantCulture)");
}
