using System.Globalization;
namespace Metarill.Data.Relations;

/// <summary>一个事务事件队列统一普通多值与混合单值副本，内部更新不再次进入消费者Service／钩子。</summary>
public sealed class ProjectRelationMutationPropagator(DatabaseSession session)
{
    public const int PackedAppendLimit = RelationMaintenanceExecutor.PackedAppendLimit;

    /// <summary>只对生成器已排除逐行宿主副作用的叶子追加尝试共享父锁。返回 false 时调用原父校验。</summary>
    public Task<bool> TryLockSharedAppendParentAsync(RelationMaintenanceCatalog catalog, string source, string projectId,
        string parent, string? parentId, bool softDelete, Queries.MySqlExactParentCandidate.ImportRecheck recheck,
        CancellationToken token)
    {
        if (session.Options.MySqlSharedAppendProofs?.GetValueOrDefault(source) is not {} proof ||
            !long.TryParse(parentId, NumberStyles.None, CultureInfo.InvariantCulture, out var key) ||
            key <= 0 || key.ToString(CultureInfo.InvariantCulture) != parentId ||
            session.CurrentTransaction is not { Connection: MySqlConnector.MySqlConnection connection } transaction ||
            FindScalarAppendTable(catalog, source, projectId, transaction) is not { Read: {} plan } table ||
            !MatchesSharedAppendParent(table, plan, parent) ||
            !MySqlInsertCacheBatch.UsesPlainOrm(session))
            return Task.FromResult(false);

        return Queries.MySqlExactParentCandidate.TryLockSharedImportParentAsync(
            session, connection, transaction, source, parent, key, softDelete, proof, recheck, token);
    }

    /// <summary>生成追加入口已经核过宿主逐行钩子。只对当前发布目录的叶子来源接管连续写入。</summary>
    public Task<bool> TryInsertPackedScalarSourceAsync<T>(RelationMaintenanceCatalog catalog, string table, string projectId,
        IReadOnlyList<(int Row, long Id, T Entity)> rows, Queries.MySqlExactParentCandidate.BatchRecheck parent,
        CancellationToken token) where T : class
    {
        if (session.CurrentTransaction is not {} transaction || FindScalarAppendTable(catalog, table, projectId, transaction) is not {} consumer)
            return Task.FromResult(false);
        return new RelationMaintenanceExecutor(session).TryInsertPackedScalarAsync(consumer, projectId, rows, transaction, parent, token);
    }

    /// <summary>保留已有生成程序的单行插入与缓存命令入口。</summary>
    public Task<IReadOnlyDictionary<string,object?>?> TryInsertScalarSourceAsync<T>(RelationMaintenanceCatalog catalog,
        string table,long id,string projectId,T entity,CancellationToken token) where T:class =>
        TryInsertScalarSourceAsync(catalog,table,id,projectId,entity,token,null);

    /// <summary>仅在已核验结构的追加事务中合并叶子来源插入。返回 null 表示尚未发出写入，可走原 Mapper。</summary>
    public async Task<IReadOnlyDictionary<string,object?>?> TryInsertScalarSourceAsync<T>(RelationMaintenanceCatalog catalog,
        string table,long id,string projectId,T entity,CancellationToken token,Queries.MySqlExactParentCandidate.BatchRecheck? parent) where T:class
    {
        if(id<=0||session.CurrentTransaction is not {} transaction||FindScalarAppendTable(catalog,table,projectId,transaction) is not {} consumer)
            return null;
        var patches=await new RelationMaintenanceExecutor(session).TryInsertExactScalarAsync(
            consumer,id,projectId,entity,transaction,token,parent);
        return patches is null?null:patches.SingleOrDefault()?.Values??
            new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(new Dictionary<string,object?>());
    }

    /// <summary>两种追加命令共用叶子范围及事务证明，避免跳过会写下游的传播。</summary>
    private MaintenanceTable? FindScalarAppendTable(RelationMaintenanceCatalog catalog, string table, string projectId,
        System.Data.Common.DbTransaction transaction)
    {
        if (projectId != catalog.ProjectId || !session.UsesOwnedOrm || session.Provider is not MySqlDatabaseProvider ||
            transaction.Connection is not MySqlConnector.MySqlConnection || !session.HasScalarCacheWriteProof(transaction))
            return null;
        return FindScalarAppendLeaf(catalog, table);
    }

