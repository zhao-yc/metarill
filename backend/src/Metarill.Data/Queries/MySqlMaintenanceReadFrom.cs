using System.Collections.Frozen;
namespace Metarill.Data.Queries;

/// <summary>固定 LEFT JOIN 片段及字段依赖，供维护读取和普通非冗余聚合使用；编译时冻结。</summary>
internal sealed class MySqlMaintenanceReadFrom
{
    private readonly string root;
    private readonly (string Sql,FrozenSet<string> Fields)[] joins;
    /// <summary>每个片段只接受编译器产生的SQL及已追踪字段，独立复制可变依赖集合。</summary>
    internal MySqlMaintenanceReadFrom(string root,IEnumerable<(string Sql,IEnumerable<string> Fields)> joins)
    {
        this.root=root;this.joins=joins.Select(join=>(join.Sql,join.Fields.ToFrozenSet(StringComparer.OrdinalIgnoreCase))).ToArray();
    }
    /// <summary>只组合原有片段；共享前缀及祖先已由逐字段解析同时登记，顺序保持编译顺序。</summary>
    internal string Render(IReadOnlySet<string> fields)=>root+string.Concat(joins.Where(join=>join.Fields.Overlaps(fields)).Select(join=>join.Sql));
    /// <summary>使用编译器固定的 FROM 和 LEFT JOIN 前缀限定所有表，库名已由方言引用。</summary>
    internal string RenderQualified(string quotedDatabase)
    {
        var prefix=quotedDatabase+".";
        return root.Insert(" FROM ".Length,prefix)+
            string.Concat(joins.Select(join=>join.Sql.Insert(" LEFT JOIN ".Length,prefix)));
    }
}
