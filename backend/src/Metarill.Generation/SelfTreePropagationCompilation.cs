using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Data.Relations;
namespace Metarill.Generation;

/// <summary>物理R1自树缓存从固定控制定义导出，隐藏字段仍参与复制、传播和原维护。</summary>
internal static class SelfTreePropagationCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,ConcurrentDictionary<string,IReadOnlyList<RelationPropagationBinding>>> Cache=new();
    /// <summary>一个生成模型只编译一次，不使后续HTTP重新解释目录。</summary>
    internal static IReadOnlyList<RelationPropagationBinding> ForTable(GenerationModel model,TableModel table)=>Cache.GetValue(model,_=>new()).GetOrAdd(table.Source.Id,_=>Compile(model,table));
    /// <summary>BackendOnly保留未证明用途的原门槛；Full另逐字段拒绝，不能借有效同伴发布坏字段。</summary>
    private static IReadOnlyList<RelationPropagationBinding> Compile(GenerationModel model,TableModel table)
    {
        if(table.Source.IsRongyu!=1||table.IsGlobal)return [];var result=new List<RelationPropagationBinding>();
        foreach(var page in table.Pages)foreach(var source in table.Columns.Where(c=>c.Source.ControlKind=="CascadeTree"&&!NonRedundantCrossTableSubset.Declared(c)))
        {
            var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(f=>f.ConfigPageId==page.Source.Id&&f.ConfigColumnId==source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(source.Source.ControlDetail)))continue;
            var plan=CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition;if(plan is null||plan.Read.Target.IsGlobal)continue;
            try
            {
                var proof=SelfTreeCache.Compile(model.Catalog,table.Source.Id,source.Source.Id);if(proof.TargetTableId!=plan.Read.Target.Source.Id||proof.ValueColumnId!=plan.Read.Name.Source.Id||proof.CacheColumnId!=plan.Name.Source.Id)continue;
                if(plan.Read.DerivedName)DerivedCrossTableCaches.Validate(model.Catalog,[proof],[]);else _=RelationMutationPlan.Compile(model.Catalog,[proof]);result.Add(new(table,source,plan.Read.Target,plan.Read.Name,plan.Name,false));
            }
            catch(Exception unsupported)when(unsupported is InvalidOperationException or NotSupportedException or ArgumentException or JsonException){/* 完整用途审计仍会报告此字段，不生成未经证明的缓存。 */}
        }
        return result.DistinctBy(binding=>(binding.Foreign.Source.Id,binding.Target.Source.Id,binding.Value.Source.Id,binding.Cache.Source.Id)).ToArray();
    }
}
