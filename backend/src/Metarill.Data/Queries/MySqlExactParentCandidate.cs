using System.Data.Common;
using System.Globalization;
using MySqlConnector;

namespace Metarill.Data.Queries;

/// <summary>执行已发布的精确物理父键候选。调用者先完成生成服务的身份和页面校验。</summary>
public static class MySqlExactParentCandidate
{
    /// <summary>宿主自定义ORM或全局过滤仍走原强类型查询，防止原生SQL绕过其范围。</summary>
    public static bool CanUse(DatabaseSession session) =>
        session.Provider is MySqlDatabaseProvider && session.UsesOwnedOrm &&
        session.CurrentTransaction?.Connection is MySqlConnection && session.Orm.GlobalFilter.GetAllFilters().Count == 0;

    /// <summary>仅已声明的可重复读追加事务及无宿主写入扩展的 ORM 可以省去首行重复锁读。</summary>
    public static bool CanReuseInitialImportLock(DatabaseSession session) =>
        CanUse(session) && session.CurrentTransaction is {} transaction && session.HasScalarCacheWriteProof(transaction) &&
        Relations.MySqlInsertCacheBatch.UsesPlainOrm(session);

    /// <summary>只记录本批首个成功的原查询，后续各行仍从数据库读取当前父和候选。</summary>
    public sealed class ImportRecheck
    {
        internal (DbTransaction Transaction, string Table, long ParentId, bool SoftDelete, FixedConditionSqlPlan Condition)? FirstRead { get; set; }
        internal (DbTransaction Transaction, string Source, string Table, long ParentId, bool SoftDelete)? SharedParent { get; set; }
        internal BatchRecheck? Batch { get; set; }
    }

    /// <summary>只复用当前事务已实际取得的共享父锁，生成入口据此避免再次升级。</summary>
    public static bool HoldsSharedImportParent(DatabaseSession session, string table, long parentId, bool softDelete, ImportRecheck? recheck) =>
        recheck?.SharedParent is {} shared && ReferenceEquals(shared.Transaction, session.CurrentTransaction) &&
        shared.Table == table && shared.ParentId == parentId && shared.SoftDelete == softDelete;

    /// <summary>部署证据核验后保持原父存在性语义，只变更锁模式。</summary>
    internal static async Task<bool> TryLockSharedImportParentAsync(DatabaseSession session, MySqlConnection connection,
        DbTransaction transaction, string source, string table, long parentId, bool softDelete,
        MySqlSharedAppendProofOptions proof, ImportRecheck recheck, CancellationToken token)
    {
        if (recheck.SharedParent is {} shared)
            return shared.Source == source && HoldsSharedImportParent(session, table, parentId, softDelete, recheck);
        if (recheck.FirstRead is not null ||
            !await MySqlSharedAppendProof.VerifyAsync(session, connection, transaction, source, table, proof, token))
            return false;
        await using var command = MySqlFloatingCommand.Wrap(connection.CreateCommand());
        command.Transaction = transaction;
        BindKey(command, parentId);
        command.CommandText = "SELECT `id`" + ParentFrom(session, table, softDelete) + " FOR SHARE";
        if (await command.ExecuteScalarAsync(token) is null or DBNull)
            throw new InvalidOperationException("父记录不存在或已删除。");
        recheck.SharedParent = (transaction, source, table, parentId, softDelete);
        return true;
    }

    /// <summary>合并命令保留同事务的原父快照；只能从成功的首行证明建立。</summary>
    public sealed class BatchRecheck
    {
        internal const string GuardVariable="@metarill_import_parent_status";
        internal const string Accepted=GuardVariable+"=2";
        internal const string DatabaseGate="CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY)";
        internal DbTransaction Transaction { get; }
        internal string Table { get; }
        internal long ParentId { get; }
        internal string Expression { get; }
        internal string PinnedPrimaryKeyExpression { get; }
        internal string? VerifiedAppendSource { get; }
        internal IReadOnlyDictionary<string,object?> Parameters { get; }

