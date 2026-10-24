using System.Globalization;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Files;

/// <summary>已发布项目的文件基础设施；连接、物理目录和模型均来自宿主，不能由请求指定。</summary>
public sealed record ProjectFileRuntime(DatabaseSession Session, SqlRowStore Store, FileMetadataContext Metadata, FileLibraryOptions Options);

/// <summary>每个已发布项目复用生成 Service 的原始会话；额外配置连接不等于项目已经发布。</summary>
public sealed class ProjectFileRuntimes
{
    private readonly IReadOnlyDictionary<string, ProjectFileRuntime> runtimes;
    private readonly MetadataCatalog infrastructure;
    public string PlatformProjectId { get; }

    /// <summary>平台保留原物理目录，业务目录按规范项目标识隔离，初始化只验证模型而不执行 DDL。</summary>
    public ProjectFileRuntimes(TargetDatabaseSessions sessions, MetadataRegistry registry, MetadataCatalog infrastructure, FileLibraryOptions options)
    {
        var published = new FileMetadataContext([registry.Platform, registry.Demo]);
        this.infrastructure = new FileMetadataContext([infrastructure]).Catalogs.Single();
        PlatformProjectId = CanonicalProject(registry.Platform.YzProjectId);
        var slots = new Dictionary<string, ProjectFileRuntime>(StringComparer.Ordinal);
        foreach (var catalog in published.Catalogs)
        {
            var project = CanonicalProject(catalog.YzProjectId);
            MetadataCatalogComposition.MergePhysical(catalog, this.infrastructure);
            var session = sessions.Resolve(project);
            var root = Path.GetFullPath(options.RootDirectory);
            var scopedOptions = options with
            {
                RootDirectory = project == PlatformProjectId ? root : Path.Combine(root, "projects", project),
                PublicContentProjectId = project
            };
            slots.Add(project, new(session, new SqlRowStore(session, this.infrastructure), new([catalog]), scopedOptions));
        }
        runtimes = slots;
    }

    /// <summary>只解析明确发布的项目，未登记项目不得退回平台数据库或另行创建连接。</summary>
    public ProjectFileRuntime Resolve(string projectId) => runtimes.TryGetValue(CanonicalProject(projectId), out var runtime)
        ? runtime : throw new UnauthorizedAccessException("项目尚未发布文件运行时。");

    /// <summary>页面身份来自发布快照，不能拿可编辑元数据或客户端项目覆盖物理事务归属。</summary>
    public (ProjectFileRuntime Runtime, PageMetadata Page) ResolvePage(string pageId)
    {
        foreach (var runtime in runtimes.Values)
        {
            var page = runtime.Metadata.Catalogs.Single().ConfigPages.SingleOrDefault(candidate => candidate.Id == pageId);
            if (page is not null) return (runtime, page);
        }
        throw new UnauthorizedAccessException("来源页面未登记。");
    }

    /// <summary>完整项目显式结构预览和初始化共用相同基础模型，普通宿主启动不进行结构同步。</summary>
    public MetadataCatalog ComposePhysical(MetadataCatalog application) => MetadataCatalogComposition.MergePhysical(application, infrastructure);

    /// <summary>项目字符串保持唯一表示，防止目录别名和不同拼写产生第二个槽。</summary>
    private static string CanonicalProject(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || value != id.ToString(CultureInfo.InvariantCulture))
            throw new UnauthorizedAccessException("项目标识无效。");
        return value;
    }
}
