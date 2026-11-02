using System.Data;
using System.Data.Common;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>通过原DatabaseSession事务入口证明边界与命令绑定，不用伪造事务日志代替实际调用。</summary>
public sealed class DatabaseTransactionObservationTests
{
    private static byte[] Key()=>Enumerable.Repeat((byte)3,32).ToArray();
    private static DatabaseOptions Options()=>new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Pooling=false;Connection Timeout=1"};
    /// <summary>嵌套业务共用真实事务，只出现一次开始／提交；命令和边界标识对应，观察不开额外连接。</summary>
    [Fact]
    public async Task Nested_commands_use_the_same_observed_transaction_and_connection()
    {
        var provider=new Provider();var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);
        var value=await session.InTransactionAsync(async()=>
        {
            await Write();return await session.InTransactionAsync(async()=>{await Write();return 18;});
        });
        Assert.Equal(18,value);trace.Dispose();var snapshot=trace.Snapshot();Assert.True(snapshot.InternallyConsistent);Assert.Equal(2,snapshot.Commands.Count);Assert.Equal(new[]{DatabaseTransactionOperation.Begin,DatabaseTransactionOperation.Commit},snapshot.Transactions.Select(entry=>entry.Operation));
        Assert.All(snapshot.Transactions,entry=>{Assert.True(entry.Succeeded);Assert.Equal(1,entry.Transaction);Assert.Equal(1,entry.Connection);});Assert.All(snapshot.Commands,entry=>{Assert.Equal(1,entry.Transaction);Assert.Equal(1,entry.Connection);});
        Assert.Equal(1,provider.Created);Assert.Equal(1,provider.Opened);Assert.Equal(1,provider.Commits);Assert.Equal(0,provider.Rollbacks);Assert.Null(session.CurrentTransaction);
        /// <summary>沿原包装提交实际命令对象，不手工调用观察器。</summary>
        async Task Write(){using var inner=new DatabaseCommandObservationTests.Command{Connection=provider.LastConnection,Transaction=session.CurrentTransaction};using var command=new MySqlFloatingCommand(inner);Assert.Equal(9,await command.ExecuteNonQueryAsync());}
    }
    /// <summary>内层失败虽被业务捕获，原仅回滚规则不变，不能额外记录不存在的提交。</summary>
    [Fact]
    public async Task Rollback_only_inner_failure_still_prevents_commit()
    {
        var provider=new Provider();var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync(async()=>{try{await session.InTransactionAsync<int>(()=>throw new InvalidOperationException("private-nested-failure"));}catch(InvalidOperationException){}return 1;}));
        trace.Dispose();var value=trace.Snapshot();Assert.Equal(new[]{DatabaseTransactionOperation.Begin,DatabaseTransactionOperation.Rollback},value.Transactions.Select(entry=>entry.Operation));Assert.Equal(0,provider.Commits);Assert.Equal(1,provider.Rollbacks);Assert.True(value.InternallyConsistent);Assert.Null(session.CurrentTransaction);
    }
    /// <summary>取消后继续用原None令牌回滚，诊断不能让回滚重新受取消令牌影响。</summary>
    [Fact]
    public async Task Canceled_action_rolls_back_with_uncancelled_token()
    {
        var provider=new Provider();var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);using var cancellation=new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>session.InTransactionAsync(()=>{cancellation.Cancel();return Task.FromResult(1);},cancellation.Token));
        trace.Dispose();Assert.False(provider.RollbackToken.CanBeCanceled);Assert.Equal(0,provider.Commits);Assert.Equal(1,provider.Rollbacks);Assert.Equal(DatabaseTransactionOperation.Rollback,trace.Snapshot().Transactions[^1].Operation);
    }
    /// <summary>开始失败不进入业务，也不虚构回滚或事务ID，保留原异常对象。</summary>
    [Fact]
    public async Task Begin_failure_is_not_replaced_with_a_fake_transaction()
    {
        var failure=new InvalidOperationException("private-begin-failure");var provider=new Provider{BeginFailure=failure};var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);var actionCalled=false;
        Assert.Same(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync(()=>{actionCalled=true;return Task.FromResult(1);})));
        trace.Dispose();var entry=Assert.Single(trace.Snapshot().Transactions);Assert.False(entry.Succeeded);Assert.Null(entry.Transaction);Assert.False(actionCalled);Assert.Equal(0,provider.Commits);Assert.Equal(0,provider.Rollbacks);
    }
    /// <summary>提交与回滚各失败也保持原异常选择及各一次调用，证据只保存类型。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Commit_failure_keeps_original_rollback_and_error_semantics(bool rollbackFails)
    {
        var commit=new InvalidOperationException("private-commit-failure");var rollback=new ArithmeticException("private-rollback-failure");var provider=new Provider{CommitFailure=commit,RollbackFailure=rollbackFails?rollback:null};var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);
        var error=await Record.ExceptionAsync(()=>session.InTransactionAsync(()=>Task.FromResult(1)));Assert.Same(rollbackFails?rollback:commit,error);trace.Dispose();var entries=trace.Snapshot().Transactions;
        Assert.Equal(3,entries.Count);Assert.True(entries[0].Succeeded);Assert.False(entries[1].Succeeded);Assert.Equal(!rollbackFails,entries[2].Succeeded);Assert.Equal(1,provider.Commits);Assert.Equal(1,provider.Rollbacks);Assert.Null(session.CurrentTransaction);
        Assert.DoesNotContain("private-",System.Text.Json.JsonSerializer.Serialize(trace.Snapshot()));
    }
    /// <summary>诊断读取隔离级别失败不影响提交结果，证据必须明确不完整。</summary>
    [Fact]
    public async Task Broken_metadata_is_not_allowed_to_break_the_original_transaction()
    {
        var provider=new Provider{ThrowIsolation=true};var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);Assert.Equal(1,await session.InTransactionAsync(()=>Task.FromResult(1)));trace.Dispose();
        Assert.Equal(1,provider.Commits);Assert.Contains(DatabaseObservationProblem.MetadataFailure,trace.Snapshot().Problems);Assert.False(trace.Snapshot().InternallyConsistent);
    }
    /// <summary>未经过已观察begin的原事务不能仅凭命令有Transaction对象就冒认完整。</summary>
    [Fact]
    public void Preexisting_unobserved_transaction_is_explicitly_incomplete()
    {
        var provider=new Provider();using var connection=provider.CreateConnection(Options());connection.Open();using var transaction=connection.BeginTransaction();var trace=DatabaseCommandObservation.Begin(Key());
        using(var inner=new DatabaseCommandObservationTests.Command{Connection=connection,Transaction=transaction})using(var command=new MySqlFloatingCommand(inner))Assert.Equal(9,command.ExecuteNonQuery());trace.Dispose();
        Assert.Contains(DatabaseObservationProblem.MissingTransactionBegin,trace.Snapshot().Problems);Assert.False(trace.Snapshot().InternallyConsistent);transaction.Rollback();
    }
    /// <summary>事务中途关闭采集不能报告提交已观察；原业务仍按原路径提交，不受采集关闭影响。</summary>
    [Fact]
    public async Task Closing_before_transaction_end_is_rejected_but_still_commits_original_work()
    {
        var provider=new Provider();var trace=DatabaseCommandObservation.Begin(Key());using var session=new DatabaseSession(Options(),null,provider);
        Assert.Equal(1,await session.InTransactionAsync(()=>{trace.Dispose();return Task.FromResult(1);}));Assert.Equal(1,provider.Commits);
        Assert.Contains(DatabaseObservationProblem.UnresolvedTransaction,trace.Snapshot().Problems);Assert.False(trace.Snapshot().InternallyConsistent);Assert.Single(trace.Snapshot().Transactions);
    }
    /// <summary>只创建明确替身连接；ORM仍由原工厂构造，未执行网络或SQL。</summary>
    private sealed class Provider:IDatabaseProvider
    {
        internal int Created,Opened,Commits,Rollbacks;internal Exception? BeginFailure,CommitFailure,RollbackFailure;internal bool ThrowIsolation;internal CancellationToken RollbackToken;internal Connection? LastConnection;
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public string QuoteIdentifier(string value)=>new MySqlDatabaseProvider().QuoteIdentifier(value);public string ParameterPlaceholder(string value)=>"@"+value;
        public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options){Created++;return LastConnection=new(this);}
    }
    /// <summary>替身事务完成后故意禁止重新读Connection／Isolation，证明采集发生在原API之前。</summary>
    private sealed class Connection(Provider owner):DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="private-unused-connection";public override string Database=>"offline";public override string DataSource=>"unused";public override string ServerVersion=>"fake";public override ConnectionState State=>ConnectionState.Open;
        public override void Open()=>owner.Opened++;public override void Close(){}public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel level){if(owner.BeginFailure is not null)throw owner.BeginFailure;return new Transaction(this,owner);}
    }
    private sealed class Transaction(Connection connection,Provider owner):DbTransaction
    {
        private bool finished;public override IsolationLevel IsolationLevel=>owner.ThrowIsolation||finished?throw new InvalidOperationException("private-isolation-failure"):IsolationLevel.RepeatableRead;
        protected override DbConnection DbConnection=>finished?throw new InvalidOperationException("private-after-commit"):connection;
        public override void Commit(){owner.Commits++;if(owner.CommitFailure is not null)throw owner.CommitFailure;finished=true;}
        public override void Rollback(){owner.Rollbacks++;if(owner.RollbackFailure is not null)throw owner.RollbackFailure;finished=true;}
        public override Task RollbackAsync(CancellationToken token){owner.RollbackToken=token;Rollback();return Task.CompletedTask;}
    }
}
