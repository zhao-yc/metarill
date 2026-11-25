using System.Globalization;
namespace Metarill.Core.Cascades;

/// <summary>批量路径只在一次调用内去重；相同表和主键仍按固定层角色隔离。</summary>
public sealed partial class CrossTableReader
{
    /// <summary>按层逆向分批读取全部选择及祖先；不返回断链、越界或部分成功的结果。</summary>
    public async Task<IReadOnlyDictionary<(int Depth,long Id),CrossTablePath>> PathsAsync(
        IReadOnlyList<(int Depth,long Id)> requests,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(requests);
        var paths=await PathsByKeyAsync(requests.Select(item=>(item.Depth,item.Id.ToString(CultureInfo.InvariantCulture))).ToArray(),cancellationToken);
        return paths.ToDictionary(item=>(item.Key.Depth,Long(item.Key.Id)),item=>item.Value);
    }

    /// <summary>按层批读混合键，每层使用独立的字节精确字典。</summary>
    public async Task<IReadOnlyDictionary<(int Depth,string Id),CrossTablePath>> PathsByKeyAsync(
        IReadOnlyList<(int Depth,string Id)> requests,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(requests);
        var selected=requests.Distinct().ToArray();
        if(selected.Any(item=>item.Depth<0||item.Depth>=levels.Length))throw new ArgumentException("跨表级联层号越界。");
        // 先检查本次所需全部角色授权，拒权时不读取任何层；空集合也不能绕过授权。
        var deepest=selected.Length==0?levels.Length-1:selected.Max(item=>item.Depth);
        for(var index=0;index<=deepest;index++)authorize(index);
        if(selected.Any(item=>!ValidKey(item.Id,levels[item.Depth].TextKey)))throw new InvalidOperationException("跨表路径主键无效。");
        var result=new Dictionary<(int Depth,string Id),CrossTablePath>();
        if(findMany is null)
        {
            foreach(var item in selected)result.Add(item,await PathByKeyAsync(item.Depth,item.Id,cancellationToken));
            return result;
        }
        var pending=Enumerable.Range(0,deepest+1).Select(_=>new HashSet<string>(StringComparer.Ordinal)).ToArray();
        var rows=Enumerable.Range(0,deepest+1).Select(_=>new Dictionary<string,TextCascadeTreeNode>(StringComparer.Ordinal)).ToArray();
        foreach(var item in selected)pending[item.Depth].Add(item.Id);
        for(var index=deepest;index>=0;index--)
        {
            // 每批最多200键；导出总量由原导出入口控制，不误套选择控件5000项上限。
            foreach(var batch in pending[index].Chunk(200))
            {
                cancellationToken.ThrowIfCancellationRequested();var wanted=batch.ToHashSet();
                var found=await findMany(index,Array.AsReadOnly(batch),cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                foreach(var row in found)
                {
                    if(!wanted.Contains(row.Key)||!rows[index].TryAdd(row.Key,row))throw new InvalidOperationException("跨表批量路径返回未请求或重复主键。");
                    if(index>0)pending[index-1].Add(Parent(index-1,row.ParentKey)??throw new InvalidOperationException("跨表级联父链缺失。"));
                }
                if(found.Count!=wanted.Count)throw new InvalidOperationException("跨表级联节点不存在、已删除或不在当前可读范围。");
            }
        }
        foreach(var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();var path=new CrossTablePathNode[item.Depth+1];var current=item.Id;
            for(var index=item.Depth;index>=0;index--)
            {
                var row=rows[index][current];path[index]=new(row.Key,row.Name??"",levels[index].Key);
                if(index>0)current=Parent(index-1,row.ParentKey)!;
            }
            result.Add(item,new(Array.AsReadOnly(path),string.Join(joiner,path.Select(node=>node.Name).Where(name=>!string.IsNullOrWhiteSpace(name)))));
        }
        return result;
    }
}
