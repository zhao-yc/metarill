using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>生成用途需要的隐藏依赖、类型投影和请求边界，不以离线测试代替实库。</summary>
public sealed class NonRedundantReadRequestTests
{
    /// <summary>隐藏依赖只服务SQL，不扩大公开输出或可变类型字典。</summary>
    [Fact]
    public void Hidden_dependencies_do_not_expand_outputs()
    {
        var metadata=NonRedundantReadTests.Catalog("mt_read_root","mt_read_party");var plan=NonRedundantReads.Compile(metadata,"1",["id","name"],["bill_name","yz_project_id"]);Assert.Equal(new[]{"id","name"},plan.OutputTypes.Keys);Assert.Throws<NotSupportedException>(()=>((IDictionary<string,string>)plan.OutputTypes).Add("rogue","string"));Assert.Throws<NotSupportedException>(()=>NonRedundantReads.Compile(metadata,"1",["id"],["unknown"]));
    }
    /// <summary>精确长ID及可空数值直接按驱动标量转换，不经过JSON double。</summary>
    [Fact]
    public void Typed_projection_preserves_long_ids_and_nulls()
    {
        var row=new Dictionary<string,object?>{{"id","9007199254740993"},{"amount",12.50m},{"empty",null},{"enabled",1}};Assert.Equal(9007199254740993L,NonRedundantReads.Value<long>(row,"id"));Assert.Equal(12.50m,NonRedundantReads.Value<decimal?>(row,"amount"));Assert.Null(NonRedundantReads.Value<decimal?>(row,"empty"));Assert.True(NonRedundantReads.Value<bool?>(row,"enabled"));Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.Value<string>(row,"unknown"));
    }
    /// <summary>集合仅接受可冻结标量，不接受嵌套值或把小数ID取整。</summary>
    [Theory]
    [InlineData("nested")][InlineData("fraction")][InlineData("invalidProject")][InlineData("unpaged")]
    public async Task Invalid_requests_fail_before_connection(string kind)
    {
        using var session=new DatabaseSession(new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});var plan=NonRedundantReads.Compile(NonRedundantReadTests.Catalog("mt_read_root","mt_read_party"),"1",["id"]);var reader=new NonRedundantReader(session);
        var filters=kind=="nested"?new[]{new FixedReadFilter("id",FixedReadComparison.In,new object?[]{new[]{1,2}})}:kind=="fraction"?new[]{new FixedReadFilter("id",FixedReadComparison.In,new object?[]{1.5m})}:Array.Empty<FixedReadFilter>();
        if(kind=="unpaged")await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>reader.PageAsync(plan,filters,[],1,0));else await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.PageAsync(plan,filters,[],1,10,projectId:kind=="invalidProject"?"x":null));
    }
}
