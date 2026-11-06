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

    /// <summary>只记录本批首个成功的原查询，后续各行仍从数据库读取当前父和候选。</summary>
    public sealed class ImportRecheck
    {
        internal (DbTransaction Transaction, string Table, long ParentId, bool SoftDelete, FixedConditionSqlPlan Condition)? FirstRead { get; set; }
    }

    /// <summary>保留旧生成应用入口，只有追加导入的私有调用携带同批状态。</summary>
    public static Task RequireAsync(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,CancellationToken cancellationToken) =>
        RequireAsync(session,table,parentId,softDelete,condition,cancellationToken,null);

    /// <summary>同一语句返回原快照及当前候选结果；调用者已在原事务取得这个精确主键的排他锁。</summary>
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
            (candidate.Length==0?"":" AND "+candidate)+" FOR UPDATE) ELSE 0 END";
        var result=await command.ExecuteScalarAsync(cancellationToken);
        if(result is null or DBNull)throw new InvalidOperationException("关联记录已离开当前固定候选范围。");
        RequireCandidate(Convert.ToInt32(result,CultureInfo.InvariantCulture));
        if(recheck is not null&&session.HasScalarCacheWriteProof(transaction))
            recheck.FirstRead=(transaction,table,parentId,softDelete,condition);
    }

    /// <summary>在原逐行父校验位置重读当前父及候选。首行或证明变化时继续原查询。</summary>
    public static async Task<int?> TryRecheckAsync(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,ImportRecheck? recheck,CancellationToken cancellationToken)
    {
        if(recheck?.FirstRead is not {} first||session.CurrentTransaction is not {Connection:MySqlConnection connection} transaction||
            !ReferenceEquals(first.Transaction,transaction)||first.Table!=table||first.ParentId!=parentId||first.SoftDelete!=softDelete||
            !ReferenceEquals(first.Condition,condition)||!CanUse(session))
            return null;
        var from=ParentFrom(session,table,softDelete);
        await using var command=MySqlFloatingCommand.Wrap(connection.CreateCommand());
        command.Transaction=transaction;
        BindKey(command,parentId);
        var candidate=condition.BindConstants(command,session,"parent_candidate");
        // CASE 中保持标量候选，物理主键漂移时仍拒绝返回多行。
        command.CommandText="SELECT CASE WHEN NOT EXISTS(SELECT 1"+from+" FOR UPDATE) THEN 0 "+
            "WHEN NOT EXISTS(SELECT 1"+from+") THEN 1 ELSE COALESCE((SELECT 2"+from+
            (candidate.Length==0?"":" AND "+candidate)+" FOR UPDATE),3) END";
        var result=Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture);
        if(result==0)throw new InvalidOperationException("父记录不存在或已删除。");
        return result;
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
