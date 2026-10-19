using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Files;

/// <summary>文件模块只读取已发布的业务模型快照，不依赖平台的双项目注册器。</summary>
public sealed class FileMetadataContext
{
    public IReadOnlyList<MetadataCatalog> Catalogs { get; }
    /// <summary>初始化时固定来源并拒绝重复归属，防止同一页面被不同项目解释。</summary>
    public FileMetadataContext(IReadOnlyList<MetadataCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshots=catalogs.Select(catalog=>JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog,options),options)!).ToArray();
        foreach(var snapshot in snapshots) MetadataValidation.Validate(snapshot);
        if(snapshots.Length==0 || snapshots.Select(c=>c.YzProjectId).Distinct(StringComparer.Ordinal).Count()!=snapshots.Length
            || snapshots.SelectMany(c=>c.ConfigPages).GroupBy(p=>p.Id,StringComparer.Ordinal).Any(group=>group.Count()>1))
            throw new InvalidOperationException("文件发布元数据的项目或页面重复。");
        Catalogs=Array.AsReadOnly(snapshots);
    }
}
