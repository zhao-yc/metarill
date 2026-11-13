using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>实际固定层图及按名/父谓词反查；不以字符串替换模拟数据库或批量事务。</summary>
public sealed class CrossTableExcelPathTests
{
    private static readonly CascadeTreeNode[][] Rows=[
        [new(9007199254740993,"国甲",null),new(2,"国乙",null)],
        [new(9007199254740993,"区甲","9007199254740993"),new(22,"区乙","2")],
        [new(9007199254740993,"城市甲","9007199254740993"),new(32,"城市乙","22")]];
    /// <summary>完整路径、补空根/中层及唯一旧连续简写，都还原到正确深度和精确大ID。</summary>
    [Theory]
    [InlineData("国甲/区甲/城市甲",3)][InlineData("国甲//",1)][InlineData("国甲/区甲/",2)]
    [InlineData("国甲",1)][InlineData("国甲/区甲",2)][InlineData("区甲",2)][InlineData("城市甲",3)][InlineData("区甲/城市甲",3)]
    public async Task Fixed_paths_and_unique_legacy_forms_preserve_depth(string text,int depth)
    {
        var result=await Resolve(text);Assert.Equal(depth,result.Path.Count);Assert.All(result.Path,node=>Assert.Equal("9007199254740993",node.Id));
        Assert.Equal(depth switch{1=>"国甲//",2=>"国甲/区甲/",_=>"国甲/区甲/城市甲"},CrossTableExcelPath.Format(result,3,"/"));
    }
    /// <summary>不压缩空层、不截断、未命中不清空，也不把首尾空白静默当作另一名称。</summary>
    [Theory]
    [InlineData("")][InlineData("//")][InlineData("/区甲/")][InlineData("国甲//城市甲")][InlineData("国甲/")]
    [InlineData("国甲/区甲/城市甲/额外")][InlineData("国甲/区乙/城市乙")][InlineData("不存在")][InlineData(" 国甲//")][InlineData("国甲/ /")]
    public async Task Bad_or_nonrepresentable_paths_are_input_errors(string text)
        =>Assert.True((await Assert.ThrowsAsync<PageImportException>(()=>Resolve(text))).IsInputError);
    /// <summary>跨层同名和同级重名均报错，排序第一条不构成唯一还原。</summary>
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Ambiguous_name_never_picks_first(bool acrossLevels)
    {
        var rows=Rows.Select(level=>level.ToArray()).ToArray();
        if(acrossLevels)rows[2][0]=rows[2][0] with{Name="国甲"};else rows[0]=[..rows[0],new(3,"国甲",null)];
        await Assert.ThrowsAsync<PageImportException>(()=>Resolve("国甲",rows));
        if(acrossLevels)Assert.Single((await Resolve("国甲//",rows)).Path);
    }
    /// <summary>回调不能返回错误父/祖先ID、不同名称或伪造层数来混过反查。</summary>
    [Theory][InlineData("parent")][InlineData("name")][InlineData("depth")][InlineData("key")][InlineData("ancestor-id")]
    public async Task Broken_reader_contract_is_rejected(string fault)
    {
        var rows=Rows.Select(level=>level.ToArray()).ToArray();
        if(fault=="parent")rows[1][0]=rows[1][0] with{ParentId="2"};
        if(fault=="name")rows[1][0]=rows[1][0] with{Name="区乙"};
        await Assert.ThrowsAsync<PageImportException>(()=>CrossTableExcelPath.ResolveAsync("国甲/区甲/城市甲",3,"/",
            (level,text,parent,bound,ct)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(level==1?rows[level].Take(1).ToArray():rows[level].Where(row=>row.Name==text).ToArray()),
            (level,id,ct)=>Task.FromResult(fault=="ancestor-id"?new CrossTablePath(Path(level+1).Path.Select((node,index)=>index==0?node with{Id="777"}:node).ToArray(),""):fault=="depth"?Path(1):fault=="key"?new CrossTablePath([new("1","国甲","same"),new("1","区甲","same"),new("1","城市甲","same")],""):Path(level+1))));
    }
    /// <summary>自定义多字符分隔符仍保留尾空层；空值导出不创造一条空路径。</summary>
    [Fact]
    public async Task Multi_character_separator_and_empty_export_keep_shape()
    {
        Assert.Equal("国甲->->",CrossTableExcelPath.Format(Path(1),3,"->"));
        Assert.Equal(2,(await Resolve("国甲->区甲->",joiner:"->")).Path.Count);
        Assert.Null(CrossTableExcelPath.Format(null,3,"/"));Assert.Null(CrossTableExcelPath.Format(new([],""),3,"/"));
    }
    /// <summary>名称含分隔符、空白或非规范主键时，拒绝产生不能安全反查的导出文本。</summary>
    [Theory][InlineData("国/甲","1")][InlineData("国甲 ","1")][InlineData("","1")][InlineData("国甲","01")][InlineData("国甲","1e2")]
    public void Unsafe_export_is_rejected(string name,string id)
        =>Assert.Throws<PageImportException>(()=>CrossTableExcelPath.Format(new([new(id,name,"country")],""),3,"/"));
    /// <summary>数值外观仅是名称，不以数字文本选择同号主键。</summary>
    [Fact]
    public async Task Numeric_label_is_resolved_by_name_instead_of_primary_key()
    {
        var rows=Rows.Select(level=>level.ToArray()).ToArray();rows[2][0]=rows[2][0] with{Name="123"};
        Assert.Equal("9007199254740993",(await Resolve("123",rows)).Path[^1].Id);
    }
    /// <summary>固定跨表不是递归自树，不额外套用64层上限；原空格分隔符仍可表达不含空格的名称。</summary>
    [Theory][InlineData(3," ")][InlineData(70,"/")]
    public async Task Original_configured_separator_and_fixed_depth_remain_supported(int count,string joiner)
    {
        var text=CrossTableExcelPath.Format(Path(1),count,joiner)!;
        Assert.Equal(Enumerable.Repeat("国甲",1).Concat(Enumerable.Repeat("",count-1)),text.Split(joiner,StringSplitOptions.None));
        var path=await CrossTableExcelPath.ResolveAsync(text,count,joiner,
            (_,name,_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([Rows[0][0]]),
            (_,_,_)=>Task.FromResult(Path(1)));
        Assert.Single(path.Path);
    }
    /// <summary>取消不得触发任何目标读取。</summary>
    [Fact]
    public async Task Cancellation_prevents_callbacks()
    {
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CrossTableExcelPath.ResolveAsync("国甲//",3,"/",(_,_,_,_,_)=>throw new InvalidOperationException(),(_,_,_)=>throw new InvalidOperationException(),cancel.Token));
    }
    /// <summary>按名称与父键筛选实际固定图，再按主键逆读祖先。</summary>
    private static Task<CrossTablePath> Resolve(string text,CascadeTreeNode[][]? rows=null,string joiner="/")
    {
        rows??=Rows;
        return CrossTableExcelPath.ResolveAsync(text,3,joiner,
            (level,name,parent,bound,ct)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(rows[level].Where(row=>row.Name==name&&(!bound||level==0||row.ParentId==parent?.ToString(System.Globalization.CultureInfo.InvariantCulture))).Take(2).ToArray()),
            (level,id,ct)=>{
                var path=new CrossTablePathNode[level+1];var current=id;
                for(var index=level;index>=0;index--){var row=rows[index].Single(row=>row.Id==current);path[index]=new(row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),row.Name!,new[]{"country","area","city"}[index]);if(index>0)current=long.Parse(row.ParentId!);}
                return Task.FromResult(new CrossTablePath(path,""));
            });
    }
    /// <summary>构造跨表同数字ID的合法路径，不将其误判为递归环。</summary>
    private static CrossTablePath Path(int count)=>new(Enumerable.Range(0,count).Select(index=>new CrossTablePathNode("9007199254740993",Rows[index][0].Name!,new[]{"country","area","city"}[index])).ToArray(),"");
}
