using System.Text.Json;
using Metarill.Core.Contracts;

namespace Metarill.Core.Cascades;

/// <summary>节点键是原业务文本，Cursor 是独立的真实主键，用于稳定分页。</summary>
public sealed record TextCascadeTreeNode(string Key,long Cursor,string? Name,string? ParentKey);

/// <summary>读取固定项目范围内的文本键自树，路径和后代按原值识别。</summary>
public sealed class TextSelfTreeReader(
    Func<string?,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> children,
    Func<IReadOnlyList<string>,CancellationToken,Task<IReadOnlySet<string>>> parentsWithChildren,
    Func<IReadOnlyList<string>,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> fetch,
    Func<IReadOnlyList<string>,long,int,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> childrenPage,
    string joiner)
{
    /// <summary>筛选与整数键沿用同一个原节点对象约束。</summary>
    public static string? FilterId(JsonElement value,string idKey,string nameKey)=>
        CascadeTreeId.ParseText(CascadeTreeGraphReader.FilterValue(value,idKey,nameKey),true);

    /// <summary>名称与排序分开读取时，用同一批真实主键对齐两次查询。</summary>
    public static IReadOnlyList<TextCascadeTreeNode> OrderCandidates(IReadOnlyList<TextCascadeTreeNode> rows,IReadOnlyList<long> orderedIds)=>
        CascadeTreeNode.OrderCandidates(rows,orderedIds,row=>row.Cursor);

    /// <summary>当前层保留原排序，分批查询下级标记并校验父键。</summary>
    public async Task<IReadOnlyList<CascadeTreeOption>> OptionsAsync(string? parent,CancellationToken cancellationToken=default)
    {
        if(parent is not null && (await PathAsync(parent,cancellationToken)).Path.Count>=SelfTreeReader.MaximumDepth)
            throw new InvalidOperationException("级联候选不能超过64层。");
        cancellationToken.ThrowIfCancellationRequested();
        var rows=await children(parent,cancellationToken);
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            if(row.Cursor<=0||Blank(row.Key) is null||Blank(row.ParentKey)!=parent||!keys.Add(row.Key))
                throw new InvalidOperationException("级联候选不符合固定父范围。");
        }
        var parents=new HashSet<string>(StringComparer.Ordinal);
        foreach(var batch in keys.Chunk(CascadeTreeGraphReader.BatchSize))
        {
            var matches=await parentsWithChildren(batch,cancellationToken);
            if(matches.Any(key=>!batch.Contains(key,StringComparer.Ordinal)))
                throw new InvalidOperationException("级联子节点标记超出候选范围。");
            parents.UnionWith(matches);
        }
        return rows.Select(row=>new CascadeTreeOption(row.Key,row.Name??"",row.Key,row.Name??"",parents.Contains(row.Key))).ToArray();
    }

    /// <summary>单路径拒绝断链和循环，写入调用使用当前读委托锁定每一个实际节点。</summary>
    public async Task<CascadeTreePath> PathAsync(string? leaf,CancellationToken cancellationToken=default)
    {
        var paths=await ReadPathsAsync(leaf is null?[]:[leaf],cancellationToken);
        if(leaf is null)return new([],"");
        return paths[leaf]??throw new InvalidOperationException("级联路径有循环、超过64层，或节点不在当前可读范围。");
    }

    /// <summary>沿固定父键按真实主键向后分页，始终验证整棵子树。</summary>
    public async Task<IReadOnlyList<string>> FilterIdsAsync(string selected,bool exact,CancellationToken cancellationToken=default)
    {
        if(Blank(selected) is null)throw new InvalidOperationException("级联节点键不能为空白。");
        var depth=(await PathAsync(selected,cancellationToken)).Path.Count;
        if(exact)return [selected];
        var visited=new HashSet<string>(StringComparer.Ordinal){selected};
        IReadOnlyList<string> frontier=[selected];
        while(frontier.Count>0)
        {
            var next=new List<string>();
            foreach(var batch in frontier.Chunk(CascadeTreeGraphReader.BatchSize))
            {
                long cursor=0;
                while(true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rows=await childrenPage(batch,cursor,CascadeTreeGraphReader.PageSize,cancellationToken);
                    if(rows.Count>CascadeTreeGraphReader.PageSize)
                        throw new InvalidOperationException("级联后代页超过固定读取大小。");
                    foreach(var row in rows)
                    {
                        if(depth>=SelfTreeReader.MaximumDepth||row.Cursor<=cursor||Blank(row.Key) is null||
                            row.ParentKey is null||!batch.Contains(row.ParentKey,StringComparer.Ordinal)||!visited.Add(row.Key))
                            throw new InvalidOperationException("级联后代存在循环、超过64层，或游标/父范围错误。");
                        cursor=row.Cursor;
                        next.Add(row.Key);
                    }
                    if(rows.Count<CascadeTreeGraphReader.PageSize)break;
                }
            }
            frontier=next;
            depth++;
        }
        return visited.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>单次请求批量回读祖先，损坏路径保持为空，查询错误继续向上传递。</summary>
    public Task<IReadOnlyDictionary<string,CascadeTreePath?>> PathsAsync(IReadOnlyList<string> selected,CancellationToken cancellationToken=default)=>
        ReadPathsAsync(selected,cancellationToken);

    /// <summary>列表沿用原展示文案及无路径时的空文本。</summary>
    public async Task<IReadOnlyDictionary<string,string>> DisplayAsync(IReadOnlyList<string> selected,CancellationToken cancellationToken=default)=>
        (await ReadPathsAsync(selected,cancellationToken)).ToDictionary(pair=>pair.Key,pair=>pair.Value?.DisplayText??"",StringComparer.Ordinal);

    private async Task<IReadOnlyDictionary<string,CascadeTreePath?>> ReadPathsAsync(IReadOnlyList<string> selected,CancellationToken cancellationToken)
    {
        var cache=new Dictionary<string,TextCascadeTreeNode?>(StringComparer.Ordinal);
        var frontier=selected.Where(key=>Blank(key) is not null).Distinct(StringComparer.Ordinal).ToArray();
        for(var depth=0;depth<SelfTreeReader.MaximumDepth&&frontier.Length>0;depth++)
        {
            var next=new HashSet<string>(StringComparer.Ordinal);
            foreach(var batch in frontier.Chunk(CascadeTreeGraphReader.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach(var key in batch)cache.Add(key,null);
                var rows=await fetch(batch,cancellationToken);
                foreach(var row in rows)
                {
                    if(row.Cursor<=0||!batch.Contains(row.Key,StringComparer.Ordinal)||cache[row.Key] is not null)
                        throw new InvalidOperationException("级联路径读取超出固定ID范围。");
                    cache[row.Key]=row;
                    if(Blank(row.ParentKey) is{} parent)next.Add(parent);
                }
            }
            frontier=next.Where(key=>!cache.ContainsKey(key)).ToArray();
        }
        return selected.Distinct(StringComparer.Ordinal).ToDictionary(key=>key,key=>ReadCachedPath(key,cache),StringComparer.Ordinal);
    }

    private CascadeTreePath? ReadCachedPath(string leaf,IReadOnlyDictionary<string,TextCascadeTreeNode?> cache)
    {
        var path=new List<CascadeTreePathNode>();
        var visited=new HashSet<string>(StringComparer.Ordinal);
        for(string? key=leaf; key is not null;)
        {
            if(path.Count>=SelfTreeReader.MaximumDepth||!visited.Add(key)||cache.GetValueOrDefault(key) is not{} row)return null;
            path.Add(new(key,row.Name));
            key=Blank(row.ParentKey);
        }
        path.Reverse();
        return new(path,string.Join(joiner,path.Where(node=>!string.IsNullOrWhiteSpace(node.Name)).Select(node=>node.Name)));
    }

    private static string? Blank(string? key)=>string.IsNullOrWhiteSpace(key)?null:key;
}
