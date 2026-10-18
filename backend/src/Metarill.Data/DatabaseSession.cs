using FreeSql;
using System.Data.Common;

namespace Metarill.Data;

/// <summary>一个实例可供多个请求使用；事务仅沿当前异步调用链传播。</summary>
public sealed class DatabaseSession : IDisposable
{
    private sealed class TransactionScope(DbTransaction transaction)
    {
        public DbTransaction Transaction { get; } = transaction;
        public bool RollbackOnly { get; set; }
    }
    private readonly AsyncLocal<TransactionScope?> _transaction = new();
    private readonly bool _ownsOrm;
    public DatabaseOptions Options { get; }
    public IDatabaseProvider Provider { get; }
    public IFreeSql Orm { get; }
    public DbTransaction? CurrentTransaction => _transaction.Value?.Transaction;

    public DatabaseSession(DatabaseOptions options) : this(options, null, null) { }

    /// <summary>生产入口默认创建并拥有一个 ORM；测试或宿主可注入共享实例。</summary>
    public DatabaseSession(DatabaseOptions options, IFreeSql? orm, IDatabaseProvider? provider = null)
    {
        Options = options;
        Provider = provider ?? DatabaseProviders.Resolve(options.Provider);
        Provider.Validate(options);
        Orm = orm ?? Provider.CreateOrm(options);
        _ownsOrm = orm is null;
    }

    public ISelect<T> Select<T>() where T : class => Orm.Select<T>().WithTransaction(CurrentTransaction);
    public IInsert<T> Insert<T>() where T : class => Orm.Insert<T>().WithTransaction(CurrentTransaction);
    public IUpdate<T> Update<T>() where T : class => Orm.Update<T>().WithTransaction(CurrentTransaction);
    public IDelete<T> Delete<T>() where T : class => Orm.Delete<T>().WithTransaction(CurrentTransaction);

    /// <summary>完整业务流程开启事务；内层 Service 加入同一事务，任何内层失败均阻止外层提交。</summary>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_transaction.Value is { } parent)
        {
            try { return await action(); }
            catch { parent.RollbackOnly = true; throw; }
        }
        await using var connection = Provider.CreateConnection(Options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = new TransactionScope(transaction);
        _transaction.Value = current;
        try
        {
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            if (current.RollbackOnly) throw new InvalidOperationException("内层操作失败，事务已被标记为仅回滚。");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // 回滚不使用已取消的请求令牌，避免取消使清理动作被跳过。
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally { _transaction.Value = null; }
    }

    public void Dispose()
    {
        if (_ownsOrm) Orm.Dispose();
    }
}

