using System.Runtime.CompilerServices;
using Metarill.Data.Relations;
namespace Metarill.Generation;

/// <summary>一个生成模型只编译一次项目关系权威，弱引用不把已结束的生成任务保留在进程缓存中。</summary>
internal static class MixedRelationPropagationCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,RelationMaintenanceCatalog> Catalogs=new();
    /// <summary>平台已证明配置缓存保留专用协调器；其他消费者必须经过与维护相同的整表证明。</summary>
    internal static string[] Excluded(GenerationModel model)=>model.Tables.Where(table=>MaterializedDerivedWriteCompilation.ForTable(model,table).Count>0).Select(table=>table.Source.Id).Order(StringComparer.Ordinal).ToArray();
    /// <summary>编译静态读取／反向依赖，不在生成的HTTP请求中重读目录或拼SQL。</summary>
    internal static RelationMaintenanceCatalog ForModel(GenerationModel model)=>Catalogs.GetValue(model,current=>RelationMaintenanceCatalog.Compile(GenerationMetadataPublisher.ForRelations(current.Catalog),RelationPropagationCompilation.Definitions(RelationPropagationCompilation.Bindings(current)),Excluded(current)));
}
