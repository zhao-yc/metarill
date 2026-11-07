using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>时间默认精度与正反编译真实往返；不连接数据库。</summary>
public sealed class ClockTimeSchemaTests
{
    /// <summary>只编译可由声明精度精确表示的固定时间常量。</summary>
    [Theory][InlineData(0,"12:34:56")][InlineData(1,"12:34:56.1")][InlineData(6,"12:34:56.123456")]
    public void Clock_default_compiles_and_roundtrips(int precision,string value)
    {
        var compiled=MySqlSchemaPlanner.CompileColumn(Column(precision,value));Assert.Equal("'"+value+"'",compiled.DefaultSql);
        var actual=compiled with{DefaultSql=value};var schema=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),actual])]);
        var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"123");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));
        var time=Assert.Single(draft.LogicalMetadata.ConfigColumns,column=>column.ColumnName=="clock");Assert.Equal("time",time.LogicalType);Assert.Equal(value,time.DefaultValue);
        Assert.Empty(MySqlSchemaPlanner.Compare(schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"db")));
    }
    /// <summary>零补齐只影响TIME同值默认，不让不同精度或值变成相等。</summary>
    [Fact]
    public void Database_default_zero_padding_is_value_equivalent()
    {
        DatabaseSchema Schema(string type,string value)=>new("db",[new("record",[new("id","bigint",false,true),new("clock",type,true,DefaultSql:value)])]);
        Assert.Empty(MySqlSchemaPlanner.Compare(Schema("time(6)","12:34:56.100000"),Schema("time(6)","'12:34:56.1'")));
        Assert.NotEmpty(MySqlSchemaPlanner.Compare(Schema("time(6)","12:34:56.2"),Schema("time(6)","'12:34:56.1'")));
        Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compare(Schema("time(6)","12:34:56.1"),Schema("time","'12:34:56.1'")));
    }
    /// <summary>未知函数/SQL、跨日、精度丢失（含不足一tick的输入）都在DDL前拒绝。</summary>
    [Theory][InlineData(6,"CURRENT_TIME()")][InlineData(6,"12:34:56';DROP TABLE x")][InlineData(6,"839:00:00")]
    [InlineData(0,"12:34:56.1")][InlineData(6,"12:34:56.1234567")][InlineData(0,"12:34:56.000000001")]
    public void Invalid_or_lossy_defaults_are_rejected(int precision,string value)=>Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.CompileColumn(Column(precision,value)));
    /// <summary>原生TIME反查不附加日内控件限制；MySQL边界保留负数与838小时。</summary>
    [Theory][InlineData("30:00:00")][InlineData("-25:01:02.123456")][InlineData("838:59:58.999999")][InlineData("-838:59:58.999999")][InlineData("838:59:59")][InlineData("-838:59:59")]
    public void Native_time_defaults_roundtrip_without_clock_restriction(string value)
    {
        var column=MySqlSchemaPlanner.CompileColumn(Column(6,value));Assert.Equal("'"+value+"'",column.DefaultSql);
        var schema=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),column with{DefaultSql=value}])]);
        var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"123");
        Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));
        Assert.Empty(MySqlSchemaPlanner.Compare(schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"db")));
    }
    /// <summary>范围属于MySQL适配器，不依赖页面、SQL mode或服务器静默截断。</summary>
    [Fact]
    public void MySql_time_range_is_explicit()
    {
        var provider=new Metarill.Data.MySqlDatabaseProvider();
        foreach(var text in new[]{"-838:59:59","30:00","838:59:59","838:59:58.999999"})provider.ValidateTimeValue(Metarill.Core.Contracts.SqlTimeValue.ParseText(text)!.Value);
        foreach(var text in new[]{"-839:00","839:00","838:59:59.000001","-838:59:59.000001","838:59:59.9999999"})Assert.Throws<ArgumentOutOfRangeException>(()=>provider.ValidateTimeValue(Metarill.Core.Contracts.SqlTimeValue.ParseText(text)!.Value));
    }
    /// <summary>基础设施真正的值归一支持原生总小时，时钟typed值也不能绕过校验。</summary>
    [Fact]
    public void Infrastructure_and_read_projection_use_time_contract()
    {
        var method=typeof(Metarill.Runtime.Persistence.SqlRowStore).GetMethod("Coerce",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        var column=Column(6,"");var expected=Metarill.Core.Contracts.SqlTimeValue.ParseText("-30:01:02.123456");
        Assert.Equal(expected,method.Invoke(null,["-30:01:02.123456",column]));
        Assert.Equal(expected,Metarill.Data.Queries.NonRedundantReads.Value<TimeSpan?>(new Dictionary<string,object?>{{"time","-30:01:02.123456"}},"time"));
        Assert.Equal(expected,method.Invoke(null,[System.Text.Json.JsonSerializer.SerializeToElement("-30:01:02.123456"),column]));
        column.ControlKind="time";
        foreach(var value in new object[]{"-30:00",TimeSpan.FromHours(30),"2026-02-30 12:34:56"})
            Assert.IsType<System.Text.Json.JsonException>(Assert.Throws<System.Reflection.TargetInvocationException>(()=>method.Invoke(null,[value,column])).InnerException);
    }
    /// <summary>独立元数据固定精度，避免复用历史ColumnLength推导小数位。</summary>
    private static ColumnMetadata Column(int precision,string value)=>new(){Id="clock",ConfigTableId="table",ColumnName="clock",LogicalType="time",ColumnType="time("+precision+")",DefaultValue=value};
}
