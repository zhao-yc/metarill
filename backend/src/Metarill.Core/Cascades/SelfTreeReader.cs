using System.Globalization;

namespace Metarill.Core.Cascades;

/// <summary>固定SQL的最小内部投影；父键只在路径算法中使用。</summary>
public sealed record CascadeTreeNode(long Id,string? Name,string? ParentId);
/// <summary>保留原组件五字段响应与字符串ID。</summary>
public sealed record CascadeTreeOption(string Id,string? Name,string Value,string? Text,bool HasChildren);
/// <summary>原路径每层只返回ID和名称。</summary>
public sealed record CascadeTreePathNode(string Id,string? Name);
/// <summary>原路径数组与完整展示文本。</summary>
public sealed record CascadeTreePath(IReadOnlyList<CascadeTreePathNode> Path,string DisplayText);

/// <summary>仅沿固定查询委托读取自关联树，不加载整表或解释元数据；写入及后代筛选另行接线。</summary>
public sealed class SelfTreeReader(
    Func<long,CancellationToken,Task<CascadeTreeNode?>> find,
    Func<long?,int,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> children,
    Func<IReadOnlyList<long>,CancellationToken,Task<IReadOnlySet<long>>> parentsWithChildren,
    string displayJoiner)
{
    public const int MaximumDepth=64;
    public const int MaximumSiblings=500;

    /// <summary>非根请求先验证完整祖先链，不能由有效子节点穿透越权或已删父节点。</summary>
    public async Task<IReadOnlyList<CascadeTreeOption>> OptionsAsync(long? parentId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(parentId is not null && (await PathAsync(parentId,cancellationToken)).Path.Count>=MaximumDepth)throw new InvalidOperationException("级联候选不能超过64层。");
        var rows=await children(parentId,MaximumSiblings+1,cancellationToken);
        if(rows.Count>MaximumSiblings) throw new InvalidOperationException("级联同层候选超过500项，请调整数据分组；不会返回截断的候选。");
        if(rows.Select(row=>row.Id).Distinct().Count()!=rows.Count || rows.Any(row=>row.Id<=0 || Parent(row.ParentId)!=parentId)) throw new InvalidOperationException("级联候选不符合固定父范围。");
        if(rows.Count==0)return [];
        var ids=rows.Select(row=>row.Id).ToArray();var hasChildren=await parentsWithChildren(ids,cancellationToken);
        if(hasChildren.Any(id=>!ids.Contains(id))) throw new InvalidOperationException("级联子节点标记超出候选范围。");
        return rows.Select(row=>new CascadeTreeOption(Text(row.Id),row.Name,Text(row.Id),row.Name,hasChildren.Contains(row.Id))).ToArray();
    }

    /// <summary>循环、断链和超过64层均整体拒绝，路径不得静默截断。</summary>
    public async Task<CascadeTreePath> PathAsync(long? leafId,CancellationToken cancellationToken=default)
    {
        if(leafId is null)return new([],"");
        var path=new List<CascadeTreePathNode>();var visited=new HashSet<long>();var current=leafId;
        while(current is {} id)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(id<=0 || !visited.Add(id) || path.Count>=MaximumDepth) throw new InvalidOperationException("级联路径存在循环或超过64层。");
            var row=await find(id,cancellationToken);
            if(row is null || row.Id!=id)throw new InvalidOperationException("级联记录不存在、已删除或不在当前可读范围。");
            path.Add(new(Text(id),row.Name));current=Parent(row.ParentId);
        }
        path.Reverse();return new(path,string.Join(displayJoiner,path.Select(row=>row.Name??"")));
    }

    /// <summary>根节点兼容原空文本与NULL，非空父键必须是有效正整数。</summary>
    private static long? Parent(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0)throw new InvalidOperationException("级联父ID无效。");
        return id;
    }
    /// <summary>ID文本转换始终使用固定文化，避免本地格式干扰协议。</summary>
    private static string Text(long id)=>id.ToString(CultureInfo.InvariantCulture);
}
