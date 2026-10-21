using FreeSql;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>精确范围集合按固定名称绑定ADO参数，避免ORM的Contains把账号匹配值内联进SQL或日志。</summary>
public static class ExactDataManageSql
{
    /// <summary>派生匹配使用固定相关子查询；内外分别锁读，关联值与主记录共同保持至原写事务结束。</summary>
    public static ISelect<T> ApplyDerived<T>(ISelect<T> query,DatabaseSession session,NonRedundantReadPlan plan,string field,string parameterPrefix,IReadOnlyList<object?> values,string projectId)where T:class
    {
        MetadataValidation.RequireIdentifier(parameterPrefix);
        if(session.Provider.Name!="mysql")throw new NotSupportedException("派生匹配计划与数据库方言不一致。");
        if(session.CurrentTransaction is null)throw new InvalidOperationException("派生匹配锁定必须加入真实写事务。");
        if(!long.TryParse(projectId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var project)||project<=0)throw new InvalidOperationException("派生匹配项目标识无效。");
        var fixedPlan=plan.MySql;var expression=fixedPlan.Expression(field);var provider=session.Provider;
        if(values.Count==0)return query.Where("1=0");
        var parameters=new Dictionary<string,object?>{{parameterPrefix+"_project_number",project},{parameterPrefix+"_project_text",projectId}};var names=new List<string>();
        foreach(var value in values)
        {
            if(value is not(string or long or int or decimal or double or bool or DateTime))throw new ArgumentException("精确管理只接受已转换的非空标量参数。");
            var name=parameterPrefix+"_value_"+names.Count;parameters.Add(name,value);names.Add(provider.ParameterPlaceholder(name));
        }
        // 生成Mapper从创建时固定使用a；保持同名，不能在已有lambda后改名。
        const string outer="a";
        var predicates=new[]{fixedPlan.Scope,fixedPlan.Expression("id")+"="+provider.QuoteIdentifier(outer)+"."+provider.QuoteIdentifier("id"),expression+" IN ("+string.Join(",",names)+")"};
        var sql="EXISTS (SELECT 1"+fixedPlan.From+" WHERE "+string.Join(" AND ",predicates.Where(part=>part.Length>0))+" FOR UPDATE)";
        sql=sql.Replace("@project_scope_number",provider.ParameterPlaceholder(parameterPrefix+"_project_number"),StringComparison.Ordinal).Replace("@project_scope_text",provider.ParameterPlaceholder(parameterPrefix+"_project_text"),StringComparison.Ordinal);
        return query.As(outer).Where(sql,parameters);
    }
    /// <summary>只接收生成期固定物理列/前缀；值永不进入SQL语法，空集合固定不匹配。</summary>
    public static ISelect<T> Apply<T>(ISelect<T> query,DatabaseSession session,string column,string parameterPrefix,IReadOnlyList<object?> values)where T:class
    {
        MetadataValidation.RequireIdentifier(column);MetadataValidation.RequireIdentifier(parameterPrefix);
        if(values.Count==0)return query.Where("1=0");
        var provider=session.Provider;
        var parameters=new Dictionary<string,object?>();var names=new List<string>();
        foreach(var value in values)
        {
            if(value is not(string or long or int or decimal or double or bool or DateTime))throw new ArgumentException("精确管理只接受已转换的非空标量参数。");
            var name=parameterPrefix+"_"+parameters.Count;parameters.Add(name,value);names.Add(provider.ParameterPlaceholder(name));
        }
        // Where的对象重载识别参数字典；DbParameter数组会被当普通对象而丢失绑定。
        return query.Where(provider.QuoteIdentifier(column)+" IN ("+string.Join(",",names)+")",parameters);
    }
}
