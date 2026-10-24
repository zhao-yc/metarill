using System.Globalization;
namespace Metarill.Data.Relations;

/// <summary>一个事务事件队列统一普通多值与混合单值副本，内部更新不再次进入消费者Service／钩子。</summary>
public sealed class ProjectRelationMutationPropagator(DatabaseSession session)
{
    /// <summary>生成服务传入实际身份／记录项目和变更字段；只返回原实体被刷新值，回执与同事务数据库保持一致。</summary>
    public async Task<IReadOnlyDictionary<string,object?>> AfterMutationAsync(RelationMaintenanceCatalog catalog,string table,long id,string projectId,IReadOnlySet<string> changed,bool allFields,bool refreshSource,CancellationToken token=default)
    {
        if(session.Options.Provider!="mysql")throw new NotSupportedException("该数据库尚未完成项目关系传播验收。");if(session.CurrentTransaction is null)throw new InvalidOperationException("项目关系传播要求原业务事务。");if(id<=0||!long.TryParse(projectId,System.Globalization.NumberStyles.None,CultureInfo.InvariantCulture,out var project)||project<=0||project.ToString(CultureInfo.InvariantCulture)!=projectId)throw new ArgumentException("关系事件缺少规范记录或项目ID。");
        var patches=new Dictionary<string,object?>(StringComparer.Ordinal);var queue=new Queue<(string Table,long Id,IReadOnlySet<string> Fields,bool All)>();var validated=new HashSet<string>(StringComparer.Ordinal);var mixed=new MySqlMixedRelationMutationExecutor(session);
        void Changed(string source,long key,IReadOnlyDictionary<string,object?> values)
        {
            if(source==table&&key==id)foreach(var pair in values)patches[pair.Key]=pair.Value;queue.Enqueue((source,key,new HashSet<string>(values.Keys,StringComparer.Ordinal),false));
        }
        try
        {
            if(refreshSource&&catalog.Mixed.Consumers.SingleOrDefault(consumer=>consumer.Table.Option.TableName==table) is{} own&&(allFields||own.Inputs.Overlaps(changed)))await mixed.RefreshSourceAsync(own,id,projectId,validated,Changed,token);
            queue.Enqueue((table,id,new HashSet<string>(changed,StringComparer.Ordinal),allFields));
            while(queue.TryDequeue(out var change))
            {
                token.ThrowIfCancellationRequested();var origin=await new MySqlRelationMutationExecutor(session).AfterMutationAsync(catalog.Propagation,change.Table,change.Id,projectId,change.Fields,change.All,token,Changed);if(change.Table==table&&change.Id==id)foreach(var pair in origin)patches[pair.Key]=pair.Value;
                await mixed.PropagateAsync(catalog.Mixed,change.Table,change.Id,projectId,change.Fields,change.All,validated,Changed,token);
            }
        }
        catch(MySqlConnector.MySqlException error)when(error.Number is 1213 or 1205){throw new InvalidOperationException("混合关系传播遇到并发冲突，当前操作不提交，请重试。",error);}
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(patches);
    }
}
