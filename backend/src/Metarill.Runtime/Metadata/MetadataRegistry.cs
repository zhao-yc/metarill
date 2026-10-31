using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Security;
using Metarill.Runtime.Agent;

namespace Metarill.Runtime.Metadata;

/// <summary>发布包内不可变的物理模型；项目可编辑元数据由数据库仓储读取。</summary>
public sealed class MetadataRegistry : IAgentHostResources
{
    public AgentHostCatalog AgentCatalog { get; }
    public MetadataCatalog Platform { get; }
    public MetadataCatalog Demo { get; }
    public string PlatformPath { get; }
    private readonly IReadOnlyList<PagePermissionInheritanceLink> platformInheritance;
    private readonly IReadOnlyList<PagePermissionInheritanceLink> demoInheritance;
    public MetadataRegistry(string platformPath, string demoPath)
    {
        PlatformPath = Path.GetFullPath(platformPath);
        Platform = Load(platformPath); Demo = Load(demoPath);
        AgentCatalog = AgentHostCatalog.ForPlatform(Platform, Demo);
        // 固定继承关系在发布目录加载时编译，普通认证不重复解析控件JSON。
        platformInheritance=PagePermissionInheritance.Compile(Platform);demoInheritance=PagePermissionInheritance.Compile(Demo);
    }
    /// <summary>共享认证只看到固定资源投影；平台原始目录仍由本仓储保管。</summary>
    IReadOnlyList<SecurityResourceCatalog> ISecurityResourceRegistry.PublishedCatalogs=>
        [PlatformSecurityResourceProjection.Create(Platform,platformInheritance),PlatformSecurityResourceProjection.Create(Demo,demoInheritance)];
    /// <summary>真实平台身份继续要求发布项目和发布后台同时匹配。</summary>
    bool ISecurityResourceRegistry.IsPlatformRealm(string projectId,string adminTypeId)=>
        projectId==Platform.YzProjectId&&Platform.YzAdminTypes.Any(item=>item.Id==adminTypeId);
    /// <summary>账号匹配字段来自当前发布模型，与生成SQL固定声明一致，不读取客户端字段或任意后台新值。</summary>
    string? ISecurityResourceRegistry.ExactDataManageManagerField(string projectId,string adminTypeId)
    {
        var catalog=new[]{Platform,Demo}.SingleOrDefault(catalog=>catalog.YzProjectId==projectId);
        var admin=catalog?.YzAdminTypes.SingleOrDefault(admin=>admin.Id==adminTypeId);if(admin is null)return null;
        if(!admin.Extra.TryGetValue("exactDataManageMatchField",out var value)||value.ValueKind==JsonValueKind.Null)return "";
        return value.ValueKind==JsonValueKind.String?value.GetString():throw new NotSupportedException("已发布后台匹配字段声明无效。");
    }
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
