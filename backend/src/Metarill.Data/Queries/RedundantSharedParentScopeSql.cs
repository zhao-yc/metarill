using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

/// <summary>将固定 R1 目标路径约束到来源真实物理父键，读取与写入使用同一谓词。</summary>
public static class RedundantSharedParentScopeSql
{
    /// <summary>只从已发布的层级、物理范围和当前账号产生 SQL。写入时目标与来源在同一事务取当前锁。</summary>
    public static ISelect<T> Apply<T>(ISelect<T> query,DatabaseSession session,IReadOnlyList<RedundantSharedParentPlan> paths,
        RuntimeActor actor,string prefix,bool locking=false) where T:class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(session);
        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException("冗余跨表父范围锁定必须加入业务事务。");
        var clause=Render(session.Provider,paths,actor,prefix,
            column=>session.Provider.QuoteIdentifier("a")+"."+session.Provider.QuoteIdentifier(column),locking);
        if(clause.Sql.Length==0)return query;
        if(locking)query=query.WithTransaction(session.CurrentTransaction);
        return query.As("a").Where(clause.Sql,clause.Parameters);
    }

    /// <summary>父参数真实可见时才进入候选读取；写入时全部祖先由当前业务事务锁定。</summary>
    public static async Task RequireParentAsync(DatabaseSession session,IReadOnlyList<RedundantSharedParentPlan> paths,
        long parentId,RuntimeActor actor,bool locking,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        var transaction=session.CurrentTransaction;
        if(locking&&transaction is null)throw new InvalidOperationException("冗余跨表父范围锁定必须加入业务事务。");
        if(parentId<=0||paths.Count==0)throw new ArgumentException("当前关联要求已发布的正整数父标识。");
        var placeholder=session.Provider.ParameterPlaceholder("r1_candidate_parent");
        var clause=Render(session.Provider,paths,actor,"r1_ancestor",_=>placeholder,locking);

        async Task CheckAsync(System.Data.Common.DbConnection connection)
        {
            await using var command=MySqlFloatingCommand.Wrap(connection.CreateCommand());
            command.Transaction=transaction;
            command.CommandText="SELECT 1 WHERE "+clause.Sql;
            Add("r1_candidate_parent",parentId);
            foreach(var pair in clause.Parameters)Add(pair.Key,pair.Value);
            if(await command.ExecuteScalarAsync(token) is null or DBNull)
                throw new InvalidOperationException("关联父记录不存在、已删除或不在当前可选范围。");

            void Add(string name,object? value)
            {
                var parameter=command.CreateParameter();
                parameter.ParameterName=name;
                parameter.Value=value??DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        if(transaction is not null)
        {
            await CheckAsync(transaction.Connection??throw new InvalidOperationException("事务连接已关闭。"));
            return;
        }
        await using var connection=session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync(token);
        await CheckAsync(connection);
    }

    /// <summary>普通 ORM、固定整数候选和父参数核验共用相关谓词；来源表达式由适配器提供。</summary>
    internal static FixedConditionSqlPlan.Clause Render(IDatabaseProvider provider,IReadOnlyList<RedundantSharedParentPlan> paths,
        RuntimeActor actor,string prefix,Func<string,string> sourceColumn,bool locking)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(actor);
        MetadataValidation.RequireIdentifier(prefix);
        if(provider.Name!="mysql")throw new NotSupportedException("冗余跨表账号父范围尚未完成该数据库适配。");
        if(!long.TryParse(actor.ProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project)||
            project<=0||actor.ProjectId!=project.ToString(CultureInfo.InvariantCulture))
            throw new UnauthorizedAccessException("当前项目身份无效。");

        var predicates=new List<string>();
        var parameters=new Dictionary<string,object?>(StringComparer.Ordinal);
        for(var pathIndex=0;pathIndex<paths.Count;pathIndex++)
        {
            var path=paths[pathIndex];
            if(path.Levels.Count==0||path.LogicalType is not("string" or "int64")||
                !path.Levels.Any(level=>level.RequiresCurrentScope))throw new InvalidOperationException("冗余父范围缺少已发布的当前读取条件。");
            string Alias(int index)=>"rp"+index.ToString(CultureInfo.InvariantCulture);
            string Field(int index,string column)=>Alias(index)+"."+provider.QuoteIdentifier(column);
            var last=path.Levels.Count-1;
            var from=new StringBuilder("SELECT 1 FROM ").Append(provider.QuoteIdentifier(path.Levels[last].TableName)).Append(' ').Append(Alias(last));
            // R1 比较的是来源真实存储父键；祖先从目标当前外键读取，不能依赖来源名称缓存。
            for(var index=last;index>0;index--)
            {
                var parent=path.Levels[index].Parent??throw new InvalidOperationException("冗余父路径缺少物理祖先列。");
                from.Append(" INNER JOIN ").Append(provider.QuoteIdentifier(path.Levels[index-1].TableName)).Append(' ').Append(Alias(index-1))
                    .Append(" ON BINARY CAST(").Append(Field(index,parent)).Append(" AS CHAR)=BINARY CAST(").Append(Field(index-1,path.Levels[index-1].KeyColumn)).Append(" AS CHAR)");
            }
            var correlated=new List<string>
            {
                "BINARY CAST("+Field(last,path.Levels[last].KeyColumn)+" AS CHAR)=BINARY CAST("+sourceColumn(path.ColumnName)+" AS CHAR)"
            };
            for(var index=0;index<path.Levels.Count;index++)
            {
                var level=path.Levels[index];
                var key=prefix+"_"+pathIndex.ToString(CultureInfo.InvariantCulture)+"_"+index.ToString(CultureInfo.InvariantCulture);
                correlated.Add(Field(index,level.KeyColumn)+">0");
                if(level.ProjectType is not null)
                {
                    parameters.Add(key+"_project",level.ProjectType=="string"?actor.ProjectId:(object)project);
                    correlated.Add(Field(index,"yz_project_id")+"="+provider.ParameterPlaceholder(key+"_project"));
                }
                if(level.SoftDelete)correlated.Add(Field(index,"delete_no")+" IS NULL");
                if(level.RecursiveCandidate is { } recursive)
                {
                    correlated.Add(RecursiveScope(provider,recursive,Field(index,"id"),actor,project,key,locking,parameters));
                    continue;
                }
                if(level.Condition is not { } condition)continue;
                var clause=condition.BindAccount(actor).RenderConstants(provider,key+"_scope",column=>Field(index,column));
                correlated.Add(clause.Sql);
                foreach(var parameter in clause.Parameters)parameters.Add(parameter.Key,parameter.Value);
            }
            var sql=from.Append(" WHERE ").AppendJoin(" AND ",correlated).ToString();
            predicates.Add("EXISTS ("+(locking?provider.BuildSelectForUpdate(sql):sql)+")");
        }
        return new(string.Join(" AND ",predicates),parameters);
    }

    /// <summary>在实际父节点上应用现有递归候选计划。写入同时锁定物理父和内层级联行。</summary>
    private static string RecursiveScope(IDatabaseProvider provider,NonRedundantReadPlan recursive,string parentKey,
        RuntimeActor actor,long project,string key,bool locking,Dictionary<string,object?> parameters)
    {
        var plan=recursive.BindAccount(actor).MySql;
        if(plan.RequiresPublishedProjectIdentity&&plan.ProjectId!=actor.ProjectId)
            throw new UnauthorizedAccessException("当前身份不属于已发布的全局候选项目。");
        var constants=new Dictionary<string,object?>(plan.Constants,StringComparer.Ordinal)
        {
            ["project_scope_number"]=project,
            ["project_scope_text"]=actor.ProjectId
        };
        var scope=plan.Scope.Length==0?"":" AND "+plan.Scope;
        // 外层已经以排他锁保护物理父，递归子查询以共享锁读取其余依赖的当前值。
        var sql="SELECT 1"+plan.From+" WHERE "+plan.Expression("id")+"="+parentKey+scope+
            (locking?" FOR SHARE":"");
        return "EXISTS ("+Regex.Replace(sql,@"@([A-Za-z_][A-Za-z0-9_]*)",match=>
        {
            var name=match.Groups[1].Value;
            if(!constants.TryGetValue(name,out var value))
                throw new InvalidOperationException("递归父范围中存在未绑定的固定参数。");
            var parameter=key+"_recursive_"+name;
            parameters.TryAdd(parameter,value);
            return provider.ParameterPlaceholder(parameter);
        },RegexOptions.CultureInvariant)+")";
    }
}