        internal BatchRecheck(DbTransaction transaction,string table,long parentId,string expression,
            string pinnedPrimaryKeyExpression,IReadOnlyDictionary<string,object?> parameters,string? verifiedAppendSource)
        {
            Transaction=transaction;
            Table=table;
            ParentId=parentId;
            Expression=expression;
            PinnedPrimaryKeyExpression=pinnedPrimaryKeyExpression;
            VerifiedAppendSource=verifiedAppendSource;
            Parameters=parameters;
        }
    }

    /// <summary>固定父证明、连接重置及显式用户变量支持缺一时，生成服务保持原读写顺序。</summary>
    public static BatchRecheck? PlanBatch(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,ImportRecheck? recheck)
    {
        if(recheck is null||!HasFirstRead(session,table,parentId,softDelete,condition,recheck,out var transaction)||
            transaction.Connection is not MySqlConnection connection)
            return null;
        // 同批只复用已绑定事务、父键和固定条件的 SQL 计划。每行的父状态仍由当前锁读决定。
        if(recheck.Batch is {} batch)return batch;
        var options=new MySqlConnectionStringBuilder(connection.ConnectionString);
        if(!options.AllowUserVariables||!options.ConnectionReset)return null;
        var clause=condition.RenderConstants(session.Provider,"mt_import_parent_candidate",session.Provider.QuoteIdentifier);
        var from=" FROM "+session.Provider.QuoteIdentifier(session.Options.ExpectedDatabase)+"."+session.Provider.QuoteIdentifier(table)+
            " WHERE `id`=CASE WHEN "+BatchRecheck.DatabaseGate+" THEN @mt_import_parent_id END"+(softDelete?" AND `delete_no` IS NULL":"");
        var parameters=new Dictionary<string,object?>(clause.Parameters,StringComparer.Ordinal)
        {
            ["mt_import_parent_id"]=parentId
        };
        var shared=HoldsSharedImportParent(session,table,parentId,softDelete,recheck);
        return recheck.Batch=new BatchRecheck(transaction,table,parentId,RecheckExpression(from,clause.Sql,shared,BatchRecheck.DatabaseGate),
            PinnedPrimaryKeyRecheckExpression(from,clause.Sql,shared),parameters,shared ? recheck.SharedParent?.Source : null);
    }

    /// <summary>保留旧生成应用入口，只有追加导入的私有调用携带同批状态。</summary>
    public static Task RequireAsync(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,CancellationToken cancellationToken) =>
        RequireAsync(session,table,parentId,softDelete,condition,cancellationToken,null);

    /// <summary>同一语句返回原快照及当前候选结果，并复用调用者已取得的父锁模式。</summary>
    public static async Task RequireAsync(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,CancellationToken cancellationToken,ImportRecheck? recheck)
    {
        if(session.CurrentTransaction is not {Connection:MySqlConnection connection} transaction)
            throw new InvalidOperationException("关联候选验证缺少当前MySQL事务。");
        if(parentId<=0)throw new ArgumentException("关联来源页要求有效 parentId。");
        var from=ParentFrom(session,table,softDelete);
        await using var command=MySqlFloatingCommand.Wrap(connection.CreateCommand());
        command.Transaction=transaction;
        BindKey(command,parentId);
        var candidate=condition.BindConstants(command,session,"parent_candidate");
        // MySQL 可在表达式求值前读取常量主键。生成器仅在该父锁已由原入口取得时使用本语句。
        command.CommandText="SELECT CASE WHEN EXISTS(SELECT 1"+from+") THEN (SELECT 2"+from+
            (candidate.Length==0?"":" AND "+candidate)+LockClause(HoldsSharedImportParent(session,table,parentId,softDelete,recheck))+") ELSE 0 END";
        var result=await command.ExecuteScalarAsync(cancellationToken);
        if(result is null or DBNull)throw new InvalidOperationException("关联记录已离开当前固定候选范围。");
        RequireCandidate(Convert.ToInt32(result,CultureInfo.InvariantCulture));
        if(recheck is not null&&session.HasScalarCacheWriteProof(transaction))
        {
            recheck.FirstRead=(transaction,table,parentId,softDelete,condition);
            recheck.Batch=null;
        }
    }

