using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Files;

/// <summary>文件模块只读取已发布的业务模型快照，不依赖平台的双项目注册器。</summary>
public sealed class FileMetadataContext
{
    public IReadOnlyList<MetadataCatalog> Catalogs { get; }
    private readonly Dictionary<(string Project,string Table),IReadOnlyList<ColumnMetadata>> physicalColumns=[];
    /// <summary>发布时编译文件引用所用物理列，运行时不按提交值或派生名称重新解释存储归属。</summary>
    internal IReadOnlyList<ColumnMetadata> PhysicalColumns(MetadataCatalog catalog,TableMetadata table)
    {
        if(!Catalogs.Contains(catalog)||!catalog.ConfigTables.Contains(table))throw new InvalidOperationException("文件存储计划不属于当前发布快照。");
        return physicalColumns[(catalog.YzProjectId,table.Id)];
    }
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
        foreach(var catalog in snapshots)foreach(var table in catalog.ConfigTables)physicalColumns.Add((catalog.YzProjectId,table.Id),MetadataStoragePolicy.ForTable(catalog,table).PhysicalColumns);
    }
}
