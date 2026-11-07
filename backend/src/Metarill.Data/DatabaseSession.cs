using FreeSql;
using Metarill.Data.Queries;
using MySqlConnector;
using System.Collections.Concurrent;
using System.Data.Common;

namespace Metarill.Data;

/// <summary>一个实例可供多个请求使用；事务仅沿当前异步调用链传播。</summary>
public sealed class DatabaseSession : IDisposable
{
    private sealed class TransactionScope(DbTransaction transaction,bool scalarCacheWrite=false)
    {
        public DbTransaction Transaction { get; } = transaction;
        public bool RollbackOnly { get; set; }
        public List<Func<Task>>? RollbackActions { get; set; }
        public bool ScalarCacheWrite { get; } = scalarCacheWrite;
        public Dictionary<MySqlNonRedundantReadPlan,long>? PinnedRelationLayouts { get; set; }
        public string? PackedAppendVariablePrefix { get; set; }
    }
    private readonly AsyncLocal<TransactionScope?> _transaction = new();
    private readonly ConcurrentDictionary<string, long> _missingInlineTriggerGrants = new(StringComparer.Ordinal);
    private readonly bool _ownsOrm;
    private readonly Lock _resourceGate = new();
    private IFreeSql? _orm;
    private MySqlDataSource? _sharedAppendProofSource;
    private bool _disposed;
    public DatabaseOptions Options { get; }
    public IDatabaseProvider Provider { get; }
    /// <summary>首次实际使用 ORM 时初始化，单纯解析会话及手写 SQL 不启动额外连接池。</summary>
    public IFreeSql Orm
    {
        get
        {
            lock (_resourceGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _orm ??= Provider.CreateOrm(Options);
            }
        }
    }
    public DbTransaction? CurrentTransaction => _transaction.Value?.Transaction;
    internal bool UsesOwnedOrm => _ownsOrm;

