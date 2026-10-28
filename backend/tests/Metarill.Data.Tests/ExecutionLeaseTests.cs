using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FreeSql;
using Metarill.Core.Contracts;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>真实提供程序SQL/租约状态机与统一守卫；替身只实现连接传输，MySQL跨进程语义另验。</summary>
public sealed class ExecutionLeaseTests
{
    private static DatabaseOptions Options=>new(){ConnectionString="Server=127.0.0.1;Database=test_db;User ID=unused;Pooling=true;ConnectionReset=false",ExpectedDatabase="test_db"};
    [Fact]
    public void Dedicated_channel_is_unpooled_and_business_options_are_unchanged()
    {
        using var connection=MySqlExecutionLocks.CreateConnection(Options);Assert.False(new MySqlConnectionStringBuilder(connection.ConnectionString).Pooling);
        Assert.True(new MySqlConnectionStringBuilder(Options.ConnectionString).Pooling);
        Assert.Throws<NotSupportedException>(()=>MySqlExecutionLocks.CreateConnection(Options with{ConnectionString="Server=host1,host2;Database=test_db;User ID=unused"}));
        var key=MySqlExecutionLocks.Key("test_db","original-button:54:5");Assert.Equal(64,key.Length);Assert.Matches("^[a-f0-9]{64}$",key);
        Assert.Equal(key,MySqlExecutionLocks.Key("test_db","original-button:54:5"));Assert.NotEqual(key,MySqlExecutionLocks.Key("other_db","original-button:54:5"));Assert.NotEqual(key,MySqlExecutionLocks.Key("test_db","original-button:54:6"));
    }
    [Fact]
    public async Task Lease_sets_server_timeout_before_nonblocking_parameterized_lock_and_closes_only_its_connection()
    {
        var connection=new Connection();var clock=new Clock();var key=MySqlExecutionLocks.Key("test_db","original-button:54:5");
        var lease=Assert.IsAssignableFrom<IDatabaseExecutionLease>(await MySqlExecutionLocks.AcquireAsync(connection,key,1800,clock,CancellationToken.None));
        Assert.Equal(new[]{"SET SESSION wait_timeout = @lease_seconds","SELECT GET_LOCK(@lease_key, 0)"},connection.Sql);
        Assert.Equal(1800,connection.Parameters[0]["lease_seconds"]);Assert.Equal(key,connection.Parameters[1]["lease_key"]);Assert.All(connection.Transactions,Assert.Null);
        Assert.Equal(TimeSpan.FromMinutes(30),clock.Due);Assert.Equal(0,connection.Closes);
        await Task.WhenAll(lease.DisposeAsync().AsTask(),lease.DisposeAsync().AsTask());Assert.Equal(1,connection.Closes);Assert.False(lease.IsExpired);Assert.False(lease.CleanupFailed);
        clock.Fire();Assert.Equal(1,connection.Closes);Assert.DoesNotContain(connection.Sql,value=>value.Contains("RELEASE_LOCK",StringComparison.Ordinal));
    }
    [Theory][InlineData("busy")][InlineData("null")][InlineData("invalid")][InlineData("command")][InlineData("cancel-before")][InlineData("cancel-after")][InlineData("timer")]
    public async Task Failed_or_cancelled_acquisition_never_transfers_or_leaks_owner(string scenario)
    {
        using var cancellation=new CancellationTokenSource();var connection=new Connection();var clock=new Clock();
        if(scenario=="busy")connection.Result=0L;if(scenario=="null")connection.Result=DBNull.Value;if(scenario=="invalid")connection.Result=2L;
        if(scenario=="command")connection.Fail=true;if(scenario=="cancel-before")cancellation.Cancel();if(scenario=="cancel-after")connection.AfterLock=cancellation.Cancel;if(scenario=="timer")clock.Fail=true;
        if(scenario=="busy")Assert.Null(await MySqlExecutionLocks.AcquireAsync(connection,new string('a',64),1800,clock,cancellation.Token));
        else Assert.NotNull(await Record.ExceptionAsync(()=>MySqlExecutionLocks.AcquireAsync(connection,new string('a',64),1800,clock,cancellation.Token)));
        Assert.Equal(1,connection.Closes);Assert.Equal(scenario=="cancel-before"?0:1,connection.Opens);
    }
    [Fact]
    public async Task Expiration_and_late_dispose_share_one_cleanup_without_releasing_successor()
    {
        var connection=new Connection();var clock=new Clock();var lease=(await MySqlExecutionLocks.AcquireAsync(connection,new string('a',64),1,clock,CancellationToken.None))!;
        clock.Fire();await lease.DisposeAsync();clock.Fire();Assert.True(lease.IsExpired);Assert.Equal(1,connection.Closes);Assert.Equal(2,connection.Sql.Count);
    }
    [Fact]
    public async Task Cleanup_failure_is_reported_without_dispose_throwing_or_reconnecting()
    {
        var connection=new Connection{FailClose=true};var lease=(await MySqlExecutionLocks.AcquireAsync(connection,new string('a',64),1800,new Clock(),CancellationToken.None))!;
        await lease.DisposeAsync();await lease.DisposeAsync();Assert.True(lease.CleanupFailed);Assert.Equal(1,connection.Opens);Assert.Equal(1,connection.Closes);
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(1801)][InlineData(1.5)]
    public void Invalid_lifetime_is_rejected_before_open(double seconds)=>Assert.Throws<ArgumentException>(()=>MySqlExecutionLocks.Seconds(TimeSpan.FromSeconds(seconds)));
    [Theory][InlineData("success")][InlineData("busy")][InlineData("action-error")][InlineData("cancel-after")][InlineData("cleanup-error")][InlineData("expired")]
    public async Task Guard_calls_business_only_after_lease_and_keeps_original_outcome(string scenario)
    {
        var provider=new Provider{Busy=scenario=="busy"};using var session=new DatabaseSession(Options,null,provider);using var cancellation=new CancellationTokenSource();
        var calls=0;var notices=0;var expected=new BusinessRuleException("原对象规则");
        if(scenario=="cancel-after")provider.OnAcquire=cancellation.Cancel;if(scenario=="cleanup-error")provider.Lease.Fail=true;if(scenario=="expired")provider.Lease.Expired=true;
        var guard=new DatabaseButtonExecutionGuard(_=>session,cleanupFailure:()=>notices++);
        async Task<int> Action(){calls++;await Task.Yield();if(scenario=="action-error")throw expected;return 42;}
        if(scenario=="busy"){var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>guard.ExecuteAsync("54","5",Action,cancellation.Token));Assert.Equal("按钮正在执行中，请稍后再试",error.Message);Assert.Equal(0,calls);Assert.Equal(0,provider.Lease.Closes);}
        else if(scenario=="expired"){await Assert.ThrowsAsync<BusinessRuleException>(()=>guard.ExecuteAsync("54","5",Action,cancellation.Token));Assert.Equal(0,calls);}
        else if(scenario=="action-error")Assert.Same(expected,await Assert.ThrowsAsync<BusinessRuleException>(()=>guard.ExecuteAsync("54","5",Action,cancellation.Token)));
        else if(scenario=="cancel-after")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>guard.ExecuteAsync("54","5",Action,cancellation.Token));
        else Assert.Equal(42,await guard.ExecuteAsync("54","5",Action,cancellation.Token));
        Assert.Equal("original-button:54:5",provider.Scope);Assert.Equal(TimeSpan.FromMinutes(30),provider.Lifetime);
        if(scenario!="busy")Assert.Equal(1,provider.Lease.Closes);Assert.Equal(scenario=="cleanup-error"?1:0,notices);
    }
    /// <summary>取锁响应交付延迟也消耗有效期，即使计时回调尚未调度也不能把租约当成未到期。</summary>
    [Fact]
    public async Task Delayed_acquisition_delivery_is_expired_without_timer_callback()
    {
        var clock=new Clock();var connection=new Connection{AfterLock=()=>clock.Seconds=1800};
        var lease=(await MySqlExecutionLocks.AcquireAsync(connection,new string('a',64),1800,clock,CancellationToken.None))!;
        Assert.True(lease.IsExpired);Assert.Equal(TimeSpan.Zero,clock.Due);await lease.DisposeAsync();Assert.Equal(1,connection.Closes);
    }
    /// <summary>旧提供程序没有租约能力时必须使用接口默认拒绝，不能执行任何业务或改成进程内锁。</summary>
    [Fact]
    public async Task Legacy_provider_default_is_not_an_execution_permission()
    {
        using var session=new DatabaseSession(Options,null,new LegacyProvider());var calls=0;var guard=new DatabaseButtonExecutionGuard(_=>session);
        await Assert.ThrowsAsync<NotSupportedException>(()=>guard.ExecuteAsync("54","5",()=>{calls++;return Task.FromResult(1);}));Assert.Equal(0,calls);
    }
    private sealed class LegacyProvider:IDatabaseProvider
    {
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);
        public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options)=>throw new InvalidOperationException("旧提供程序不能伪造协调连接");public string QuoteIdentifier(string value)=>throw new NotSupportedException();public string ParameterPlaceholder(string value)=>"@"+value;
    }
    private sealed class Clock:TimeProvider
    {
        public override long TimestampFrequency=>1;
        internal long Seconds;public override long GetTimestamp()=>Seconds;
        internal TimeSpan Due;internal bool Fail;private TimerCallback? callback;private object? state;
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {if(Fail)throw new InvalidOperationException("计时器不可用");this.callback=callback;this.state=state;Due=dueTime;return new Timer();}
        internal void Fire()=>callback?.Invoke(state);
        private sealed class Timer:ITimer{public bool Change(TimeSpan due,TimeSpan period)=>false;public void Dispose(){}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
    }
    private sealed class Provider:IDatabaseProvider
    {
        internal string? Scope;internal TimeSpan Lifetime;internal bool Busy;internal Action? OnAcquire;internal readonly FakeLease Lease=new();
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);
        public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options)=>throw new InvalidOperationException("守卫不能建立业务事务连接");public string QuoteIdentifier(string value)=>throw new NotSupportedException();public string ParameterPlaceholder(string value)=>"@"+value;
        public Task<IDatabaseExecutionLease?> TryAcquireExecutionLeaseAsync(DatabaseOptions options,string scope,TimeSpan lifetime,TimeProvider clock,CancellationToken token=default)
        {Scope=scope;Lifetime=lifetime;OnAcquire?.Invoke();return Task.FromResult<IDatabaseExecutionLease?>(Busy?null:Lease);}
    }
    private sealed class FakeLease:IDatabaseExecutionLease
    {internal int Closes;internal bool Fail,Expired;public bool IsExpired=>Expired;public bool CleanupFailed=>false;public ValueTask DisposeAsync(){Closes++;if(Fail)throw new IOException("清理失败");return ValueTask.CompletedTask;}}
    private sealed class Connection:DbConnection
    {
        internal readonly List<string> Sql=[];internal readonly List<Dictionary<string,object?>> Parameters=[];internal readonly List<DbTransaction?> Transactions=[];
        internal object? Result=1L;internal bool Fail,FailClose;internal int Opens,Closes;internal Action? AfterLock;private ConnectionState state;
        [AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"test_db";public override string DataSource=>"offline";public override string ServerVersion=>"8.4";public override ConnectionState State=>state;
        public override void Open(){Opens++;state=ConnectionState.Open;}public override void Close(){Closes++;state=ConnectionState.Closed;}
        public override ValueTask DisposeAsync(){Close();if(FailClose)throw new IOException("断开失败");return ValueTask.CompletedTask;}
        public override void ChangeDatabase(string name)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>throw new InvalidOperationException("租约不能使用业务事务");protected override DbCommand CreateDbCommand()=>new Command(this);
        private sealed class Command(Connection owner):DbCommand
        {
            private readonly ParameterList values=new();[AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=owner;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>values;
            private void Record(){owner.Sql.Add(CommandText);owner.Transactions.Add(DbTransaction);owner.Parameters.Add(values.Items.Cast<DbParameter>().ToDictionary(p=>p.ParameterName,p=>p.Value));Assert.Equal(5,CommandTimeout);if(owner.Fail)throw new IOException("命令失败");}
            public override int ExecuteNonQuery(){Record();return 0;}public override object? ExecuteScalar(){Record();owner.AfterLock?.Invoke();return owner.Result;}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>throw new NotSupportedException();public override void Prepare(){}public override void Cancel(){}protected override DbParameter CreateDbParameter()=>new Parameter();
        }
        private sealed class Parameter:DbParameter
        {[AllowNull]public override string ParameterName{get;set;}="";[AllowNull]public override string SourceColumn{get;set;}="";public override object? Value{get;set;}public override DbType DbType{get;set;}public override ParameterDirection Direction{get;set;}public override bool IsNullable{get;set;}public override bool SourceColumnNullMapping{get;set;}public override int Size{get;set;}public override void ResetDbType(){}}
        private sealed class ParameterList:DbParameterCollection
        {
            internal readonly ArrayList Items=[];public override int Count=>Items.Count;public override object SyncRoot=>Items.SyncRoot;public override int Add(object value)=>Items.Add(value);public override void AddRange(Array values){foreach(var value in values)Items.Add(value!);}public override void Clear()=>Items.Clear();public override bool Contains(object value)=>Items.Contains(value);public override bool Contains(string name)=>IndexOf(name)>=0;public override void CopyTo(Array array,int index)=>Items.CopyTo(array,index);public override IEnumerator GetEnumerator()=>Items.GetEnumerator();public override int IndexOf(object value)=>Items.IndexOf(value);public override int IndexOf(string name)=>Items.Cast<DbParameter>().ToList().FindIndex(value=>value.ParameterName==name);public override void Insert(int index,object value)=>Items.Insert(index,value);public override void Remove(object value)=>Items.Remove(value);public override void RemoveAt(int index)=>Items.RemoveAt(index);public override void RemoveAt(string name)=>Items.RemoveAt(IndexOf(name));protected override DbParameter GetParameter(int index)=>(DbParameter)Items[index]!;protected override DbParameter GetParameter(string name)=>GetParameter(IndexOf(name));protected override void SetParameter(int index,DbParameter value)=>Items[index]=value;protected override void SetParameter(string name,DbParameter value)=>SetParameter(IndexOf(name),value);
        }
    }
}
