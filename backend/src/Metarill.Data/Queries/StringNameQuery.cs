using System.Linq.Expressions;
using FreeSql;

namespace Metarill.Data.Queries;

/// <summary>固定标量字符串LIKE；原文本模式和已确认的名称字面搜索分别调用。</summary>
public static class StringNameQuery
{
    /// <summary>普通文本沿用原Java StringUtils.isBlank的UTF-16空白集合；不把NBSP／NEL等当作可省略输入。</summary>
    public static bool IsPatternBlank(string? value)
    {
        if(value is null)return true;
        foreach(var character in value)
            if(character is not(>='\u0009' and <='\u000d' or >='\u001c' and <='\u0020' or '\u1680' or >='\u2000' and <='\u2006' or >='\u2008' and <='\u200a' or '\u2028' or '\u2029' or '\u205f' or '\u3000'))return false;
        return true;
    }

    /// <summary>原普通文本筛选保留LIKE通配、反斜杠及列排序规则；关键词只绑定参数，不作为SQL。</summary>
    public static ISelect<T> ApplyPattern<T>(DatabaseSession session,ISelect<T> query,Expression<Func<T,string?>> storageColumn,string keyword) where T:class
        => JsonArrayNameQuery.ApplyNameLike(session,query,storageColumn,keyword,false,true);

    /// <summary>固定映射列直接参与LIKE，继承实际列排序规则，关键词与数组名称使用相同参数化转义。</summary>
    public static ISelect<T> ApplyLike<T>(DatabaseSession session, ISelect<T> query, Expression<Func<T, string?>> storageColumn, string keyword) where T : class
        => JsonArrayNameQuery.ApplyNameLike(session, query, storageColumn, keyword, false);
}