    /// <summary>在原逐行父校验位置重读当前父及候选。首行或证明变化时继续原查询。</summary>
    public static async Task<int?> TryRecheckAsync(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,ImportRecheck? recheck,CancellationToken cancellationToken)
    {
        if(!HasFirstRead(session,table,parentId,softDelete,condition,recheck,out var transaction)||
            transaction.Connection is not MySqlConnection connection)
            return null;
        var from=ParentFrom(session,table,softDelete);
        await using var command=MySqlFloatingCommand.Wrap(connection.CreateCommand());
        command.Transaction=transaction;
        BindKey(command,parentId);
        var candidate=condition.BindConstants(command,session,"parent_candidate");
        // CASE 中保持标量候选，物理主键漂移时仍拒绝返回多行。
        command.CommandText="SELECT "+RecheckExpression(from,candidate,HoldsSharedImportParent(session,table,parentId,softDelete,recheck));
        var result=Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture);
        if(result==0)throw new InvalidOperationException("父记录不存在或已删除。");
        return result;
    }

    /// <summary>独立读取和合并读取共用 CASE；SELECT INTO 保持普通子查询的原一致性快照。</summary>
    private static string RecheckExpression(string from,string candidate,bool shared,string? databaseGate=null) =>
        "CASE "+(databaseGate is null?"":"WHEN NOT ("+databaseGate+") THEN -1 ")+
        "WHEN NOT EXISTS(SELECT 1"+from+LockClause(shared)+") THEN 0 "+
        "WHEN NOT EXISTS(SELECT 1"+from+") THEN 1 ELSE COALESCE((SELECT 2"+from+
        (candidate.Length==0?"":" AND "+candidate)+LockClause(shared)+"),3) END";

    /// <summary>结构锁已固定单列主键时，一次当前锁读同时判断父行和候选；内层普通读保留原快照和错误优先级。</summary>
    private static string PinnedPrimaryKeyRecheckExpression(string from,string candidate,bool shared) =>
        "CASE WHEN NOT ("+BatchRecheck.DatabaseGate+") THEN -1 ELSE COALESCE((SELECT CASE "+
        "WHEN NOT EXISTS(SELECT 1"+from+") THEN 1 "+
        (candidate.Length==0?"ELSE 2 ":"WHEN "+candidate+" THEN 2 ELSE 3 ")+
        "END"+from+LockClause(shared)+"),0) END";

    private static string LockClause(bool shared) => shared ? " FOR SHARE" : " FOR UPDATE";

    /// <summary>首行证明只在原连接、原事务及原固定候选计划中使用。</summary>
    private static bool HasFirstRead(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,ImportRecheck? recheck,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DbTransaction? transaction)
    {
        transaction=session.CurrentTransaction;
        return recheck?.FirstRead is {} first&&transaction is not null&&ReferenceEquals(first.Transaction,transaction)&&
            first.Table==table&&first.ParentId==parentId&&first.SoftDelete==softDelete&&
            ReferenceEquals(first.Condition,condition)&&CanUse(session);
    }

    /// <summary>快照缺父或候选拒绝仍在原关联校验位置处理。</summary>
    public static void RequireCandidate(int result)
    {
        if(result==3)throw new InvalidOperationException("关联记录已离开当前固定候选范围。");
        if(result!=2)throw new InvalidOperationException("父记录不存在或已删除。");
    }

    /// <summary>仅从已发布物理表生成精确主键与活动谓词。</summary>
    private static string ParentFrom(DatabaseSession session,string table,bool softDelete) =>
        " FROM "+session.Provider.QuoteIdentifier(table)+" WHERE `id`=@parent_id"+(softDelete?" AND `delete_no` IS NULL":"");

    /// <summary>物理父键使用参数绑定，避免从客户端拼接查询。</summary>
    private static void BindKey(DbCommand command,long parentId)
    {
        var key=command.CreateParameter();
        key.ParameterName="parent_id";
        key.Value=parentId;
        command.Parameters.Add(key);
    }
}