    /// <summary>离线安装计划和运行时使用同一叶子来源判定。</summary>
    internal static bool MatchesSharedAppendParent(RelationMaintenanceCatalog catalog, string source, string parent)
        => FindScalarAppendLeaf(catalog, source) is { Read: {} plan } table && MatchesSharedAppendParent(table, plan, parent);

    private static bool MatchesSharedAppendParent(MaintenanceTable table, Queries.MySqlNonRedundantReadPlan plan, string parent)
        => RelationMaintenanceExecutor.CanLockExactScalarSource(table) && plan.ScalarCacheTarget?.Table == parent;

    private static MaintenanceTable? FindScalarAppendLeaf(RelationMaintenanceCatalog catalog, string table)
    {
        if (catalog.Mixed.Targets.Contains(table) || catalog.Propagation.Bindings.Any(binding => binding.Target.Name == table))
            return null;
        return catalog.Mixed.Consumers.SingleOrDefault(item => item.Table.Option.TableName == table)?.Table;
    }

    /// <summary>生成服务传入实际身份／记录项目和变更字段；只返回原实体被刷新值，回执与同事务数据库保持一致。</summary>
    public async Task<IReadOnlyDictionary<string,object?>> AfterMutationAsync(RelationMaintenanceCatalog catalog,string table,long id,string projectId,IReadOnlySet<string> changed,bool allFields,bool refreshSource,CancellationToken token=default)
    {
        if(catalog.RequiresPublishedProjectIdentity&&projectId!=catalog.ProjectId)throw new UnauthorizedAccessException("当前身份不属于已发布全局来源目录。");
        if(session.Options.Provider!="mysql")throw new NotSupportedException("该数据库尚未完成项目关系传播验收。");if(session.CurrentTransaction is null)throw new InvalidOperationException("项目关系传播要求原业务事务。");if(id<=0||!long.TryParse(projectId,System.Globalization.NumberStyles.None,CultureInfo.InvariantCulture,out var project)||project<=0||project.ToString(CultureInfo.InvariantCulture)!=projectId)throw new ArgumentException("关系事件缺少规范记录或项目ID。");
        var patches=new Dictionary<string,object?>(StringComparer.Ordinal);var queue=new Queue<(string Table,long Id,IReadOnlySet<string> Fields,bool All,bool TargetSaved)>();var validated=new HashSet<string>(StringComparer.Ordinal);var mixed=new MySqlMixedRelationMutationExecutor(session);
        void Changed(string source,long key,IReadOnlyDictionary<string,object?> values)
        {
            if(source==table&&key==id)foreach(var pair in values)patches[pair.Key]=pair.Value;queue.Enqueue((source,key,new HashSet<string>(values.Keys,StringComparer.Ordinal),false,false));
        }
        try
        {
            if(refreshSource&&catalog.Mixed.Consumers.SingleOrDefault(consumer=>consumer.Table.Option.TableName==table) is{} own&&(allFields||own.Inputs.Overlaps(changed)))await mixed.RefreshSourceAsync(own,id,projectId,validated,Changed,token);
            // 只有原始部分更新触发兼容刷新，维护补丁和下游事件仍携带精确变更字段。
            queue.Enqueue((table,id,new HashSet<string>(changed,StringComparer.Ordinal),allFields,refreshSource&&!allFields));
            while(queue.TryDequeue(out var change))
            {
                token.ThrowIfCancellationRequested();var origin=await new MySqlRelationMutationExecutor(session).AfterMutationAsync(catalog.Propagation,change.Table,change.Id,projectId,change.Fields,change.All,token,Changed);if(change.Table==table&&change.Id==id)foreach(var pair in origin)patches[pair.Key]=pair.Value;
                await mixed.PropagateAsync(catalog.Mixed,change.Table,change.Id,projectId,change.Fields,change.All,change.TargetSaved,validated,Changed,token);
            }
        }
        catch(MySqlConnector.MySqlException error)when(error.Number is 1213 or 1205){throw new InvalidOperationException("混合关系传播遇到并发冲突，当前操作不提交，请重试。",error);}
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(patches);
    }
}
