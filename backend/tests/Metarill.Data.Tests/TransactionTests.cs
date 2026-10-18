using System.Data;
using System.Data.Common;
using FreeSql;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class TransactionTests
{
    private static DatabaseOptions Options => new() { ConnectionString = "Server=127.0.0.1;Database=test_db;User Id=test", ExpectedDatabase = "test_db" };

    [Fact]
    public async Task Nested_calls_share_one_transaction_and_commit_once()
    {
        var provider = new FakeProvider();
        using var session = new DatabaseSession(Options, null, provider);
        var result = await session.InTransactionAsync(async () =>
        {
            var transaction = session.CurrentTransaction;
            Assert.NotNull(transaction);
            return await session.InTransactionAsync(() =>
            {
                Assert.Same(transaction, session.CurrentTransaction);
                return Task.FromResult(42);
            });
        });
        Assert.Equal(42, result);
        Assert.Equal(1, provider.Connection.Begins);
        Assert.Equal(1, provider.Connection.Transaction.Commits);
        Assert.Equal(0, provider.Connection.Transaction.Rollbacks);
        Assert.Null(session.CurrentTransaction);
    }

    [Fact]
    public async Task Caught_nested_failure_still_forces_outer_rollback()
    {
        var provider = new FakeProvider();
        using var session = new DatabaseSession(Options, null, provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InTransactionAsync(async () =>
        {
            try { await session.InTransactionAsync<int>(() => throw new ArgumentException("对象规则失败")); }
            catch (ArgumentException) { }
            return 1;
        }));
        Assert.Equal(0, provider.Connection.Transaction.Commits);
        Assert.Equal(1, provider.Connection.Transaction.Rollbacks);
        Assert.Null(session.CurrentTransaction);
    }

    [Fact]
    public async Task Cancellation_before_commit_rolls_back()
    {
        var provider = new FakeProvider();
        using var session = new DatabaseSession(Options, null, provider);
        using var source = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InTransactionAsync(() =>
        {
            source.Cancel();
            return Task.FromResult(1);
        }, source.Token));
        Assert.Equal(0, provider.Connection.Transaction.Commits);
        Assert.Equal(1, provider.Connection.Transaction.Rollbacks);
    }

    [Fact]
    public async Task Precancelled_request_does_not_open_connection()
    {
        var provider = new FakeProvider();
        using var session = new DatabaseSession(Options, null, provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InTransactionAsync(() => Task.FromResult(1), new CancellationToken(true)));
        Assert.Equal(0, provider.Connection.Opens);
    }

    private sealed class FakeProvider : IDatabaseProvider
    {
        public FakeConnection Connection { get; } = new();
        public string Name => "mysql";
        public DatabaseCapabilities Capabilities => new("mysql", true, false);
        public void Validate(DatabaseOptions options) { }
        public string QuoteIdentifier(string identifier) => new MySqlDatabaseProvider().QuoteIdentifier(identifier);
        public string ParameterPlaceholder(string name) => new MySqlDatabaseProvider().ParameterPlaceholder(name);
        public IFreeSql CreateOrm(DatabaseOptions options) => new MySqlDatabaseProvider().CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options) => Connection;
    }
    private sealed class FakeConnection : DbConnection
    {
        private ConnectionState _state;
        public int Opens { get; private set; }
        public int Begins { get; private set; }
        public FakeTransaction Transaction { get; }
        public FakeConnection() => Transaction = new(this);
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "test_db";
        public override string DataSource => "fake";
        public override string ServerVersion => "8.4";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() { Opens++; _state = ConnectionState.Open; }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return Transaction; }
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
    private sealed class FakeTransaction(FakeConnection connection) : DbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() => Commits++;
        public override void Rollback() => Rollbacks++;
    }
}
