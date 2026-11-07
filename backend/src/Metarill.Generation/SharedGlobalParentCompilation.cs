using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
namespace Metarill.Generation;

internal sealed record SharedGlobalParentPath(CrossTableReadModel Read,int Index)
{
    internal CrossTableLevelModel Level=>Read.Levels[Index];
}
/// <summary>共享R0全局父范围先完成纯读取／存储证明，不能递归借用尚未发布的写入组。</summary>
internal static class SharedGlobalParentCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,ConcurrentDictionary<(string,string),IReadOnlyList<CrossTableReadModel>>> Cache=new();
    /// <summary>只接通当前来源父字段；普通、自树、单控件及来源精确权限维持原门槛。</summary>
    internal static IReadOnlyList<SharedGlobalParentPath> ForColumn(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        if(table.Source.IsRongyu!=0||!page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id))return [];
        var reads=CrossTableReadCompilation.ForService(model,table,page);var logical=reads.SelectMany(read=>read.Levels.Select((level,index)=>(read,level,index))).Where(item=>item.level.Prefix+"_id"==column.Source.ColumnName).ToArray();
        var target=logical.Length>0?logical[0].level.Target:model.Tables.SingleOrDefault(candidate=>candidate.Source.Id==column.Source.LinkConfigTableId);if(target is null||!target.IsGlobal)return [];
        if(table.IsGlobal||table.Source.YzProjectId!=model.Catalog.YzProjectId||ExactDataManageCompilation.Declared(page))throw new NotSupportedException("全局共享父范围要求已发布普通R0来源及原项目身份。");
        var proven=Cache.GetValue(model,_=>new()).GetOrAdd((table.Source.Id,page.Source.Id),_=>Prove(model,table,page,reads));
        var paths=proven.SelectMany(read=>read.Levels.Select((level,index)=>new SharedGlobalParentPath(read,index))).Where(path=>path.Level.Target.Source.Id==target.Source.Id);
        if(logical.Length>0)paths=paths.Where(path=>path.Level.Prefix+"_id"==column.Source.ColumnName);
        else
        {
            // 混合物理父键必须有独立单值证明，并与共享链同一个固定目标页／名称一致。
            var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(column.Source.ControlDetail)))throw new NotSupportedException("全局物理父键的页覆盖不能替换固定关系。");
            var single=GlobalSingleRelationTarget.Require(model.Catalog,table.Source,column.Source,target.Source);paths=paths.Where(path=>path.Level.Page.Source.Id==single.Page.Id&&path.Level.Name.Source.Id==single.Name.Id);
        }
        var result=paths.DistinctBy(Identity,StringComparer.Ordinal).ToArray();if(result.Length!=1)throw new NotSupportedException("全局共享父字段缺少唯一已证明层位，不能猜测重复目标角色。");GlobalCrossTableTarget.Require(model.Catalog,target.Source,result[0].Level.Page.Source);return result;
    }
    /// <summary>候选来源父表去重后仍收齐原字段的层位证明；目标候选自身的父表不借用本入口。</summary>
    internal static IReadOnlyList<SharedGlobalParentPath> ForTable(GenerationModel model,TableModel table,PageModel page,TableModel target)=>page.Scopes.Where(scope=>scope.Source=="parent").SelectMany(scope=>ForColumn(model,table,page,scope.Column)).Where(path=>path.Level.Target.Source.Id==target.Source.Id).DistinctBy(Identity,StringComparer.Ordinal).ToArray();
    /// <summary>真正共享的全部同伴在数据适配器中共同证明固定页／常量与稀疏存储，坏同伴不能单独放行。</summary>
    private static IReadOnlyList<CrossTableReadModel> Prove(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<CrossTableReadModel> reads)
    {
        var result=new List<CrossTableReadModel>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var read in reads)
        {
            if(seen.Contains(read.Source.Source.Id))continue;var group=SharedCrossTableSparseSelection.Compile(table.Source,read.Source.Source.Id,model.Catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Source.Id));if(group.Branches.Count<2)continue;
            var peers=group.Branches.Select(branch=>reads.SingleOrDefault(peer=>peer.Source.Source.Id==branch.SourceColumnId)??throw new InvalidOperationException("全局父范围的共享同伴读取定义缺失。")).ToArray();
            foreach(var peer in peers)
            {
                var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==peer.Source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(peer.Source.Source.ControlDetail)))throw new NotSupportedException("全局共享父范围不能使用变动页覆盖。");if(peer.Levels.Any(level=>level.Target.Source.Id==table.Source.Id))throw new NotSupportedException("全局共享父范围的来源自参与尚需独立证明。");
            }
            var names=group.Slots.SelectMany(slot=>new[]{slot.ColumnName,slot.ColumnName[..^3]+"_name"}).ToHashSet(StringComparer.Ordinal);if(page.Condition?.Syntax.Fields.Any(names.Contains)==true)throw new NotSupportedException("全局共享来源逻辑常量范围尚需独立证明。");
            var scopes=peers.Select(peer=>new NonRedundantCrossTableScope(peer.Source.Source.Id,peer.Levels.Select(level=>new NonRedundantCrossTableTargetScope(level.Page.Source.Id,level.Page.Condition is{} condition?FixedConditionSqlPlan.Compile(condition.Syntax,condition.Types):null)).ToArray())).ToArray();_=NonRedundantReads.CompileWithCrossTables(model.Catalog,table.Source.Id,["id"],scopes,group.Slots.Select(slot=>slot.ColumnName).ToArray());result.AddRange(peers);seen.UnionWith(peers.Select(peer=>peer.Source.Source.Id));
        }
        return result;
    }
    /// <summary>比较完整实际祖先范围，不把同一目标表的不同角色合成一个父身份。</summary>
    private static string Identity(SharedGlobalParentPath path)=>JsonSerializer.Serialize(path.Read.Levels.Take(path.Index+1).Select(level=>new{target=level.Target.Source.Id,page=level.Page.Source.Id,name=level.Name.Source.Id,parent=level.Parent?.Source.Id,condition=level.Page.Source.WhereSql}));
    /// <summary>新全局父入口按原字符串ID规范解析，普通页面不增加此方法。</summary>
    internal static string KeyService(GenerationModel model,TableModel table,PageModel page)=>page.Scopes.Where(scope=>scope.Source=="parent").Any(scope=>ForColumn(model,table,page,scope.Column).Count>0)?$"    /// <summary>父ID不接受前导零、符号或空白，不把客户端上下文当授权。</summary>\n    private static long SharedGlobalParentKey{page.Source.Id}(string? value){{if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||value!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");return id;}}\n":"";
    /// <summary>写操作加入原事务当前读；候选来源父只读，不获取写锁或借用目标候选权限。</summary>
    internal static string Call(SharedGlobalParentPath path,PageModel page,string parentId,string locking)=>$"        await CrossReader{page.Source.Id}_{path.Read.Source.Source.Id}(actor,{locking},false).PathAsync({path.Index},SharedGlobalParentKey{page.Source.Id}({parentId}),cancellationToken);\n";
}
