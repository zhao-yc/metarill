using Metarill.Core.Security;

namespace Metarill.Runtime.Files;

/// <summary>身份选择受保护项目，公开提示仅定位已经发布的固定槽，绝不作为授权依据。</summary>
public sealed class ProjectFileLibraryResolver(ProjectFileRuntimes runtimes, IRuntimeAuthorization authorization, IIdGenerator ids) : IFileLibraryResolver
{
    /// <summary>所有写操作和受保护读取仅使用中间件建立的真实项目身份。</summary>
    public FileLibraryService RequireCurrent() => Service(runtimes.Resolve(authorization.RequireActor().ProjectId));

    /// <summary>旧公开地址仅定位平台槽；新地址携带项目提示，最终仍校验真实记录归属和公开状态。</summary>
    public Task<FileContentHandle> OpenPublicAsync(long id, string? projectId, CancellationToken token = default)
    {
        var project = projectId is null ? runtimes.PlatformProjectId : projectId;
        return Service(runtimes.Resolve(project)).OpenContentAsync(id, publicOnly: true, cancellationToken: token, expectedProjectId: project);
    }

    /// <summary>服务使用槽内固定会话与模型，公开构造过程不会尝试取得登录身份。</summary>
    private FileLibraryService Service(ProjectFileRuntime runtime) => new(runtime.Store, runtime.Metadata, ids, authorization, runtime.Options);
}
