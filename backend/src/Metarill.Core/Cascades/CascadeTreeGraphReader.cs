using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;

namespace Metarill.Core.Cascades;

/// <summary>复用固定最小投影，按请求批量缓存祖先；后代遍历使用父键分批与主键游标，不截断宽树。</summary>
public sealed class CascadeTreeGraphReader(SelfTreeReader pathReader,
    Func<IReadOnlyList<long>,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> fetch,
    Func<IReadOnlyList<long>,long,int,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> childrenPage,
    string joiner)
{
    public const int BatchSize=200;
    public const int PageSize=1000;

    /// <summary>原筛选只解释叶子ID；名称仅作回显，不能传入表名、SQL或匹配模式。</summary>
    public static long? FilterId(JsonElement value,string idKey,string nameKey)
    {
        if(value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(value.GetString()))return null;
        if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("级联筛选必须为原节点对象。");
        var fields=value.EnumerateObject().ToArray();
        if(fields.Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=fields.Length || fields.Any(field=>field.Name!=idKey&&field.Name!=nameKey))throw new ArgumentException("级联筛选含重复或未开放字段。");
        return value.TryGetProperty(idKey,out var id)?CascadeTreeId.Parse(id,true):null;
    }

    /// <summary>每个后代必须在同一固定可读范围，超过路径深度或坏数据整体拒绝，不返回部分ID。</summary>
    public async Task<IReadOnlyList<long>> FilterIdsAsync(long selected,bool exact,CancellationToken cancellationToken=default)
    {
        var path=await pathReader.PathAsync(selected,cancellationToken);
        if(exact)return [selected];
        var visited=new HashSet<long>{selected};var frontier=new List<long>{selected};var depth=path.Path.Count;
        while(frontier.Count>0)
        {
            var next=new List<long>();
            foreach(var batch in frontier.Chunk(BatchSize))
            {
                long cursor=0;
                while(true)
                {
                    cancellationToken.ThrowIfCancellationRequested();var rows=await childrenPage(batch,cursor,PageSize,cancellationToken);
                    if(rows.Count>PageSize)throw new InvalidOperationException("级联后代页超过固定读取大小。");
                    foreach(var row in rows)
                    {
                        var parent=Parent(row.ParentId);
                        if(row.Id<=cursor||parent is null||!batch.Contains(parent.Value)||!visited.Add(row.Id))throw new InvalidOperationException("级联后代存在循环、重复或游标/父范围错误。");
                        cursor=row.Id;next.Add(row.Id);
                    }
                    if(rows.Count<PageSize)break;
                }
            }
            if(next.Count>0&&depth>=SelfTreeReader.MaximumDepth)throw new InvalidOperationException("级联后代超过64层，未返回截断结果。");
            frontier=next;depth++;
        }
        return visited.Order().ToArray();
    }

    /// <summary>批量回读只缓存本次请求；坏关联输出空路径，身份、SQL与取消错误仍向上传递。</summary>
    public async Task<IReadOnlyDictionary<long,string>> DisplayAsync(IReadOnlyList<long> selected,CancellationToken cancellationToken=default)
    {
        var cache=new Dictionary<long,CascadeTreeNode?>();var frontier=selected.Distinct().ToArray();
        for(var depth=0;depth<SelfTreeReader.MaximumDepth&&frontier.Length>0;depth++)
        {
            foreach(var batch in frontier.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();foreach(var id in batch)cache[id]=null;
                var rows=await fetch(batch,cancellationToken);var seen=new HashSet<long>();
                foreach(var row in rows)
                {
                    if(!batch.Contains(row.Id)||!seen.Add(row.Id))throw new InvalidOperationException("级联路径读取超出固定ID范围。");cache[row.Id]=row;
                }
            }
            var next=new HashSet<long>();
            foreach(var id in frontier)
            {
                try{var parent=Parent(cache[id]?.ParentId);if(parent is{} key&&!cache.ContainsKey(key))next.Add(key);}catch(InvalidOperationException){/* 坏父键仅使该关联显示为空，不伪造部分路径。 */}
            }
            frontier=next.ToArray();
        }
        var reader=new SelfTreeReader((id,_)=>Task.FromResult(cache.GetValueOrDefault(id)),(_,_,_)=>throw new InvalidOperationException("路径回读不枚举候选。"),(_,_)=>throw new InvalidOperationException("路径回读不枚举候选。"),joiner);
        var result=new Dictionary<long,string>();
        foreach(var id in selected.Distinct())
        {
            try{result[id]=(await reader.PathAsync(id,cancellationToken)).DisplayText;}catch(InvalidOperationException){result[id]="";}
        }
        return result;
    }
    /// <summary>内部节点只使用正整数父键，根兼容NULL和空文本。</summary>
    private static long? Parent(string? value)
    {if(string.IsNullOrWhiteSpace(value))return null;if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0)throw new InvalidOperationException("级联父键无效。");return id;}
}
