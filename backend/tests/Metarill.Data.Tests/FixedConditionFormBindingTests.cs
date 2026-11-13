using System.Text.Json;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>请求/导入先按源物理逻辑类型绑定，目标条件不会放宽源字段的数值范围。</summary>
public sealed class FixedConditionFormBindingTests
{
    /// <summary>高位ID保留整数精度，源int32溢出、舍入和JSON容器整体拒绝。</summary>
    [Theory]
    [InlineData("2147483648","int32")]
    [InlineData("1e-100","int64")]
    [InlineData("9223372036854775808","int64")]
    [InlineData("1.0000000000000000000000000000001","bool")]
    [InlineData("0.1000000000000000000000000000000001","decimal")]
    [InlineData("1e-5000","double")]
    [InlineData("[]","string")]
    [InlineData("{}","int64")]
    [InlineData("1","string")]
    public void Inexact_or_incompatible_source_values_cannot_enter_target_filters(string json,string type)
    {using var document=JsonDocument.Parse(json);Assert.Throws<ArgumentException>(()=>FixedConditionSqlPlan.BindScalar(document.RootElement,type));}
    /// <summary>允许原字符串ID及确切数学整数，显式null仍是可绑定值。</summary>
    [Fact]
    public void Valid_source_values_keep_their_types_and_null()
    {
        using var id=JsonDocument.Parse("9007199254740993");Assert.Equal(9007199254740993L,FixedConditionSqlPlan.BindScalar(id.RootElement,"int64"));
        Assert.Equal(9007199254740993L,FixedConditionSqlPlan.BindScalar("9007199254740993","int64"));Assert.Equal(12,FixedConditionSqlPlan.BindScalar("12","int32"));Assert.Equal(0.1m,FixedConditionSqlPlan.BindScalar("0.1","decimal"));Assert.Null(FixedConditionSqlPlan.BindScalar(null,"string"));
    }
}
