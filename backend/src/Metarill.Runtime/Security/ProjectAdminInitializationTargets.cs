using System.Collections.Concurrent;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Agent;
namespace Metarill.Runtime.Security;

/// <summary>目标连接只取宿主服务端登记；固定安全模型只定义共享基础表，不从请求中读取字段或连接串。</summary>
public sealed class ProjectAdminInitializationTargets(TargetDatabaseSessions sessions,MetadataCatalog security,string platformProjectId)
{
    private readonly ConcurrentDictionary<string,SqlRowStore> stores=new(StringComparer.Ordinal);
    private readonly MetadataCatalog infrastructure=MetadataCatalogComposition.MergePhysical(security,AgentFoundationCatalog.ForApplication(security));
    /// <summary>未登记项目明确失败；平台项目不能回退到平台仓储，也不在解析时建表。</summary>
    public SqlRowStore Resolve(string projectId)
    {
        projectId=AdminAccountInitializationBusiness.RequireTargetId(projectId);
        if(projectId==platformProjectId)throw new UnauthorizedAccessException("不能把平台库作为初始化目标。");
        return stores.GetOrAdd(projectId,id=>new SqlRowStore(sessions.Resolve(id),infrastructure));
    }
}
