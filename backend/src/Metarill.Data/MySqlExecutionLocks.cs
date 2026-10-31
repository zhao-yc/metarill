using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace Metarill.Data;

public sealed partial class MySqlDatabaseProvider
{
    /// <summary>使用不入池的独立连接；单一写实例协调，不在业务事务中持有或释放命名锁。</summary>
    public Task<IDatabaseExecutionLease?> TryAcquireExecutionLeaseAsync(DatabaseOptions options,string scope,TimeSpan lifetime,TimeProvider clock,CancellationToken cancellationToken=default)
    {
        Validate(options);var seconds=MySqlExecutionLocks.Seconds(lifetime);ArgumentNullException.ThrowIfNull(clock);
        var key=MySqlExecutionLocks.Key(options.ExpectedDatabase,scope);
        return MySqlExecutionLocks.AcquireAsync(MySqlExecutionLocks.CreateConnection(options),key,seconds,clock,cancellationToken);
    }
}

/// <summary>连接只执行有界固定命令；服务端空闲超时与本机计时器共同维持原30分钟上限。</summary>
internal static class MySqlExecutionLocks
{
    /// <summary>单独构造不入池的协调通道，不改变业务库原有池设置，不允许多地址负载均衡拆分锁域。</summary>
    internal static MySqlConnection CreateConnection(DatabaseOptions options)
    {
        var builder=new MySqlConnectionStringBuilder(options.ConnectionString){Pooling=false};
        if(builder.Server.Contains(','))throw new NotSupportedException("按钮命名锁要求固定的单一MySQL写实例，不支持多主地址轮询。");
        return new(builder.ConnectionString);
    }
    /// <summary>完整SHA256十六进制正好64字符，库/用途/项目/按钮均参与命名，不包含账号或记录。</summary>
    internal static string Key(string database,string scope)
    {
        if(string.IsNullOrWhiteSpace(scope)||scope.Length>256)throw new ArgumentException("执行租约范围无效。");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("metarill-execution-v1\0"+database+"\0"+scope)));
    }
    /// <summary>有效期为整秒且最多30分钟，避免无限锁与秒舍入扩大承诺。</summary>
    internal static int Seconds(TimeSpan value)=>value.TotalSeconds>=1&&value.TotalSeconds<=1800&&value.TotalSeconds==Math.Truncate(value.TotalSeconds)?(int)value.TotalSeconds:throw new ArgumentException("执行租约有效期必须是1至1800整秒。");
    /// <summary>只在GET_LOCK明确返回1后交付所有权；忙、取消、协议错误均先关闭独占连接。</summary>
    internal static async Task<IDatabaseExecutionLease?> AcquireAsync(DbConnection connection,string key,int seconds,TimeProvider clock,CancellationToken token)
    {
        var transferred=false;
        try
        {
            token.ThrowIfCancellationRequested();await connection.OpenAsync(token);
            // 命名锁本身无TTL；独占连接此后没有业务命令，服务端超时能处理进程失联而不依赖本机计时器。
            await using(var timeout=connection.CreateCommand())
            {
                timeout.CommandTimeout=5;timeout.CommandText="SET SESSION wait_timeout = @lease_seconds";
                var value=timeout.CreateParameter();value.ParameterName="lease_seconds";value.Value=seconds;timeout.Parameters.Add(value);await timeout.ExecuteNonQueryAsync(token);
            }
            object? result;long started;
            await using(var command=connection.CreateCommand())
            {
                command.CommandTimeout=5;command.CommandText="SELECT GET_LOCK(@lease_key, 0)";
                var name=command.CreateParameter();name.ParameterName="lease_key";name.Value=key;command.Parameters.Add(name);started=clock.GetTimestamp();result=await command.ExecuteScalarAsync(token);
            }
            if(result is null or DBNull)throw new InvalidOperationException("数据库未返回有效执行租约结果。");
            var status=Convert.ToInt32(result,CultureInfo.InvariantCulture);
            if(status==0)return null;if(status!=1)throw new InvalidOperationException("数据库执行租约结果无效。");
            token.ThrowIfCancellationRequested();var lease=new Lease(connection,seconds,clock,started);transferred=true;return lease;
        }
        finally{if(!transferred)await connection.DisposeAsync();}
    }
    private sealed class Lease:IDatabaseExecutionLease
    {
        private readonly DbConnection connection;private readonly TimeProvider clock;private readonly long started;private readonly TimeSpan lifetime;private ITimer? timer;
        private readonly TaskCompletionSource closed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int closing,expired,failed;
        public bool IsExpired=>Volatile.Read(ref expired)!=0||clock.GetElapsedTime(started)>=lifetime;
        public bool CleanupFailed=>Volatile.Read(ref failed)!=0;
        /// <summary>计时器只能关闭自己的不入池连接，因此旧租约永远不能释放后继所有者的锁。</summary>
        internal Lease(DbConnection connection,int seconds,TimeProvider clock,long started)
        {
            this.connection=connection;this.clock=clock;this.started=started;lifetime=TimeSpan.FromSeconds(seconds);
            var remaining=lifetime-clock.GetElapsedTime(started);var created=clock.CreateTimer(static state=>((Lease)state!).Close(true),this,remaining>TimeSpan.Zero?remaining:TimeSpan.Zero,Timeout.InfiniteTimeSpan);
            timer=created;if(Volatile.Read(ref closing)!=0)created.Dispose();
        }
        /// <summary>取消请求也必须清理；并发Dispose与到期共用同一个完成任务。</summary>
        public ValueTask DisposeAsync(){Close(false);return new(closed.Task);}
        private void Close(bool expiration)
        {
            if(Interlocked.CompareExchange(ref closing,1,0)!=0)return;
            if(expiration)Volatile.Write(ref expired,1);
            try{timer?.Dispose();}catch(Exception){Volatile.Write(ref failed,1);}finally{_=CloseCoreAsync();}
        }
        /// <summary>不执行跨连接RELEASE，也不重连；关闭失败只报告清理不确定，不篡改已经完成的业务结果。</summary>
        private async Task CloseCoreAsync()
        {
            try{await connection.DisposeAsync();}catch(Exception){Volatile.Write(ref failed,1);}finally{closed.TrySetResult();}
        }
    }
}
