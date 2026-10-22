using System.Text.Json;
namespace Metarill.Generation;

/// <summary>维护目录随正规生成发布，完整宿主和平台使用同一编译器，不解析HTTP提交的关系计划。</summary>
internal static class RelationMaintenanceRenderer
{
    /// <summary>每项目仅初始化一次；实例持有冻结计划而非可编辑元数据。</summary>
    internal static string Catalog(GenerationModel model)
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var metadata=JsonSerializer.Serialize(GenerationMetadataPublisher.ForRelations(model.Catalog),options);var definitions=JsonSerializer.Serialize(RelationPropagationCompilation.Definitions(RelationPropagationCompilation.Bindings(model)),options);
        return $$"""

/// <summary>原系统维护的宿主项目权威，仅由生成器创建。</summary>
public static class GeneratedRelationMaintenance
{
    public static Metarill.Data.Relations.RelationMaintenanceCatalog Catalog{get;}=Create();
    /// <summary>发布结构与精确多值绑定同时冻结，当前进程不接受在线覆盖。</summary>
    private static Metarill.Data.Relations.RelationMaintenanceCatalog Create()
    {
        var options=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var metadata=System.Text.Json.JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>({{JsonSerializer.Serialize(metadata)}},options)!;
        var definitions=System.Text.Json.JsonSerializer.Deserialize<Metarill.Data.Relations.RelationCacheDefinition[]>({{JsonSerializer.Serialize(definitions)}},options)!;
        return Metarill.Data.Relations.RelationMaintenanceCatalog.Compile(metadata,definitions);
    }
}

""";
    }
}
