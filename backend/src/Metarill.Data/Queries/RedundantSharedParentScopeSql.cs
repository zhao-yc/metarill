using System.Globalization;
using System.Text;
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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(actor);
        MetadataValidation.RequireIdentifier(prefix);
        var provider=session.Provider;
        if(provider.Name!="mysql")throw new NotSupportedException("冗余跨表账号父范围尚未完成该数据库适配。");
        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException("冗余跨表父范围锁定必须加入业务事务。");
        if(!long.TryParse(actor.ProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project)||
            project<=0||actor.ProjectId!=project.ToString(CultureInfo.InvariantCulture))
            throw new UnauthorizedAccessException("当前项目身份无效。");

        var predicates=new List<string>();
        var parameters=new Dictionary<string,object?>(StringComparer.Ordinal);
        for(var pathIndex=0;pathIndex<paths.Count;pathIndex++)
        {
            var path=paths[pathIndex];
            if(path.Levels.Count==0||path.LogicalType is not("string" or "int64")||
                !path.Levels.Any(level=>level.Condition?.Variables.Count>0))throw new InvalidOperationException("冗余父范围缺少固定账号路径。");
            string Alias(int index)=>"rp"+index.ToString(CultureInfo.InvariantCulture);
            string Field(int index,string column)=>Alias(index)+"."+provider.QuoteIdentifier(column);
            var last=path.Levels.Count-1;
            var from=new StringBuilder("SELECT 1 FROM ").Append(provider.QuoteIdentifier(path.Levels[last].TableName)).Append(' ').Append(Alias(last));
            // R1 比较的是来源真实存储父键；祖先从目标当前外键读取，不能依赖来源名称缓存。
            for(var index=last;index>0;index--)
            {
                var parent=path.Levels[index].Parent??throw new InvalidOperationException("冗余父路径缺少物理祖先列。");
                from.Append(" INNER JOIN ").Append(provider.QuoteIdentifier(path.Levels[index-1].TableName)).Append(' ').Append(Alias(index-1))
                    .Append(" ON BINARY CAST(").Append(Field(index,parent)).Append(" AS CHAR)=BINARY CAST(").Append(Field(index-1,"id")).Append(" AS CHAR)");
            }
            var correlated=new List<string>
            {
                "BINARY CAST("+Field(last,"id")+" AS CHAR)=BINARY CAST("+provider.QuoteIdentifier("a")+"."+provider.QuoteIdentifier(path.ColumnName)+" AS CHAR)"
            };
            for(var index=0;index<path.Levels.Count;index++)
            {
                var level=path.Levels[index];
                var key=prefix+"_"+pathIndex.ToString(CultureInfo.InvariantCulture)+"_"+index.ToString(CultureInfo.InvariantCulture);
                correlated.Add(Field(index,"id")+">0");
                if(level.ProjectType is not null)
                {
                    parameters.Add(key+"_project",level.ProjectType=="string"?actor.ProjectId:(object)project);
                    correlated.Add(Field(index,"yz_project_id")+"="+provider.ParameterPlaceholder(key+"_project"));
                }
                if(level.SoftDelete)correlated.Add(Field(index,"delete_no")+" IS NULL");
                if(level.Condition is not { } condition)continue;
                var clause=condition.BindAccount(actor).RenderConstants(provider,key+"_scope",column=>Field(index,column));
                correlated.Add(clause.Sql);
                foreach(var parameter in clause.Parameters)parameters.Add(parameter.Key,parameter.Value);
            }
            var sql=from.Append(" WHERE ").AppendJoin(" AND ",correlated).ToString();
            predicates.Add("EXISTS ("+(locking?provider.BuildSelectForUpdate(sql):sql)+")");
        }
        if(predicates.Count==0)return query;
        if(locking)query=query.WithTransaction(session.CurrentTransaction);
        return query.As("a").Where(string.Join(" AND ",predicates),parameters);
    }
}