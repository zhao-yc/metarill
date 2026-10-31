using System.Text.Json;
using Metarill.Core.Cascades;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>树图算法和原筛选合同离线验证，实库与原页面另行验收。</summary>
public sealed class CascadeTreeGraphTests
{
    /// <summary>原对象名称不参与查询，未知字段/重复ID和SQL字符串不能被执行。</summary>
    [Fact]
    public void Original_filter_object_is_exact_and_keeps_high_integer_ids()
    {
        JsonElement Read(string text)=>JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal(9007199254740993L,CascadeTreeGraphReader.FilterId(Read("{\"treeId\":\"9007199254740993\",\"treeName\":\"任意回显\"}"),"treeId","treeName"));Assert.Null(CascadeTreeGraphReader.FilterId(Read("{}"),"treeId","treeName"));
        foreach(var text in new[]{"{\"treeId\":\"1\",\"treeId\":\"2\"}","{\"treeId\":\"1\",\"projectId\":\"2\"}","{\"treeId\":\"1 OR 1=1\"}","[1]","\"1\""})Assert.Throws<ArgumentException>(()=>CascadeTreeGraphReader.FilterId(Read(text),"treeId","treeName"));
    }
    /// <summary>绝对深度包含已有祖先，末尾游标页的一条后代也不能在宽树迁移时漏掉。</summary>
    [Fact]
    public async Task Wide_last_page_descendant_uses_absolute_depth_budget()
    {
        var ancestors=Enumerable.Range(1,62).Select(id=>new CascadeTreeNode(id,"祖先",id==1?null:(id-1).ToString()));
        var nodes=ancestors.Concat([new CascadeTreeNode(1000,"分支","62")]).Concat(Enumerable.Range(2000,1205).Select(id=>new CascadeTreeNode(id,"叶","1000"))).ToArray();
        Assert.Equal(1206,(await Graph(nodes).FilterIdsAsync(1000,false)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Graph(nodes.Concat([new CascadeTreeNode(5000,"最末孙","3204")]).ToArray()).FilterIdsAsync(1000,false));
    }

    /// <summary>同祖先多行只按批读取一次，不产生每行完整路径的N+1查询。</summary>
    [Fact]
    public async Task Display_batches_shared_ancestors_and_does_not_return_partial_or_broken_paths()
    {
        var nodes=new[]{new CascadeTreeNode(1,"根",null)}.Concat(Enumerable.Range(2,1000).Select(id=>new CascadeTreeNode(id,"叶"+id,"1"))).Concat([new CascadeTreeNode(2000,"断链","9999"),new(2001,"环","2002"),new(2002,"环","2001")]).ToArray();
        var fetches=0;var graph=Graph(nodes,()=>fetches++);var result=await graph.DisplayAsync(Enumerable.Range(2,1000).Select(id=>(long)id).Concat([2000,2001]).ToArray());
        Assert.Equal("根/叶2",result[2]);Assert.Equal("",result[2000]);Assert.Equal("",result[2001]);Assert.InRange(fetches,1,9);
    }
    /// <summary>宽度超过旧候选上限及一个游标页，后代集合仍完整；exact只包含本节点。</summary>
    [Fact]
    public async Task Wide_descendants_use_cursor_pages_without_silent_truncation()
    {
        var nodes=new[]{new CascadeTreeNode(1,"根",null)}.Concat(Enumerable.Range(2,5500).Select(id=>new CascadeTreeNode(id,"叶","1"))).Append(new(6000,"孙","5501")).ToArray();var graph=Graph(nodes);
        Assert.Equal(5502,(await graph.FilterIdsAsync(1,false)).Count);Assert.Equal(new long[]{1},await graph.FilterIdsAsync(1,true));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>graph.FilterIdsAsync(9999,false));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>graph.DisplayAsync([1],cancel.Token));
    }
    /// <summary>固定范围只包含这些节点；委托替身不代表真实SQL或权限已验收。</summary>
    private static CascadeTreeGraphReader Graph(CascadeTreeNode[] nodes,Action? fetched=null)
    {
        var reader=new SelfTreeReader((id,_)=>Task.FromResult(nodes.SingleOrDefault(row=>row.Id==id)),(_,_,_)=>throw new InvalidOperationException(),(_,_)=>throw new InvalidOperationException(),"/");
        return new(reader,(ids,_)=>{fetched?.Invoke();return Task.FromResult<IReadOnlyList<CascadeTreeNode>>(nodes.Where(row=>ids.Contains(row.Id)).ToArray());},(parents,after,take,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(nodes.Where(row=>row.Id>after&&row.ParentId is not null&&parents.Contains(long.Parse(row.ParentId))).OrderBy(row=>row.Id).Take(take).ToArray()),"/");
    }
}
