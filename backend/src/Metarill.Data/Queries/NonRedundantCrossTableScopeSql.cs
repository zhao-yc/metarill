using System.Globalization;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>原对象查询的固定逻辑父范围：关联同一来源主键，不把稀疏空位误作祖先ID。</summary>
public static class NonRedundantCrossTableScopeSql
{
    /// <summary>只接受已编译跨表ID及同类型正整数；目标整条路径须可见，锁定用途必须加入原事务。宿主仍负责动作授权、来源映射和独立参数前缀。</summary>
    public static ISelect<T> Apply<T>(ISelect<T> query,DatabaseSession session,NonRedundantReadPlan plan,string field,string parameterPrefix,object value,string projectId,bool locking=false)where T:class
    {
        ArgumentNullException.ThrowIfNull(query);ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(value);MetadataValidation.RequireIdentifier(parameterPrefix);
        if(session.Provider.Name!="mysql")throw new NotSupportedException("跨表逻辑父范围尚未完成该数据库验收。");
        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException("跨表逻辑父范围锁定必须加入真实事务。");
        var compiled=plan.MySql;
        var selected=compiled.Crosses.SingleOrDefault(cross=>cross.Selection.Slots.Any(slot=>slot.ColumnName==field));
        if(selected is null||!compiled.Types.TryGetValue(field,out var type))throw new InvalidOperationException("父范围必须是本固定计划的跨表ID表达式。");
        if(!(type=="int64"&&value is long number&&number>0||type=="string"&&value is string text&&Positive(text,out _)))throw new ArgumentException("逻辑父ID必须与固定字段同类型且为规范正整数。",nameof(value));
        if(!Positive(projectId,out var project))throw new ArgumentException("当前项目必须为规范正整数。",nameof(projectId));
        var provider=session.Provider;var replacements=new Dictionary<string,string>(StringComparer.Ordinal){{"project_scope_number",parameterPrefix+"_project_number"},{"project_scope_text",parameterPrefix+"_project_text"}};
        var parameters=new Dictionary<string,object?>(StringComparer.Ordinal){{parameterPrefix+"_project_number",project},{parameterPrefix+"_project_text",projectId},{parameterPrefix+"_value",value}};
        foreach(var pair in compiled.Constants){var name=parameterPrefix+"_constant_"+pair.Key;replacements.Add(pair.Key,name);parameters.Add(name,pair.Value);}
        // 生成Mapper创建时固定为a，不能把已有lambda切换成另一个外层别名。
        var clauses=new[]{compiled.Scope,compiled.Expression("id")+"="+provider.QuoteIdentifier("a")+"."+provider.QuoteIdentifier("id"),compiled.Expression(field)+"="+provider.ParameterPlaceholder(parameterPrefix+"_value")}
            .Concat(compiled.Crosses.Select(cross=>"("+cross.Validity+")")).Append(selected.Reachable);
        var sql="EXISTS (SELECT 1"+compiled.From+" WHERE "+string.Join(" AND ",clauses.Where(part=>part.Length>0))+(locking?" FOR UPDATE":"")+")";
        // 一次替换完整参数名，避免_1误改_10或重复作用于已加前缀的名字；元数据常量始终参数绑定。
        sql=Regex.Replace(sql,"@([A-Za-z_][A-Za-z0-9_]*)",match=>replacements.TryGetValue(match.Groups[1].Value,out var name)?provider.ParameterPlaceholder(name):match.Value,RegexOptions.CultureInvariant);
        // 查询可能在事务开始前创建，锁定时显式接入当前事务，不能只检查AsyncLocal后依赖旧绑定。
        if(locking)query=query.WithTransaction(session.CurrentTransaction);
        return query.As("a").Where(sql,parameters);
    }
    /// <summary>参数不经浮点或宽松数值转换，字符串前导零不伪装为另一个规范父ID。</summary>
    private static bool Positive(string text,out long value)=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value>0&&text==value.ToString(CultureInfo.InvariantCulture);
}
