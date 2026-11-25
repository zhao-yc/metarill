using FreeSql;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>文本业务键按字节匹配，避免不区分大小写或忽略尾空格的列排序规则合并节点标识。</summary>
public static class TextCascadeTreeSql
{
    /// <summary>列名和参数前缀来自固定生成期计划，所有节点值通过 ADO 参数绑定。</summary>
    public static ISelect<T> Match<T>(ISelect<T> query,DatabaseSession session,string column,string prefix,IReadOnlyList<string> keys) where T:class
    {
        MetadataValidation.RequireIdentifier(prefix);
        var field=Column(session,column);
        if(keys.Count==0)return query.Where("1=0");
        var values=new Dictionary<string,object?>();
        var placeholders=new List<string>();
        foreach(var key in keys)
        {
            var name=prefix+"_"+values.Count;
            values.Add(name,key);
            placeholders.Add("BINARY "+session.Provider.ParameterPlaceholder(name));
        }
        return query.Where("BINARY "+field+" IN ("+string.Join(",",placeholders)+")",values);
    }

    /// <summary>原根模式支持 NULL、空文本与纯空白串，不能把非空节点键修剪后匹配。</summary>
    public static ISelect<T> Root<T>(ISelect<T> query,DatabaseSession session,string column) where T:class
    {
        var field=Column(session,column);
        return query.Where("("+field+" IS NULL OR "+field+" REGEXP '^[[:space:]]*$')");
    }

    private static string Column(DatabaseSession session,string column)
    {
        MetadataValidation.RequireIdentifier(column);
        if(session.Provider.Name!="mysql")throw new NotSupportedException("文本自树查询仅适用于 MySQL。");
        return session.Provider.QuoteIdentifier(column);
    }
}
