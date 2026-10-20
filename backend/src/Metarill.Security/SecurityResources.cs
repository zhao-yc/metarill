namespace Metarill.Runtime.Security;

/// <summary>宿主提供已发布安全资源，不包含连接、字段配置、SQL或平台编辑能力。</summary>
public interface ISecurityResourceRegistry
{
    IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs { get; }
    /// <summary>只有宿主声明的真实平台后台可产生平台管理员身份。</summary>
    bool IsPlatformRealm(string projectId,string adminTypeId);
}

/// <summary>普通认证和锁后决策共用的窄资源投影；调用方负责使用正确的已发布或当前读来源。</summary>
public sealed record SecurityResourceCatalog(string ProjectId,IReadOnlyList<SecurityTableResource> Tables,
    IReadOnlyList<SecurityPageResource> Pages,IReadOnlyList<SecurityMenuResource> Menus,IReadOnlyList<SecurityButtonResource> Buttons);

/// <summary>表身份只用于与已发布物理表交集，不携带字段、连接或查询定义。</summary>
public sealed record SecurityTableResource(string Id,string TableName);

/// <summary>页面只携带固定归属和动作开关；具体路由、范围和输入仍由原Service验证。</summary>
public sealed record SecurityPageResource(string Id,string TableId,string ProjectId,string AdminTypeId,
    bool CanCreate,bool CanUpdate,bool CanDelete,bool CanExport,bool CanCopy=false,bool CanImport=false);

/// <summary>菜单显示授权只以真实项目、后台和已发布ID取交集。</summary>
public sealed record SecurityMenuResource(string Id,string ProjectId,string AdminTypeId);

/// <summary>按钮绑定固定页面，菜单显示不能替代按钮或页面动作权限。</summary>
public sealed record SecurityButtonResource(string Id,string PageId);
