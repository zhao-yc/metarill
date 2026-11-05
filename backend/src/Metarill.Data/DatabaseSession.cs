using FreeSql;
using Metarill.Data.Queries;
using System.Data.Common;

namespace Metarill.Data;

/// <summary>一个实例可供多个请求使用；事务仅沿当前异步调用链传播。</summary>
public sealed class DatabaseSession : IDisposable
{
    private sealed class TransactionScope(DbTransaction transaction)
    {
        public DbTransaction Transaction { get; } = transaction;
        public bool RollbackOnly { get; set; }
        public HashSet<MySqlNonRedundantReadPlan>? PinnedRelationLayouts { get; set; }
    }
    private readonly AsyncLocal<TransactionScope?> _transaction = new();
    private readonly bool _ownsOrm;
    private readonly Lock _ormGate = new();
    private IFreeSql? _orm;
    private bool _disposed;
    public DatabaseOptions Options { get; }
    public IDatabaseProvider Provider { get; }
    /// <summary>首次实际使用 ORM 时初始化，单纯解析会话及手写 SQL 不启动额外连接池。</summary>
    public IFreeSql Orm
    {
        get
        {
            lock (_ormGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _orm ??= Provider.CreateOrm(Options);
            }
        }
    }
    public DbTransaction? CurrentTransaction => _transaction.Value?.Transaction;

    /// <summary>只在实际持有元数据锁的会话事务内复用同一发布计划的结构证明。</summary>
    internal HashSet<MySqlNonRedundantReadPlan>? PinnedRelationLayouts(DbTransaction transaction)
    {
        if (_transaction.Value is not { } scope || !ReferenceEquals(scope.Transaction, transaction)) return null;
        return scope.PinnedRelationLayouts ??= new(ReferenceEqualityComparer.Instance);
    }

    public DatabaseSession(DatabaseOptions options) : this(options, null, null) { }

    /// <summary>默认按需创建并拥有一个 ORM；测试或宿主可注入共享实例。</summary>
    public DatabaseSession(DatabaseOptions options, IFreeSql? orm, IDatabaseProvider? provider = null)
    {
        Options = options;
        Provider = provider ?? DatabaseProviders.Resolve(options.Provider);
        Provider.Validate(options);
        _orm = orm;
        _ownsOrm = orm is null;
    }

    /// <summary>创建强类型 Select 指令并加入当前业务事务。</summary>
    public ISelect<T> Select<T>() where T : class => Orm.Select<T>().WithTransaction(CurrentTransaction);
    /// <summary>创建强类型 Insert 指令并加入当前业务事务。</summary>
    public IInsert<T> Insert<T>() where T : class => Orm.Insert<T>().WithTransaction(CurrentTransaction);
    /// <summary>创建强类型 Update 指令并加入当前业务事务。</summary>
    public IUpdate<T> Update<T>() where T : class => Orm.Update<T>().WithTransaction(CurrentTransaction);
    /// <summary>创建强类型 Delete 指令并加入当前业务事务。</summary>
    public IDelete<T> Delete<T>() where T : class => Orm.Delete<T>().WithTransaction(CurrentTransaction);

    /// <summary>完整业务流程开启事务；内层 Service 加入同一事务，任何内层失败均阻止外层提交。</summary>
    public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)=>ExecuteTransactionAsync(action,false,cancellationToken);

    /// <summary>固定图分批读取与来源查询共享一个快照；外层事务不满足条件时明确拒绝。</summary>
    public Task<T> InReadSnapshotAsync<T>(Func<Task<T>> action,CancellationToken cancellationToken=default)=>ExecuteTransactionAsync(action,true,cancellationToken);

    /// <summary>读快照与业务事务共用取消、回滚标记、嵌套和异步上下文边界。</summary>
    private async Task<T> ExecuteTransactionAsync<T>(Func<Task<T>> action,bool readSnapshot,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_transaction.Value is { } parent)
        {
            try { if(readSnapshot)await Provider.EnsureReadSnapshotAsync(parent.Transaction,cancellationToken);return await action(); }
            catch { parent.RollbackOnly = true; throw; }
        }
        await using var connection = Provider.CreateConnection(Options);
        await connection.OpenAsync(cancellationToken);
        var beginObservation=DatabaseCommandObservation.StartTransaction(connection,null,DatabaseTransactionOperation.Begin);
        DbTransaction transaction;
        try
        {
            transaction=readSnapshot?await Provider.BeginReadSnapshotAsync(connection,cancellationToken):await connection.BeginTransactionAsync(cancellationToken);
            beginObservation?.Complete(transaction,null);
        }
        catch(Exception error){beginObservation?.Complete(null,error);throw;}
        await using var ownedTransaction=transaction;
        var current = new TransactionScope(transaction);
        _transaction.Value = current;
        try
        {
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            if (current.RollbackOnly) throw new InvalidOperationException("内层操作失败，事务已被标记为仅回滚。");
            var commitObservation=DatabaseCommandObservation.StartTransaction(connection,transaction,DatabaseTransactionOperation.Commit);
            try{await transaction.CommitAsync(cancellationToken);commitObservation?.Complete(transaction,null);}
            catch(Exception error){commitObservation?.Complete(transaction,error);throw;}
            return result;
        }
        catch
        {
            // 回滚不使用已取消的请求令牌，避免取消使清理动作被跳过。
            var rollbackObservation=DatabaseCommandObservation.StartTransaction(connection,transaction,DatabaseTransactionOperation.Rollback);
            try{await transaction.RollbackAsync(CancellationToken.None);rollbackObservation?.Complete(transaction,null);}
            catch(Exception error){rollbackObservation?.Complete(transaction,error);throw;}
            throw;
        }
        finally { _transaction.Value = null; }
    }

    /// <summary>仅释放本会话创建的 ORM，宿主注入的共享实例由宿主管理。</summary>
    public void Dispose()
    {
        IFreeSql? ownedOrm;
        lock (_ormGate)
        {
            if (_disposed) return;
            _disposed = true;
            ownedOrm = _ownsOrm ? _orm : null;
        }
        ownedOrm?.Dispose();
    }
}
