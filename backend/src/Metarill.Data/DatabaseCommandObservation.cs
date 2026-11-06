using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Metarill.Data;

/// <summary>仅供可信宿主显式开启的当前异步调用链观测；不开连接、不写文件、不注册全局日志。</summary>
public static class DatabaseCommandObservation
{
    internal const int MaximumSqlLength = 32768;
    internal const int MaximumParameters = 256;
    private static readonly AsyncLocal<DatabaseCommandTrace?> Current=new();
    /// <summary>摘要密钥只用于本轮私有值证明；同链禁止嵌套，防止外层遗漏内部命令。</summary>
    public static DatabaseCommandTrace Begin(ReadOnlySpan<byte> valueKey,int maximumCommands=4096)
    {
        if(valueKey.Length!=32)throw new ArgumentException("值摘要密钥必须为32字节。",nameof(valueKey));
        if(maximumCommands is <1 or >65536)throw new ArgumentOutOfRangeException(nameof(maximumCommands));
        if(Current.Value is {IsClosed:false})throw new InvalidOperationException("同一调用链不能嵌套数据库命令观测。");
        var trace=new DatabaseCommandTrace(valueKey.ToArray(),maximumCommands);Current.Value=trace;return trace;
    }
    /// <summary>默认只有空上下文检查；未启用时不读取SQL、参数、连接或创建委托。</summary>
    internal static DatabaseCommandAttempt? Start(DbCommand command,DatabaseCommandOperation operation,IDatabaseCommandObservationMetadata metadata)=>Current.Value?.Start(command,operation,metadata);
    /// <summary>事务API边界独立于命令计数；未启用时不读取连接或事务元数据。</summary>
    internal static DatabaseTransactionAttempt? StartTransaction(DbConnection connection,DbTransaction? transaction,DatabaseTransactionOperation operation)=>Current.Value?.StartTransaction(connection,transaction,operation);
    /// <summary>关闭只影响当前逻辑链；已派生子任务仍通过同一关闭状态识别迟到命令。</summary>
    internal static void Close(DatabaseCommandTrace trace){if(ReferenceEquals(Current.Value,trace))Current.Value=null;}
}

/// <summary>品牌特有属性由对应数据适配器只读提供，不进入Service／Business或通用快照逻辑。</summary>
internal interface IDatabaseCommandObservationMetadata
{
    /// <summary>只读取对应驱动的真实准备状态。</summary>
    bool Prepared(DbCommand command);
    /// <summary>只读取品牌枚举名，不返回参数原值。</summary>
    string? ParameterType(DbParameter parameter);
}
public enum DatabaseCommandOperation{NonQuery,Scalar,Reader}
public enum DatabaseTransactionOperation{Begin,Commit,Rollback}
public enum DatabaseObservationProblem{CommandLimit,TransactionLimit,SqlLength,ParameterLimit,ValueLimit,UnsupportedValue,MetadataFailure,ClosedWithPendingCommands,MissingTransactionBegin,UnresolvedTransaction,LateCommand}
/// <summary>不保留参数原值、连接串或驱动对象；参数摘要使用本轮私有密钥。</summary>
public sealed record DatabaseParameterObservation(string Name,string DbType,string? ProviderType,string ClrType,string ValueKind,string? ValueHash,ParameterDirection Direction,int Size,byte Precision,byte Scale);
/// <summary>返回reader仅代表ExecuteReader已返回，不代表结果全部读取或数据库完整耗时。</summary>
public sealed record DatabaseCommandEntry(long Number,DatabaseCommandOperation Operation,string Sql,IReadOnlyList<DatabaseParameterObservation> Parameters,string Database,long? Connection,long? Transaction,bool Prepared,bool DriverCalled,bool Succeeded,bool Canceled,string? ErrorType,int? AffectedRows,long StartedTimestamp,long DriverTimestamp,long FinishedTimestamp);
/// <summary>实际事务API返回／失败边界，不伪造BEGIN或COMMIT的服务器SQL文本。</summary>
public sealed record DatabaseTransactionEntry(long Number,DatabaseTransactionOperation Operation,long? Connection,long? Transaction,string? Isolation,bool Succeeded,bool Canceled,string? ErrorType,long StartedTimestamp,long FinishedTimestamp);
/// <summary>内部完整性只针对已接线的命令；不证明未包装路径、Java或服务器全量SQL已覆盖。</summary>
public sealed record DatabaseCommandTraceSnapshot(IReadOnlyList<DatabaseCommandEntry> Commands,IReadOnlyList<DatabaseTransactionEntry> Transactions,long Started,long Completed,long StartedTransactions,long CompletedTransactions,bool Closed,IReadOnlyList<DatabaseObservationProblem> Problems,long TimestampFrequency)
{
    /// <summary>值证明格式独立于CLR对象字符串表示，便于配对工具按版本校验。</summary>
    public string ValueProofFormat=>"metarill-sql-values-v1";
    public bool InternallyConsistent=>Closed&&Started==Completed&&StartedTransactions==CompletedTransactions&&Commands.Count==Completed&&Transactions.Count==CompletedTransactions&&Problems.Count==0;
}

