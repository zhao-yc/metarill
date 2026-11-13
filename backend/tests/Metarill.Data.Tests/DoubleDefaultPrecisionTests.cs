using System.Globalization;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>默认SQL和指纹按真实位型证明，不能将两个相邻double格式化成同一文本。</summary>
public sealed class DoubleDefaultPrecisionTests
{
    /// <summary>正向默认常量和反查草案都保持已知精确位型，未执行DDL。</summary>
    [Theory]
    [InlineData("4.1045368012983762E-289",0x0410_0000_0000_0000L)]
    [InlineData("4.1045368012983758E-289",0x040f_ffff_ffff_ffffL)]
    public void Planned_default_and_reverse_draft_preserve_the_original_bits(string input,long expected)
    {
        var column=MySqlSchemaPlanner.CompileColumn(new(){Id="2",ConfigTableId="1",ColumnName="value",LogicalType="double",DefaultValue=input});
        Assert.Equal(expected,BitConverter.DoubleToInt64Bits(double.Parse(column.DefaultSql!,CultureInfo.InvariantCulture)));
        var schema=Schema(column.DefaultSql!);var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"54");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));
        Assert.Equal(expected,BitConverter.DoubleToInt64Bits(double.Parse(draft.LogicalMetadata.ConfigColumns.Single(field=>field.ColumnName=="value").DefaultValue,CultureInfo.InvariantCulture)));Assert.Empty(MySqlSchemaPlanner.Compare(schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"db")));
    }
    /// <summary>相邻默认值应产生差异和不同指纹，不能隐藏一次真实结构变更。</summary>
    [Fact]
    public void Adjacent_double_defaults_do_not_share_the_schema_fingerprint()
    {
        var upper=Schema("4.1045368012983762E-289");var lower=Schema("4.1045368012983758E-289");Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(upper),MySqlSchemaPlanner.Fingerprint(lower));Assert.NotEmpty(MySqlSchemaPlanner.Compare(upper,lower));
    }
    /// <summary>真实double行只读规则不能把上值格式化成下值而错误允许写入；原0.1语义保持。</summary>
    [Fact]
    public void Readonly_rule_distinguishes_adjacent_double_rows_without_changing_ordinary_decimals()
    {
        var lower=BitConverter.Int64BitsToDouble(0x040f_ffff_ffff_ffffL);var rule=ReadOnlyConditionRule.Compile("score > threshold");Assert.True(rule.IsReadOnly(new Dictionary<string,object?>{{"score",BitConverter.Int64BitsToDouble(0x0410_0000_0000_0000L)},{"threshold",lower}}));Assert.False(rule.IsReadOnly(new Dictionary<string,object?>{{"score",lower},{"threshold",lower}}));
        Assert.True(ReadOnlyConditionRule.Compile("score == 0.1").IsReadOnly(new Dictionary<string,object?>{{"score",0.1d}}));
    }
    /// <summary>固定目标条件的当前证明不能将相邻double常量判成相同计划。</summary>
    [Fact]
    public void Fixed_condition_proof_does_not_accept_an_adjacent_double_constant()
    {
        var types=new Dictionary<string,string>{{"score","double"}};var plan=Metarill.Data.Queries.FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("score=@number",types.Keys),types);
        var upper=plan.BindVariables(new Dictionary<string,object?>{{"number",BitConverter.Int64BitsToDouble(0x0410_0000_0000_0000L)}});var lower=plan.BindVariables(new Dictionary<string,object?>{{"number",BitConverter.Int64BitsToDouble(0x040f_ffff_ffff_ffffL)}});
        Assert.False(upper.SameConstants(lower));Assert.True(upper.SameConstants(upper));
    }
    /// <summary>纯快照只含合成列，不连接已有业务表。</summary>
    private static DatabaseSchema Schema(string value)=>new("db",[new("record",[new("id","bigint",false,true),new("value","double",true,DefaultSql:value)])]);
}
