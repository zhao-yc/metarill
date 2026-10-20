using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using MySqlConnector;

namespace Metarill.Agent.Tests;

internal sealed record RealmLock(string Table,string? Index,string Type,string Mode,string Status,string? Data);
internal sealed record RealmWait(ulong RequestingConnection,ulong BlockingConnection,RealmLock Requested,RealmLock Held);
internal sealed record RealmIndexShape(string Table,string OwnerColumn,string[] LeadingIndexes);
internal sealed record RealmIndexPlan(string Table,string OwnerColumn,long OwnerId,string[] LeadingIndexes,string? ChosenIndex,string Access,long EstimatedRows);

/// <summary>只读观测本用例连接的MySQL8锁；不读SQL正文、不打开采集、不以任务状态代替数据库等待证据。</summary>
internal sealed class SecurityRealmRrProbe : IAsyncDisposable
{
    private readonly MySqlConnection monitor;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly string serverUuid;
    private readonly string database;
    internal string ServerPin=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serverUuid)));
    internal string Version{get;}
    private SecurityRealmRrProbe(MySqlConnection connection,string uuid,string version,string db){monitor=connection;serverUuid=uuid;Version=version;database=db;}

    /// <summary>只接受既有私有配置的隔离库，记录服务器指纹供独占执行前核对Server200，不输出连接串。</summary>
    internal static async Task<SecurityRealmRrProbe> OpenAsync(DatabaseOptions options,CancellationToken token)
    {
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_test")throw new InvalidOperationException("RR验收只允许既有metarill_test隔离库。");
        var connection=new MySqlConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync(token);string uuid,version,database;
            await using(var command=connection.CreateCommand())
            {
                command.CommandText="SELECT @@server_uuid,VERSION(),DATABASE()";await using var reader=await command.ExecuteReaderAsync(token);
                if(!await reader.ReadAsync(token)||reader.GetString(2)!=options.ExpectedDatabase)throw new InvalidOperationException("RR观测连接不是目标隔离库。");
                uuid=reader.GetString(0);version=reader.GetString(1);database=reader.GetString(2);
            }
            // 身份reader关闭后检查真实列权限；失败时Open不返回，调用方尚未创建任何业务夹具。
            await CheckObservationColumnsAsync(async sql=>{await using var command=connection.CreateCommand();command.CommandText=sql;await using var reader=await command.ExecuteReaderAsync(token);if(await reader.ReadAsync(token))throw new InvalidOperationException("观测权限空集检查意外返回数据。");});
            return new(connection,uuid,version,database);
        }
        catch{await connection.DisposeAsync();throw;}
    }
    /// <summary>只请求三张固定观测表实际使用的列；WHERE 1=0不读锁行、不修改采集或授权。</summary>
    internal static async Task CheckObservationColumnsAsync(Func<string,Task> execute)
    {
        foreach(var(table,sql)in ObservationPermissionQueries)
        {
            try{await execute(sql);}
            catch(Exception error){throw new InvalidOperationException("RR观测权限前置检查失败：performance_schema."+table+"所需列不可读取；尚未创建业务夹具。",error);}
        }
    }
    private static readonly (string Table,string Sql)[] ObservationPermissionQueries=
    [
        ("data_lock_waits","SELECT `ENGINE`,`REQUESTING_ENGINE_LOCK_ID`,`BLOCKING_ENGINE_LOCK_ID`,`REQUESTING_THREAD_ID`,`BLOCKING_THREAD_ID` FROM `performance_schema`.`data_lock_waits` WHERE 1=0"),
        ("data_locks","SELECT `ENGINE`,`ENGINE_LOCK_ID`,`THREAD_ID`,`OBJECT_SCHEMA`,`OBJECT_NAME`,`INDEX_NAME`,`LOCK_TYPE`,`LOCK_MODE`,`LOCK_STATUS`,`LOCK_DATA` FROM `performance_schema`.`data_locks` WHERE 1=0"),
        ("threads","SELECT `THREAD_ID`,`PROCESSLIST_ID` FROM `performance_schema`.`threads` WHERE 1=0")
    ];
    /// <summary>每条业务事务与观测连接必须同库同实例，并明确断言真实隔离级别，不能假设默认就是RR。</summary>
    internal async Task<ulong> RequireRepeatableReadAsync(DbTransaction transaction,CancellationToken token)
    {
        if(transaction.Connection is not MySqlConnection connection)throw new InvalidOperationException("RR验收必须使用真实MySqlConnector事务。");
        await using var command=connection.CreateCommand();command.Transaction=(MySqlTransaction)transaction;command.CommandText="SELECT @@server_uuid,DATABASE(),@@transaction_isolation";
        await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token)||reader.GetString(0)!=serverUuid||reader.GetString(1)!=database||reader.GetString(2)!="REPEATABLE-READ")throw new InvalidOperationException("业务事务实例、数据库或真实RR隔离级别不匹配。");
        return Convert.ToUInt64(connection.ServerThread,CultureInfo.InvariantCulture);
    }
    /// <summary>等待双方连接在真实项目主键产生阻塞关系；仅重读观测表，不重试任何业务写入。</summary>
    internal async Task<RealmWait> WaitForProjectAsync(ulong requesting,ulong blocking,string project,CancellationToken token)
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(10))
        {
            var rows=await WaitsAsync(requesting,blocking,token);
            var match=rows.FirstOrDefault(row=>row.Requested.Table=="yz_project"&&row.Requested.Index=="PRIMARY"&&row.Requested.Type=="RECORD"&&row.Requested.Data==project&&row.Requested.Status=="WAITING"&&row.Held.Status=="GRANTED");
            if(match is not null)return match;await Task.Delay(100,token);
        }
        throw new InvalidOperationException("未取得本用例两连接在项目主键上的真实锁等待证据；不能宣布并发验收通过。");
    }
    /// <summary>只读取两个已知连接间的等待元信息，联结同时使用引擎与锁ID，不解释内部锁ID格式。</summary>
    internal async Task<RealmWait[]> WaitsAsync(ulong requesting,ulong blocking,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var command=monitor.CreateCommand();command.CommandText="""
SELECT rt.PROCESSLIST_ID,bt.PROCESSLIST_ID,
 r.OBJECT_NAME,r.INDEX_NAME,r.LOCK_TYPE,r.LOCK_MODE,r.LOCK_STATUS,r.LOCK_DATA,
 b.OBJECT_NAME,b.INDEX_NAME,b.LOCK_TYPE,b.LOCK_MODE,b.LOCK_STATUS,b.LOCK_DATA
FROM performance_schema.data_lock_waits w
JOIN performance_schema.data_locks r ON r.ENGINE=w.ENGINE AND r.ENGINE_LOCK_ID=w.REQUESTING_ENGINE_LOCK_ID
JOIN performance_schema.data_locks b ON b.ENGINE=w.ENGINE AND b.ENGINE_LOCK_ID=w.BLOCKING_ENGINE_LOCK_ID
JOIN performance_schema.threads rt ON rt.THREAD_ID=w.REQUESTING_THREAD_ID
JOIN performance_schema.threads bt ON bt.THREAD_ID=w.BLOCKING_THREAD_ID
WHERE rt.PROCESSLIST_ID=@requesting AND bt.PROCESSLIST_ID=@blocking AND r.OBJECT_SCHEMA=@db
 AND r.OBJECT_NAME IN ('yz_project','yz_admin_type','yz_manager','yz_role','yz_role_permission')
LIMIT 33
""";
            command.Parameters.AddWithValue("requesting",requesting);command.Parameters.AddWithValue("blocking",blocking);command.Parameters.AddWithValue("db",database);
            var result=new List<RealmWait>();await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))result.Add(new(Convert.ToUInt64(reader.GetValue(0),CultureInfo.InvariantCulture),Convert.ToUInt64(reader.GetValue(1),CultureInfo.InvariantCulture),Lock(reader,2),Lock(reader,8)));
            if(result.Count>32)throw new InvalidOperationException("本用例锁等待观测超过32项，停止而不截断证据。");return result.ToArray();
        }
        finally{gate.Release();}
    }
    /// <summary>在外层事务尚未回滚时查看实际持锁主键；NULL记录锁不可被当成不存在某个后台的证明。</summary>
    internal async Task<RealmLock[]> LocksAsync(ulong connection,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var command=monitor.CreateCommand();command.CommandText="""
SELECT l.OBJECT_NAME,l.INDEX_NAME,l.LOCK_TYPE,l.LOCK_MODE,l.LOCK_STATUS,l.LOCK_DATA
FROM performance_schema.data_locks l JOIN performance_schema.threads t ON t.THREAD_ID=l.THREAD_ID
WHERE t.PROCESSLIST_ID=@connection AND l.OBJECT_SCHEMA=@db
 AND l.OBJECT_NAME IN ('yz_project','yz_admin_type','yz_manager','yz_role','yz_role_permission')
 AND (l.LOCK_TYPE='TABLE' OR l.INDEX_NAME='PRIMARY') LIMIT 2001
""";
            command.Parameters.AddWithValue("connection",connection);command.Parameters.AddWithValue("db",database);var result=new List<RealmLock>();
            await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(Lock(reader,0));
            if(result.Count>2000)throw new InvalidOperationException("本用例主键锁超过2000项，停止而不截断。");return result.ToArray();
        }
        finally{gate.Release();}
    }
    private static readonly (string Table,string Owner)[] Relations=[("yz_manager","yz_admin_type_id"),("yz_role","yz_admin_type_id"),("yz_role_permission","yz_role_id")];
    /// <summary>创建业务夹具前只核对结构存在性，不用0或尚不存在的role替代真实业务计划。</summary>
    internal async Task<RealmIndexShape[]> IndexShapesAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try{var result=new List<RealmIndexShape>();foreach(var(table,owner)in Relations)result.Add(new(table,owner,await LeadingIndexes(table,owner,token)));return result.ToArray();}
        finally{gate.Release();}
    }
    /// <summary>每case已创建实际fixture后，以真实参与的后台和角色参数核完整投影计划；不加FORCE INDEX。</summary>
    internal async Task<RealmIndexPlan[]> IndexPlansAsync(MetadataRegistry registry,IReadOnlyCollection<long> admins,IReadOnlyCollection<long> roles,CancellationToken token)
    {
        if(admins.Count==0||roles.Count==0||admins.Count>16||roles.Count>16||admins.Concat(roles).Any(id=>id<=0))throw new InvalidOperationException("EXPLAIN必须指定有界实际正整数后台和角色集合。");
        await gate.WaitAsync(token);
        try
        {
            var result=new List<RealmIndexPlan>();
            foreach(var(table,owner)in Relations)
            {
                var leading=await LeadingIndexes(table,owner,token);var definition=registry.Platform.ConfigTables.Single(row=>row.TableName==table);
                var columns=registry.Platform.ConfigColumns.Where(row=>row.ConfigTableId==definition.Id&&row.IsVirtualColumn==0).ToArray();foreach(var column in columns)Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(column.ColumnName);
                var names=string.Join(",",columns.Select(column=>"`"+column.ColumnName+"`"));var relationSet=table=="yz_role_permission";
                foreach(var value in (relationSet?roles:admins).Distinct().Order())
                {
                    await using var explain=monitor.CreateCommand();explain.CommandText="EXPLAIN SELECT "+names+" FROM `"+table+"` WHERE `delete_no` IS NULL AND `"+owner+(relationSet?"` IN (@owner) ORDER BY `id`":"`=@owner")+" FOR UPDATE";explain.Parameters.AddWithValue("owner",value);
                    await using var plan=await explain.ExecuteReaderAsync(token);if(!await plan.ReadAsync(token))throw new InvalidOperationException("未取得固定安全查询EXPLAIN。");
                    result.Add(new(table,owner,value,leading,plan["key"] is DBNull?null:Convert.ToString(plan["key"],CultureInfo.InvariantCulture),Convert.ToString(plan["type"],CultureInfo.InvariantCulture)!,plan["rows"] is DBNull?0:Convert.ToInt64(plan["rows"],CultureInfo.InvariantCulture)));
                }
            }
            return result.ToArray();
        }
        finally{gate.Release();}
    }
    /// <summary>表与父列只来自三个固定关系声明，SHOW结果不包含连接或业务列内容。</summary>
    private async Task<string[]> LeadingIndexes(string table,string owner,CancellationToken token)
    {
        var leading=new List<string>();await using var indexes=monitor.CreateCommand();indexes.CommandText="SHOW INDEX FROM `"+table+"`";await using var reader=await indexes.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))if(Convert.ToInt32(reader["Seq_in_index"],CultureInfo.InvariantCulture)==1&&Convert.ToString(reader["Column_name"],CultureInfo.InvariantCulture)==owner)leading.Add(Convert.ToString(reader["Key_name"],CultureInfo.InvariantCulture)!);
        return leading.ToArray();
    }
    /// <summary>字段只来自固定锁观测投影；不输出SQL、认证数据或连接配置。</summary>
    private static RealmLock Lock(DbDataReader reader,int offset)=>new(reader.GetString(offset),reader.IsDBNull(offset+1)?null:reader.GetString(offset+1),reader.GetString(offset+2),reader.GetString(offset+3),reader.GetString(offset+4),reader.IsDBNull(offset+5)?null:reader.GetString(offset+5));
    /// <summary>仅释放本次观测连接，不改变服务器采集配置。</summary>
    public async ValueTask DisposeAsync(){await monitor.DisposeAsync();gate.Dispose();}
}