/// <summary>有界只读快照；所有存储为不可变文本、标量和只读集合，不给观察方可修改的DbCommand。</summary>
public sealed class DatabaseCommandTrace:IDisposable
{
    private readonly object gate=new();private readonly byte[] key;private readonly int maximum;
    private readonly List<DatabaseCommandEntry> commands=[];private readonly List<DatabaseTransactionEntry> transactionEntries=[];private readonly HashSet<DatabaseObservationProblem> problems=[];
    private readonly Dictionary<object,long> connections=new(ReferenceEqualityComparer.Instance),transactions=new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object,bool> transactionEnds=new(ReferenceEqualityComparer.Instance);
    private long started,completed,startedTransactions,completedTransactions;private bool closed;
    internal bool IsClosed{get{lock(gate)return closed;}}
    /// <summary>密钥副本由Begin创建，只由此scope拥有并清零。</summary>
    internal DatabaseCommandTrace(byte[] key,int maximum){this.key=key;this.maximum=maximum;}
    /// <summary>先登记意图，准备失败与取消也能留证；超限继续原SQL但整批证据不可通过。</summary>
    internal DatabaseCommandAttempt? Start(DbCommand command,DatabaseCommandOperation operation,IDatabaseCommandObservationMetadata metadata)
    {
        lock(gate)
        {
            if(closed){problems.Add(DatabaseObservationProblem.LateCommand);return null;}
            var number=++started;var retained=number<=maximum;if(!retained)problems.Add(DatabaseObservationProblem.CommandLimit);
            return new(this,command,operation,number,retained,metadata);
        }
    }
    /// <summary>事务登记单独有界；begin失败同样保留，不伪造已建立事务标识。</summary>
    internal DatabaseTransactionAttempt? StartTransaction(DbConnection connection,DbTransaction? transaction,DatabaseTransactionOperation operation)
    {
        lock(gate)
        {
            if(closed){problems.Add(DatabaseObservationProblem.LateCommand);return null;}
            var number=++startedTransactions;var retained=number<=maximum;if(!retained)problems.Add(DatabaseObservationProblem.TransactionLimit);
            return new(this,connection,transaction,operation,number,retained);
        }
    }
    /// <summary>API前读取原事务身份与隔离级别，不能在commit后重新访问失效的连接属性。</summary>
    internal (long? Connection,long? Transaction,string? Isolation) TransactionMetadata(DbConnection connection,DbTransaction? transaction)
    {
        lock(gate)
        {
            if(closed){problems.Add(DatabaseObservationProblem.LateCommand);return (null,null,null);}
            try{return (Id(connections,connection),Id(transactions,transaction),transaction?.IsolationLevel.ToString());}
            catch(Exception){problems.Add(DatabaseObservationProblem.MetadataFailure);return (null,null,null);}
        }
    }
    /// <summary>原事务API的完成证据不包含异常正文；失败观察不会改写原异常。</summary>
    internal void FinishTransaction(DatabaseTransactionAttempt attempt,DbTransaction? transaction,Exception? error)
    {
        lock(gate)
        {
            completedTransactions++;if(closed)problems.Add(DatabaseObservationProblem.LateCommand);
            if(attempt.Retained)
            {
                if(transaction is not null&&error is null)
                {
                    if(attempt.Operation==DatabaseTransactionOperation.Begin)transactionEnds[transaction]=false;
                    else if(transactionEnds.ContainsKey(transaction))transactionEnds[transaction]=true;
                    else problems.Add(DatabaseObservationProblem.MissingTransactionBegin);
                }
                var metadata=attempt.Operation==DatabaseTransactionOperation.Begin?TransactionMetadata(attempt.Connection,transaction):attempt.Metadata;
                transactionEntries.Add(new(attempt.Number,attempt.Operation,metadata.Connection,metadata.Transaction,metadata.Isolation,error is null,error is OperationCanceledException,error?.GetType().FullName,attempt.StartedTimestamp,Stopwatch.GetTimestamp()));
            }
        }
    }
    /// <summary>只复制本时刻证据，调用方不能修改内存中的命令表或问题集合。</summary>
    public DatabaseCommandTraceSnapshot Snapshot()
    {
        lock(gate)return new(Array.AsReadOnly(commands.OrderBy(command=>command.Number).ToArray()),Array.AsReadOnly(transactionEntries.OrderBy(entry=>entry.Number).ToArray()),started,completed,startedTransactions,completedTransactions,closed,Array.AsReadOnly(problems.Order().ToArray()),Stopwatch.Frequency);
    }
    /// <summary>清除摘要密钥与驱动对象引用；未完成命令明确标为不完整，不推定已停止SQL。</summary>
    public void Dispose()
    {
        lock(gate)
        {
            if(!closed){closed=true;if(started!=completed||startedTransactions!=completedTransactions)problems.Add(DatabaseObservationProblem.ClosedWithPendingCommands);if(transactionEnds.Values.Any(ended=>!ended))problems.Add(DatabaseObservationProblem.UnresolvedTransaction);CryptographicOperations.ZeroMemory(key);connections.Clear();transactions.Clear();transactionEnds.Clear();}
        }
        DatabaseCommandObservation.Close(this);
    }
    /// <summary>准备完成后读取实际绑定类型；任何诊断元数据失败只污染证据，不改变业务执行。</summary>
    internal CommandSnapshot Capture(DbCommand command,IDatabaseCommandObservationMetadata metadata)
    {
        lock(gate)
        {
            if(closed){problems.Add(DatabaseObservationProblem.LateCommand);return CommandSnapshot.Empty;}
            try
            {
                var sql=command.CommandText??"";if(sql.Length>DatabaseCommandObservation.MaximumSqlLength){problems.Add(DatabaseObservationProblem.SqlLength);sql=sql[..DatabaseCommandObservation.MaximumSqlLength];}
                var parameters=new List<DatabaseParameterObservation>();if(command.Parameters.Count>DatabaseCommandObservation.MaximumParameters)problems.Add(DatabaseObservationProblem.ParameterLimit);
                foreach(DbParameter parameter in command.Parameters.Cast<DbParameter>().Take(DatabaseCommandObservation.MaximumParameters))
                {
                    var value=parameter.Value;var (kind,data)=Encode(value);string? hash=null;
                    if(data is not null)
                    {
                        var tag=Encoding.UTF8.GetBytes(kind+"\0");var bytes=new byte[tag.Length+data.Length];tag.CopyTo(bytes,0);data.CopyTo(bytes,tag.Length);
                        try{hash=Convert.ToHexStringLower(HMACSHA256.HashData(key,bytes));}
                        finally{CryptographicOperations.ZeroMemory(bytes);CryptographicOperations.ZeroMemory(data);}
                    }
                    parameters.Add(new(parameter.ParameterName,parameter.DbType.ToString(),metadata.ParameterType(parameter),value is null or DBNull?"null":value.GetType().FullName??"unknown",kind,hash,parameter.Direction,parameter.Size,parameter.Precision,parameter.Scale));
                }
                var connection=command.Connection;var transaction=command.Transaction;
                if(transaction is not null&&!transactionEnds.ContainsKey(transaction))problems.Add(DatabaseObservationProblem.MissingTransactionBegin);
                return new(sql,Array.AsReadOnly(parameters.ToArray()),connection?.Database??"",Id(connections,connection),Id(transactions,transaction),metadata.Prepared(command));
            }
            catch(Exception){problems.Add(DatabaseObservationProblem.MetadataFailure);return CommandSnapshot.Empty;}
        }
    }
    /// <summary>只记录结果类型／取消及非查询影响行数，不读取scalar结果、reader数据或异常正文。</summary>
    internal void Finish(DatabaseCommandAttempt attempt,CommandSnapshot snapshot,bool succeeded,Exception? error,int? rows)
    {
        lock(gate)
        {
            completed++;if(closed)problems.Add(DatabaseObservationProblem.LateCommand);
            if(attempt.Retained)commands.Add(new(attempt.Number,attempt.Operation,snapshot.Sql,snapshot.Parameters,snapshot.Database,snapshot.Connection,snapshot.Transaction,snapshot.Prepared,attempt.DriverCalled,succeeded,error is OperationCanceledException,error?.GetType().FullName,rows,attempt.StartedTimestamp,attempt.DriverTimestamp,Stopwatch.GetTimestamp()));
        }
    }
    /// <summary>仅用引用标识连接和事务，不读取连接串、口令或额外连接属性。</summary>
    private static long? Id(Dictionary<object,long> values,object? value){if(value is null)return null;if(!values.TryGetValue(value,out var id)){id=values.Count+1;values.Add(value,id);}return id;}
    /// <summary>只编码已知标量，不调用未知对象的ToString；浮点保留位型，日历时间保留原kind。</summary>
    private (string Kind,byte[]? Data) Encode(object? value)
    {
        string kind,text;
        switch(value)
        {
            case null or DBNull:return ("null",[]);
            case byte[] bytes:if(bytes.Length>65536){problems.Add(DatabaseObservationProblem.ValueLimit);return ("bytes",null);}return ("bytes",bytes.ToArray());
            case string s:kind="string";if(s.Length>16384){problems.Add(DatabaseObservationProblem.ValueLimit);return (kind,null);}text=s;break;
            case bool boolean:kind="boolean";text=boolean?"1":"0";break;
            case byte or sbyte or short or ushort or int or uint or long or ulong:kind="integer";text=((IFormattable)value).ToString(null,CultureInfo.InvariantCulture);break;
            case decimal number:kind="decimal";text=number==0m?"0":number.ToString("0.############################",CultureInfo.InvariantCulture);break;
            case double number when double.IsFinite(number):kind="double-bits";text=BitConverter.DoubleToUInt64Bits(number).ToString("x16",CultureInfo.InvariantCulture);break;
            case float number when float.IsFinite(number):kind="single-bits";text=BitConverter.SingleToUInt32Bits(number).ToString("x8",CultureInfo.InvariantCulture);break;
            case DateTime date:kind="datetime:"+date.Kind; text=date.Ticks.ToString(CultureInfo.InvariantCulture);break;
            case DateOnly date:kind="date";text=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);break;
            case TimeOnly time:kind="time";text=time.Ticks.ToString(CultureInfo.InvariantCulture);break;
            case TimeSpan time:kind="timespan";text=time.Ticks.ToString(CultureInfo.InvariantCulture);break;
            case Guid guid:kind="guid";text=guid.ToString("D");break;
            default:problems.Add(DatabaseObservationProblem.UnsupportedValue);return ("unsupported",null);
        }
        return (kind,Encoding.UTF8.GetBytes(text));
    }
    internal sealed record CommandSnapshot(string Sql,IReadOnlyList<DatabaseParameterObservation> Parameters,string Database,long? Connection,long? Transaction,bool Prepared)
    {
        internal static readonly CommandSnapshot Empty=new("",Array.Empty<DatabaseParameterObservation>(),"",null,null,false);
    }
}

