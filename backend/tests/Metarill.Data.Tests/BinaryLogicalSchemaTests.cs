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
    /// <summary>Base64默认与VARBINARY/BLOB的安全常量及实际反查格式保持无损等价。</summary>
    [Theory][InlineData("4","X'00FF007F'")][InlineData("","(X'00FF007F')")]
    public void Binary_defaults_roundtrip_safe_hex_without_charset_conversion(string length,string expected)
    {
        var column=Column(length);column.DefaultValue="AP8Afw==";var compiled=MySqlSchemaPlanner.CompileColumn(column);Assert.Equal(expected,compiled.DefaultSql);
        foreach(var physical in new[]{"0x00ff007f","X'00FF007F'","(x'00ff007f')"})
        {
            var source=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),compiled with{DefaultSql=physical}])]);
            var desired=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),compiled])]);
            Assert.Empty(MySqlSchemaPlanner.Compare(source,desired));Assert.Equal(MySqlSchemaPlanner.Fingerprint(source),MySqlSchemaPlanner.Fingerprint(desired));
            var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",source,"source"),[]),"123");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));
            Assert.Equal("AP8Afw==",draft.LogicalMetadata.ConfigColumns.Single(item=>item.ColumnName=="value").DefaultValue);
            Assert.Empty(MySqlSchemaPlanner.Compare(source,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"db")));
        }
        var create=Assert.Single(MySqlSchemaPlanner.Compare(new("db",[]),new("db",[new("record",[new("id","bigint",false,true),compiled])]))).Sql;
        Assert.Contains("DEFAULT "+expected,create);
    }
    /// <summary>拒绝函数、奇数半字节、注释/拼接及非十六进制形式，不把字符常量默认等同二进制。</summary>
    [Theory][InlineData("UNHEX('00FF')")][InlineData("X'0'")][InlineData("0x0")][InlineData("X'GG'")][InlineData("X'00'; DROP TABLE x")][InlineData("((X'00'))")][InlineData("'00FF'")][InlineData("0x00/*x*/")][InlineData("0x00+1")]
    public void Binary_unknown_default_sql_is_not_normalized_or_imported(string sql)
    {
        Assert.False(MySqlBinaryDefault.TryNormalize(sql,out _));
        var schema=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),new("value","varbinary(4)",true,DefaultSql:sql)])]);
        Assert.False(SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"123").ColumnsAndIndexesRepresentable);
    }
    /// <summary>空二进制常量是合法物理值，但不能丢成现有元数据的“无默认”；生成列表达式保持保护。</summary>
    [Fact]
    public void Empty_and_generated_defaults_do_not_silently_lose_semantics()
    {
        Assert.True(MySqlBinaryDefault.TryNormalize("X''",out var empty));Assert.Equal("X''",empty);
        var schema=new DatabaseSchema("db",[new("record",[new("id","bigint",false,true),new("value","varbinary(4)",true,DefaultSql:"X''")])]);
        var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"source"),[]),"123");Assert.False(draft.ColumnsAndIndexesRepresentable);Assert.Contains(draft.Issues,item=>item.Message.Contains("零字节"));
        var column=Column("4");column.DefaultValue="AA==";var expected=MySqlSchemaPlanner.CompileColumn(column);
        Assert.False(MySqlSchemaPlanner.Equivalent(expected with{Generated=true},expected));
        foreach(var value in new[]{"错误","AQIDBAU=","UNHEX('00FF')"})
        {column.DefaultValue=value;Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.CompileColumn(column));}
        column.DefaultValue="";Assert.Null(MySqlSchemaPlanner.CompileColumn(column).DefaultSql);
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
