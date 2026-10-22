using System.Text.Json;
namespace Metarill.Generation;

/// <summary>每个生成项目只持有一份不可变传播计划，写入方法执行固定计划而不再解析元数据。</summary>
internal static class RelationPropagationRenderer
{
    /// <summary>静态初始化只编译一次，快照是局部变量，不提供在线改写计划入口。</summary>
    internal static string Catalog(GenerationModel model)
    {
        var bindings=RelationPropagationCompilation.Bindings(model);if(bindings.Count==0)return "";var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var metadata=JsonSerializer.Serialize(model.Catalog,options);var definitions=JsonSerializer.Serialize(RelationPropagationCompilation.Definitions(bindings),options);
        return $$"""

/// <summary>发布项目的关系传播快照，不从HTTP或MCP接收SQL／计划。</summary>
internal static class GeneratedRelationMutation
{
    internal static readonly Metarill.Data.Relations.RelationMutationPlan Plan=Create();
    /// <summary>同时固定元数据与精确字段ID，执行阶段不再持有可变目录。</summary>
    private static Metarill.Data.Relations.RelationMutationPlan Create()
    {
        var options=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var catalog=System.Text.Json.JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>({{JsonSerializer.Serialize(metadata)}},options)!;
        var definitions=System.Text.Json.JsonSerializer.Deserialize<Metarill.Data.Relations.RelationCacheDefinition[]>({{JsonSerializer.Serialize(definitions)}},options)!;
        return Metarill.Data.Relations.RelationMutationPlan.Compile(catalog,definitions);
    }
}

""";
    }
    /// <summary>通知观察者前传播，异常由同一外层事务回滚；自身被刷新字段补回实体以保证VO一致。</summary>
    internal static string Notify(GenerationModel model,TableModel table,string project)
    {
        var bindings=RelationPropagationCompilation.Bindings(model);if(!bindings.Any(binding=>binding.Target.Source.Id==table.Source.Id))return "";
        var text=$"        var relationPatch=await new Metarill.Data.Relations.RelationMutationPropagator(session).AfterMutationAsync(global::{model.Namespace}.Generated.GeneratedRelationMutation.Plan, {JsonSerializer.Serialize(table.Source.TableName)},entity.Id,{project},new HashSet<string>(submitted,StringComparer.Ordinal),deleting||allFields,cancellationToken);\n";
        foreach(var cache in bindings.Where(binding=>binding.Source.Source.Id==table.Source.Id).Select(binding=>binding.Cache).DistinctBy(cache=>cache.Source.Id))text+=$"        if(relationPatch.ContainsKey({JsonSerializer.Serialize(cache.Source.ColumnName)})){{entity.{cache.Property}=Metarill.Data.Queries.NonRedundantReads.Value<{cache.Type}>(relationPatch,{JsonSerializer.Serialize(cache.Source.ColumnName)});submitted.Add({JsonSerializer.Serialize(cache.Source.ColumnName)});}}\n";
        return text;
    }
}
