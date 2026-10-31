using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
namespace Metarill.Runtime;

/// <summary>完整结构预览共用文件/安全/Agent基础图；平台管理页仅由服务端固定目录授权，不放宽普通应用合成。</summary>
public sealed class ProjectPhysicalModels
{
    private readonly MetadataCatalog files,security;private readonly MetadataCatalog? platform;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    /// <summary>普通应用仅允许严格物理合成，JSON中的项目标识不能开启平台例外。</summary>
    public ProjectPhysicalModels(MetadataCatalog files,MetadataCatalog security){this.files=Clone(files);this.security=Clone(security);}
    /// <summary>仅平台宿主显式装配真实发布目录；保存副本，后续调用者改写输入不能扩大允许页面。</summary>
    public ProjectPhysicalModels(MetadataCatalog files,MetadataCatalog security,MetadataRegistry registry):this(files,security)
    {platform=Clone(registry.Platform);}
    /// <summary>结构只形成独立模型；平台原管理页先校验归属后暂离物理合成，保留表/字段/索引仍执行原严格检查。</summary>
    public MetadataCatalog Compose(MetadataCatalog application)
    {
        if(platform is null||application.YzProjectId!=platform.YzProjectId)return Merge(application);
        MetadataValidation.Validate(application);
        var infrastructure=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(files,security),AgentFoundationCatalog.ForApplication(application));
        var reserved=infrastructure.ConfigTables.Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        var current=application.ConfigPages.Where(page=>reserved.Contains(page.ConfigTableId)).ToArray();
        foreach(var page in current)
        {
            var published=platform.ConfigPages.SingleOrDefault(candidate=>candidate.Id==page.Id);
            if(published is null||published.ConfigTableId!=page.ConfigTableId||page.YzProjectId!=platform.YzProjectId
                ||published.YzProjectId!=page.YzProjectId||published.YzAdminTypeId!=page.YzAdminTypeId)
                throw new InvalidOperationException("平台基础设施页面不是当前发布目录的同归属页面，未生成结构计划。");
        }
        var physical=Clone(application);var pageIds=current.Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        physical.ConfigPages.RemoveAll(page=>pageIds.Contains(page.Id));physical.ConfigPageColumns.RemoveAll(field=>pageIds.Contains(field.ConfigPageId));
        var result=Merge(physical);
        // 原布局仅在物理事实核验完成后恢复到结果副本，不授予新CRUD或生成安全模块的资格。
        result.ConfigPages=Clone(application.ConfigPages);result.ConfigPageColumns=Clone(application.ConfigPageColumns);
        return result;
    }
    /// <summary>保留通用合成器全部冲突门禁，平台例外只移除已核页面对物理校验的干扰。</summary>
    private MetadataCatalog Merge(MetadataCatalog application)
    {
        var physical=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(application,files),security);
        return MetadataCatalogComposition.MergePhysical(physical,AgentFoundationCatalog.ForApplication(application));
    }
    /// <summary>目录和结果都为独立副本，结构预览不能修改调用者元数据。</summary>
    private static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,Json),Json)!;
}
