using System.Collections.Concurrent;
using Metarill.Data.Schema;
namespace Metarill.Runtime;

/// <summary>每个宿主维护自己的项目结构计划，避免跨宿主复用已释放连接或签名状态。</summary>
public sealed class ProjectSchemaService(TargetDatabaseSessions sessions,ISchemaChangeJournal journal)
{
    private readonly ConcurrentDictionary<string,SchemaService> schemas=new(StringComparer.Ordinal);
    public SchemaService Resolve(string projectId)=>schemas.GetOrAdd(projectId,id=>new SchemaService(sessions.Resolve(id),journal));
}
