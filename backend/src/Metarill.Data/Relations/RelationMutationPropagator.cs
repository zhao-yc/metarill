namespace Metarill.Data.Relations;

/// <summary>传播的数据库中立入口，具体反查／锁／JSON方言集中在适配层。</summary>
public sealed class RelationMutationPropagator(DatabaseSession session)
{
    /// <summary>执行发布计划并返回原实体补丁，不能从业务请求提供SQL或改变事务边界。</summary>
    public Task<IReadOnlyDictionary<string,object?>> AfterMutationAsync(RelationMutationPlan plan,string table,long id,string projectId,IReadOnlySet<string> changed,bool allFields,CancellationToken token=default)=>session.Options.Provider switch
    {
        "mysql"=>new MySqlRelationMutationExecutor(session).AfterMutationAsync(plan,table,id,projectId,changed,allFields,token),
        _=>throw new NotSupportedException("该方言尚未完成关系传播验收。")
    };
}
