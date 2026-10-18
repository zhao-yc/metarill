using Metarill.Data;
using Metarill.Runtime.Metadata;
namespace Metarill.Runtime;

/// <summary>平台库和业务库独立；同项目事务绑定同一数据库会话。</summary>
public sealed class TargetDatabaseSessions(DatabaseOptions platformOptions,DatabaseOptions businessOptions,MetadataRegistry registry):IDisposable
{
    public DatabaseSession Platform {get;}=new(platformOptions);
    public DatabaseSession Business {get;}=new(businessOptions);
    public DatabaseSession Resolve(string projectId)=>projectId==registry.Platform.YzProjectId?Platform:projectId==registry.Demo.YzProjectId?Business:throw new InvalidOperationException("项目尚未配置服务端数据库连接引用。");
    public void Dispose(){Platform.Dispose();Business.Dispose();}
}
