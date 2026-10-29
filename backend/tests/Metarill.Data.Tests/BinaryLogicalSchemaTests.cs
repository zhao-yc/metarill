using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Data.Queries;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>二进制结构计划及真实行归一；离线执行不计实库验收。</summary>
public sealed class BinaryLogicalSchemaTests
{
    /// <summary>原已支持的长度映射和反查草案往返保持。</summary>
    [Theory][InlineData("","longblob")][InlineData("4","varbinary(4)")][InlineData(" ","varbinary(255)")]
    public void Binary_schema_roundtrips_without_text_narrowing(string length,string physical)
    {
        var column=Column(length);var compiled=MySqlSchemaPlanner.CompileColumn(column);Assert.Equal(physical,compiled.StoreType);
        var schema=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),compiled])]);
        var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"123");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));
        Assert.Equal("binary",draft.LogicalMetadata.ConfigColumns.Single(item=>item.ColumnName=="value").LogicalType);
        Assert.Empty(MySqlSchemaPlanner.Compare(schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"db")));
    }
    /// <summary>基础设施真实转换保持字节参数；不会把空字节串改null或把数值数组当二进制。</summary>
    [Fact]
    public void Infrastructure_binary_coercion_preserves_empty_null_and_byte_length()
    {
        var method=typeof(SqlRowStore).GetMethod("Coerce",BindingFlags.NonPublic|BindingFlags.Static)!;
        object? Convert(object? input)=>method.Invoke(null,[input,Column("4")]);
        var bytes=new byte[]{0,255,0,127};Assert.Same(bytes,Convert(bytes));Assert.Equal(bytes,Assert.IsType<byte[]>(Convert("AP8Afw==")));Assert.Equal(bytes,Assert.IsType<byte[]>(Convert(JsonSerializer.SerializeToElement("AP8Afw=="))));
        Assert.Empty(Assert.IsType<byte[]>(Convert("")));Assert.Null(Convert(null));Assert.Null(Convert(JsonSerializer.SerializeToElement<object?>(null)));
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>Convert(new byte[5])).InnerException);
        foreach(var input in new object[]{"错误",JsonSerializer.SerializeToElement(new[]{0,255}),42})Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>Convert(input)).InnerException);
    }
    /// <summary>执行适配器真正的准备/SQL/绑定，字节作为单个参数；未知文本和LIKE不得绕过类型。</summary>
    [Fact]
    public void Nonredundant_binary_filters_bind_actual_driver_bytes()
    {
        var catalog=NonRedundantReadTests.Catalog("mt_binary_root","mt_binary_party");
        catalog.ConfigColumns.Add(new(){Id="9900",ConfigTableId="1",ColumnName="payload",LogicalType="binary",ColumnLength="4"});
        var plan=MySqlNonRedundantReadCompiler.Compile(catalog,"1",["id","payload"]);
        var methods=typeof(MySqlNonRedundantReader);
        FixedReadFilter Prepare(FixedReadFilter filter)=>(FixedReadFilter)methods.GetMethod("Prepare",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[plan,filter])!;
        string Sql(FixedReadFilter filter)=>(string)methods.GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[plan,filter,0])!;
        var bytes=new byte[]{0,255};var scalar=Prepare(new("payload",FixedReadComparison.Equal,bytes));Assert.NotSame(bytes,scalar.Value);Assert.Equal(bytes,Assert.IsType<byte[]>(scalar.Value));
        Assert.Contains("=@filter_0",Sql(scalar));
        using var command=new MySqlConnector.MySqlCommand();
        methods.GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[command,"100",new[]{scalar}]);Assert.Same(scalar.Value,command.Parameters["filter_0"].Value);
        var multiple=Prepare(new("payload",FixedReadComparison.In,new object?[]{bytes,Array.Empty<byte>(),null}));Assert.Contains("IN (@filter_0_0,@filter_0_1)",Sql(multiple));Assert.Contains("IS NULL",Sql(multiple));
        command.Parameters.Clear();methods.GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[command,"100",new[]{multiple}]);Assert.NotSame(bytes,command.Parameters["filter_0_0"].Value);Assert.Equal(bytes,Assert.IsType<byte[]>(command.Parameters["filter_0_0"].Value));Assert.Empty(Assert.IsType<byte[]>(command.Parameters["filter_0_1"].Value));
        bytes[0]=99;Assert.Equal((byte)0,Assert.IsType<byte[]>(scalar.Value)[0]);Assert.Equal((byte)0,Assert.IsType<byte[]>(command.Parameters["filter_0_0"].Value)[0]);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Prepare(new("payload",FixedReadComparison.Equal,"AP8="))).InnerException);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Sql(Prepare(new("payload",FixedReadComparison.Contains,bytes)))).InnerException);
    }
    /// <summary>逻辑类型沿已有binary元数据，不新增UI物理词表。</summary>
    private static ColumnMetadata Column(string length)=>new(){Id="value",ConfigTableId="table",ColumnName="value",LogicalType="binary",ColumnType="blob",ColumnLength=length};
}
