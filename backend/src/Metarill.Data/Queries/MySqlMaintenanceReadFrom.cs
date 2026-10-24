using System.Collections.Frozen;
namespace Metarill.Data.Queries;

/// <summary>维护读取的固定LEFT JOIN片段及输出依赖；编译时冻结，不从SQL文本猜别名。</summary>
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
}
