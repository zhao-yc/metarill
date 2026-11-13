using Metarill.Core.Security;

namespace Metarill.Runtime.Files;

/// <summary>宿主按认证身份选择文件运行时；公开路由提示只定位项目，不授予私有访问权限。</summary>
public interface IFileLibraryResolver
{
    /// <summary>列表、写入和受保护读取必须选择当前认证项目。</summary>
    FileLibraryService RequireCurrent();
    /// <summary>公开流仍核验实际记录项目、完成状态与公开级别。</summary>
    Task<FileContentHandle> OpenPublicAsync(long id, string? projectId, CancellationToken token = default);
}

/// <summary>独立应用只有一个发布项目，客户端提示不能把访问切换到其他项目。</summary>
public sealed class SingleDatabaseFileLibraryResolver(FileLibraryService files, FileMetadataContext metadata, IRuntimeAuthorization authorization) : IFileLibraryResolver
{
    private readonly string project = metadata.Catalogs.Count == 1 ? metadata.Catalogs[0].YzProjectId
        : throw new InvalidOperationException("单库文件解析器必须绑定唯一发布项目。");

    /// <summary>管理员身份也不能将独立宿主切到其他项目。</summary>
    public FileLibraryService RequireCurrent()
    {
        if (authorization.RequireActor().ProjectId != project) throw new UnauthorizedAccessException("文件项目与认证身份不一致。");
        return files;
    }

    /// <summary>无提示使用唯一项目；错误提示明确拒绝，不能忽略后继续读取。</summary>
    public Task<FileContentHandle> OpenPublicAsync(long id, string? projectId, CancellationToken token = default)
    {
        if (projectId is not null && projectId != project) throw new UnauthorizedAccessException("公开文件项目未发布。");
        return files.OpenContentAsync(id, true, cancellationToken: token, expectedProjectId: project);
    }
}
