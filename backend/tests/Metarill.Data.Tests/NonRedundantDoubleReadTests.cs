using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>固定非冗余读取沿逻辑double保留有限测量值，不能下溢为零后扩大筛选。</summary>
public sealed class NonRedundantDoubleReadTests
{
    /// <summary>超过decimal范围与最小非零double均不经过十进制或JSON中转。</summary>
    [Theory]
    [InlineData("1.25e100",1.25e100)][InlineData("-3.5e-200",-3.5e-200)]
    [InlineData("4.9406564584124654e-324",double.Epsilon)][InlineData("0",0d)]
    public void Typed_projection_preserves_finite_double(string input,double expected)
    {
        var row=new Dictionary<string,object?>{{"measure",input},{"empty",null},{"id","9007199254740993"}};
        Assert.Equal(expected,NonRedundantReads.Value<double?>(row,"measure"));Assert.Null(NonRedundantReads.Value<double?>(row,"empty"));Assert.Equal(9007199254740993L,NonRedundantReads.Value<long>(row,"id"));
        row["measure"]=expected;Assert.Equal(expected,NonRedundantReads.Value<double?>(row,"measure"));
    }
    /// <summary>驱动或固定读取别名中的非法值在VO投影时拒绝，不留到JSON序列化才失败。</summary>
    [Theory]
    [InlineData("nan")][InlineData("infinity")][InlineData("negative-infinity")][InlineData("overflow")][InlineData("underflow")]
    public void Typed_projection_rejects_nonfinite_and_underflow(string kind)
    {
        object value=kind switch{"nan"=>double.NaN,"infinity"=>double.PositiveInfinity,"negative-infinity"=>double.NegativeInfinity,"overflow"=>"1e309",_=>"1e-999"};
        Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.Value<double?>(new Dictionary<string,object?>{{"measure",value}},"measure"));
    }
    /// <summary>标量及IN中的非零下溢文本均须在首次连接前拒绝，不能把零记录读出来。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Underflow_filters_fail_before_connection(bool list)
    {
        var catalog=NonRedundantReadTests.Catalog("mt_double_root","mt_double_target");foreach(var field in catalog.ConfigColumns.Where(column=>column.ColumnName is "bill_amount" or "amount"))field.LogicalType="double";
        var plan=MySqlNonRedundantReadCompiler.Compile(catalog,"1",["id","bill_amount"]);
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        object input=list?new object?[]{1.25d,"1e-999"}:"1e-999";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadPageAsync(plan,[new("bill_amount",list?FixedReadComparison.In:FixedReadComparison.Equal,input)],[],1,10));
    }
    /// <summary>浮点合计接通后，非法筛选仍须在连接前拒绝，不能下溢为零。</summary>
    [Fact]
    public async Task Double_statistics_reject_underflow_filters_before_connecting()
    {
        var catalog=NonRedundantReadTests.Catalog("mt_double_root","mt_double_target");foreach(var field in catalog.ConfigColumns.Where(column=>column.ColumnName is "bill_amount" or "amount"))field.LogicalType="double";
        var plan=MySqlNonRedundantReadCompiler.Compile(catalog,"1",["id","bill_amount"]);
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadStatisticsAsync(plan,[new("bill_amount",FixedReadComparison.Equal,"1e-999")],["bill_amount"]));
    }
}
