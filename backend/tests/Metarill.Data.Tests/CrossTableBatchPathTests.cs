using Metarill.Core.Cascades;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>固定图批读的范围、去重及失败原子性；不冒充SQL或耗时基准。</summary>
public sealed class CrossTableBatchPathTests
{
    private static readonly CrossTableLevel[] Levels=[new("root","rootId"),new("middle","middleId"),new("leaf","leafId")];
    /// <summary>501个不同叶子只分三批，两个公共祖先各读一次；混合选择深度与重复ID保持。</summary>
    [Fact]
    public async Task Shared_ancestors_and_duplicate_selections_are_read_once_per_role_in_bounded_batches()
    {
        var rows=new[]{new[]{new CascadeTreeNode(1,"根",null)},new[]{new CascadeTreeNode(1,"中","01")},Enumerable.Range(1,501).Select(id=>new CascadeTreeNode(id,"叶"+id,"1")).ToArray()};
        var calls=new List<(int Level,long[] Ids)>();var scalar=0;
        var reader=Create(rows,(_,_,_)=>{scalar++;throw new InvalidOperationException("不得逐个回读");},(index,ids,_)=>{calls.Add((index,ids.ToArray()));return Task.FromResult<IReadOnlyList<CascadeTreeNode>>(rows[index].Where(row=>ids.Contains(row.Id)).Reverse().ToArray());});
        var requests=Enumerable.Range(1,501).Select(id=>(2,(long)id)).Concat([(0,1L),(1,1L),(2,1L)]).ToArray();
        var paths=await reader.PathsAsync(requests);Assert.Equal(503,paths.Count);Assert.Equal(0,scalar);Assert.Equal(5,calls.Count);Assert.All(calls,call=>Assert.InRange(call.Ids.Length,1,200));
        Assert.Equal(new[]{200,200,101},calls.Where(call=>call.Level==2).Select(call=>call.Ids.Length));
        Assert.Single(calls,call=>call.Level==0);Assert.Single(calls,call=>call.Level==1);
        foreach(var request in requests)Assert.Equal(await Scalar(rows).PathAsync(request.Item1,request.Item2),paths[request],PathComparer.Instance);
        Assert.Equal(new[]{"root","middle","leaf"},paths[(2,1)].Path.Select(node=>node.LevelKey));
    }
    /// <summary>真实权限先于任意层读取，空集合也检查全部层；失效主键不触发传输。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Authorization_denial_precedes_every_read_even_for_empty_requests(bool empty)
    {
        var calls=0;var authorized=new List<int>();
        var reader=new CrossTableReader(Levels,index=>{authorized.Add(index);if(index==1)throw new UnauthorizedAccessException();},(_,_,_)=>throw new InvalidOperationException(),(_,_,_)=>throw new InvalidOperationException(),"/",(_,_,_)=>{calls++;throw new InvalidOperationException();});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.PathsAsync(empty?[]:[(2,1)]));Assert.Equal(new[]{0,1},authorized);Assert.Equal(0,calls);
    }
    /// <summary>缺行、重复行、越界行及坏父键均整次拒绝；不能用返回条数代替主键集合核对。</summary>
    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("foreign")][InlineData("parent")][InlineData("null-parent")]
    public async Task Missing_duplicate_foreign_rows_and_broken_parents_never_return_partial_paths(string kind)
    {
        var rows=new[]{new[]{new CascadeTreeNode(1,"根",null)},new[]{new CascadeTreeNode(2,"中","1")},new[]{new CascadeTreeNode(3,"叶","2")}};
        var reader=Create(rows,null,(index,ids,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(index==2?kind switch{"missing"=>[],"duplicate"=>[rows[2][0],rows[2][0]],"foreign"=>[new(99,"越界","2")],"parent"=>[new(3,"叶","坏")],_=>[new(3,"叶",null)]}:rows[index]));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.PathsAsync([(0,1),(2,3)]));
    }
    /// <summary>取消、查询故障不转换为数据空值，授权后回调取消也不继续读取祖先。</summary>
    [Fact]
    public async Task Cancellation_and_read_failure_keep_the_original_exception()
    {
        using var stop=new CancellationTokenSource();var calls=0;
        var reader=Create([],null,(_,_,_)=>{calls++;stop.Cancel();return Task.FromResult<IReadOnlyList<CascadeTreeNode>>([new(3,"叶","2")]);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reader.PathsAsync([(2,3)],stop.Token));Assert.Equal(1,calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reader.PathsAsync([],stop.Token));Assert.Equal(1,calls);
        var failure=new TimeoutException("传输故障");var broken=Create([],null,(_,_,_)=>throw failure);Assert.Same(failure,await Assert.ThrowsAsync<TimeoutException>(()=>broken.PathsAsync([(2,3)])));
    }
    /// <summary>旧构造仍逐跳执行待写覆盖委托；批读不跨调用缓存，也不限制导出为5000项。</summary>
    [Fact]
    public async Task Legacy_constructor_and_fresh_calls_preserve_overlay_and_large_export_semantics()
    {
        var rows=new[]{new[]{new CascadeTreeNode(9007199254740993,"根",null)},new[]{new CascadeTreeNode(9007199254740993,"中","9007199254740993")},new[]{new CascadeTreeNode(9007199254740993,null,"9007199254740993")}};
        var reader=Scalar(rows);var key=(2,9007199254740993L);var first=(await reader.PathsAsync([key]))[key];Assert.Equal("根/中",first.DisplayText);
        rows[2][0]=rows[2][0] with{Name="新叶"};Assert.Equal("根/中/新叶",(await reader.PathsAsync([key]))[key].DisplayText);Assert.Equal("",first.Path[2].Name);
        var bulk=Create([],null,(index,ids,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(ids.Select(id=>new CascadeTreeNode(id,"值",index==0?null:"1")).ToArray()));
        Assert.Equal(5001,(await bulk.PathsAsync(Enumerable.Range(1,5001).Select(id=>(2,(long)id)).ToArray())).Count);
        await Assert.ThrowsAsync<ArgumentException>(()=>reader.PathsAsync([(3,1)]));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.PathsAsync([(2,0)]));
    }
    /// <summary>固定层数据供逐跳与批读对照，只有缺省批读允许旧委托回退。</summary>
    private static CrossTableReader Scalar(CascadeTreeNode[][] rows)=>new(Levels,_=>{},(index,id,_)=>Task.FromResult(rows[index].SingleOrDefault(row=>row.Id==id)),(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([]),"/");
    /// <summary>批读委托只作为受控传输，生成SQL另行验证。</summary>
    private static CrossTableReader Create(CascadeTreeNode[][] rows,Func<int,long,CancellationToken,Task<CascadeTreeNode?>>? find,Func<int,IReadOnlyList<long>,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> batch)=>new(Levels,_=>{},find??((index,id,_)=>Task.FromResult(rows[index].SingleOrDefault(row=>row.Id==id))),(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([]),"/",batch);
    /// <summary>比较路径内容，不依赖只读集合的引用相等性。</summary>
    private sealed class PathComparer:IEqualityComparer<CrossTablePath>
    {
        internal static readonly PathComparer Instance=new();
        public bool Equals(CrossTablePath? first,CrossTablePath? second)=>first is not null&&second is not null&&first.DisplayText==second.DisplayText&&first.Path.SequenceEqual(second.Path);
        public int GetHashCode(CrossTablePath value)=>value.DisplayText.GetHashCode(StringComparison.Ordinal);
    }
}
