using System.Collections.ObjectModel;
using System.Collections.Frozen;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;

internal sealed record MixedRelationConsumer(MaintenanceTable Table,MySqlNonRedundantReadPlan Read,IReadOnlySet<string> Inputs);
/// <summary>混合传播只保留可执行读取与反向关系快照，不持有可变目录，不给R0中间节点补物理名称。</summary>
internal sealed class MixedRelationMutationPlan
{
    internal IReadOnlyList<MixedRelationConsumer> Consumers{get;}
    internal IReadOnlyList<string> Targets{get;}
    internal IReadOnlyDictionary<string,IReadOnlyList<string>> SourceColumns{get;}
    private MixedRelationMutationPlan(MixedRelationConsumer[] consumers)
    {
        Consumers=Array.AsReadOnly(consumers);Targets=Array.AsReadOnly(consumers.SelectMany(item=>item.Read.Reverse).Select(item=>item.TargetTable).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());SourceColumns=new ReadOnlyDictionary<string,IReadOnlyList<string>>(consumers.ToDictionary(item=>item.Table.Option.TableName,item=>(IReadOnlyList<string>)Array.AsReadOnly(item.Table.Fields.Select(field=>field.Name).ToArray()),StringComparer.Ordinal));
    }
    /// <summary>完整回填证明是入口；不支持的整表仍保留缺口，不能只激活其中一条未知缓存。</summary>
    internal static MixedRelationMutationPlan Compile(MetadataCatalog catalog,IReadOnlyDictionary<string,MaintenanceTable> tables,IReadOnlySet<string> excluded,IReadOnlyList<RelationCacheDefinition>? scalarCaches=null)
    {
        var result=new List<MixedRelationConsumer>();var multipleNames=tables.Values.SelectMany(table=>table.Multiples.SelectMany(multiple=>multiple.Caches.Select(name=>(table.Option.TableName,name)))).ToHashSet();
        foreach(var table in tables.Values.Where(table=>table.Error is null&&table.Read is not null&&!excluded.Contains(table.Option.Id)))
        {
            // 已登记的直接标量缓存由普通传播执行器唯一处理；混合队列只处理剩余联接，避免双重刷新。
            var scalarNames=(scalarCaches??[]).Where(item=>item.SourceTableId==table.Option.Id).Select(item=>RelationStorageColumns.Expand(catalog.ConfigTables.Single(t=>t.Id==item.SourceTableId),catalog.ConfigColumns.Where(c=>c.ConfigTableId==item.SourceTableId)).Single(c=>c.Id==item.CacheColumnId).ColumnName).ToHashSet(StringComparer.Ordinal);
            var fields=table.Fields.Where(field=>!table.Multiples.Any(multiple=>multiple.Caches.Contains(field.Name))&&!scalarNames.Contains(field.Name)).ToArray();if(fields.Length==0)continue;
            var read=MySqlNonRedundantReadCompiler.CompileRefresh(catalog,table.Option.Id,fields.Select(field=>field.Name).ToArray(),[],fields.ToDictionary(field=>field.Name,field=>field.Path,StringComparer.Ordinal));
            if(read.Reverse.SelectMany(reverse=>reverse.Outputs.Values.SelectMany(names=>names.Select(name=>(reverse.TargetTable,name)))).Any(multipleNames.Contains))continue; // 多值缓存作为标量中间值尚无完整合同，不把数组文本冒充名称。
            var inputs=read.PhysicalReferences.Where(reference=>reference.Table==table.Option.TableName).Select(reference=>reference.Column.Name).ToFrozenSet(StringComparer.Ordinal);
            read=new(read.From,read.ProjectId,read.Scope,read.Expressions.ToDictionary(),read.Types.ToDictionary(),read.Outputs.ToArray(),read.ArrayNamePredicates.ToDictionary(),read.MultiProjections.ToDictionary(),read.ArrayIdPredicates.ToDictionary(),table.Read!.PhysicalReferences,read.Origins,read.Reverse);
            var supported=table with{Fields=Array.AsReadOnly(fields),Multiples=[],Read=read};result.Add(new(supported,read,inputs));
        }
        // R1物理缓存之间也可能构成循环；R0路径自身的递归由读取编译器提前拒绝。
        var nodes=result.SelectMany(consumer=>consumer.Table.Fields.Select(field=>(Key:consumer.Table.Option.TableName+"."+field.Name,Consumer:consumer,Field:field.Name))).ToDictionary(item=>item.Key,StringComparer.Ordinal);var visiting=new HashSet<string>(StringComparer.Ordinal);var visited=new HashSet<string>(StringComparer.Ordinal);
        void Visit(string key)
        {
            if(visited.Contains(key))return;if(!visiting.Add(key))throw new InvalidOperationException("混合派生缓存存在循环依赖，未发布自动传播计划。");var node=nodes[key];foreach(var reverse in node.Consumer.Read.Reverse)if(reverse.Outputs.TryGetValue(node.Field,out var dependencies))foreach(var field in dependencies){var dependency=reverse.TargetTable+"."+field;if(nodes.ContainsKey(dependency))Visit(dependency);}visiting.Remove(key);visited.Add(key);
        }
        foreach(var key in nodes.Keys.Order(StringComparer.Ordinal))Visit(key);return new(result.OrderBy(item=>item.Table.Option.TableName,StringComparer.Ordinal).ToArray());
    }
}
