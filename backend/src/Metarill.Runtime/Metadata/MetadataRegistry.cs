using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

/// <summary>发布包内不可变的物理模型；项目可编辑元数据由数据库仓储读取。</summary>
public sealed class MetadataRegistry : ISecurityResourceRegistry
{
    public MetadataCatalog Platform { get; }
    public MetadataCatalog Demo { get; }
    public string PlatformPath { get; }
    public MetadataRegistry(string platformPath, string demoPath)
    {
        PlatformPath = Path.GetFullPath(platformPath);
        Platform = Load(platformPath); Demo = Load(demoPath);
    }
    /// <summary>共享认证只看到固定资源投影；平台原始目录仍由本仓储保管。</summary>
    IReadOnlyList<SecurityResourceCatalog> ISecurityResourceRegistry.PublishedCatalogs=>
        [PlatformSecurityResourceProjection.Create(Platform),PlatformSecurityResourceProjection.Create(Demo)];
    /// <summary>真实平台身份继续要求发布项目和发布后台同时匹配。</summary>
    bool ISecurityResourceRegistry.IsPlatformRealm(string projectId,string adminTypeId)=>
        projectId==Platform.YzProjectId&&Platform.YzAdminTypes.Any(item=>item.Id==adminTypeId);
    public static MetadataCatalog Load(string path)
    {
        var metadata = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(path), JsonDefaults.Options) ?? throw new InvalidOperationException("元数据文件为空。");
        MetadataValidation.Validate(metadata);
        return metadata;
    }
    public PageMetadata? FindPage(string id) => Platform.ConfigPages.Concat(Demo.ConfigPages).FirstOrDefault(p => p.Id == id);
    public static string Camel(string snake) => System.Text.RegularExpressions.Regex.Replace(snake, "_([a-z])", m => m.Groups[1].Value.ToUpperInvariant());
    public static string Snake(string camel) => System.Text.RegularExpressions.Regex.Replace(camel, "[A-Z]", m => "_" + m.Value.ToLowerInvariant());
}