/// <summary>只有显式观测时才创建；原准备／执行各一次，异常始终保持原对象。</summary>
internal sealed class DatabaseCommandAttempt(DatabaseCommandTrace owner,DbCommand command,DatabaseCommandOperation operation,long number,bool retained,IDatabaseCommandObservationMetadata metadata)
{
    internal long Number{get;}=number;internal bool Retained{get;}=retained;internal DatabaseCommandOperation Operation{get;}=operation;
    internal long StartedTimestamp{get;}=Stopwatch.GetTimestamp();internal long DriverTimestamp{get;private set;}internal bool DriverCalled{get;private set;}
    private DatabaseCommandTrace.CommandSnapshot? snapshot;
    /// <summary>快照位于准备之后、原驱动调用之前；不包装返回reader或读取结果。</summary>
    private void Ready(){if(Retained)snapshot=owner.Capture(command,metadata);DriverTimestamp=Stopwatch.GetTimestamp();DriverCalled=true;}
    /// <summary>准备失败仍留意图；错误或成功证据不能替换原结果。</summary>
    private void Complete(bool success,Exception? error,object? result)
    {
        snapshot??=Retained?owner.Capture(command,metadata):DatabaseCommandTrace.CommandSnapshot.Empty;
        owner.Finish(this,snapshot,success,error,Operation==DatabaseCommandOperation.NonQuery&&success&&result is int rows?rows:null);
    }
    /// <summary>同步入口原准备和驱动各执行一次，结果／异常对象不变。</summary>
    internal T Execute<T>(Action prepare,Func<T> execute)
    {
        try{prepare();Ready();var result=execute();Complete(true,null,result);return result;}
        catch(Exception error){Complete(false,error,null);throw;}
    }
    /// <summary>异步入口保持原取消令牌与执行链，不另开后台任务。</summary>
    internal async Task<T> ExecuteAsync<T>(Func<CancellationToken,Task> prepare,Func<CancellationToken,Task<T>> execute,CancellationToken token)
    {
        try{await prepare(token);Ready();var result=await execute(token);Complete(true,null,result);return result;}
        catch(Exception error){Complete(false,error,null);throw;}
    }
}

/// <summary>持有实际API调用前的元数据；结束后不再访问已提交／回滚事务的Connection。</summary>
internal sealed class DatabaseTransactionAttempt
{
    private readonly DatabaseCommandTrace owner;
    internal DbConnection Connection{get;}internal DatabaseTransactionOperation Operation{get;}internal long Number{get;}internal bool Retained{get;}
    internal long StartedTimestamp{get;}=Stopwatch.GetTimestamp();internal (long? Connection,long? Transaction,string? Isolation) Metadata{get;}
    /// <summary>仅保留API调用期间所需原引用，完成记录不持有驱动对象。</summary>
    internal DatabaseTransactionAttempt(DatabaseCommandTrace owner,DbConnection connection,DbTransaction? transaction,DatabaseTransactionOperation operation,long number,bool retained)
    {
        this.owner=owner;Connection=connection;Operation=operation;Number=number;Retained=retained;
        Metadata=retained?owner.TransactionMetadata(connection,transaction):(null,null,null);
    }
    /// <summary>原API完成后记录一次，开始失败时transaction为空，不能冒认成功。</summary>
    internal void Complete(DbTransaction? transaction,Exception? error)=>owner.FinishTransaction(this,transaction,error);
}