    /// <summary>证明连接只读且不借用业务池，避免持有事务的请求等待同池第二条连接。</summary>
    internal MySqlConnection CreateSharedAppendProofConnection()
    {
        lock (_resourceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sharedAppendProofSource is null)
            {
                // 保留目标、身份和连接上限。独立池按需使用，不复制业务池的预热连接数。
                var options = new MySqlConnectionStringBuilder(Options.ConnectionString) { MinimumPoolSize = 0 };
                _sharedAppendProofSource = new MySqlDataSource(options.ConnectionString);
            }
            return _sharedAppendProofSource.CreateConnection();
        }
    }

    /// <summary>短期跳过已确认缺少直接权限的表。缓存只会选择原两阶段写入。</summary>
    internal bool IsInlineCacheTriggerGrantMissing(string table) =>
        _missingInlineTriggerGrants.TryGetValue(table, out var expires) && Environment.TickCount64 < expires;

    /// <summary>只记明确缺少的权限，授权变更最多等待半分钟后重新探测。</summary>
    internal void RememberMissingInlineCacheTriggerGrant(string table) =>
        _missingInlineTriggerGrants[table] = Environment.TickCount64 + 30_000;

    /// <summary>只在实际持有元数据锁的会话事务内复用同一发布计划的结构证明。</summary>
    internal Dictionary<MySqlNonRedundantReadPlan,long>? PinnedRelationLayouts(DbTransaction transaction)
    {
        if (_transaction.Value is not { } scope || !ReferenceEquals(scope.Transaction, transaction)) return null;
        return scope.PinnedRelationLayouts ??= new(ReferenceEqualityComparer.Instance);
    }
    /// <summary>仅由当前会话明确建立的可重复读写事务允许合并缓存写回。</summary>
    internal bool HasScalarCacheWriteProof(DbTransaction transaction)=>
        _transaction.Value is {ScalarCacheWrite:true} scope&&ReferenceEquals(scope.Transaction,transaction);

    /// <summary>每个事务只分配一组私有变量名，连续小批先清空并复用，避免大工作簿累积会话变量。</summary>
    internal string GetPackedAppendVariablePrefix(DbTransaction transaction)
    {
        if (_transaction.Value is not {ScalarCacheWrite:true} scope || !ReferenceEquals(scope.Transaction, transaction))
            throw new InvalidOperationException("合并追加必须属于当前写入事务。");
        // 固定长度和单字母后缀已划清变量名边界，保留 64 位随机量并减少批量 SQL 中反复出现的字符。
        return scope.PackedAppendVariablePrefix ??= "@m" + System.Security.Cryptography.RandomNumberGenerator.GetHexString(16, lowercase:true);
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

    /// <summary>登记本事务新建的外部对象清理。只有未尝试提交且回滚成功时才执行。</summary>
    public void AfterConfirmedRollback(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = _transaction.Value ?? throw new InvalidOperationException("回滚清理必须登记在当前事务内。");
        (scope.RollbackActions ??= []).Add(action);
    }

    /// <summary>生成的标量缓存导入显式固定原 MySQL 可重复读；嵌套调用保持外层事务和原隔离。</summary>
    public Task<T> InScalarCacheImportTransactionAsync<T>(Func<Task<T>> action,CancellationToken cancellationToken=default)=>
        ExecuteTransactionAsync(action,false,cancellationToken,scalarCacheWrite:true);

    /// <summary>固定图分批读取与来源查询共享一个快照；外层事务不满足条件时明确拒绝。</summary>
    public Task<T> InReadSnapshotAsync<T>(Func<Task<T>> action,CancellationToken cancellationToken=default)=>ExecuteTransactionAsync(action,true,cancellationToken);

    /// <summary>读快照与业务事务共用取消、回滚标记、嵌套和异步上下文边界。</summary>
    private async Task<T> ExecuteTransactionAsync<T>(Func<Task<T>> action,bool readSnapshot,CancellationToken cancellationToken,bool scalarCacheWrite=false)
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
        scalarCacheWrite=scalarCacheWrite&&Provider is MySqlDatabaseProvider&&connection is MySqlConnector.MySqlConnection
            &&new MySqlConnector.MySqlConnectionStringBuilder(connection.ConnectionString).ConnectionReset;
        // 连接重置清除上次借用遗留的单事务设置，MySqlConnector 以显式隔离级别开始本事务。
        var beginObservation=DatabaseCommandObservation.StartTransaction(connection,null,DatabaseTransactionOperation.Begin);
        DbTransaction transaction;
        try
        {
            transaction=readSnapshot?await Provider.BeginReadSnapshotAsync(connection,cancellationToken):
                scalarCacheWrite?await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,cancellationToken):
                await connection.BeginTransactionAsync(cancellationToken);
            beginObservation?.Complete(transaction,null);
        }
        catch(Exception error){beginObservation?.Complete(null,error);throw;}
        await using var ownedTransaction=transaction;
        var current = new TransactionScope(transaction,scalarCacheWrite);
        _transaction.Value = current;
        var commitAttempted = false;
        try
        {
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            if (current.RollbackOnly) throw new InvalidOperationException("内层操作失败，事务已被标记为仅回滚。");
            var commitObservation=DatabaseCommandObservation.StartTransaction(connection,transaction,DatabaseTransactionOperation.Commit);
            commitAttempted = true;
            try{await transaction.CommitAsync(cancellationToken);commitObservation?.Complete(transaction,null);}
            catch(Exception error){commitObservation?.Complete(transaction,error);throw;}
            return result;
        }
        catch (Exception error)
        {
            // 回滚不使用已取消的请求令牌，避免取消使清理动作被跳过。
            var rollbackObservation=DatabaseCommandObservation.StartTransaction(connection,transaction,DatabaseTransactionOperation.Rollback);
            try{await transaction.RollbackAsync(CancellationToken.None);rollbackObservation?.Complete(transaction,null);}
            catch(Exception rollbackError){rollbackObservation?.Complete(transaction,rollbackError);throw;}
            // 提交请求已发出时结果可能不明，即使随后回滚返回成功也必须保留对象。
            if (!commitAttempted && current.RollbackActions is { } actions)
            {
                _transaction.Value = null;
                foreach (var cleanup in actions)
                {
                    try { await cleanup(); }
                    catch (Exception cleanupError)
                    {
                        // 清理失败单独留诊断，避免改变业务错误或取消语义。
                        System.Diagnostics.Trace.TraceWarning("事务回滚后清理外部对象失败。错误类型 {0}，原错误类型 {1}。",
                            cleanupError.GetType().Name, error.GetType().Name);
                    }
                }
            }
            throw;
        }
        finally { _transaction.Value = null; }
    }

    /// <summary>释放会话创建的证明池和 ORM，宿主注入的 ORM 由宿主管理。</summary>
    public void Dispose()
    {
        IFreeSql? ownedOrm;
        MySqlDataSource? proofSource;
        lock (_resourceGate)
        {
            if (_disposed) return;
            _disposed = true;
            ownedOrm = _ownsOrm ? _orm : null;
            proofSource = _sharedAppendProofSource;
        }
        try { proofSource?.Dispose(); }
        finally { ownedOrm?.Dispose(); }
    }
}
