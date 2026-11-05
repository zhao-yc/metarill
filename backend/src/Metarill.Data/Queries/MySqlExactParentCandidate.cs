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

    /// <summary>同一语句返回原快照及当前候选结果；调用者已在原事务取得这个精确主键的排他锁。</summary>
    public static async Task RequireAsync(DatabaseSession session,string table,long parentId,bool softDelete,
        FixedConditionSqlPlan condition,CancellationToken cancellationToken)
    {
        if(session.CurrentTransaction is not {Connection:MySqlConnection connection} transaction)
            throw new InvalidOperationException("关联候选验证缺少当前MySQL事务。");
        if(parentId<=0)throw new ArgumentException("关联来源页要求有效 parentId。");
        var from=" FROM "+session.Provider.QuoteIdentifier(table)+" WHERE `id`=@parent_id"+
            (softDelete?" AND `delete_no` IS NULL":"");
        await using var command=MySqlFloatingCommand.Wrap(connection.CreateCommand());
        command.Transaction=transaction;
        var key=command.CreateParameter();
        key.ParameterName="parent_id";
        key.Value=parentId;
        command.Parameters.Add(key);
        var candidate=condition.BindConstants(command,session,"parent_candidate");
        // MySQL 可在表达式求值前读取常量主键。生成器仅在该父锁已由原入口取得时使用本语句。
        command.CommandText="SELECT CASE WHEN EXISTS(SELECT 1"+from+") THEN (SELECT 2"+from+
            (candidate.Length==0?"":" AND "+candidate)+" FOR UPDATE) ELSE 0 END";
        var result=await command.ExecuteScalarAsync(cancellationToken);
        if(result is null or DBNull)throw new InvalidOperationException("关联记录已离开当前固定候选范围。");
        if(Convert.ToInt32(result,CultureInfo.InvariantCulture)!=2)throw new InvalidOperationException("父记录不存在或已删除。");
    }
}
