using System.Linq.Expressions;
using FreeSql;

namespace Metarill.Data.Queries;

/// <summary>已编译标量名称列的字面LIKE；不改变普通文本字段的既有筛选。</summary>
public static class StringNameQuery
{
    /// <summary>固定映射列直接参与LIKE，继承实际列排序规则，关键词与数组名称使用相同参数化转义。</summary>
    public static ISelect<T> ApplyLike<T>(DatabaseSession session, ISelect<T> query, Expression<Func<T, string?>> storageColumn, string keyword) where T : class
        => JsonArrayNameQuery.ApplyNameLike(session, query, storageColumn, keyword, false);
}
