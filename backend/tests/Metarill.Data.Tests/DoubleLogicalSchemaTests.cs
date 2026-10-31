using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>实际正向／反向编译及基础仓储转换；不将离线计划当作MySQL执行证明。</summary>
public sealed class DoubleLogicalSchemaTests
{
    /// <summary>默认值使用double范围和往返格式，真实反查草案不自动退成decimal。</summary>
    [Theory]
    [InlineData("1.5000","1.5")][InlineData("+1.5e100","1.5E+100")][InlineData("-0.0","0")][InlineData("1.7976931348623157e308","1.7976931348623157E+308")]
    public void Double_column_defaults_roundtrip_without_decimal_narrowing(string value,string expected)
    {
        var column=Column();column.DefaultValue=value;var compiled=MySqlSchemaPlanner.CompileColumn(column);Assert.Equal("double",compiled.StoreType);Assert.Equal(expected,compiled.DefaultSql);
        var schema=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),compiled])]);var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"123");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));Assert.Equal("double",draft.LogicalMetadata.ConfigColumns.Single(item=>item.ColumnName=="value").LogicalType);
        Assert.Empty(MySqlSchemaPlanner.Compare(schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"db")));
        Assert.Contains("`value` double",Assert.Single(MySqlSchemaPlanner.Compare(new("db",[]),schema)).Sql);
    }
    /// <summary>非法默认值不能成为数据库表达式、NaN、无穷大或零。</summary>
    [Theory][InlineData("NaN")][InlineData("Infinity")][InlineData("1e309")][InlineData("1e-999")][InlineData("1,5")][InlineData("1);DROP TABLE x")]
    public void Invalid_double_defaults_are_rejected(string value){var column=Column();column.DefaultValue=value;Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.CompileColumn(column));}
    /// <summary>数据库尾零和指数格式只在double语义内归一，不改变decimal或把不同值当相等。</summary>
    [Fact]
    public void Double_default_comparison_uses_binary64_value_and_preserves_other_types()
    {
        DatabaseSchema Schema(string value)=>new("db",[new("record",[new("id","bigint",false,true),new("value","double",true,DefaultSql:value)])]);
        Assert.Empty(MySqlSchemaPlanner.Compare(Schema("1.500000"),Schema("1.5e0")));Assert.Equal(MySqlSchemaPlanner.Fingerprint(Schema("1.500000")),MySqlSchemaPlanner.Fingerprint(Schema("1.5e0")));
        Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(Schema("1.5")),MySqlSchemaPlanner.Fingerprint(Schema("2.5")));
        Assert.Equal("decimal(18,2)",MySqlSchemaPlanner.CompileColumn(new(){ColumnName="money",LogicalType="decimal"}).StoreType);
        var lossy=SchemaImportDraftCompiler.Compile(new(new("mysql",new("db",[new("record",[new("id","bigint",false,true),new("value","double(10,2)",true)])]),"source"),[]),"123");Assert.False(lossy.ColumnsAndIndexesRepresentable);
    }
    /// <summary>执行仓储真正的标量归一方法，证明参数变为double而不是把字符串原样交给驱动。</summary>
    [Fact]
    public void Infrastructure_coercion_is_typed_invariant_nullable_and_finite()
    {
        var method=typeof(SqlRowStore).GetMethod("Coerce",BindingFlags.Static|BindingFlags.NonPublic)!;var culture=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            object? Convert(object? value)=>method.Invoke(null,[value,Column()]);
            Assert.Equal(1.5e100,Assert.IsType<double>(Convert("1.5e100")));Assert.Equal(1.5,Assert.IsType<double>(Convert(JsonSerializer.SerializeToElement("1.5"))));Assert.Null(Convert(null));Assert.Null(Convert(""));
            foreach(var invalid in new object[]{double.NaN,double.PositiveInfinity,"1e-999","1e309","1,5"})Assert.IsType<FormatException>(Assert.Throws<TargetInvocationException>(()=>Convert(invalid)).InnerException);
        }
        finally{CultureInfo.CurrentCulture=culture;}
    }
    /// <summary>声明原已支持的double词表，不新增物理类型UI或SQL片段参数。</summary>
    private static ColumnMetadata Column()=>new(){Id="value",ConfigTableId="table",ColumnName="value",LogicalType="double",ColumnType="double"};
}
