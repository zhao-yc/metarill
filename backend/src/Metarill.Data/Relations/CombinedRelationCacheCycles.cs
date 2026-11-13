namespace Metarill.Data.Relations;
/// <summary>完整跨表发布不能让物理与混合执行器之间的字段循环遗漏。</summary>
internal static class CombinedRelationCacheCycles
{
    /// <summary>统一物理副本和混合真实依赖；不以运行时队列反复重算代替有向无环证明。</summary>
    internal static void Require(RelationMutationPlan physical,MixedRelationMutationPlan mixed)
    {
        var graph=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        foreach(var b in physical.Bindings)graph.Add(b.Source.Name+"."+b.Cache,new(StringComparer.Ordinal){b.Target.Name+"."+b.Value});
        foreach(var consumer in mixed.Consumers)foreach(var field in consumer.Table.Fields){var key=consumer.Table.Option.TableName+"."+field.Name;if(!graph.TryAdd(key,new(StringComparer.Ordinal)))throw new InvalidOperationException("同一缓存不能由物理及混合执行器重复维护。");foreach(var reverse in consumer.Read.Reverse)if(reverse.Outputs.TryGetValue(field.Name,out var names))foreach(var name in names)graph[key].Add(reverse.TargetTable+"."+name);}
        var active=new HashSet<string>(StringComparer.Ordinal);var complete=new HashSet<string>(StringComparer.Ordinal);
        void Visit(string key){if(complete.Contains(key))return;if(!active.Add(key))throw new InvalidOperationException("物理／混合缓存之间存在字段循环，未发布跨表传播。");foreach(var dependency in graph[key])if(graph.ContainsKey(dependency))Visit(dependency);active.Remove(key);complete.Add(key);}
        foreach(var key in graph.Keys)Visit(key);
    }
}
