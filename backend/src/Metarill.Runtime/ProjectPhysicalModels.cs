using Metarill.Core.Metadata;
using Metarill.Runtime.Agent;
namespace Metarill.Runtime;

/// <summary>平台完整结构预览与独立应用共用文件/安全/Agent基础图；只形成模型，不连接数据库或执行DDL。</summary>
public sealed class ProjectPhysicalModels(MetadataCatalog files,MetadataCatalog security)
{
    /// <summary>返回独立物理副本，保留业务表、字段和索引；冲突明确拒绝，不让缺表留到初始化账号时才发现。</summary>
    public MetadataCatalog Compose(MetadataCatalog application)
    {
        var physical=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(application,files),security);
        return MetadataCatalogComposition.MergePhysical(physical,AgentFoundationCatalog.ForApplication(application));
    }
}
