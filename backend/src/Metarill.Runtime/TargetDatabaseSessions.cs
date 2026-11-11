using System.Collections.Concurrent;
using Metarill.Data;
using Metarill.Runtime.Metadata;
namespace Metarill.Runtime;

/// <summary>平台与各项目独立选择数据库；连接只来自服务端配置，不能执行客户端提交的连接串。</summary>
public sealed class TargetDatabaseSessions:IDisposable
{
    private readonly MetadataRegistry registry;
    private readonly IReadOnlyDictionary<string,DatabaseOptions> targets;
    private readonly ConcurrentDictionary<string,DatabaseSession> additional=new(StringComparer.Ordinal);
    public DatabaseSession Platform {get;}
    public DatabaseSession Business {get;}
    public TargetDatabaseSessions(DatabaseOptions platformOptions,DatabaseOptions businessOptions,MetadataRegistry registry,IReadOnlyDictionary<string,DatabaseOptions>? projectDatabases=null)
    {
        this.registry=registry;targets=projectDatabases??new Dictionary<string,DatabaseOptions>();
        Platform=new(platformOptions);Business=new(businessOptions);
    }
    /// <summary>配置页只查看已绑定的提供程序，不启动业务连接，也不使用客户端提供的数据库类型。</summary>
    public string? GetConfiguredProvider(string projectId)
    {
        if (projectId == registry.Platform.YzProjectId) return Platform.Provider.Name;
        if (projectId == registry.Demo.YzProjectId) return Business.Provider.Name;
        return FindAdditionalOptions(projectId)?.Provider;
    }

    /// <summary>每项目固定一个已验证连接；未配置时明确失败，不能退回平台库。</summary>
    public DatabaseSession Resolve(string projectId)
    {
        if(projectId==registry.Platform.YzProjectId)return Platform;
        if(projectId==registry.Demo.YzProjectId)return Business;
        var options = FindAdditionalOptions(projectId)
            ?? throw new InvalidOperationException("项目尚未配置 Metarill:ProjectDatabases 服务端数据库连接。");
        return additional.GetOrAdd(projectId,_=>new DatabaseSession(options));
    }

    private DatabaseOptions? FindAdditionalOptions(string projectId)
    {
        if (!long.TryParse(projectId, out var id) || id <= 0) return null;
        return targets.GetValueOrDefault(projectId);
    }

    public void Dispose(){Platform.Dispose();Business.Dispose();foreach(var session in additional.Values)session.Dispose();}
}
